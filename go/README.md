# DocumentDB for langchaingo

This Go module provides a DocumentDB vector store implementing langchaingo's
`vectorstores.VectorStore` interface. The module path is
`github.com/documentdb/langchain-documentdb/go`. No release has been published;
use the repository source while the API remains pre-release.

## Requirements

- Go 1.25 or later
- `github.com/tmc/langchaingo` v0.1.14
- `go.mongodb.org/mongo-driver/v2` v2.9.0
- A DocumentDB deployment with vector search support

## Usage

The provider accepts a caller-owned `*mongo.Database`. The caller controls
authentication, client options, connection health checks, and shutdown.

```go
package main

import (
	"context"

	"github.com/documentdb/langchain-documentdb/go/documentdb"
	"github.com/tmc/langchaingo/embeddings"
	"github.com/tmc/langchaingo/schema"
	"go.mongodb.org/mongo-driver/v2/mongo"
)

func configureStore(database *mongo.Database, embedder embeddings.Embedder) (*documentdb.Store, error) {
	return documentdb.New(documentdb.Config{
		Database:       database,
		CollectionName: "langchain_documents",
		Embedder:       embedder,
		Dimensions:     1536,
		IndexKind:      documentdb.IndexDiskANN,
		Similarity:     documentdb.SimilarityCosine,
	})
}

func addAndSearch(ctx context.Context, store *documentdb.Store) ([]schema.Document, error) {
	if err := store.EnsureCollectionExists(ctx); err != nil {
		return nil, err
	}
	_, err := store.AddDocuments(ctx, []schema.Document{{
		PageContent: "DocumentDB supports vector search.",
		Metadata:    map[string]any{"category": "database"},
	}})
	if err != nil {
		return nil, err
	}
	return store.SimilaritySearch(ctx, "vector database", 5)
}
```

Production applications should construct the MongoDB client with managed or
workload identity and pass its database to `documentdb.New`. Do not place
connection strings or account keys in application code. A connection string is
supported only by the explicitly gated live test as a local test fallback.

## Capabilities

- IVF, HNSW, and DiskANN vector index commands
- Cosine, Euclidean, and inner-product similarity
- Metadata prefilters and score thresholds
- Namespaces, per-call embedders, and deduplication callbacks
- Document embedding and direct vector APIs
- ID-based, filtered, namespace-scoped, and all-document deletion
- Idempotent collection creation, index creation, and collection deletion

Per-call behavior uses standard langchaingo options such as
`vectorstores.WithNameSpace`, `WithFilters`, `WithScoreThreshold`,
`WithEmbedder`, and `WithDeduplicater`. Filter fields map below the configured
metadata field; `$and`, `$or`, and `$nor` are supported as top-level logical
operators.

## Validation

```powershell
Set-Location go
go fmt ./...
go vet ./...
go test -race -cover ./...
go build ./...
```

Live tests are disabled unless `DOCUMENTDB_RUN_INTEGRATION_TESTS=1`. See
[`../docs/testing.md`](../docs/testing.md) for required environment variables.

## Example

See [`../examples/go/vector_store.go`](../examples/go/vector_store.go) for a
complete collection, write, and filtered-search workflow using caller-owned
database and embedding dependencies.
