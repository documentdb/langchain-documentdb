package io.documentdb.langchain4j;

import static org.junit.jupiter.api.Assertions.assertEquals;

import com.mongodb.ConnectionString;
import com.mongodb.MongoClientSettings;
import com.mongodb.MongoCredential;
import com.mongodb.client.MongoClient;
import com.mongodb.client.MongoClients;
import dev.langchain4j.data.document.Metadata;
import dev.langchain4j.data.embedding.Embedding;
import dev.langchain4j.data.segment.TextSegment;
import dev.langchain4j.store.embedding.EmbeddingSearchRequest;
import dev.langchain4j.store.embedding.EmbeddingSearchResult;
import java.util.List;
import java.util.UUID;
import org.junit.jupiter.api.Assumptions;
import org.junit.jupiter.api.Test;

class DocumentDBIntegrationTest {
  @Test
  void storesAndSearchesTextSegments() {
    Assumptions.assumeTrue("1".equals(System.getenv("DOCUMENTDB_RUN_INTEGRATION_TESTS")));
    String databaseName = required("DOCUMENTDB_DATABASE");
    String prefix = System.getenv().getOrDefault("DOCUMENTDB_COLLECTION", "langchain_tests");
    String collectionName = prefix + "_" + UUID.randomUUID().toString().replace("-", "");

    try (MongoClient client = createClient()) {
      DocumentDBEmbeddingStore store =
          new DocumentDBEmbeddingStore(
              DocumentDBEmbeddingStoreConfig.builder()
                  .database(client.getDatabase(databaseName))
                  .collectionName(collectionName)
                  .dimensions(3)
                  .indexKind(DocumentDBVectorIndexKind.IVF)
                  .ivfNumLists(1)
                  .ivfNumProbes(1)
                  .build());
      try {
        store.ensureCollectionExists();
        store.addAll(
            List.of("doc-1"),
            List.of(Embedding.from(new float[] {1, 0, 0})),
            List.of(
                TextSegment.from(
                    "DocumentDB vector search",
                    Metadata.from(java.util.Map.of("category", "database")))));

        EmbeddingSearchResult<TextSegment> result =
            store.search(
                new EmbeddingSearchRequest(Embedding.from(new float[] {1, 0, 0}), 1, 0.0, null));
        assertEquals(1, result.matches().size());
        assertEquals("doc-1", result.matches().get(0).embeddingId());
      } finally {
        store.ensureCollectionDeleted();
      }
    }
  }

  private static MongoClient createClient() {
    if ("entra".equals(System.getenv("DOCUMENTDB_AUTH_MODE"))) {
      MongoCredential.OidcCallback callback =
          context -> new MongoCredential.OidcCallbackResult(required("DOCUMENTDB_OIDC_TOKEN"));
      MongoCredential credential =
          MongoCredential.createOidcCredential(null)
              .withMechanismProperty(MongoCredential.OIDC_CALLBACK_KEY, callback);
      MongoClientSettings settings =
          MongoClientSettings.builder()
              .applyConnectionString(new ConnectionString(required("DOCUMENTDB_ENDPOINT")))
              .credential(credential)
              .retryWrites(false)
              .build();
      return MongoClients.create(settings);
    }

    System.err.println(
        "WARNING: integration-test-only connection string authentication is enabled.");
    return MongoClients.create(required("DOCUMENTDB_CONNECTION_STRING"));
  }

  private static String required(String name) {
    String value = System.getenv(name);
    if (value == null || value.isBlank()) {
      throw new IllegalStateException(
          name + " is required when live integration tests are enabled");
    }
    return value;
  }
}
