import type { Document, Filter } from "mongodb";
import type { DocumentDBStoredDocument, ResolvedDocumentDBVectorStoreConfig } from "./options.js";

export function createVectorIndexCommand(config: ResolvedDocumentDBVectorStoreConfig): Document {
  const cosmosSearchOptions: Document = {
    kind: `vector-${config.indexKind}`,
    dimensions: config.dimensions,
    similarity: config.similarity,
  };

  if (config.indexKind === "ivf") {
    cosmosSearchOptions.numLists = config.ivfNumLists;
  } else if (config.indexKind === "hnsw") {
    cosmosSearchOptions.m = config.hnswM;
    cosmosSearchOptions.efConstruction = config.hnswEfConstruction;
  } else {
    cosmosSearchOptions.maxDegree = config.diskAnnMaxDegree;
    cosmosSearchOptions.lBuild = config.diskAnnBuildCandidates;
  }

  return {
    createIndexes: config.collection.collectionName,
    indexes: [
      {
        name: config.indexName,
        key: { [config.embeddingKey]: "cosmosSearch" },
        cosmosSearchOptions,
      },
    ],
  };
}

export function createSearchPipeline(
  config: ResolvedDocumentDBVectorStoreConfig,
  query: number[],
  k: number,
  filter?: Filter<DocumentDBStoredDocument>,
): Document[] {
  validateVector(query, config.dimensions, "query");
  if (!Number.isInteger(k) || k < 1) {
    throw new RangeError("k must be a positive integer");
  }

  const cosmosSearch: Document = {
    vector: query,
    path: config.embeddingKey,
    k,
  };
  if (filter && Object.keys(filter).length > 0) {
    cosmosSearch.filter = filter;
  }

  if (config.indexKind === "ivf") {
    cosmosSearch.nProbes = config.ivfNumProbes;
  } else if (config.indexKind === "hnsw") {
    cosmosSearch.efSearch = config.hnswEfSearch;
  } else {
    cosmosSearch.lSearch = config.diskAnnSearchCandidates;
  }

  return [
    {
      $search: {
        cosmosSearch,
        returnStoredSource: true,
      },
    },
    {
      $project: {
        similarityScore: { $meta: "searchScore" },
        document: "$$ROOT",
      },
    },
    { $limit: k },
  ];
}

export function validateVector(vector: number[], dimensions: number, name: string): void {
  if (vector.length !== dimensions) {
    throw new RangeError(`${name} must contain exactly ${dimensions} values`);
  }
  if (vector.some((value) => !Number.isFinite(value))) {
    throw new TypeError(`${name} must contain only finite numbers`);
  }
}
