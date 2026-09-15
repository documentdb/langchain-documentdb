package documentdb

import (
	"context"
	"fmt"
	"strings"

	"github.com/google/uuid"
	"github.com/tmc/langchaingo/embeddings"
	"github.com/tmc/langchaingo/schema"
	"github.com/tmc/langchaingo/vectorstores"
	"go.mongodb.org/mongo-driver/v2/bson"
)

var _ vectorstores.VectorStore = (*Store)(nil)

type storedDocument struct {
	ID        string
	Text      string
	Embedding []float32
	Metadata  map[string]any
	Namespace string
}

type searchResult struct {
	Document storedDocument
	Score    float32
}

type backend interface {
	upsert(context.Context, []storedDocument) error
	search(context.Context, []float32, int, float32, string, bson.D) ([]searchResult, error)
	delete(context.Context, string, bson.D) (int64, error)
	ensureCollection(context.Context) error
	dropCollection(context.Context) error
}

// Store is a DocumentDB vector store compatible with langchaingo.
type Store struct {
	config  Config
	backend backend
}

// New creates a Store using a caller-owned, authenticated MongoDB database.
func New(config Config) (*Store, error) {
	resolved, err := config.withDefaults()
	if err != nil {
		return nil, err
	}
	return &Store{config: resolved, backend: newMongoBackend(resolved)}, nil
}

// AddDocuments embeds and upserts documents.
func (store *Store) AddDocuments(ctx context.Context, documents []schema.Document, options ...vectorstores.Option) ([]string, error) {
	resolved := applyOptions(options)
	documents = deduplicate(ctx, documents, resolved.Deduplicater)
	if len(documents) == 0 {
		return []string{}, nil
	}
	embedder := store.embedder(resolved.Embedder)
	texts := make([]string, len(documents))
	for index, document := range documents {
		texts[index] = document.PageContent
	}
	vectors, err := embedder.EmbedDocuments(ctx, texts)
	if err != nil {
		return nil, fmt.Errorf("embed documents: %w", err)
	}
	return store.addVectors(ctx, documents, vectors, resolved.NameSpace)
}

// AddVectors upserts documents with caller-provided vectors.
func (store *Store) AddVectors(ctx context.Context, documents []schema.Document, vectors [][]float32, options ...vectorstores.Option) ([]string, error) {
	resolved := applyOptions(options)
	documents, vectors = deduplicateVectors(ctx, documents, vectors, resolved.Deduplicater)
	return store.addVectors(ctx, documents, vectors, resolved.NameSpace)
}

func (store *Store) addVectors(ctx context.Context, documents []schema.Document, vectors [][]float32, namespace string) ([]string, error) {
	if len(documents) != len(vectors) {
		return nil, fmt.Errorf("documents and vectors must have equal lengths")
	}
	if len(documents) == 0 {
		return []string{}, nil
	}
	records := make([]storedDocument, len(documents))
	ids := make([]string, len(documents))
	for index, document := range documents {
		if err := validateVector(vectors[index], store.config.Dimensions); err != nil {
			return nil, fmt.Errorf("document %d: %w", index, err)
		}
		metadata := cloneMetadata(document.Metadata)
		id, _ := metadata[store.config.IDMetadataKey].(string)
		if strings.TrimSpace(id) == "" {
			id = uuid.NewString()
			metadata[store.config.IDMetadataKey] = id
		}
		ids[index] = id
		records[index] = storedDocument{ID: id, Text: document.PageContent, Embedding: vectors[index], Metadata: metadata, Namespace: namespace}
	}
	if err := store.backend.upsert(ctx, records); err != nil {
		return nil, fmt.Errorf("upsert documents: %w", err)
	}
	return ids, nil
}

// SimilaritySearch embeds a query and returns the closest documents.
func (store *Store) SimilaritySearch(ctx context.Context, query string, count int, options ...vectorstores.Option) ([]schema.Document, error) {
	resolved := applyOptions(options)
	vector, err := store.embedder(resolved.Embedder).EmbedQuery(ctx, query)
	if err != nil {
		return nil, fmt.Errorf("embed query: %w", err)
	}
	return store.similaritySearchVector(ctx, vector, count, resolved)
}

// SimilaritySearchVector searches with a caller-provided vector.
func (store *Store) SimilaritySearchVector(ctx context.Context, vector []float32, count int, options ...vectorstores.Option) ([]schema.Document, error) {
	return store.similaritySearchVector(ctx, vector, count, applyOptions(options))
}

func (store *Store) similaritySearchVector(ctx context.Context, vector []float32, count int, options vectorstores.Options) ([]schema.Document, error) {
	filter, err := normalizeFilter(options.Filters, store.config.MetadataKey)
	if err != nil {
		return nil, err
	}
	results, err := store.backend.search(ctx, vector, count, options.ScoreThreshold, options.NameSpace, filter)
	if err != nil {
		return nil, fmt.Errorf("similarity search: %w", err)
	}
	documents := make([]schema.Document, len(results))
	for index, result := range results {
		documents[index] = schema.Document{PageContent: result.Document.Text, Metadata: cloneMetadata(result.Document.Metadata), Score: result.Score}
	}
	return documents, nil
}

// Delete removes documents matching IDs and optional vector store filters.
func (store *Store) Delete(ctx context.Context, ids []string, options ...vectorstores.Option) (int64, error) {
	if len(ids) == 0 {
		return 0, fmt.Errorf("at least one ID is required; use DeleteAll for an explicit broad deletion")
	}
	resolved := applyOptions(options)
	filter, err := normalizeFilter(resolved.Filters, store.config.MetadataKey)
	if err != nil {
		return 0, err
	}
	filter = append(filter, bson.E{Key: store.config.MetadataKey + "." + store.config.IDMetadataKey, Value: bson.D{{Key: "$in", Value: ids}}})
	return store.delete(ctx, resolved.NameSpace, filter)
}

// DeleteAll removes every document matching the selected namespace and filter.
func (store *Store) DeleteAll(ctx context.Context, options ...vectorstores.Option) (int64, error) {
	resolved := applyOptions(options)
	filter, err := normalizeFilter(resolved.Filters, store.config.MetadataKey)
	if err != nil {
		return 0, err
	}
	return store.delete(ctx, resolved.NameSpace, filter)
}

func (store *Store) delete(ctx context.Context, namespace string, filter bson.D) (int64, error) {
	count, err := store.backend.delete(ctx, namespace, filter)
	if err != nil {
		return 0, fmt.Errorf("delete documents: %w", err)
	}
	return count, nil
}

// EnsureCollectionExists creates the collection and vector index when absent.
func (store *Store) EnsureCollectionExists(ctx context.Context) error {
	return store.backend.ensureCollection(ctx)
}

// EnsureCollectionDeleted drops the collection if it exists.
func (store *Store) EnsureCollectionDeleted(ctx context.Context) error {
	return store.backend.dropCollection(ctx)
}

func (store *Store) embedder(override embeddings.Embedder) embeddings.Embedder {
	if override != nil {
		return override
	}
	return store.config.Embedder
}

func applyOptions(options []vectorstores.Option) vectorstores.Options {
	var resolved vectorstores.Options
	for _, option := range options {
		option(&resolved)
	}
	return resolved
}

func deduplicate(ctx context.Context, documents []schema.Document, deduplicater func(context.Context, schema.Document) bool) []schema.Document {
	if deduplicater == nil {
		return documents
	}
	result := make([]schema.Document, 0, len(documents))
	for _, document := range documents {
		if !deduplicater(ctx, document) {
			result = append(result, document)
		}
	}
	return result
}

func deduplicateVectors(ctx context.Context, documents []schema.Document, vectors [][]float32, deduplicater func(context.Context, schema.Document) bool) ([]schema.Document, [][]float32) {
	if deduplicater == nil || len(documents) != len(vectors) {
		return documents, vectors
	}
	keptDocuments := make([]schema.Document, 0, len(documents))
	keptVectors := make([][]float32, 0, len(vectors))
	for index, document := range documents {
		if !deduplicater(ctx, document) {
			keptDocuments = append(keptDocuments, document)
			keptVectors = append(keptVectors, vectors[index])
		}
	}
	return keptDocuments, keptVectors
}

func cloneMetadata(metadata map[string]any) map[string]any {
	result := make(map[string]any, len(metadata)+1)
	for key, value := range metadata {
		result[key] = value
	}
	return result
}
