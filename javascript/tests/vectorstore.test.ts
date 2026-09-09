import type { DocumentInterface } from "@langchain/core/documents";
import type { EmbeddingsInterface } from "@langchain/core/embeddings";
import type { Collection } from "mongodb";
import { describe, expect, it, vi } from "vitest";
import { DocumentDBVectorStore, type DocumentDBStoredDocument } from "../src/index.js";

function createHarness() {
  const bulkWrite = vi.fn().mockResolvedValue({});
  const deleteMany = vi.fn().mockResolvedValue({});
  const aggregateToArray = vi.fn().mockResolvedValue([]);
  const command = vi.fn().mockResolvedValue({ ok: 1 });
  const createCollection = vi.fn().mockResolvedValue({});
  const drop = vi.fn().mockResolvedValue(true);
  const hasNext = vi.fn().mockResolvedValue(false);
  const listIndexesToArray = vi.fn().mockResolvedValue([]);
  const collection = {
    collectionName: "documents",
    db: {
      listCollections: vi.fn(() => ({ hasNext })),
      createCollection,
      command,
    },
    bulkWrite,
    deleteMany,
    aggregate: vi.fn(() => ({ toArray: aggregateToArray })),
    listIndexes: vi.fn(() => ({ toArray: listIndexesToArray })),
    drop,
  } as unknown as Collection<DocumentDBStoredDocument>;
  const embeddings: EmbeddingsInterface = {
    embedDocuments: vi.fn().mockResolvedValue([[1, 0, 0]]),
    embedQuery: vi.fn().mockResolvedValue([1, 0, 0]),
  };
  const store = new DocumentDBVectorStore(embeddings, {
    collection,
    dimensions: 3,
  });
  return {
    aggregateToArray,
    bulkWrite,
    collection,
    command,
    createCollection,
    deleteMany,
    drop,
    embeddings,
    hasNext,
    listIndexesToArray,
    store,
  };
}

describe("DocumentDBVectorStore", () => {
  it("embeds and upserts documents while preserving IDs", async () => {
    const { bulkWrite, embeddings, store } = createHarness();
    const documents: DocumentInterface[] = [
      { id: "doc-1", pageContent: "DocumentDB", metadata: { category: "database" } },
    ];

    await expect(store.addDocuments(documents)).resolves.toEqual(["doc-1"]);
    expect(embeddings.embedDocuments).toHaveBeenCalledWith(["DocumentDB"]);
    expect(bulkWrite).toHaveBeenCalledWith(
      [
        {
          updateOne: {
            filter: { _id: "doc-1" },
            update: {
              $set: {
                text: "DocumentDB",
                embedding: [1, 0, 0],
                metadata: { category: "database" },
              },
            },
            upsert: true,
          },
        },
      ],
      { ordered: false },
    );
  });

  it("maps scored search results to LangChain documents", async () => {
    const { aggregateToArray, collection, store } = createHarness();
    aggregateToArray.mockResolvedValue([
      {
        similarityScore: 0.98,
        document: {
          _id: "doc-1",
          text: "DocumentDB",
          embedding: [1, 0, 0],
          metadata: { category: "database" },
        },
      },
    ]);

    const results = await store.similaritySearchVectorWithScore([1, 0, 0], 1, {
      "metadata.category": "database",
    });

    expect(collection.aggregate).toHaveBeenCalledOnce();
    expect(results[0][0]).toMatchObject({
      id: "doc-1",
      pageContent: "DocumentDB",
      metadata: { category: "database" },
    });
    expect(results[0][1]).toBe(0.98);
  });

  it("requires an explicit delete scope", async () => {
    const { deleteMany, store } = createHarness();

    await expect(store.delete()).rejects.toThrow("delete requires ids or a filter");
    await expect(store.delete({ filter: {} })).rejects.toThrow("delete filter must not be empty");
    await store.delete({ ids: ["one", "two"] });
    expect(deleteMany).toHaveBeenCalledWith({ _id: { $in: ["one", "two"] } });
    await store.delete({ filter: { "metadata.category": "expired" } });
    expect(deleteMany).toHaveBeenLastCalledWith({ "metadata.category": "expired" });
    await store.delete({ ids: [] });
    expect(deleteMany).toHaveBeenCalledTimes(2);
  });

  it("creates a missing collection and vector index", async () => {
    const { command, createCollection, store } = createHarness();

    await store.ensureCollectionExists();

    expect(createCollection).toHaveBeenCalledWith("documents");
    expect(command).toHaveBeenCalledWith(expect.objectContaining({ createIndexes: "documents" }));
  });

  it("does not recreate an existing collection or index", async () => {
    const { command, createCollection, hasNext, listIndexesToArray, store } = createHarness();
    hasNext.mockResolvedValue(true);
    listIndexesToArray.mockResolvedValue([{ name: "embedding_vector" }]);

    await store.ensureCollectionExists();

    expect(createCollection).not.toHaveBeenCalled();
    expect(command).not.toHaveBeenCalled();
  });

  it("tolerates concurrent collection creation", async () => {
    const { command, createCollection, store } = createHarness();
    createCollection.mockRejectedValueOnce({ code: 48 });

    await expect(store.ensureCollectionExists()).resolves.toBeUndefined();
    expect(command).toHaveBeenCalledOnce();

    createCollection.mockRejectedValueOnce(new Error("permission denied"));
    await expect(store.ensureCollectionExists()).rejects.toThrow("permission denied");
  });

  it("treats a missing collection as already deleted", async () => {
    const { drop, store } = createHarness();
    drop.mockRejectedValueOnce({ code: 26 });
    await expect(store.ensureCollectionDeleted()).resolves.toBeUndefined();

    drop.mockRejectedValueOnce(new Error("permission denied"));
    await expect(store.ensureCollectionDeleted()).rejects.toThrow("permission denied");
  });

  it("rejects mismatched inputs before writing", async () => {
    const { bulkWrite, store } = createHarness();
    const document = { pageContent: "DocumentDB", metadata: {} };

    await expect(store.addVectors([], [document])).rejects.toThrow(RangeError);
    await expect(store.addVectors([[1, 0]], [document])).rejects.toThrow(RangeError);
    await expect(store.addVectors([[1, 0, 0]], [document], { ids: [] })).rejects.toThrow(
      RangeError,
    );
    expect(bulkWrite).not.toHaveBeenCalled();
  });

  it("handles empty batches and caller-provided IDs", async () => {
    const { bulkWrite, store } = createHarness();
    await expect(store.addVectors([], [])).resolves.toEqual([]);
    await expect(
      store.addVectors([[1, 0, 0]], [{ pageContent: "DocumentDB", metadata: {} }], {
        ids: ["explicit-id"],
      }),
    ).resolves.toEqual(["explicit-id"]);
    expect(bulkWrite).toHaveBeenCalledOnce();
    expect(store._vectorstoreType()).toBe("documentdb");
  });

  it.each([
    { text: 42, metadata: {} },
    { text: "DocumentDB", metadata: [] },
  ])("rejects malformed stored documents", async ({ text, metadata }) => {
    const { aggregateToArray, store } = createHarness();
    aggregateToArray.mockResolvedValue([
      {
        similarityScore: 1,
        document: { _id: "doc-1", text, embedding: [1, 0, 0], metadata },
      },
    ]);

    await expect(store.similaritySearchVectorWithScore([1, 0, 0], 1)).rejects.toThrow(TypeError);
  });
});
