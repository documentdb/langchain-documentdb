package io.documentdb.langchain4j;

import com.mongodb.MongoCommandException;
import com.mongodb.client.MongoCollection;
import com.mongodb.client.model.BulkWriteOptions;
import com.mongodb.client.model.UpdateOneModel;
import com.mongodb.client.model.UpdateOptions;
import com.mongodb.client.model.WriteModel;
import dev.langchain4j.data.document.Metadata;
import dev.langchain4j.data.embedding.Embedding;
import dev.langchain4j.data.segment.TextSegment;
import dev.langchain4j.store.embedding.EmbeddingMatch;
import dev.langchain4j.store.embedding.EmbeddingSearchRequest;
import dev.langchain4j.store.embedding.EmbeddingSearchResult;
import dev.langchain4j.store.embedding.EmbeddingStore;
import dev.langchain4j.store.embedding.filter.Filter;
import java.util.ArrayList;
import java.util.Collection;
import java.util.Collections;
import java.util.List;
import java.util.Objects;
import java.util.UUID;
import org.bson.Document;

/** LangChain4j embedding store backed by DocumentDB vector search. */
public final class DocumentDBEmbeddingStore implements EmbeddingStore<TextSegment> {
  private final DocumentDBEmbeddingStoreConfig config;
  private final MongoCollection<Document> collection;

  public DocumentDBEmbeddingStore(DocumentDBEmbeddingStoreConfig config) {
    this.config = Objects.requireNonNull(config, "config");
    this.collection = config.database.getCollection(config.collectionName);
  }

  @Override
  public String add(Embedding embedding) {
    String id = UUID.randomUUID().toString();
    upsert(id, embedding, null);
    return id;
  }

  @Override
  public void add(String id, Embedding embedding) {
    upsert(id, embedding, null);
  }

  @Override
  public String add(Embedding embedding, TextSegment segment) {
    String id = UUID.randomUUID().toString();
    upsert(id, embedding, segment);
    return id;
  }

  /** Adds or updates an embedding and text segment under a caller-provided ID. */
  public void add(String id, Embedding embedding, TextSegment segment) {
    upsert(id, embedding, segment);
  }

  @Override
  public List<String> addAll(List<Embedding> embeddings) {
    Objects.requireNonNull(embeddings, "embeddings");
    List<String> ids = generateIds(embeddings.size());
    upsertAll(ids, embeddings, Collections.nCopies(embeddings.size(), null));
    return ids;
  }

  @Override
  public List<String> addAll(List<Embedding> embeddings, List<TextSegment> segments) {
    Objects.requireNonNull(embeddings, "embeddings");
    List<String> ids = generateIds(embeddings.size());
    upsertAll(ids, embeddings, segments);
    return ids;
  }

  @Override
  public void addAll(List<String> ids, List<Embedding> embeddings, List<TextSegment> segments) {
    upsertAll(ids, embeddings, segments);
  }

  @Override
  public void remove(String id) {
    collection.deleteOne(new Document("_id", requireId(id)));
  }

  @Override
  public void removeAll(Collection<String> ids) {
    Objects.requireNonNull(ids, "ids");
    if (ids.isEmpty()) {
      return;
    }
    List<String> validated = ids.stream().map(DocumentDBEmbeddingStore::requireId).toList();
    collection.deleteMany(new Document("_id", new Document("$in", validated)));
  }

  @Override
  public void removeAll(Filter filter) {
    collection.deleteMany(
        DocumentDBFilterMapper.toBson(
            Objects.requireNonNull(filter, "filter"), config.metadataKey));
  }

  @Override
  public void removeAll() {
    collection.deleteMany(new Document());
  }

  @Override
  public EmbeddingSearchResult<TextSegment> search(EmbeddingSearchRequest request) {
    Objects.requireNonNull(request, "request");
    List<Document> results =
        collection
            .aggregate(DocumentDBCommandBuilder.createSearchPipeline(config, request))
            .into(new ArrayList<>());
    List<EmbeddingMatch<TextSegment>> matches = results.stream().map(this::toMatch).toList();
    return new EmbeddingSearchResult<>(matches);
  }

  public void ensureCollectionExists() {
    boolean exists =
        config
            .database
            .listCollectionNames()
            .into(new ArrayList<>())
            .contains(config.collectionName);
    if (!exists) {
      try {
        config.database.createCollection(config.collectionName);
      } catch (MongoCommandException exception) {
        if (exception.getErrorCode() != 48) {
          throw exception;
        }
      }
    }

    boolean indexExists =
        collection.listIndexes().into(new ArrayList<>()).stream()
            .anyMatch(index -> config.indexName.equals(index.getString("name")));
    if (!indexExists) {
      config.database.runCommand(DocumentDBCommandBuilder.createVectorIndex(config));
    }
  }

  public void ensureCollectionDeleted() {
    try {
      collection.drop();
    } catch (MongoCommandException exception) {
      if (exception.getErrorCode() != 26) {
        throw exception;
      }
    }
  }

  private void upsert(String id, Embedding embedding, TextSegment segment) {
    upsertAll(List.of(id), List.of(embedding), Collections.singletonList(segment));
  }

  private void upsertAll(List<String> ids, List<Embedding> embeddings, List<TextSegment> segments) {
    Objects.requireNonNull(ids, "ids");
    Objects.requireNonNull(embeddings, "embeddings");
    Objects.requireNonNull(segments, "segments");
    if (ids.size() != embeddings.size() || ids.size() != segments.size()) {
      throw new IllegalArgumentException("ids, embeddings, and segments must have the same size");
    }
    if (ids.isEmpty()) {
      return;
    }

    List<WriteModel<Document>> writes = new ArrayList<>(ids.size());
    for (int index = 0; index < ids.size(); index++) {
      String id = requireId(ids.get(index));
      Embedding embedding = Objects.requireNonNull(embeddings.get(index), "embedding");
      DocumentDBCommandBuilder.validateVector(embedding.vector(), config.dimensions, "embedding");
      TextSegment segment = segments.get(index);

      Document set = new Document(config.embeddingKey, embedding.vectorAsList());
      Document update = new Document("$set", set);
      if (segment != null) {
        set.append(config.textKey, segment.text())
            .append(config.metadataKey, new Document(segment.metadata().toMap()));
      }
      writes.add(
          new UpdateOneModel<>(new Document("_id", id), update, new UpdateOptions().upsert(true)));
    }
    collection.bulkWrite(writes, new BulkWriteOptions().ordered(false));
  }

  private EmbeddingMatch<TextSegment> toMatch(Document result) {
    Number score =
        Objects.requireNonNull(result.get("similarityScore", Number.class), "similarityScore");
    Document stored = Objects.requireNonNull(result.get("document", Document.class), "document");
    String id = Objects.requireNonNull(stored.getString("_id"), "_id");
    List<Number> values =
        Objects.requireNonNull(
            stored.getList(config.embeddingKey, Number.class), config.embeddingKey);
    float[] vector = new float[values.size()];
    for (int index = 0; index < values.size(); index++) {
      vector[index] = values.get(index).floatValue();
    }

    String text = stored.getString(config.textKey);
    Document metadata = stored.get(config.metadataKey, Document.class);
    TextSegment segment =
        text == null
            ? null
            : TextSegment.from(
                text, Metadata.from(metadata == null ? java.util.Map.of() : metadata));
    return new EmbeddingMatch<>(score.doubleValue(), id, Embedding.from(vector), segment);
  }

  private static String requireId(String id) {
    if (id == null || id.isBlank()) {
      throw new IllegalArgumentException("id must be a non-empty string");
    }
    return id;
  }
}
