import type { EmbeddingsInterface } from "@langchain/core/embeddings";
import type { Document } from "@langchain/core/documents";
import type { Collection } from "mongodb";
import {
  DocumentDBVectorStore,
  type DocumentDBStoredDocument,
} from "@documentdb/langchain-documentdb";

export async function run(
  collection: Collection<DocumentDBStoredDocument>,
  embeddings: EmbeddingsInterface,
): Promise<Document[]> {
  const store = new DocumentDBVectorStore(embeddings, {
    collection,
    dimensions: 1536,
    indexName: "embedding_vector",
    indexKind: "diskann",
    similarity: "COS",
    diskAnnMaxDegree: 32,
    diskAnnBuildCandidates: 50,
    diskAnnSearchCandidates: 80,
  });

  await store.ensureCollectionExists();
  await store.addDocuments([
    {
      id: "documentdb-overview",
      pageContent: "DocumentDB provides a MongoDB-compatible API.",
      metadata: { category: "database", published: true },
    },
    {
      id: "langchain-overview",
      pageContent: "LangChain composes retrieval with language models.",
      metadata: { category: "framework", published: true },
    },
  ]);

  return store.similaritySearch(
    "Which database works with MongoDB drivers?",
    4,
    {
      "metadata.category": "database",
    },
  );
}
