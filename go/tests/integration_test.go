package tests

import (
	"context"
	"fmt"
	"os"
	"testing"
	"time"

	"github.com/documentdb/langchain-documentdb/go/documentdb"
	"github.com/tmc/langchaingo/schema"
	"go.mongodb.org/mongo-driver/v2/mongo"
	"go.mongodb.org/mongo-driver/v2/mongo/options"
)

type deterministicEmbedder struct{}

func (deterministicEmbedder) EmbedDocuments(_ context.Context, texts []string) ([][]float32, error) {
	vectors := make([][]float32, len(texts))
	for index, text := range texts {
		vectors[index] = vectorFor(text)
	}
	return vectors, nil
}

func (deterministicEmbedder) EmbedQuery(_ context.Context, text string) ([]float32, error) {
	return vectorFor(text), nil
}

func vectorFor(text string) []float32 {
	vector := []float32{0, 0, 0}
	for index, value := range []byte(text) {
		vector[index%len(vector)] += float32(value) / 255
	}
	return vector
}

func TestDocumentDBLive(t *testing.T) {
	if os.Getenv("DOCUMENTDB_RUN_INTEGRATION_TESTS") != "1" {
		t.Skip("set DOCUMENTDB_RUN_INTEGRATION_TESTS=1 to run live tests")
	}
	databaseName := requiredEnvironment(t, "DOCUMENTDB_DATABASE")
	collectionPrefix := os.Getenv("DOCUMENTDB_COLLECTION")
	if collectionPrefix == "" {
		collectionPrefix = "langchaingo"
	}
	collectionName := fmt.Sprintf("%s_%d", collectionPrefix, time.Now().UnixNano())

	ctx, cancel := context.WithTimeout(context.Background(), 3*time.Minute)
	defer cancel()
	clientOptions := options.Client()
	if os.Getenv("DOCUMENTDB_AUTH_MODE") == "entra" {
		accessToken := requiredEnvironment(t, "DOCUMENTDB_OIDC_TOKEN")
		clientOptions.ApplyURI(requiredEnvironment(t, "DOCUMENTDB_ENDPOINT"))
		clientOptions.SetAuth(options.Credential{
			AuthMechanism: "MONGODB-OIDC",
			OIDCMachineCallback: func(context.Context, *options.OIDCArgs) (*options.OIDCCredential, error) {
				return &options.OIDCCredential{AccessToken: accessToken}, nil
			},
		})
		clientOptions.SetRetryWrites(false)
		clientOptions.SetMaxConnIdleTime(2 * time.Minute)
	} else {
		t.Log("WARNING: live test is using connection-string authentication; production applications must use managed or workload identity")
		clientOptions.ApplyURI(requiredEnvironment(t, "DOCUMENTDB_CONNECTION_STRING"))
	}
	client, err := mongo.Connect(clientOptions)
	if err != nil {
		t.Fatal(err)
	}
	defer client.Disconnect(context.Background())
	store, err := documentdb.New(documentdb.Config{
		Database:       client.Database(databaseName),
		CollectionName: collectionName,
		Embedder:       deterministicEmbedder{},
		Dimensions:     3,
		IndexKind:      documentdb.IndexIVF,
		IVFNumLists:    1,
		IVFNumProbes:   1,
	})
	if err != nil {
		t.Fatal(err)
	}
	defer func() {
		if err := store.EnsureCollectionDeleted(context.Background()); err != nil {
			t.Errorf("clean up collection: %v", err)
		}
	}()
	if err := store.EnsureCollectionExists(ctx); err != nil {
		t.Fatal(err)
	}
	ids, err := store.AddDocuments(ctx, []schema.Document{
		{PageContent: "document databases", Metadata: map[string]any{"category": "database"}},
		{PageContent: "ocean weather", Metadata: map[string]any{"category": "weather"}},
	})
	if err != nil {
		t.Fatal(err)
	}
	if len(ids) != 2 {
		t.Fatalf("expected two IDs, got %d", len(ids))
	}
	if deleted, err := store.Delete(ctx, ids[:1]); err != nil || deleted != 1 {
		t.Fatalf("delete one document: count=%d err=%v", deleted, err)
	}
}

func requiredEnvironment(t *testing.T, name string) string {
	t.Helper()
	value := os.Getenv(name)
	if value == "" {
		t.Fatalf("%s is required when live tests are enabled", name)
	}
	return value
}
