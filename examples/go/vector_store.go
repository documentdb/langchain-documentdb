package examples

import (
	"context"

	"github.com/documentdb/langchain-documentdb/go/documentdb"
	"github.com/tmc/langchaingo/embeddings"
	"github.com/tmc/langchaingo/schema"
	"github.com/tmc/langchaingo/vectorstores"
	"go.mongodb.org/mongo-driver/v2/bson"
	"go.mongodb.org/mongo-driver/v2/mongo"
)

func Run(ctx context.Context, database *mongo.Database, embedder embeddings.Embedder) ([]schema.Document, error) {
	store, err := documentdb.New(documentdb.Config{
		Database:                database,
		CollectionName:          "documents",
		Embedder:                embedder,
		Dimensions:              1536,
		IndexKind:               documentdb.IndexDiskANN,
		Similarity:              documentdb.SimilarityCosine,
		DiskANNMaxDegree:        32,
		DiskANNBuildCandidates:  50,
		DiskANNSearchCandidates: 80,
	})
	if err != nil {
		return nil, err
	}
	if err := store.EnsureCollectionExists(ctx); err != nil {
		return nil, err
	}
	_, err = store.AddDocuments(ctx, []schema.Document{
		{PageContent: "DocumentDB provides a MongoDB-compatible API.", Metadata: map[string]any{"id": "documentdb-overview", "category": "database"}},
		{PageContent: "LangChain composes retrieval with language models.", Metadata: map[string]any{"id": "langchain-overview", "category": "framework"}},
	})
	if err != nil {
		return nil, err
	}
	return store.SimilaritySearch(
		ctx,
		"Which database works with MongoDB drivers?",
		4,
		vectorstores.WithFilters(bson.D{{Key: "category", Value: "database"}}),
		vectorstores.WithScoreThreshold(0.75),
	)
}
