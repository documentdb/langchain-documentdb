# DocumentDB.LangChain

`DocumentDB.LangChain` implements the Microsoft Extensions Vector Data (MEVA)
abstractions used by current LangChain.NET integrations on top of DocumentDB's
MongoDB-compatible API.

The package supports:

- typed vector collections with string keys;
- collection and vector-index lifecycle operations;
- single and batch upsert, retrieval, filtered retrieval, and deletion;
- IVF, HNSW, and DiskANN indexes;
- vector search with prefilters, score thresholds, skip, and index tuning; and
- MEVA service discovery for the underlying `IMongoDatabase` and collection.

The package is not published. Reference the repository project while the API
and package identity are under development:

```xml
<ProjectReference Include="path/to/langchain-documentdb/csharp/src/DocumentDB.LangChain/DocumentDB.LangChain.csproj" />
```

## Define a record

Use MEVA attributes to define one string key, data properties, and one or more
vector properties. `StorageName` controls the BSON field name stored in
DocumentDB.

```csharp
using Microsoft.Extensions.VectorData;

public sealed class KnowledgeRecord
{
	[VectorStoreKey]
	public string Id { get; set; } = string.Empty;

	[VectorStoreData(StorageName = "content")]
	public string Text { get; set; } = string.Empty;

	[VectorStoreData(StorageName = "category")]
	public string Category { get; set; } = string.Empty;

	[VectorStoreVector(
		1536,
		StorageName = "embedding",
		IndexKind = IndexKind.DiskAnn,
		DistanceFunction = DistanceFunction.CosineSimilarity)]
	public ReadOnlyMemory<float> Embedding { get; set; }
}
```

## Create the store

Authentication and `MongoClient` lifetime belong to the application. Configure
the driver with the identity mechanism supported by the target DocumentDB
deployment, keep the client for the application lifetime, and inject its
`IMongoDatabase`. The provider does not accept, retain, or log credentials.

```csharp
using DocumentDB.LangChain;
using Microsoft.Extensions.VectorData;
using MongoDB.Driver;

IMongoDatabase database = authenticatedMongoClient.GetDatabase("knowledge");

var store = new DocumentDBVectorStore(database, new DocumentDBVectorStoreOptions
{
	DiskAnnMaxDegree = 32,
	DiskAnnBuildCandidates = 50,
	DiskAnnSearchCandidates = 40,
});

VectorStoreCollection<string, KnowledgeRecord> collection =
	store.GetCollection<string, KnowledgeRecord>("documents");

await collection.EnsureCollectionExistsAsync();
```

For hosted production workloads, use workload or managed identity through the
driver's supported token-based authentication path. Do not put connection
strings or keys in application source or production configuration.

## Store and search records

```csharp
await collection.UpsertAsync(new KnowledgeRecord
{
	Id = "doc-1",
	Text = "DocumentDB provides a MongoDB-compatible API.",
	Category = "database",
	Embedding = embedding,
});

var searchOptions = new VectorSearchOptions<KnowledgeRecord>
{
	Filter = record => record.Category == "database",
	ScoreThreshold = 0.75,
	IncludeVectors = false,
};

await foreach (var result in collection.SearchAsync(queryEmbedding, top: 5, searchOptions))
{
	Console.WriteLine($"{result.Record.Id}: {result.Score}");
}
```

When a record has multiple vector properties, set `VectorProperty` in the
search options. Filtered retrieval also supports `Skip`, `IncludeVectors`, and
MEVA's `OrderBy` builder. Vectors are omitted from returned records unless
`IncludeVectors` is enabled.

## Index configuration

Set `IndexKind` on each `[VectorStoreVector]` property. Provider options tune
the corresponding DocumentDB index and search operation:

| Index   | Build options                                | Search option             |
| ------- | -------------------------------------------- | ------------------------- |
| IVF     | `IvfNumLists`                                | `IvfNumProbes`            |
| HNSW    | `HnswM`, `HnswEfConstruction`                | `HnswEfSearch`            |
| DiskANN | `DiskAnnMaxDegree`, `DiskAnnBuildCandidates` | `DiskAnnSearchCandidates` |

`EnsureCollectionExistsAsync` creates missing indexes. It does not replace an
existing index when options change; apply index migrations deliberately.

## Build and test

Restore dependencies, then build and run the unit suite:

```powershell
dotnet restore csharp/DocumentDB.LangChain.sln
dotnet build csharp/DocumentDB.LangChain.sln --no-restore
dotnet test csharp/DocumentDB.LangChain.sln --no-build
```

The live test is disabled unless `DOCUMENTDB_RUN_INTEGRATION_TESTS` is exactly
`1`. It creates a uniquely named collection and deletes it in `finally`.

```powershell
$env:DOCUMENTDB_RUN_INTEGRATION_TESTS = "1"
$env:DOCUMENTDB_CONNECTION_STRING = "<integration-test-only connection string>"
$env:DOCUMENTDB_DATABASE = "<isolated test database>"
$env:DOCUMENTDB_COLLECTION = "langchain_tests"
dotnet test csharp/DocumentDB.LangChain.sln --no-restore
```

Connection-string authentication is an explicit development and end-to-end
test fallback only. The test emits a warning when it is enabled. Never commit
the value or use this path for production authentication.

## Example

See [`../examples/csharp/VectorStoreExample.cs`](../examples/csharp/VectorStoreExample.cs)
for a complete collection, upsert, and filtered-search workflow using an
authenticated caller-owned `IMongoDatabase`.
