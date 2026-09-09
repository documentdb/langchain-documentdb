import type { Collection, Document as MongoDocument, Filter } from "mongodb";

export type DocumentDBVectorIndexKind = "ivf" | "hnsw" | "diskann";
export type DocumentDBSimilarity = "COS" | "L2" | "IP";

export interface DocumentDBStoredDocument extends MongoDocument {
  _id: string;
  [key: string]: unknown;
}

export interface DocumentDBVectorStoreConfig {
  collection: Collection<DocumentDBStoredDocument>;
  dimensions: number;
  indexName?: string;
  indexKind?: DocumentDBVectorIndexKind;
  similarity?: DocumentDBSimilarity;
  textKey?: string;
  embeddingKey?: string;
  metadataKey?: string;
  ivfNumLists?: number;
  ivfNumProbes?: number;
  hnswM?: number;
  hnswEfConstruction?: number;
  hnswEfSearch?: number;
  diskAnnMaxDegree?: number;
  diskAnnBuildCandidates?: number;
  diskAnnSearchCandidates?: number;
}

export interface DocumentDBDeleteOptions {
  ids?: string[];
  filter?: Filter<DocumentDBStoredDocument>;
}

export interface DocumentDBAddOptions {
  ids?: string[];
}

export interface ResolvedDocumentDBVectorStoreConfig {
  collection: Collection<DocumentDBStoredDocument>;
  dimensions: number;
  indexName: string;
  indexKind: DocumentDBVectorIndexKind;
  similarity: DocumentDBSimilarity;
  textKey: string;
  embeddingKey: string;
  metadataKey: string;
  ivfNumLists: number;
  ivfNumProbes: number;
  hnswM: number;
  hnswEfConstruction: number;
  hnswEfSearch: number;
  diskAnnMaxDegree: number;
  diskAnnBuildCandidates: number;
  diskAnnSearchCandidates: number;
}

const DEFAULTS = {
  indexName: "embedding_vector",
  indexKind: "diskann" as const,
  similarity: "COS" as const,
  textKey: "text",
  embeddingKey: "embedding",
  metadataKey: "metadata",
  ivfNumLists: 1,
  ivfNumProbes: 1,
  hnswM: 16,
  hnswEfConstruction: 64,
  hnswEfSearch: 40,
  diskAnnMaxDegree: 32,
  diskAnnBuildCandidates: 50,
  diskAnnSearchCandidates: 40,
};

export function resolveConfig(
  config: DocumentDBVectorStoreConfig,
): ResolvedDocumentDBVectorStoreConfig {
  if (!config.collection) {
    throw new TypeError("collection is required");
  }

  const resolved = { ...DEFAULTS, ...config };
  positiveInteger(resolved.dimensions, "dimensions");
  positiveInteger(resolved.ivfNumLists, "ivfNumLists");
  positiveInteger(resolved.ivfNumProbes, "ivfNumProbes");
  integerInRange(resolved.hnswM, 2, 100, "hnswM");
  integerInRange(resolved.hnswEfConstruction, 4, 1000, "hnswEfConstruction");
  if (resolved.hnswEfConstruction < 2 * resolved.hnswM) {
    throw new RangeError("hnswEfConstruction must be at least twice hnswM");
  }
  positiveInteger(resolved.hnswEfSearch, "hnswEfSearch");
  integerInRange(resolved.diskAnnMaxDegree, 20, 2048, "diskAnnMaxDegree");
  integerInRange(resolved.diskAnnBuildCandidates, 10, 500, "diskAnnBuildCandidates");
  integerInRange(resolved.diskAnnSearchCandidates, 10, 1000, "diskAnnSearchCandidates");

  for (const [name, value] of [
    ["indexName", resolved.indexName],
    ["textKey", resolved.textKey],
    ["embeddingKey", resolved.embeddingKey],
    ["metadataKey", resolved.metadataKey],
  ] as const) {
    if (!value.trim() || value.startsWith("$") || value.includes("\0")) {
      throw new TypeError(`${name} must be a non-empty safe field or index name`);
    }
  }

  return resolved;
}

function positiveInteger(value: number, name: string): void {
  integerInRange(value, 1, Number.MAX_SAFE_INTEGER, name);
}

function integerInRange(value: number, minimum: number, maximum: number, name: string): void {
  if (!Number.isInteger(value) || value < minimum || value > maximum) {
    throw new RangeError(`${name} must be an integer between ${minimum} and ${maximum}`);
  }
}
