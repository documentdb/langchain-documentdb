# langchain4j-documentdb

`io.documentdb:langchain4j-documentdb` implements LangChain4j's
`EmbeddingStore<TextSegment>` contract on DocumentDB's MongoDB-compatible API.

The module supports:

- embedding-only and `TextSegment` upserts;
- single and batch operations with caller-provided or generated IDs;
- scored vector search with minimum-score thresholds;
- LangChain4j metadata filters;
- deletion by ID, ID collection, metadata filter, or all records;
- IVF, HNSW, and DiskANN vector indexes; and
- idempotent collection and vector-index lifecycle operations.

The artifact is not published. Build and install it only into the local Maven
repository while the coordinates and API are under development:

```powershell
Set-Location java
.\mvnw.cmd install
```

## Create the store

The application owns authentication and `MongoClient` lifetime. Configure a
long-lived client using the identity mechanism supported by the target
DocumentDB deployment, then inject its `MongoDatabase`:

```java
import io.documentdb.langchain4j.DocumentDBEmbeddingStore;
import io.documentdb.langchain4j.DocumentDBEmbeddingStoreConfig;
import io.documentdb.langchain4j.DocumentDBSimilarity;
import io.documentdb.langchain4j.DocumentDBVectorIndexKind;

var store = new DocumentDBEmbeddingStore(
	DocumentDBEmbeddingStoreConfig.builder()
		.database(authenticatedMongoClient.getDatabase("knowledge"))
		.collectionName("documents")
		.dimensions(1536)
		.embeddingKey("embedding")
		.textKey("text")
		.metadataKey("metadata")
		.indexName("embedding_vector")
		.indexKind(DocumentDBVectorIndexKind.DISKANN)
		.similarity(DocumentDBSimilarity.COS)
		.build());

store.ensureCollectionExists();
```

For hosted production workloads, use managed or workload identity through the
MongoDB driver's supported `MONGODB-OIDC` flow, backed by
`DefaultAzureCredential` or another appropriate `TokenCredential`. The token
resource depends on the DocumentDB deployment. This module does not accept,
retain, or log credentials. Do not use key-bearing connection strings in
production.

## Add and search segments

```java
import dev.langchain4j.data.document.Metadata;
import dev.langchain4j.data.embedding.Embedding;
import dev.langchain4j.data.segment.TextSegment;
import dev.langchain4j.store.embedding.EmbeddingSearchRequest;
import dev.langchain4j.store.embedding.filter.comparison.IsEqualTo;
import java.util.List;

store.addAll(
	List.of("doc-1"),
	List.of(Embedding.from(embeddingVector)),
	List.of(TextSegment.from(
		"DocumentDB provides a MongoDB-compatible API.",
		Metadata.from(java.util.Map.of("category", "database")))));

var request = new EmbeddingSearchRequest(
	Embedding.from(queryVector),
	5,
	0.75,
	new IsEqualTo("category", "database"));

var result = store.search(request);
for (var match : result.matches()) {
    System.out.println(match.embeddingId() + ": " + match.score());
}
```

Vectors must exactly match the configured dimensions and contain only finite
values. Upserts update the configured embedding, text, and metadata fields
while preserving unrelated root fields in the BSON document. Embedding-only
updates also preserve existing text and metadata fields.

Supported metadata filters include equality, inequality, numeric comparisons,
membership, string containment, `And`, `Or`, and `Not`. Filter keys are resolved
under the configured metadata field.

## Index configuration

`ensureCollectionExists()` creates the collection and configured vector index
when missing. It does not replace an existing index when settings change.

| Index     | Build options                                | Search option             |
| --------- | -------------------------------------------- | ------------------------- |
| `IVF`     | `ivfNumLists`                                | `ivfNumProbes`            |
| `HNSW`    | `hnswM`, `hnswEfConstruction`                | `hnswEfSearch`            |
| `DISKANN` | `diskAnnMaxDegree`, `diskAnnBuildCandidates` | `diskAnnSearchCandidates` |

Supported similarities are `COS`, `L2`, and `IP`.

## Build and test

The Maven wrapper downloads Maven 3.9.11 and uses Maven Central for dependency
resolution:

```powershell
Set-Location java
.\mvnw.cmd spotless:check
.\mvnw.cmd verify
```

The live test is skipped unless `DOCUMENTDB_RUN_INTEGRATION_TESTS` is exactly
`1`. It creates a uniquely named collection and deletes it in `finally`.

```powershell
$env:DOCUMENTDB_RUN_INTEGRATION_TESTS = "1"
$env:DOCUMENTDB_CONNECTION_STRING = "<integration-test-only connection string>"
$env:DOCUMENTDB_DATABASE = "<isolated test database>"
$env:DOCUMENTDB_COLLECTION = "langchain_tests"
.\mvnw.cmd test
```

Connection-string authentication is an explicit local and end-to-end test
fallback only. The live test prints a warning when enabled. Never commit the
value or use this path for production authentication.

## Example

See [`../examples/java/VectorStoreExample.java`](../examples/java/VectorStoreExample.java)
for a complete collection, write, and filtered-search workflow using a
caller-owned `MongoDatabase` and precomputed vectors.
