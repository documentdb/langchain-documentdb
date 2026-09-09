package io.documentdb.langchain4j;

import dev.langchain4j.store.embedding.EmbeddingSearchRequest;
import java.util.ArrayList;
import java.util.List;
import org.bson.Document;

final class DocumentDBCommandBuilder {
  private DocumentDBCommandBuilder() {}

  static Document createVectorIndex(DocumentDBEmbeddingStoreConfig config) {
    Document options =
        new Document("kind", config.indexKind.wireName())
            .append("dimensions", config.dimensions)
            .append("similarity", config.similarity.name());
    switch (config.indexKind) {
      case IVF -> options.append("numLists", config.ivfNumLists);
      case HNSW ->
          options.append("m", config.hnswM).append("efConstruction", config.hnswEfConstruction);
      case DISKANN ->
          options
              .append("maxDegree", config.diskAnnMaxDegree)
              .append("lBuild", config.diskAnnBuildCandidates);
    }

    Document index =
        new Document("name", config.indexName)
            .append("key", new Document(config.embeddingKey, "cosmosSearch"))
            .append("cosmosSearchOptions", options);
    return new Document("createIndexes", config.collectionName).append("indexes", List.of(index));
  }

  static List<Document> createSearchPipeline(
      DocumentDBEmbeddingStoreConfig config, EmbeddingSearchRequest request) {
    if (request.queryEmbedding() == null) {
      throw new IllegalArgumentException("queryEmbedding is required");
    }
    validateVector(request.queryEmbedding().vector(), config.dimensions, "queryEmbedding");

    Document search =
        new Document("vector", request.queryEmbedding().vectorAsList())
            .append("path", config.embeddingKey)
            .append("k", request.maxResults());
    if (request.filter() != null) {
      search.append("filter", DocumentDBFilterMapper.toBson(request.filter(), config.metadataKey));
    }
    switch (config.indexKind) {
      case IVF -> search.append("nProbes", config.ivfNumProbes);
      case HNSW -> search.append("efSearch", config.hnswEfSearch);
      case DISKANN -> search.append("lSearch", config.diskAnnSearchCandidates);
    }

    List<Document> pipeline = new ArrayList<>();
    pipeline.add(
        new Document(
            "$search", new Document("cosmosSearch", search).append("returnStoredSource", true)));
    pipeline.add(
        new Document(
            "$project",
            new Document("similarityScore", new Document("$meta", "searchScore"))
                .append("document", "$$ROOT")));
    if (request.minScore() > 0) {
      pipeline.add(
          new Document(
              "$match", new Document("similarityScore", new Document("$gte", request.minScore()))));
    }
    pipeline.add(new Document("$limit", request.maxResults()));
    return pipeline;
  }

  static void validateVector(float[] vector, int dimensions, String name) {
    if (vector.length != dimensions) {
      throw new IllegalArgumentException(name + " must contain exactly " + dimensions + " values");
    }
    for (float value : vector) {
      if (!Float.isFinite(value)) {
        throw new IllegalArgumentException(name + " must contain only finite values");
      }
    }
  }
}
