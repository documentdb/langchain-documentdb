# @documentdb/langchain-documentdb

`@documentdb/langchain-documentdb` is a typed DocumentDB vector store for LangChain.js. It uses
DocumentDB's MongoDB-compatible API and implements the current `@langchain/core` `VectorStore`
contract.

The package supports:

- document embedding and precomputed-vector upserts;
- similarity search with scores and DocumentDB prefilters;
- IVF, HNSW, and DiskANN vector indexes;
- caller-selected BSON field names and vector-search tuning;
- deletion by explicit IDs or filter; and
- collection and vector-index lifecycle operations.

The package is private and is not published. For repository-local development:

```powershell
Set-Location javascript
npm install
npm run build
```

## Create a vector store

The application owns authentication and `MongoClient` lifetime. Configure a long-lived client using
the identity mechanism supported by the target DocumentDB deployment, then inject its collection:

```typescript
import type { EmbeddingsInterface } from "@langchain/core/embeddings";
import type { MongoClient } from "mongodb";
import {
  DocumentDBVectorStore,
  type DocumentDBStoredDocument,
} from "@documentdb/langchain-documentdb";

declare const embeddings: EmbeddingsInterface;
declare const authenticatedMongoClient: MongoClient;

const collection = authenticatedMongoClient
  .db("knowledge")
  .collection<DocumentDBStoredDocument>("documents");

const vectorStore = new DocumentDBVectorStore(embeddings, {
  collection,
  dimensions: 1536,
  embeddingKey: "embedding",
  textKey: "text",
  metadataKey: "metadata",
  indexName: "embedding_vector",
  indexKind: "diskann",
  similarity: "COS",
});

await vectorStore.ensureCollectionExists();
```

For hosted production workloads, configure managed or workload identity through the MongoDB driver's
supported `MONGODB-OIDC` Azure workflow or a `TokenCredential`-backed OIDC callback. The required
token resource depends on the DocumentDB deployment. This package does not accept, retain, or log
credentials. Do not use key-bearing connection strings in production.

## Add and search documents

`addDocuments` calls the configured LangChain embeddings implementation. A LangChain document ID is
stored as the BSON `_id`; a UUID is generated when the document and options do not provide one.

```typescript
await vectorStore.addDocuments([
  {
    id: "doc-1",
    pageContent: "DocumentDB provides a MongoDB-compatible API.",
    metadata: { category: "database", tenant: "contoso" },
  },
]);

const results = await vectorStore.similaritySearchWithScore("document database", 5, {
  "metadata.category": "database",
  "metadata.tenant": "contoso",
});

for (const [document, score] of results) {
  console.log(document.id, score);
}
```

Use `addVectors` when embeddings are already available. Every vector must match the configured
dimensions and contain only finite numbers. Upserts update the configured text, embedding, and
metadata fields while preserving unrelated root fields already present in the BSON document.

The inherited `asRetriever` method provides standard LangChain.js retriever composition without a
separate DocumentDB-specific retriever.

## Delete documents

Deletion requires non-empty IDs or a non-empty filter to prevent accidental collection-wide removal:

```typescript
await vectorStore.delete({ ids: ["doc-1", "doc-2"] });
await vectorStore.delete({ filter: { "metadata.tenant": "contoso" } });
```

Use `ensureCollectionDeleted()` only for deliberate lifecycle cleanup.

## Index configuration

`ensureCollectionExists()` creates the collection and missing configured index. It does not replace
an existing index when settings change.

| Index kind | Build options                                | Search option             |
| ---------- | -------------------------------------------- | ------------------------- |
| `ivf`      | `ivfNumLists`                                | `ivfNumProbes`            |
| `hnsw`     | `hnswM`, `hnswEfConstruction`                | `hnswEfSearch`            |
| `diskann`  | `diskAnnMaxDegree`, `diskAnnBuildCandidates` | `diskAnnSearchCandidates` |

Supported similarities are `COS`, `L2`, and `IP`.

## Test

```powershell
Set-Location javascript
npm ci
npm run typecheck
npm run build
npm run test:coverage
```

The live test is skipped unless `DOCUMENTDB_RUN_INTEGRATION_TESTS` is exactly `1`. It creates a
uniquely named collection and deletes it in `finally`.

```powershell
$env:DOCUMENTDB_RUN_INTEGRATION_TESTS = "1"
$env:DOCUMENTDB_CONNECTION_STRING = "<integration-test-only connection string>"
$env:DOCUMENTDB_DATABASE = "<isolated test database>"
$env:DOCUMENTDB_COLLECTION = "langchain_tests"
npm test
```

Connection-string authentication is an explicit local and end-to-end test fallback only. The live
test prints a warning when enabled. Never commit the value or use this path for production
authentication.

## Example

See [`../examples/javascript/vector-store.ts`](../examples/javascript/vector-store.ts) for a
complete collection, write, and filtered-search workflow using caller-owned collection and embedding
dependencies.
