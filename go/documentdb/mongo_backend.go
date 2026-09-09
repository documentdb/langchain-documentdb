package documentdb

import (
	"context"
	"fmt"

	"go.mongodb.org/mongo-driver/v2/bson"
	"go.mongodb.org/mongo-driver/v2/mongo"
)

type mongoBackend struct {
	config     Config
	collection *mongo.Collection
}

func newMongoBackend(config Config) *mongoBackend {
	return &mongoBackend{config: config, collection: config.Database.Collection(config.CollectionName)}
}

func (backend *mongoBackend) upsert(ctx context.Context, documents []storedDocument) error {
	models := make([]mongo.WriteModel, len(documents))
	for index, document := range documents {
		identity := bson.D{{Key: "namespace", Value: document.Namespace}, {Key: "id", Value: document.ID}}
		values := bson.D{
			{Key: backend.config.TextKey, Value: document.Text},
			{Key: backend.config.EmbeddingKey, Value: document.Embedding},
			{Key: backend.config.MetadataKey, Value: document.Metadata},
			{Key: backend.config.NamespaceKey, Value: document.Namespace},
		}
		models[index] = mongo.NewUpdateOneModel().SetFilter(bson.D{{Key: "_id", Value: identity}}).SetUpdate(bson.D{{Key: "$set", Value: values}}).SetUpsert(true)
	}
	_, err := backend.collection.BulkWrite(ctx, models)
	return err
}

func (backend *mongoBackend) search(ctx context.Context, vector []float32, count int, threshold float32, namespace string, filter bson.D) ([]searchResult, error) {
	filter = append(filter, bson.E{Key: backend.config.NamespaceKey, Value: namespace})
	pipeline, err := createSearchPipeline(backend.config, vector, count, threshold, filter)
	if err != nil {
		return nil, err
	}
	cursor, err := backend.collection.Aggregate(ctx, pipeline)
	if err != nil {
		return nil, err
	}
	defer cursor.Close(ctx)
	var rows []struct {
		SimilarityScore float32 `bson:"similarityScore"`
		Document        bson.M  `bson:"document"`
	}
	if err := cursor.All(ctx, &rows); err != nil {
		return nil, err
	}
	results := make([]searchResult, len(rows))
	for index, row := range rows {
		text, _ := row.Document[backend.config.TextKey].(string)
		metadata, err := mapValue(row.Document[backend.config.MetadataKey])
		if err != nil {
			return nil, fmt.Errorf("decode metadata: %w", err)
		}
		results[index] = searchResult{Document: storedDocument{Text: text, Metadata: metadata, Namespace: namespace}, Score: row.SimilarityScore}
	}
	return results, nil
}

func (backend *mongoBackend) delete(ctx context.Context, namespace string, filter bson.D) (int64, error) {
	filter = append(filter, bson.E{Key: backend.config.NamespaceKey, Value: namespace})
	result, err := backend.collection.DeleteMany(ctx, filter)
	if err != nil {
		return 0, err
	}
	return result.DeletedCount, nil
}

func (backend *mongoBackend) ensureCollection(ctx context.Context) error {
	names, err := backend.config.Database.ListCollectionNames(ctx, bson.D{{Key: "name", Value: backend.config.CollectionName}})
	if err != nil {
		return err
	}
	if len(names) == 0 {
		if err := backend.config.Database.CreateCollection(ctx, backend.config.CollectionName); err != nil {
			return err
		}
	}
	indexes, err := backend.collection.Indexes().List(ctx)
	if err != nil {
		return err
	}
	defer indexes.Close(ctx)
	for indexes.Next(ctx) {
		var index struct {
			Name string `bson:"name"`
		}
		if err := indexes.Decode(&index); err != nil {
			return err
		}
		if index.Name == backend.config.IndexName {
			return nil
		}
	}
	if err := indexes.Err(); err != nil {
		return err
	}
	return backend.config.Database.RunCommand(ctx, createVectorIndexCommand(backend.config)).Err()
}

func (backend *mongoBackend) dropCollection(ctx context.Context) error {
	return backend.collection.Drop(ctx)
}

func mapValue(value any) (map[string]any, error) {
	if value == nil {
		return map[string]any{}, nil
	}
	if metadata, ok := value.(bson.M); ok {
		return map[string]any(metadata), nil
	}
	encoded, err := bson.Marshal(value)
	if err != nil {
		return nil, err
	}
	var metadata map[string]any
	if err := bson.Unmarshal(encoded, &metadata); err != nil {
		return nil, err
	}
	return metadata, nil
}
