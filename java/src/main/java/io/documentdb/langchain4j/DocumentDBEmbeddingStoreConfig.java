package io.documentdb.langchain4j;

import com.mongodb.client.MongoDatabase;
import java.util.Objects;

/** Immutable configuration for a {@link DocumentDBEmbeddingStore}. */
public final class DocumentDBEmbeddingStoreConfig {
  final MongoDatabase database;
  final String collectionName;
  final int dimensions;
  final String indexName;
  final DocumentDBVectorIndexKind indexKind;
  final DocumentDBSimilarity similarity;
  final String textKey;
  final String embeddingKey;
  final String metadataKey;
  final int ivfNumLists;
  final int ivfNumProbes;
  final int hnswM;
  final int hnswEfConstruction;
  final int hnswEfSearch;
  final int diskAnnMaxDegree;
  final int diskAnnBuildCandidates;
  final int diskAnnSearchCandidates;

  private DocumentDBEmbeddingStoreConfig(Builder builder) {
    database = Objects.requireNonNull(builder.database, "database");
    collectionName = safeName(builder.collectionName, "collectionName");
    dimensions = positive(builder.dimensions, "dimensions");
    indexName = safeName(builder.indexName, "indexName");
    indexKind = Objects.requireNonNull(builder.indexKind, "indexKind");
    similarity = Objects.requireNonNull(builder.similarity, "similarity");
    textKey = safeName(builder.textKey, "textKey");
    embeddingKey = safeName(builder.embeddingKey, "embeddingKey");
    metadataKey = safeName(builder.metadataKey, "metadataKey");
    ivfNumLists = positive(builder.ivfNumLists, "ivfNumLists");
    ivfNumProbes = positive(builder.ivfNumProbes, "ivfNumProbes");
    hnswM = range(builder.hnswM, 2, 100, "hnswM");
    hnswEfConstruction = range(builder.hnswEfConstruction, 4, 1000, "hnswEfConstruction");
    if (hnswEfConstruction < 2 * hnswM) {
      throw new IllegalArgumentException("hnswEfConstruction must be at least twice hnswM");
    }
    hnswEfSearch = positive(builder.hnswEfSearch, "hnswEfSearch");
    diskAnnMaxDegree = range(builder.diskAnnMaxDegree, 20, 2048, "diskAnnMaxDegree");
    diskAnnBuildCandidates =
        range(builder.diskAnnBuildCandidates, 10, 500, "diskAnnBuildCandidates");
    diskAnnSearchCandidates =
        range(builder.diskAnnSearchCandidates, 10, 1000, "diskAnnSearchCandidates");
  }

  /** Creates a configuration builder. */
  public static Builder builder() {
    return new Builder();
  }

  /** Fluent builder for DocumentDB embedding store configuration. */
  public static final class Builder {
    private MongoDatabase database;
    private String collectionName;
    private int dimensions;
    private String indexName = "embedding_vector";
    private DocumentDBVectorIndexKind indexKind = DocumentDBVectorIndexKind.DISKANN;
    private DocumentDBSimilarity similarity = DocumentDBSimilarity.COS;
    private String textKey = "text";
    private String embeddingKey = "embedding";
    private String metadataKey = "metadata";
    private int ivfNumLists = 1;
    private int ivfNumProbes = 1;
    private int hnswM = 16;
    private int hnswEfConstruction = 64;
    private int hnswEfSearch = 40;
    private int diskAnnMaxDegree = 32;
    private int diskAnnBuildCandidates = 50;
    private int diskAnnSearchCandidates = 40;

    private Builder() {}

    public Builder database(MongoDatabase value) {
      database = value;
      return this;
    }

    public Builder collectionName(String value) {
      collectionName = value;
      return this;
    }

    public Builder dimensions(int value) {
      dimensions = value;
      return this;
    }

    public Builder indexName(String value) {
      indexName = value;
      return this;
    }

    public Builder indexKind(DocumentDBVectorIndexKind value) {
      indexKind = value;
      return this;
    }

    public Builder similarity(DocumentDBSimilarity value) {
      similarity = value;
      return this;
    }

    public Builder textKey(String value) {
      textKey = value;
      return this;
    }

    public Builder embeddingKey(String value) {
      embeddingKey = value;
      return this;
    }

    public Builder metadataKey(String value) {
      metadataKey = value;
      return this;
    }

    public Builder ivfNumLists(int value) {
      ivfNumLists = value;
      return this;
    }

    public Builder ivfNumProbes(int value) {
      ivfNumProbes = value;
      return this;
    }

    public Builder hnswM(int value) {
      hnswM = value;
      return this;
    }

    public Builder hnswEfConstruction(int value) {
      hnswEfConstruction = value;
      return this;
    }

    public Builder hnswEfSearch(int value) {
      hnswEfSearch = value;
      return this;
    }

    public Builder diskAnnMaxDegree(int value) {
      diskAnnMaxDegree = value;
      return this;
    }

    public Builder diskAnnBuildCandidates(int value) {
      diskAnnBuildCandidates = value;
      return this;
    }

    public Builder diskAnnSearchCandidates(int value) {
      diskAnnSearchCandidates = value;
      return this;
    }

    public DocumentDBEmbeddingStoreConfig build() {
      return new DocumentDBEmbeddingStoreConfig(this);
    }
  }

  private static int positive(int value, String name) {
    return range(value, 1, Integer.MAX_VALUE, name);
  }

  private static int range(int value, int minimum, int maximum, String name) {
    if (value < minimum || value > maximum) {
      throw new IllegalArgumentException(name + " must be between " + minimum + " and " + maximum);
    }
    return value;
  }

  private static String safeName(String value, String name) {
    if (value == null || value.isBlank() || value.startsWith("$") || value.indexOf('\0') >= 0) {
      throw new IllegalArgumentException(name + " must be a non-empty safe name");
    }
    return value;
  }
}
