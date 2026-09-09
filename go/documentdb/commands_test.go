package documentdb

import (
	"math"
	"reflect"
	"testing"

	"go.mongodb.org/mongo-driver/v2/bson"
	"go.mongodb.org/mongo-driver/v2/mongo"
)

func TestConfigDefaultsAndValidation(t *testing.T) {
	config := Config{Database: &mongo.Database{}, CollectionName: "documents", Embedder: &fakeEmbedder{}, Dimensions: 3}
	resolved, err := config.withDefaults()
	if err != nil {
		t.Fatal(err)
	}
	if resolved.IndexKind != IndexDiskANN || resolved.Similarity != SimilarityCosine || resolved.TextKey != "text" || resolved.IDMetadataKey != "id" {
		t.Fatalf("defaults mismatch: %#v", resolved)
	}
	config.Dimensions = 0
	if _, err := config.withDefaults(); err == nil {
		t.Fatal("expected invalid dimensions to fail")
	}
}

func TestCreateVectorIndexCommand(t *testing.T) {
	tests := []struct {
		name string
		kind VectorIndexKind
		want bson.D
	}{
		{name: "IVF", kind: IndexIVF, want: bson.D{{Key: "kind", Value: "vector-ivf"}, {Key: "dimensions", Value: 3}, {Key: "similarity", Value: "COS"}, {Key: "numLists", Value: 7}}},
		{name: "HNSW", kind: IndexHNSW, want: bson.D{{Key: "kind", Value: "vector-hnsw"}, {Key: "dimensions", Value: 3}, {Key: "similarity", Value: "COS"}, {Key: "m", Value: 12}, {Key: "efConstruction", Value: 30}}},
		{name: "DiskANN", kind: IndexDiskANN, want: bson.D{{Key: "kind", Value: "vector-diskann"}, {Key: "dimensions", Value: 3}, {Key: "similarity", Value: "COS"}, {Key: "maxDegree", Value: 24}, {Key: "lBuild", Value: 45}}},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			config := testConfig()
			config.IndexKind = test.kind
			command := createVectorIndexCommand(config)
			indexes := command[1].Value.(bson.A)
			index := indexes[0].(bson.D)
			if got := index[2].Value; !reflect.DeepEqual(got, test.want) {
				t.Fatalf("index options mismatch\ngot:  %#v\nwant: %#v", got, test.want)
			}
		})
	}
}

func TestCreateSearchPipeline(t *testing.T) {
	config := testConfig()
	config.IndexKind = IndexHNSW
	filter := bson.D{{Key: "metadata.category", Value: "news"}}
	pipeline, err := createSearchPipeline(config, []float32{1, 2, 3}, 4, 0.7, filter)
	if err != nil {
		t.Fatal(err)
	}
	searchStage := pipeline[0].(bson.D)
	searchBody := searchStage[0].Value.(bson.D)
	cosmosSearch := searchBody[0].Value.(bson.D)
	want := bson.D{
		{Key: "vector", Value: []float32{1, 2, 3}},
		{Key: "path", Value: "embedding"},
		{Key: "k", Value: 4},
		{Key: "filter", Value: filter},
		{Key: "efSearch", Value: 23},
	}
	if !reflect.DeepEqual(cosmosSearch, want) {
		t.Fatalf("search mismatch\ngot:  %#v\nwant: %#v", cosmosSearch, want)
	}
	if len(pipeline) != 4 {
		t.Fatalf("expected search, project, threshold, and limit stages; got %d", len(pipeline))
	}
}

func TestNormalizeFilter(t *testing.T) {
	filter, err := normalizeFilter(bson.D{
		{Key: "category", Value: "news"},
		{Key: "$or", Value: bson.A{bson.D{{Key: "year", Value: 2025}}, bson.M{"featured": true}}},
	}, "metadata")
	if err != nil {
		t.Fatal(err)
	}
	want := bson.D{
		{Key: "metadata.category", Value: "news"},
		{Key: "$or", Value: bson.A{bson.D{{Key: "metadata.year", Value: 2025}}, bson.D{{Key: "metadata.featured", Value: true}}}},
	}
	if !reflect.DeepEqual(filter, want) {
		t.Fatalf("filter mismatch\ngot:  %#v\nwant: %#v", filter, want)
	}
	if _, err := normalizeFilter(bson.D{{Key: "$where", Value: "true"}}, "metadata"); err == nil {
		t.Fatal("expected unsafe top-level operator to fail")
	}
}

func TestValidateVector(t *testing.T) {
	for _, test := range []struct {
		name   string
		vector []float32
	}{
		{name: "wrong dimensions", vector: []float32{1}},
		{name: "NaN", vector: []float32{1, float32(math.NaN()), 3}},
		{name: "infinity", vector: []float32{1, float32(math.Inf(1)), 3}},
	} {
		t.Run(test.name, func(t *testing.T) {
			if err := validateVector(test.vector, 3); err == nil {
				t.Fatal("expected invalid vector to fail")
			}
		})
	}
}

func testConfig() Config {
	return Config{
		CollectionName:          "documents",
		Dimensions:              3,
		IndexName:               "embedding_vector",
		IndexKind:               IndexDiskANN,
		Similarity:              SimilarityCosine,
		TextKey:                 "text",
		EmbeddingKey:            "embedding",
		MetadataKey:             "metadata",
		NamespaceKey:            "namespace",
		IDMetadataKey:           "id",
		IVFNumLists:             7,
		IVFNumProbes:            5,
		HNSWM:                   12,
		HNSWEFConstruction:      30,
		HNSWEFSearch:            23,
		DiskANNMaxDegree:        24,
		DiskANNBuildCandidates:  45,
		DiskANNSearchCandidates: 35,
	}
}
