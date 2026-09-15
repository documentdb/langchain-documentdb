package documentdb

import (
	"fmt"
	"strings"

	"github.com/tmc/langchaingo/embeddings"
	"go.mongodb.org/mongo-driver/v2/mongo"
)

// VectorIndexKind identifies a DocumentDB vector index algorithm.
type VectorIndexKind string

const (
	IndexIVF     VectorIndexKind = "ivf"
	IndexHNSW    VectorIndexKind = "hnsw"
	IndexDiskANN VectorIndexKind = "diskann"
)

// Similarity identifies a DocumentDB vector similarity function.
type Similarity string

const (
	SimilarityCosine    Similarity = "COS"
	SimilarityEuclidean Similarity = "L2"
	SimilarityInnerProd Similarity = "IP"
)

// Config configures a Store. Database authentication and lifetime remain caller-owned.
type Config struct {
	Database                *mongo.Database
	CollectionName          string
	Embedder                embeddings.Embedder
	Dimensions              int
	IndexName               string
	IndexKind               VectorIndexKind
	Similarity              Similarity
	TextKey                 string
	EmbeddingKey            string
	MetadataKey             string
	NamespaceKey            string
	IDMetadataKey           string
	IVFNumLists             int
	IVFNumProbes            int
	HNSWM                   int
	HNSWEFConstruction      int
	HNSWEFSearch            int
	DiskANNMaxDegree        int
	DiskANNBuildCandidates  int
	DiskANNSearchCandidates int
}

func (config Config) withDefaults() (Config, error) {
	if config.Database == nil {
		return Config{}, fmt.Errorf("database is required")
	}
	if config.Embedder == nil {
		return Config{}, fmt.Errorf("embedder is required")
	}
	if config.IndexName == "" {
		config.IndexName = "embedding_vector"
	}
	if config.IndexKind == "" {
		config.IndexKind = IndexDiskANN
	}
	if config.Similarity == "" {
		config.Similarity = SimilarityCosine
	}
	if config.TextKey == "" {
		config.TextKey = "text"
	}
	if config.EmbeddingKey == "" {
		config.EmbeddingKey = "embedding"
	}
	if config.MetadataKey == "" {
		config.MetadataKey = "metadata"
	}
	if config.NamespaceKey == "" {
		config.NamespaceKey = "namespace"
	}
	if config.IDMetadataKey == "" {
		config.IDMetadataKey = "id"
	}
	if config.IVFNumLists == 0 {
		config.IVFNumLists = 1
	}
	if config.IVFNumProbes == 0 {
		config.IVFNumProbes = 1
	}
	if config.HNSWM == 0 {
		config.HNSWM = 16
	}
	if config.HNSWEFConstruction == 0 {
		config.HNSWEFConstruction = 64
	}
	if config.HNSWEFSearch == 0 {
		config.HNSWEFSearch = 40
	}
	if config.DiskANNMaxDegree == 0 {
		config.DiskANNMaxDegree = 32
	}
	if config.DiskANNBuildCandidates == 0 {
		config.DiskANNBuildCandidates = 50
	}
	if config.DiskANNSearchCandidates == 0 {
		config.DiskANNSearchCandidates = 40
	}

	for name, value := range map[string]string{
		"collection name": config.CollectionName, "index name": config.IndexName,
		"text key": config.TextKey, "embedding key": config.EmbeddingKey,
		"metadata key": config.MetadataKey, "namespace key": config.NamespaceKey,
		"ID metadata key": config.IDMetadataKey,
	} {
		if err := validateName(value, name); err != nil {
			return Config{}, err
		}
	}
	if config.Dimensions < 1 {
		return Config{}, fmt.Errorf("dimensions must be positive")
	}
	if config.IVFNumLists < 1 || config.IVFNumProbes < 1 {
		return Config{}, fmt.Errorf("IVF tuning values must be positive")
	}
	if config.HNSWM < 2 || config.HNSWM > 100 {
		return Config{}, fmt.Errorf("HNSW m must be between 2 and 100")
	}
	if config.HNSWEFConstruction < 2*config.HNSWM || config.HNSWEFConstruction > 1000 {
		return Config{}, fmt.Errorf("HNSW efConstruction must be between twice m and 1000")
	}
	if config.HNSWEFSearch < 1 {
		return Config{}, fmt.Errorf("HNSW efSearch must be positive")
	}
	if config.DiskANNMaxDegree < 20 || config.DiskANNMaxDegree > 2048 {
		return Config{}, fmt.Errorf("DiskANN max degree must be between 20 and 2048")
	}
	if config.DiskANNBuildCandidates < 10 || config.DiskANNBuildCandidates > 500 {
		return Config{}, fmt.Errorf("DiskANN build candidates must be between 10 and 500")
	}
	if config.DiskANNSearchCandidates < 10 || config.DiskANNSearchCandidates > 1000 {
		return Config{}, fmt.Errorf("DiskANN search candidates must be between 10 and 1000")
	}
	if config.IndexKind != IndexIVF && config.IndexKind != IndexHNSW && config.IndexKind != IndexDiskANN {
		return Config{}, fmt.Errorf("unsupported index kind %q", config.IndexKind)
	}
	if config.Similarity != SimilarityCosine && config.Similarity != SimilarityEuclidean && config.Similarity != SimilarityInnerProd {
		return Config{}, fmt.Errorf("unsupported similarity %q", config.Similarity)
	}
	return config, nil
}

func validateName(value, name string) error {
	if strings.TrimSpace(value) == "" || strings.HasPrefix(value, "$") || strings.ContainsRune(value, '\x00') {
		return fmt.Errorf("%s must be a non-empty safe name", name)
	}
	return nil
}
