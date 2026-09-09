package io.documentdb.langchain4j;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.anyList;
import static org.mockito.Mockito.doThrow;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import com.mongodb.MongoCommandException;
import com.mongodb.client.AggregateIterable;
import com.mongodb.client.ListCollectionNamesIterable;
import com.mongodb.client.ListIndexesIterable;
import com.mongodb.client.MongoCollection;
import com.mongodb.client.MongoDatabase;
import com.mongodb.client.model.BulkWriteOptions;
import com.mongodb.client.model.WriteModel;
import com.mongodb.client.result.DeleteResult;
import dev.langchain4j.data.document.Metadata;
import dev.langchain4j.data.embedding.Embedding;
import dev.langchain4j.data.segment.TextSegment;
import dev.langchain4j.store.embedding.EmbeddingSearchRequest;
import dev.langchain4j.store.embedding.EmbeddingSearchResult;
import dev.langchain4j.store.embedding.filter.comparison.IsEqualTo;
import java.util.List;
import org.bson.Document;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.mockito.ArgumentCaptor;

@SuppressWarnings("unchecked")
class DocumentDBEmbeddingStoreTest {
  private MongoCollection<Document> collection;
  private MongoDatabase database;
  private DocumentDBEmbeddingStore store;

  @BeforeEach
  void setUp() {
    database = mock(MongoDatabase.class);
    collection = mock(MongoCollection.class);
    when(database.getCollection("documents")).thenReturn(collection);
    store =
        new DocumentDBEmbeddingStore(
            DocumentDBEmbeddingStoreConfig.builder()
                .database(database)
                .collectionName("documents")
                .dimensions(3)
                .build());
  }

  @Test
  void batchUpsertPreservesIdsTextAndMetadata() {
    TextSegment segment =
        TextSegment.from("DocumentDB", Metadata.from(java.util.Map.of("category", "database")));

    store.addAll(
        List.of("doc-1"), List.of(Embedding.from(new float[] {1, 0, 0})), List.of(segment));

    ArgumentCaptor<List<WriteModel<Document>>> writes = ArgumentCaptor.forClass(List.class);
    verify(collection).bulkWrite(writes.capture(), any(BulkWriteOptions.class));
    String command = writes.getValue().get(0).toString();
    org.junit.jupiter.api.Assertions.assertTrue(command.contains("doc-1"));
    org.junit.jupiter.api.Assertions.assertTrue(command.contains("DocumentDB"));
    org.junit.jupiter.api.Assertions.assertTrue(command.contains("database"));
  }

  @Test
  void supportsGeneratedIdsAndEmptyBatches() {
    List<String> ids = store.addAll(List.of(Embedding.from(new float[] {1, 0, 0})));
    assertEquals(1, ids.size());
    verify(collection).bulkWrite(anyList(), any(BulkWriteOptions.class));

    store.addAll(List.of(), List.of(), List.of());
    verify(collection, org.mockito.Mockito.times(1))
        .bulkWrite(anyList(), any(BulkWriteOptions.class));
  }

  @Test
  void embeddingOnlyUpdatePreservesExistingSegmentFields() {
    store.add("doc-1", Embedding.from(new float[] {1, 0, 0}));

    ArgumentCaptor<List<WriteModel<Document>>> writes = ArgumentCaptor.forClass(List.class);
    verify(collection).bulkWrite(writes.capture(), any(BulkWriteOptions.class));
    String update = writes.getValue().get(0).toString();
    org.junit.jupiter.api.Assertions.assertTrue(update.contains("embedding"));
    org.junit.jupiter.api.Assertions.assertFalse(update.contains("$unset"));
  }

  @Test
  void supportsExplicitAndGeneratedIdsForSegments() {
    TextSegment segment = TextSegment.from("DocumentDB");
    store.add("explicit-id", Embedding.from(new float[] {1, 0, 0}), segment);
    List<String> generated =
        store.addAll(List.of(Embedding.from(new float[] {1, 0, 0})), List.of(segment));

    assertEquals(1, generated.size());
    verify(collection, org.mockito.Mockito.times(2))
        .bulkWrite(anyList(), any(BulkWriteOptions.class));
  }

  @Test
  void validatesBatchShapesAndVectors() {
    assertThrows(
        IllegalArgumentException.class, () -> store.addAll(List.of("one"), List.of(), List.of()));
    assertThrows(
        IllegalArgumentException.class, () -> store.add("one", Embedding.from(new float[] {1, 0})));
    assertThrows(IllegalArgumentException.class, () -> store.remove(" "));
  }

  @Test
  void removesByIdIdsFilterAndAll() {
    when(collection.deleteOne(any(Document.class))).thenReturn(mock(DeleteResult.class));
    when(collection.deleteMany(any(Document.class))).thenReturn(mock(DeleteResult.class));

    store.remove("one");
    store.removeAll(List.of("two", "three"));
    store.removeAll(new IsEqualTo("category", "expired"));
    store.removeAll();

    verify(collection).deleteOne(new Document("_id", "one"));
    verify(collection, org.mockito.Mockito.times(3)).deleteMany(any(Document.class));
  }

  @Test
  void mapsSearchResults() {
    AggregateIterable<Document> aggregate = mock(AggregateIterable.class);
    when(collection.aggregate(any())).thenReturn(aggregate);
    Document stored =
        new Document("_id", "doc-1")
            .append("embedding", List.of(1.0, 0.0, 0.0))
            .append("text", "DocumentDB")
            .append("metadata", new Document("category", "database"));
    when(aggregate.into(any()))
        .thenAnswer(
            invocation -> {
              List<Document> target = invocation.getArgument(0);
              target.add(new Document("similarityScore", 0.98).append("document", stored));
              return target;
            });

    EmbeddingSearchResult<TextSegment> result =
        store.search(
            new EmbeddingSearchRequest(Embedding.from(new float[] {1, 0, 0}), 1, 0.0, null));

    assertEquals(1, result.matches().size());
    assertEquals("doc-1", result.matches().get(0).embeddingId());
    assertEquals("DocumentDB", result.matches().get(0).embedded().text());
    assertEquals(0.98, result.matches().get(0).score());
  }

  @Test
  void mapsEmbeddingOnlySearchResults() {
    AggregateIterable<Document> aggregate = mock(AggregateIterable.class);
    when(collection.aggregate(any())).thenReturn(aggregate);
    Document stored = new Document("_id", "doc-1").append("embedding", List.of(1, 0, 0));
    when(aggregate.into(any()))
        .thenAnswer(
            invocation -> {
              List<Document> target = invocation.getArgument(0);
              target.add(new Document("similarityScore", 1).append("document", stored));
              return target;
            });

    EmbeddingSearchResult<TextSegment> result =
        store.search(
            new EmbeddingSearchRequest(Embedding.from(new float[] {1, 0, 0}), 1, 0.0, null));
    assertNull(result.matches().get(0).embedded());
  }

  @Test
  void createsOnlyMissingCollectionAndIndex() {
    ListCollectionNamesIterable collections = mock(ListCollectionNamesIterable.class);
    ListIndexesIterable<Document> indexes = mock(ListIndexesIterable.class);
    when(database.listCollectionNames()).thenReturn(collections);
    when(collections.into(any())).thenAnswer(invocation -> invocation.getArgument(0));
    when(collection.listIndexes()).thenReturn(indexes);
    when(indexes.into(any())).thenAnswer(invocation -> invocation.getArgument(0));

    store.ensureCollectionExists();

    verify(database).createCollection("documents");
    verify(database).runCommand(any(Document.class));
  }

  @Test
  void doesNotRecreateExistingCollectionOrIndex() {
    ListCollectionNamesIterable collections = mock(ListCollectionNamesIterable.class);
    ListIndexesIterable<Document> indexes = mock(ListIndexesIterable.class);
    when(database.listCollectionNames()).thenReturn(collections);
    when(collections.into(any()))
        .thenAnswer(
            invocation -> {
              List<String> target = invocation.getArgument(0);
              target.add("documents");
              return target;
            });
    when(collection.listIndexes()).thenReturn(indexes);
    when(indexes.into(any()))
        .thenAnswer(
            invocation -> {
              List<Document> target = invocation.getArgument(0);
              target.add(new Document("name", "embedding_vector"));
              return target;
            });

    store.ensureCollectionExists();

    verify(database, never()).createCollection(any());
    verify(database, never()).runCommand(any(Document.class));
  }

  @Test
  void ignoresOnlyExpectedLifecycleErrors() {
    ListCollectionNamesIterable collections = mock(ListCollectionNamesIterable.class);
    ListIndexesIterable<Document> indexes = mock(ListIndexesIterable.class);
    when(database.listCollectionNames()).thenReturn(collections);
    when(collections.into(any())).thenAnswer(invocation -> invocation.getArgument(0));
    when(collection.listIndexes()).thenReturn(indexes);
    when(indexes.into(any())).thenAnswer(invocation -> invocation.getArgument(0));

    MongoCommandException alreadyExists = mock(MongoCommandException.class);
    when(alreadyExists.getErrorCode()).thenReturn(48);
    doThrow(alreadyExists).when(database).createCollection("documents");
    store.ensureCollectionExists();

    MongoCommandException missing = mock(MongoCommandException.class);
    when(missing.getErrorCode()).thenReturn(26);
    doThrow(missing).when(collection).drop();
    store.ensureCollectionDeleted();

    MongoCommandException denied = mock(MongoCommandException.class);
    when(denied.getErrorCode()).thenReturn(13);
    doThrow(denied).when(collection).drop();
    assertThrows(MongoCommandException.class, store::ensureCollectionDeleted);
  }
}
