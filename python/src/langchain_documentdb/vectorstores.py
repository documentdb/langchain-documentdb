"""DocumentDB vector store for LangChain."""

from __future__ import annotations

from collections.abc import Callable, Iterable, Mapping, Sequence
from copy import deepcopy
from typing import Any
from uuid import uuid4

import numpy as np
from langchain_core.documents import Document
from langchain_core.embeddings import Embeddings
from langchain_core.vectorstores import VectorStore
from langchain_core.vectorstores.utils import maximal_marginal_relevance
from pymongo.collection import Collection
from pymongo.operations import ReplaceOne

from langchain_documentdb.indexes import (
    VectorIndexKind,
    VectorSimilarity,
    create_vector_index,
    delete_vector_index,
)

_RESERVED_SEARCH_OPTIONS = frozenset({"vector", "path", "k", "filter"})


def _get_path(document: Mapping[str, Any], path: str) -> Any:
    value: Any = document
    for part in path.split("."):
        if not isinstance(value, Mapping) or part not in value:
            raise KeyError(path)
        value = value[part]
    return value


def _set_path(document: dict[str, Any], path: str, value: Any) -> None:
    target = document
    parts = path.split(".")
    for part in parts[:-1]:
        child = target.setdefault(part, {})
        if not isinstance(child, dict):
            msg = f"Field paths overlap at {part!r}"
            raise ValueError(msg)
        target = child
    target[parts[-1]] = value


def _remove_path(document: dict[str, Any], path: str) -> Any:
    target = document
    parts = path.split(".")
    parents: list[tuple[dict[str, Any], str]] = []
    for part in parts[:-1]:
        child = target.get(part)
        if not isinstance(child, dict):
            return None
        parents.append((target, part))
        target = child
    value = target.pop(parts[-1], None)
    for parent, key in reversed(parents):
        if parent[key]:
            break
        parent.pop(key)
    return value


class DocumentDBVectorStore(VectorStore):
    """Store and retrieve LangChain documents using DocumentDB vector search."""

    def __init__(
        self,
        collection: Collection[dict[str, Any]],
        embedding: Embeddings,
        *,
        text_key: str = "text",
        embedding_key: str = "embedding",
        metadata_key: str = "metadata",
        index_name: str = "vectorSearchIndex",
        relevance_score_fn: Callable[[float], float] | None = None,
    ) -> None:
        """Initialize the store with a caller-configured PyMongo collection."""
        if not all((text_key, embedding_key, metadata_key, index_name)):
            msg = "Field keys and index_name must not be empty"
            raise ValueError(msg)
        if len({text_key, embedding_key, metadata_key}) != 3:
            msg = "text_key, embedding_key, and metadata_key must be distinct"
            raise ValueError(msg)

        self._collection = collection
        self._embedding = embedding
        self._text_key = text_key
        self._embedding_key = embedding_key
        self._metadata_key = metadata_key
        self._index_name = index_name
        self._relevance_score_fn = relevance_score_fn or (lambda score: score)

    @property
    def embeddings(self) -> Embeddings:
        """Return the embedding model used by the store."""
        return self._embedding

    def _select_relevance_score_fn(self) -> Callable[[float], float]:
        return self._relevance_score_fn

    def add_texts(
        self,
        texts: Iterable[str],
        metadatas: list[dict[str, Any]] | None = None,
        *,
        ids: list[str] | None = None,
        **kwargs: Any,
    ) -> list[str]:
        """Embed and insert texts into DocumentDB."""
        text_list = list(texts)
        if not text_list:
            return []

        if metadatas is None:
            metadata_list: list[dict[str, Any]] = [{} for _ in text_list]
        else:
            metadata_list = metadatas
            if len(metadata_list) != len(text_list):
                msg = "metadatas must have the same length as texts"
                raise ValueError(msg)

        if ids is None:
            id_list = [str(uuid4()) for _ in text_list]
        else:
            if len(ids) != len(text_list):
                msg = "ids must have the same length as texts"
                raise ValueError(msg)
            id_list = [item_id or str(uuid4()) for item_id in ids]

        vectors = self._embedding.embed_documents(text_list)
        if len(vectors) != len(text_list):
            msg = "The embedding model returned an unexpected number of vectors"
            raise ValueError(msg)

        documents: list[dict[str, Any]] = []
        for item_id, text, vector, metadata in zip(
            id_list, text_list, vectors, metadata_list, strict=True
        ):
            stored: dict[str, Any] = {"_id": item_id}
            _set_path(stored, self._text_key, text)
            _set_path(stored, self._embedding_key, vector)
            _set_path(stored, self._metadata_key, deepcopy(metadata))
            documents.append(stored)

        if ids is None:
            self._collection.insert_many(documents, **kwargs)
        else:
            operations = [
                ReplaceOne({"_id": document["_id"]}, document, upsert=True)
                for document in documents
            ]
            self._collection.bulk_write(operations, **kwargs)
        return id_list

    @classmethod
    def from_texts(
        cls,
        texts: list[str],
        embedding: Embeddings,
        metadatas: list[dict[str, Any]] | None = None,
        *,
        collection: Collection[dict[str, Any]] | None = None,
        ids: list[str] | None = None,
        **kwargs: Any,
    ) -> DocumentDBVectorStore:
        """Create a store and add texts to a provided collection."""
        if collection is None:
            msg = "collection is required"
            raise ValueError(msg)
        store = cls(collection, embedding, **kwargs)
        store.add_texts(texts, metadatas, ids=ids)
        return store

    def get_by_ids(self, ids: Sequence[str], /) -> list[Document]:
        """Get documents by string IDs."""
        if not ids:
            return []
        return [
            self._to_document(item)
            for item in self._collection.find({"_id": {"$in": list(ids)}})
        ]

    def delete(self, ids: list[str] | None = None, **kwargs: Any) -> bool | None:
        """Delete documents by ID without allowing an accidental full delete."""
        if ids is None:
            return None
        if not ids:
            return True
        result = self._collection.delete_many({"_id": {"$in": ids}}, **kwargs)
        return bool(result.acknowledged)

    def similarity_search(
        self, query: str, k: int = 4, **kwargs: Any
    ) -> list[Document]:
        """Return the documents most similar to a text query."""
        return [
            document
            for document, _ in self.similarity_search_with_score(query, k, **kwargs)
        ]

    def similarity_search_with_score(
        self, query: str, k: int = 4, **kwargs: Any
    ) -> list[tuple[Document, float]]:
        """Return documents and native DocumentDB search scores."""
        return self.similarity_search_by_vector_with_score(
            self._embedding.embed_query(query), k=k, **kwargs
        )

    def similarity_search_by_vector(
        self, embedding: list[float], k: int = 4, **kwargs: Any
    ) -> list[Document]:
        """Return documents most similar to an embedding vector."""
        return [
            document
            for document, _ in self.similarity_search_by_vector_with_score(
                embedding, k=k, **kwargs
            )
        ]

    def similarity_search_by_vector_with_score(
        self,
        embedding: list[float],
        k: int = 4,
        *,
        pre_filter: Mapping[str, Any] | None = None,
        search_options: Mapping[str, Any] | None = None,
        post_filter_pipeline: Sequence[Mapping[str, Any]] | None = None,
        **aggregate_kwargs: Any,
    ) -> list[tuple[Document, float]]:
        """Run a DocumentDB ``cosmosSearch`` aggregation."""
        results = self._search(
            embedding,
            k=k,
            pre_filter=pre_filter,
            search_options=search_options,
            post_filter_pipeline=post_filter_pipeline,
            **aggregate_kwargs,
        )
        return [(document, score) for document, score, _ in results]

    def max_marginal_relevance_search(
        self,
        query: str,
        k: int = 4,
        fetch_k: int = 20,
        lambda_mult: float = 0.5,
        **kwargs: Any,
    ) -> list[Document]:
        """Return diverse documents selected by maximal marginal relevance."""
        return self.max_marginal_relevance_search_by_vector(
            self._embedding.embed_query(query),
            k=k,
            fetch_k=fetch_k,
            lambda_mult=lambda_mult,
            **kwargs,
        )

    def max_marginal_relevance_search_by_vector(
        self,
        embedding: list[float],
        k: int = 4,
        fetch_k: int = 20,
        lambda_mult: float = 0.5,
        **kwargs: Any,
    ) -> list[Document]:
        """Return diverse documents selected from vector-search candidates."""
        if not 0 <= lambda_mult <= 1:
            msg = "lambda_mult must be between 0 and 1"
            raise ValueError(msg)
        results = self._search(embedding, k=fetch_k, **kwargs)
        indexes = maximal_marginal_relevance(
            np.asarray(embedding, dtype=np.float32),
            [stored_embedding for _, _, stored_embedding in results],
            lambda_mult=lambda_mult,
            k=k,
        )
        return [results[index][0] for index in indexes]

    def create_vector_index(
        self,
        dimensions: int,
        *,
        kind: VectorIndexKind = VectorIndexKind.DISKANN,
        similarity: VectorSimilarity = VectorSimilarity.COSINE,
        **kwargs: Any,
    ) -> dict[str, Any]:
        """Create the configured DocumentDB vector index."""
        return create_vector_index(
            self._collection,
            index_name=self._index_name,
            embedding_key=self._embedding_key,
            dimensions=dimensions,
            kind=kind,
            similarity=similarity,
            **kwargs,
        )

    def delete_vector_index(self) -> None:
        """Delete the configured DocumentDB vector index."""
        delete_vector_index(self._collection, self._index_name)

    def _search(
        self,
        embedding: list[float],
        k: int,
        *,
        pre_filter: Mapping[str, Any] | None = None,
        search_options: Mapping[str, Any] | None = None,
        post_filter_pipeline: Sequence[Mapping[str, Any]] | None = None,
        **aggregate_kwargs: Any,
    ) -> list[tuple[Document, float, list[float]]]:
        if k <= 0:
            msg = "k must be greater than zero"
            raise ValueError(msg)
        if not embedding:
            msg = "embedding must not be empty"
            raise ValueError(msg)

        cosmos_search: dict[str, Any] = {
            "vector": embedding,
            "path": self._embedding_key,
            "k": k,
        }
        if pre_filter is not None:
            cosmos_search["filter"] = dict(pre_filter)
        if search_options:
            conflicts = _RESERVED_SEARCH_OPTIONS.intersection(search_options)
            if conflicts:
                names = ", ".join(sorted(conflicts))
                msg = f"search_options cannot override reserved fields: {names}"
                raise ValueError(msg)
            cosmos_search.update(search_options)

        pipeline: list[dict[str, Any]] = [
            {
                "$search": {
                    "cosmosSearch": cosmos_search,
                    "returnStoredSource": True,
                }
            },
            {
                "$project": {
                    "similarityScore": {"$meta": "searchScore"},
                    "document": "$$ROOT",
                }
            },
        ]
        if post_filter_pipeline:
            pipeline.extend(deepcopy(dict(stage)) for stage in post_filter_pipeline)

        output: list[tuple[Document, float, list[float]]] = []
        for result in self._collection.aggregate(pipeline, **aggregate_kwargs):
            stored = deepcopy(result["document"])
            stored_embedding = _get_path(stored, self._embedding_key)
            output.append(
                (
                    self._to_document(stored),
                    float(result["similarityScore"]),
                    list(stored_embedding),
                )
            )
        return output

    def _to_document(self, stored: Mapping[str, Any]) -> Document:
        item = deepcopy(dict(stored))
        item_id = str(item.pop("_id"))
        item.pop("__cosmos_meta__", None)
        text = _remove_path(item, self._text_key)
        _remove_path(item, self._embedding_key)
        metadata = _remove_path(item, self._metadata_key)
        if not isinstance(text, str):
            msg = f"Stored document {item_id!r} has no string text field"
            raise ValueError(msg)
        if metadata is None:
            metadata = {}
        if not isinstance(metadata, dict):
            msg = f"Stored document {item_id!r} has a non-object metadata field"
            raise ValueError(msg)
        metadata.update(item)
        return Document(page_content=text, id=item_id, metadata=metadata)
