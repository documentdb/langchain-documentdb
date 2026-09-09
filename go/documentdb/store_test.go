package documentdb

import (
	"context"
	"errors"
	"reflect"
	"testing"

	"github.com/tmc/langchaingo/schema"
	"github.com/tmc/langchaingo/vectorstores"
	"go.mongodb.org/mongo-driver/v2/bson"
)

type fakeEmbedder struct {
	documentVectors [][]float32
	queryVector     []float32
	documentTexts   []string
	queryText       string
}

func (embedder *fakeEmbedder) EmbedDocuments(_ context.Context, texts []string) ([][]float32, error) {
	embedder.documentTexts = texts
	return embedder.documentVectors, nil
}

func (embedder *fakeEmbedder) EmbedQuery(_ context.Context, text string) ([]float32, error) {
	embedder.queryText = text
	return embedder.queryVector, nil
}

type fakeBackend struct {
	upserted          []storedDocument
	searchResults     []searchResult
	searchedVector    []float32
	searchedCount     int
	searchedThreshold float32
	searchedNamespace string
	searchedFilter    bson.D
	deletedNamespace  string
	deletedFilter     bson.D
	ensureCalls       int
	dropCalls         int
	err               error
}

func (backend *fakeBackend) upsert(_ context.Context, documents []storedDocument) error {
	backend.upserted = documents
	return backend.err
}

func (backend *fakeBackend) search(_ context.Context, vector []float32, count int, threshold float32, namespace string, filter bson.D) ([]searchResult, error) {
	backend.searchedVector = vector
	backend.searchedCount = count
	backend.searchedThreshold = threshold
	backend.searchedNamespace = namespace
	backend.searchedFilter = filter
	return backend.searchResults, backend.err
}

func (backend *fakeBackend) delete(_ context.Context, namespace string, filter bson.D) (int64, error) {
	backend.deletedNamespace = namespace
	backend.deletedFilter = filter
	return 2, backend.err
}

func (backend *fakeBackend) ensureCollection(context.Context) error {
	backend.ensureCalls++
	return backend.err
}

func (backend *fakeBackend) dropCollection(context.Context) error {
	backend.dropCalls++
	return backend.err
}

func TestAddDocumentsOptionsAndDeduplication(t *testing.T) {
	defaultEmbedder := &fakeEmbedder{}
	overrideEmbedder := &fakeEmbedder{documentVectors: [][]float32{{1, 2, 3}}}
	backend := &fakeBackend{}
	store := testStore(defaultEmbedder, backend)
	documents := []schema.Document{
		{PageContent: "keep", Metadata: map[string]any{"id": "known", "category": "a"}},
		{PageContent: "skip", Metadata: map[string]any{"duplicate": true}},
	}
	ids, err := store.AddDocuments(context.Background(), documents,
		vectorstores.WithNameSpace("tenant-a"),
		vectorstores.WithEmbedder(overrideEmbedder),
		vectorstores.WithDeduplicater(func(_ context.Context, document schema.Document) bool { return document.Metadata["duplicate"] == true }),
	)
	if err != nil {
		t.Fatal(err)
	}
	if !reflect.DeepEqual(ids, []string{"known"}) || !reflect.DeepEqual(overrideEmbedder.documentTexts, []string{"keep"}) {
		t.Fatalf("unexpected IDs or embedded texts: %#v %#v", ids, overrideEmbedder.documentTexts)
	}
	if len(backend.upserted) != 1 || backend.upserted[0].Namespace != "tenant-a" {
		t.Fatalf("unexpected upsert: %#v", backend.upserted)
	}
	if documents[0].Metadata["new"] != nil || len(documents[0].Metadata) != 2 {
		t.Fatal("input metadata was mutated")
	}
}

func TestAddVectorsGeneratesID(t *testing.T) {
	backend := &fakeBackend{}
	store := testStore(&fakeEmbedder{}, backend)
	document := schema.Document{PageContent: "generated"}
	ids, err := store.AddVectors(context.Background(), []schema.Document{document}, [][]float32{{1, 2, 3}})
	if err != nil {
		t.Fatal(err)
	}
	if len(ids) != 1 || ids[0] == "" || backend.upserted[0].Metadata["id"] != ids[0] {
		t.Fatalf("ID was not generated consistently: %#v %#v", ids, backend.upserted)
	}
}

func TestSimilaritySearchOptionsAndMapping(t *testing.T) {
	embedder := &fakeEmbedder{queryVector: []float32{1, 2, 3}}
	backend := &fakeBackend{searchResults: []searchResult{{Document: storedDocument{Text: "result", Metadata: map[string]any{"id": "one"}}, Score: 0.91}}}
	store := testStore(embedder, backend)
	documents, err := store.SimilaritySearch(context.Background(), "question", 5,
		vectorstores.WithNameSpace("tenant-b"),
		vectorstores.WithScoreThreshold(0.8),
		vectorstores.WithFilters(bson.D{{Key: "category", Value: "news"}}),
	)
	if err != nil {
		t.Fatal(err)
	}
	if embedder.queryText != "question" || backend.searchedNamespace != "tenant-b" || backend.searchedThreshold != 0.8 || backend.searchedCount != 5 {
		t.Fatalf("search options were not forwarded: %#v", backend)
	}
	wantFilter := bson.D{{Key: "metadata.category", Value: "news"}}
	if !reflect.DeepEqual(backend.searchedFilter, wantFilter) {
		t.Fatalf("filter mismatch: %#v", backend.searchedFilter)
	}
	if len(documents) != 1 || documents[0].PageContent != "result" || documents[0].Score != 0.91 {
		t.Fatalf("result mapping mismatch: %#v", documents)
	}
}

func TestDeleteAndLifecycle(t *testing.T) {
	backend := &fakeBackend{}
	store := testStore(&fakeEmbedder{}, backend)
	if _, err := store.Delete(context.Background(), nil); err == nil {
		t.Fatal("expected an empty ID list to be rejected")
	}
	count, err := store.Delete(context.Background(), []string{"one", "two"}, vectorstores.WithNameSpace("tenant-c"), vectorstores.WithFilters(bson.M{"active": true}))
	if err != nil {
		t.Fatal(err)
	}
	wantFilter := bson.D{{Key: "metadata.active", Value: true}, {Key: "metadata.id", Value: bson.D{{Key: "$in", Value: []string{"one", "two"}}}}}
	if count != 2 || backend.deletedNamespace != "tenant-c" || !reflect.DeepEqual(backend.deletedFilter, wantFilter) {
		t.Fatalf("delete mismatch: count=%d namespace=%q filter=%#v", count, backend.deletedNamespace, backend.deletedFilter)
	}
	if err := store.EnsureCollectionExists(context.Background()); err != nil {
		t.Fatal(err)
	}
	if err := store.EnsureCollectionDeleted(context.Background()); err != nil {
		t.Fatal(err)
	}
	if backend.ensureCalls != 1 || backend.dropCalls != 1 {
		t.Fatalf("lifecycle calls mismatch: %#v", backend)
	}
	backend.err = errors.New("backend failed")
	if _, err := store.DeleteAll(context.Background()); err == nil {
		t.Fatal("expected backend error to be wrapped")
	}
}

func testStore(embedder *fakeEmbedder, backend backend) *Store {
	config := testConfig()
	config.Embedder = embedder
	return &Store{config: config, backend: backend}
}
