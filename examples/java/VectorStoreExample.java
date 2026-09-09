package examples;

import com.mongodb.client.MongoDatabase;
import dev.langchain4j.data.document.Metadata;
import dev.langchain4j.data.embedding.Embedding;
import dev.langchain4j.data.segment.TextSegment;
import dev.langchain4j.store.embedding.EmbeddingMatch;
import dev.langchain4j.store.embedding.EmbeddingSearchRequest;
import dev.langchain4j.store.embedding.filter.comparison.IsEqualTo;
import io.documentdb.langchain4j.DocumentDBEmbeddingStore;
import io.documentdb.langchain4j.DocumentDBEmbeddingStoreConfig;
import io.documentdb.langchain4j.DocumentDBSimilarity;
import io.documentdb.langchain4j.DocumentDBVectorIndexKind;
import java.util.List;
import java.util.Map;

public final class VectorStoreExample {
    private VectorStoreExample() {}

    public static List<EmbeddingMatch<TextSegment>> run(
            MongoDatabase database, float[] documentVector, float[] queryVector) {
        var store = new DocumentDBEmbeddingStore(
                DocumentDBEmbeddingStoreConfig.builder()
                        .database(database)
                        .collectionName("documents")
                        .dimensions(documentVector.length)
                        .indexName("embedding_vector")
                        .indexKind(DocumentDBVectorIndexKind.DISKANN)
                        .similarity(DocumentDBSimilarity.COS)
                        .diskAnnMaxDegree(32)
                        .diskAnnBuildCandidates(50)
                        .diskAnnSearchCandidates(80)
                        .build());

        store.ensureCollectionExists();
        store.add(
                "documentdb-overview",
                Embedding.from(documentVector),
                TextSegment.from(
                        "DocumentDB provides a MongoDB-compatible API.",
                        Metadata.from(Map.of("category", "database", "published", true))));

        var request = new EmbeddingSearchRequest(
                Embedding.from(queryVector),
                4,
                0.75,
                new IsEqualTo("category", "database"));
        return store.search(request).matches();
    }
}