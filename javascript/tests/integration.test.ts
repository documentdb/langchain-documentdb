import { randomUUID } from "node:crypto";
import type { EmbeddingsInterface } from "@langchain/core/embeddings";
import { MongoClient, type OIDCResponse } from "mongodb";
import { describe, expect, it } from "vitest";
import { DocumentDBVectorStore, type DocumentDBStoredDocument } from "../src/index.js";

const liveTestsEnabled = process.env.DOCUMENTDB_RUN_INTEGRATION_TESTS === "1";
const describeLive = liveTestsEnabled ? describe : describe.skip;

describeLive("DocumentDB live integration", () => {
  it("stores and searches a LangChain document", async () => {
    const databaseName = requiredEnvironmentVariable("DOCUMENTDB_DATABASE");
    const collectionPrefix = process.env.DOCUMENTDB_COLLECTION ?? "langchain_tests";
    const collectionName = `${collectionPrefix}_${randomUUID().replaceAll("-", "")}`;

    const client = createClient();
    const collection = client.db(databaseName).collection<DocumentDBStoredDocument>(collectionName);
    const embeddings: EmbeddingsInterface = {
      embedDocuments: async () => [[1, 0, 0]],
      embedQuery: async () => [1, 0, 0],
    };
    const store = new DocumentDBVectorStore(embeddings, {
      collection,
      dimensions: 3,
      indexKind: "ivf",
      ivfNumLists: 1,
      ivfNumProbes: 1,
    });

    try {
      await store.ensureCollectionExists();
      await store.addDocuments([
        {
          id: "doc-1",
          pageContent: "DocumentDB vector search",
          metadata: { category: "database" },
        },
      ]);

      const results = await store.similaritySearchVectorWithScore([1, 0, 0], 1);
      expect(results).toHaveLength(1);
      expect(results[0][0].id).toBe("doc-1");
    } finally {
      await store.ensureCollectionDeleted();
      await client.close();
    }
  }, 180_000);
});

function requiredEnvironmentVariable(name: string): string {
  const value = process.env[name];
  if (!value) {
    throw new Error(`${name} is required when live integration tests are enabled`);
  }
  return value;
}

function createClient(): MongoClient {
  if (process.env.DOCUMENTDB_AUTH_MODE === "entra") {
    const accessToken = requiredEnvironmentVariable("DOCUMENTDB_OIDC_TOKEN");
    const oidcCallback = async (): Promise<OIDCResponse> => ({ accessToken });
    return new MongoClient(requiredEnvironmentVariable("DOCUMENTDB_ENDPOINT"), {
      tls: true,
      retryWrites: false,
      maxIdleTimeMS: 120_000,
      authMechanism: "MONGODB-OIDC",
      authMechanismProperties: {
        OIDC_CALLBACK: oidcCallback,
        ALLOWED_HOSTS: ["*.azure.com"],
      },
    });
  }

  console.warn("WARNING: integration-test-only connection string authentication is enabled.");
  return new MongoClient(requiredEnvironmentVariable("DOCUMENTDB_CONNECTION_STRING"));
}
