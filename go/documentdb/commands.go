package documentdb

import (
	"fmt"
	"math"
	"strings"

	"go.mongodb.org/mongo-driver/v2/bson"
)

func createVectorIndexCommand(config Config) bson.D {
	indexOptions := bson.D{{Key: "kind", Value: "vector-" + string(config.IndexKind)}, {Key: "dimensions", Value: config.Dimensions}, {Key: "similarity", Value: string(config.Similarity)}}
	switch config.IndexKind {
	case IndexIVF:
		indexOptions = append(indexOptions, bson.E{Key: "numLists", Value: config.IVFNumLists})
	case IndexHNSW:
		indexOptions = append(indexOptions, bson.E{Key: "m", Value: config.HNSWM}, bson.E{Key: "efConstruction", Value: config.HNSWEFConstruction})
	case IndexDiskANN:
		indexOptions = append(indexOptions, bson.E{Key: "maxDegree", Value: config.DiskANNMaxDegree}, bson.E{Key: "lBuild", Value: config.DiskANNBuildCandidates})
	}
	index := bson.D{{Key: "name", Value: config.IndexName}, {Key: "key", Value: bson.D{{Key: config.EmbeddingKey, Value: "cosmosSearch"}}}, {Key: "cosmosSearchOptions", Value: indexOptions}}
	return bson.D{{Key: "createIndexes", Value: config.CollectionName}, {Key: "indexes", Value: bson.A{index}}}
}

func createSearchPipeline(config Config, vector []float32, count int, threshold float32, filter bson.D) (bson.A, error) {
	if err := validateVector(vector, config.Dimensions); err != nil {
		return nil, err
	}
	if count < 1 {
		return nil, fmt.Errorf("number of documents must be positive")
	}
	if threshold < 0 || threshold > 1 {
		return nil, fmt.Errorf("score threshold must be between 0 and 1")
	}
	search := bson.D{{Key: "vector", Value: vector}, {Key: "path", Value: config.EmbeddingKey}, {Key: "k", Value: count}}
	if len(filter) > 0 {
		search = append(search, bson.E{Key: "filter", Value: filter})
	}
	switch config.IndexKind {
	case IndexIVF:
		search = append(search, bson.E{Key: "nProbes", Value: config.IVFNumProbes})
	case IndexHNSW:
		search = append(search, bson.E{Key: "efSearch", Value: config.HNSWEFSearch})
	case IndexDiskANN:
		search = append(search, bson.E{Key: "lSearch", Value: config.DiskANNSearchCandidates})
	}
	pipeline := bson.A{
		bson.D{{Key: "$search", Value: bson.D{{Key: "cosmosSearch", Value: search}, {Key: "returnStoredSource", Value: true}}}},
		bson.D{{Key: "$project", Value: bson.D{{Key: "similarityScore", Value: bson.D{{Key: "$meta", Value: "searchScore"}}}, {Key: "document", Value: "$$ROOT"}}}},
	}
	if threshold > 0 {
		pipeline = append(pipeline, bson.D{{Key: "$match", Value: bson.D{{Key: "similarityScore", Value: bson.D{{Key: "$gte", Value: threshold}}}}}})
	}
	return append(pipeline, bson.D{{Key: "$limit", Value: count}}), nil
}

func normalizeFilter(value any, metadataKey string) (bson.D, error) {
	if value == nil {
		return nil, nil
	}
	var source bson.D
	switch typed := value.(type) {
	case bson.D:
		source = typed
	case bson.M:
		for key, item := range typed {
			source = append(source, bson.E{Key: key, Value: item})
		}
	case map[string]any:
		for key, item := range typed {
			source = append(source, bson.E{Key: key, Value: item})
		}
	default:
		return nil, fmt.Errorf("filters must be bson.D, bson.M, or map[string]any")
	}
	result := make(bson.D, 0, len(source))
	for _, element := range source {
		if strings.HasPrefix(element.Key, "$") {
			if element.Key != "$and" && element.Key != "$or" && element.Key != "$nor" {
				return nil, fmt.Errorf("unsupported top-level filter operator %q", element.Key)
			}
			items, ok := element.Value.(bson.A)
			if !ok {
				return nil, fmt.Errorf("%s must contain bson.A", element.Key)
			}
			mapped := make(bson.A, 0, len(items))
			for _, item := range items {
				child, err := normalizeFilter(item, metadataKey)
				if err != nil {
					return nil, err
				}
				mapped = append(mapped, child)
			}
			result = append(result, bson.E{Key: element.Key, Value: mapped})
			continue
		}
		if err := validateName(element.Key, "filter field"); err != nil {
			return nil, err
		}
		key := element.Key
		if !strings.HasPrefix(key, metadataKey+".") {
			key = metadataKey + "." + key
		}
		result = append(result, bson.E{Key: key, Value: element.Value})
	}
	return result, nil
}

func validateVector(vector []float32, dimensions int) error {
	if len(vector) != dimensions {
		return fmt.Errorf("vector must contain exactly %d values", dimensions)
	}
	for _, value := range vector {
		if math.IsNaN(float64(value)) || math.IsInf(float64(value), 0) {
			return fmt.Errorf("vector must contain only finite values")
		}
	}
	return nil
}
