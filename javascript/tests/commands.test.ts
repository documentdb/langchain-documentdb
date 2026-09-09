import { describe, expect, it } from "vitest";
import type { Collection } from "mongodb";
import {
  createSearchPipeline,
  createVectorIndexCommand,
  type DocumentDBStoredDocument,
  type DocumentDBVectorIndexKind,
} from "../src/index.js";
import { resolveConfig } from "../src/options.js";

function config(indexKind: DocumentDBVectorIndexKind = "diskann") {
  return resolveConfig({
    collection: {
      collectionName: "documents",
    } as Collection<DocumentDBStoredDocument>,
    dimensions: 3,
    indexKind,
    embeddingKey: "vector",
  });
}

describe("DocumentDB command builders", () => {
  it.each([
    ["ivf", "vector-ivf", "numLists"],
    ["hnsw", "vector-hnsw", "efConstruction"],
    ["diskann", "vector-diskann", "lBuild"],
  ] as const)("builds a %s index command", (indexKind, kind, tuningField) => {
    const command = createVectorIndexCommand(config(indexKind));
    const index = command.indexes[0];

    expect(command.createIndexes).toBe("documents");
    expect(index.key).toEqual({ vector: "cosmosSearch" });
    expect(index.cosmosSearchOptions.kind).toBe(kind);
    expect(index.cosmosSearchOptions.dimensions).toBe(3);
    expect(index.cosmosSearchOptions).toHaveProperty(tuningField);
  });

  it.each([
    ["ivf", "nProbes"],
    ["hnsw", "efSearch"],
    ["diskann", "lSearch"],
  ] as const)("builds a %s search pipeline", (indexKind, tuningField) => {
    const pipeline = createSearchPipeline(config(indexKind), [1, 0, 0], 2, {
      "metadata.category": "database",
    });
    const search = pipeline[0].$search.cosmosSearch;

    expect(search).toMatchObject({
      vector: [1, 0, 0],
      path: "vector",
      k: 2,
      filter: { "metadata.category": "database" },
    });
    expect(search).toHaveProperty(tuningField);
    expect(pipeline.at(-1)).toEqual({ $limit: 2 });
  });

  it("rejects invalid vectors and result counts", () => {
    expect(() => createSearchPipeline(config(), [1, 0], 1)).toThrow(RangeError);
    expect(() => createSearchPipeline(config(), [1, Number.NaN, 0], 1)).toThrow(TypeError);
    expect(() => createSearchPipeline(config(), [1, 0, 0], 0)).toThrow(RangeError);
  });

  it("validates index tuning", () => {
    expect(() => resolveConfig({ ...config(), hnswM: 1 })).toThrow(RangeError);
    expect(() => resolveConfig({ ...config(), embeddingKey: "$vector" })).toThrow(TypeError);
  });
});
