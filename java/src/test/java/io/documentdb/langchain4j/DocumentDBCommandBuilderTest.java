package io.documentdb.langchain4j;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;
import static org.mockito.Mockito.mock;

import com.mongodb.client.MongoDatabase;
import dev.langchain4j.data.embedding.Embedding;
import dev.langchain4j.store.embedding.EmbeddingSearchRequest;
import dev.langchain4j.store.embedding.filter.Filter;
import dev.langchain4j.store.embedding.filter.comparison.ContainsString;
import dev.langchain4j.store.embedding.filter.comparison.IsEqualTo;
import dev.langchain4j.store.embedding.filter.comparison.IsGreaterThan;
import dev.langchain4j.store.embedding.filter.comparison.IsGreaterThanOrEqualTo;
import dev.langchain4j.store.embedding.filter.comparison.IsIn;
import dev.langchain4j.store.embedding.filter.comparison.IsLessThan;
import dev.langchain4j.store.embedding.filter.comparison.IsLessThanOrEqualTo;
import dev.langchain4j.store.embedding.filter.comparison.IsNotEqualTo;
import dev.langchain4j.store.embedding.filter.comparison.IsNotIn;
import dev.langchain4j.store.embedding.filter.logical.Not;
import java.util.List;
import java.util.regex.Pattern;
import java.util.stream.Stream;
import org.bson.Document;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.Arguments;
import org.junit.jupiter.params.provider.MethodSource;

class DocumentDBCommandBuilderTest {
  @ParameterizedTest
  @MethodSource("indexCases")
  void createsIndexCommands(
      DocumentDBVectorIndexKind indexKind, String expectedKind, String tuningField) {
    Document command = DocumentDBCommandBuilder.createVectorIndex(config(indexKind));
    Document index = command.getList("indexes", Document.class).get(0);
    Document options = index.get("cosmosSearchOptions", Document.class);

    assertEquals("documents", command.getString("createIndexes"));
    assertEquals(new Document("vector", "cosmosSearch"), index.get("key"));
    assertEquals(expectedKind, options.getString("kind"));
    assertEquals(3, options.getInteger("dimensions"));
    assertTrue(options.containsKey(tuningField));
  }

  @ParameterizedTest
  @MethodSource("searchCases")
  void createsSearchPipelines(DocumentDBVectorIndexKind indexKind, String tuningField) {
    Filter filter = new IsEqualTo("category", "database");
    EmbeddingSearchRequest request =
        new EmbeddingSearchRequest(Embedding.from(new float[] {1, 0, 0}), 2, 0.75, filter);

    List<Document> pipeline =
        DocumentDBCommandBuilder.createSearchPipeline(config(indexKind), request);
    Document search =
        pipeline.get(0).get("$search", Document.class).get("cosmosSearch", Document.class);

    assertEquals(List.of(1.0F, 0.0F, 0.0F), search.get("vector"));
    assertEquals("vector", search.getString("path"));
    assertEquals(new Document("metadata.category", "database"), search.get("filter"));
    assertTrue(search.containsKey(tuningField));
    assertEquals(new Document("$limit", 2), pipeline.get(pipeline.size() - 1));
    assertTrue(pipeline.stream().anyMatch(stage -> stage.containsKey("$match")));
  }

  @Test
  void mapsComparisonLogicalAndContainsFilters() {
    Filter filter =
        new IsEqualTo("category", "database")
            .and(new IsGreaterThan("priority", 2).or(new IsIn("tenant", List.of("a", "b"))));
    Document bson = DocumentDBFilterMapper.toBson(filter, "metadata");
    assertTrue(bson.containsKey("$and"));

    Document not =
        DocumentDBFilterMapper.toBson(new Not(new IsEqualTo("category", "expired")), "metadata");
    assertTrue(not.containsKey("$nor"));

    Document contains =
        DocumentDBFilterMapper.toBson(new ContainsString("title", "DocumentDB"), "metadata");
    Pattern pattern = contains.get("metadata.title", Pattern.class);
    assertEquals("\\QDocumentDB\\E", pattern.pattern());
  }

  @ParameterizedTest
  @MethodSource("comparisonCases")
  void mapsAllComparisonOperators(Filter filter, String operator) {
    Document bson = DocumentDBFilterMapper.toBson(filter, "metadata");
    Document comparison = bson.get("metadata.priority", Document.class);
    assertTrue(comparison.containsKey(operator));
  }

  @Test
  void rejectsUnsafeAndUnknownFilters() {
    assertThrows(
        IllegalArgumentException.class,
        () -> DocumentDBFilterMapper.toBson(new IsEqualTo("$priority", 1), "metadata"));
    Filter unknown = ignored -> true;
    assertThrows(
        UnsupportedOperationException.class,
        () -> DocumentDBFilterMapper.toBson(unknown, "metadata"));
  }

  @Test
  void rejectsInvalidConfigurationAndVectors() {
    assertThrows(
        IllegalArgumentException.class,
        () ->
            DocumentDBEmbeddingStoreConfig.builder()
                .database(mock(MongoDatabase.class))
                .collectionName("documents")
                .dimensions(0)
                .build());
    assertThrows(
        IllegalArgumentException.class,
        () ->
            DocumentDBEmbeddingStoreConfig.builder()
                .database(mock(MongoDatabase.class))
                .collectionName("documents")
                .dimensions(3)
                .embeddingKey("$vector")
                .build());
    assertThrows(
        IllegalArgumentException.class,
        () -> DocumentDBCommandBuilder.validateVector(new float[] {1, 0}, 3, "embedding"));
    assertThrows(
        IllegalArgumentException.class,
        () ->
            DocumentDBCommandBuilder.validateVector(new float[] {1, Float.NaN, 0}, 3, "embedding"));
  }

  private static Stream<Arguments> indexCases() {
    return Stream.of(
        Arguments.of(DocumentDBVectorIndexKind.IVF, "vector-ivf", "numLists"),
        Arguments.of(DocumentDBVectorIndexKind.HNSW, "vector-hnsw", "efConstruction"),
        Arguments.of(DocumentDBVectorIndexKind.DISKANN, "vector-diskann", "lBuild"));
  }

  private static Stream<Arguments> searchCases() {
    return Stream.of(
        Arguments.of(DocumentDBVectorIndexKind.IVF, "nProbes"),
        Arguments.of(DocumentDBVectorIndexKind.HNSW, "efSearch"),
        Arguments.of(DocumentDBVectorIndexKind.DISKANN, "lSearch"));
  }

  private static Stream<Arguments> comparisonCases() {
    return Stream.of(
        Arguments.of(new IsNotEqualTo("priority", 1), "$ne"),
        Arguments.of(new IsGreaterThanOrEqualTo("priority", 1), "$gte"),
        Arguments.of(new IsLessThan("priority", 1), "$lt"),
        Arguments.of(new IsLessThanOrEqualTo("priority", 1), "$lte"),
        Arguments.of(new IsNotIn("priority", List.of(1, 2)), "$nin"));
  }

  private static DocumentDBEmbeddingStoreConfig config(DocumentDBVectorIndexKind indexKind) {
    return DocumentDBEmbeddingStoreConfig.builder()
        .database(mock(MongoDatabase.class))
        .collectionName("documents")
        .dimensions(3)
        .embeddingKey("vector")
        .indexKind(indexKind)
        .build();
  }
}
