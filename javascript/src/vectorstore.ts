import { randomUUID } from "node:crypto";
import { Document } from "@langchain/core/documents";
import type { DocumentInterface } from "@langchain/core/documents";
import type { EmbeddingsInterface } from "@langchain/core/embeddings";
import { VectorStore } from "@langchain/core/vectorstores";
import type { Filter, MongoServerError } from "mongodb";
import { createSearchPipeline, createVectorIndexCommand, validateVector } from "./commands.js";
import {
  type DocumentDBAddOptions,
  type DocumentDBDeleteOptions,
  type DocumentDBStoredDocument,
  type DocumentDBVectorStoreConfig,
  type ResolvedDocumentDBVectorStoreConfig,
  resolveConfig,
} from "./options.js";

interface SearchResult {
  similarityScore: number;
  document: DocumentDBStoredDocument;
}

export class DocumentDBVectorStore extends VectorStore {
  declare FilterType: Filter<DocumentDBStoredDocument>;
  readonly config: ResolvedDocumentDBVectorStoreConfig;

  constructor(embeddings: EmbeddingsInterface, config: DocumentDBVectorStoreConfig) {
    super(embeddings, {});
    this.config = resolveConfig(config);
  }

  _vectorstoreType(): string {
    return "documentdb";
  }

  async addDocuments(
    documents: DocumentInterface[],
    options?: DocumentDBAddOptions,
  ): Promise<string[]> {
    const vectors = await this.embeddings.embedDocuments(
      documents.map((document) => document.pageContent),
    );
    return this.addVectors(vectors, documents, options);
  }

  async addVectors(
    vectors: number[][],
    documents: DocumentInterface[],
    options?: DocumentDBAddOptions,
  ): Promise<string[]> {
    if (vectors.length !== documents.length) {
      throw new RangeError("vectors and documents must have the same length");
    }
    if (options?.ids && options.ids.length !== documents.length) {
      throw new RangeError("ids and documents must have the same length");
    }
    if (documents.length === 0) {
      return [];
    }

    const ids = documents.map(
      (document, index) => options?.ids?.[index] ?? document.id ?? randomUUID(),
    );
    const operations = documents.map((document, index) => {
      validateVector(vectors[index], this.config.dimensions, `vectors[${index}]`);
      const stored: DocumentDBStoredDocument = {
        _id: ids[index],
        [this.config.textKey]: document.pageContent,
        [this.config.embeddingKey]: vectors[index],
        [this.config.metadataKey]: document.metadata,
      };
      return {
        updateOne: {
          filter: { _id: ids[index] },
          update: {
            $set: {
              [this.config.textKey]: stored[this.config.textKey],
              [this.config.embeddingKey]: stored[this.config.embeddingKey],
              [this.config.metadataKey]: stored[this.config.metadataKey],
            },
          },
          upsert: true,
        },
      };
    });

    await this.config.collection.bulkWrite(operations, { ordered: false });
    return ids;
  }

  async similaritySearchVectorWithScore(
    query: number[],
    k: number,
    filter?: Filter<DocumentDBStoredDocument>,
  ): Promise<[Document, number][]> {
    const pipeline = createSearchPipeline(this.config, query, k, filter);
    const results = await this.config.collection.aggregate<SearchResult>(pipeline).toArray();
    return results.map((result) => [this.toDocument(result.document), result.similarityScore]);
  }

  override async delete(options?: DocumentDBDeleteOptions): Promise<void> {
    if (options?.ids) {
      if (options.ids.length === 0) {
        return;
      }
      await this.config.collection.deleteMany({ _id: { $in: options.ids } });
      return;
    }
    if (options?.filter) {
      if (Object.keys(options.filter).length === 0) {
        throw new TypeError("delete filter must not be empty");
      }
      await this.config.collection.deleteMany(options.filter);
      return;
    }
    throw new TypeError("delete requires ids or a filter");
  }

  async ensureCollectionExists(): Promise<void> {
    const exists = await this.config.collection.db
      .listCollections({ name: this.config.collection.collectionName }, { nameOnly: true })
      .hasNext();
    if (!exists) {
      try {
        await this.config.collection.db.createCollection(this.config.collection.collectionName);
      } catch (error) {
        if (!isMongoErrorCode(error, 48)) {
          throw error;
        }
      }
    }

    const indexes = await this.config.collection.listIndexes().toArray();
    if (!indexes.some((index) => index.name === this.config.indexName)) {
      await this.config.collection.db.command(createVectorIndexCommand(this.config));
    }
  }

  async ensureCollectionDeleted(): Promise<void> {
    try {
      await this.config.collection.drop();
    } catch (error) {
      if (!isNamespaceNotFound(error)) {
        throw error;
      }
    }
  }

  private toDocument(stored: DocumentDBStoredDocument): Document {
    const pageContent = stored[this.config.textKey];
    const metadata = stored[this.config.metadataKey];
    if (typeof pageContent !== "string") {
      throw new TypeError(`Stored field '${this.config.textKey}' must be a string`);
    }
    if (!metadata || typeof metadata !== "object" || Array.isArray(metadata)) {
      throw new TypeError(`Stored field '${this.config.metadataKey}' must be an object`);
    }
    return new Document({
      id: stored._id,
      pageContent,
      metadata: metadata as Record<string, unknown>,
    });
  }
}

function isNamespaceNotFound(error: unknown): error is MongoServerError {
  return isMongoErrorCode(error, 26);
}

function isMongoErrorCode(error: unknown, code: number): error is MongoServerError {
  return typeof error === "object" && error !== null && "code" in error && error.code === code;
}
