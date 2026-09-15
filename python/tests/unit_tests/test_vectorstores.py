from typing import Any, cast
from unittest.mock import MagicMock

import pytest
from langchain_core.documents import Document
from langchain_core.embeddings import Embeddings
from pymongo.collection import Collection

from langchain_documentdb import (
    DocumentDBVectorStore,
    VectorIndexKind,
    VectorSimilarity,
)


class FakeEmbeddings(Embeddings):
    def __init__(self) -> None:
        self.document_calls: list[list[str]] = []
        self.query_calls: list[str] = []

    def embed_documents(self, texts: list[str]) -> list[list[float]]:
        self.document_calls.append(texts)
        return [[float(len(text)), 1.0] for text in texts]

    def embed_query(self, text: str) -> list[float]:
        self.query_calls.append(text)
        return [float(len(text)), 1.0]


@pytest.fixture
def store_parts() -> tuple[DocumentDBVectorStore, MagicMock, FakeEmbeddings]:
    collection = MagicMock()
    collection.name = "documents"
    embeddings = FakeEmbeddings()
    store = DocumentDBVectorStore(
        cast("Collection[dict[str, Any]]", collection), embeddings
    )
    return store, collection, embeddings


def _result(
    item_id: str,
    text: str,
    embedding: list[float],
    score: float,
    metadata: dict[str, Any] | None = None,
) -> dict[str, Any]:
    return {
        "similarityScore": score,
        "document": {
            "_id": item_id,
            "text": text,
            "embedding": embedding,
            "metadata": metadata or {},
        },
    }


def test_add_texts_inserts_generated_ids_without_mutating_metadata(
    store_parts: tuple[DocumentDBVectorStore, MagicMock, FakeEmbeddings],
) -> None:
    store, collection, embeddings = store_parts
    metadata = [{"topic": "database"}]

    ids = store.add_texts(["DocumentDB"], metadata)

    assert len(ids) == 1
    assert metadata == [{"topic": "database"}]
    assert embeddings.document_calls == [["DocumentDB"]]
    inserted = collection.insert_many.call_args.args[0]
    assert inserted == [
        {
            "_id": ids[0],
            "text": "DocumentDB",
            "embedding": [10.0, 1.0],
            "metadata": {"topic": "database"},
        }
    ]


def test_add_texts_upserts_explicit_and_missing_document_ids(
    store_parts: tuple[DocumentDBVectorStore, MagicMock, FakeEmbeddings],
) -> None:
    store, collection, _ = store_parts

    ids = store.add_documents(
        [Document(page_content="one", id="fixed"), Document(page_content="two")]
    )

    assert ids[0] == "fixed"
    assert ids[1]
    operations = collection.bulk_write.call_args.args[0]
    assert len(operations) == 2
    assert operations[0]._filter == {"_id": "fixed"}
    assert operations[0]._doc["text"] == "one"
    assert operations[0]._upsert is True


def test_add_texts_handles_empty_and_rejects_bad_lengths(
    store_parts: tuple[DocumentDBVectorStore, MagicMock, FakeEmbeddings],
) -> None:
    store, collection, _ = store_parts

    assert store.add_texts([]) == []
    collection.insert_many.assert_not_called()
    with pytest.raises(ValueError, match="metadatas"):
        store.add_texts(["one"], [{}, {}])
    with pytest.raises(ValueError, match="ids"):
        store.add_texts(["one"], ids=["1", "2"])


def test_add_texts_rejects_wrong_embedding_count(
    store_parts: tuple[DocumentDBVectorStore, MagicMock, FakeEmbeddings],
) -> None:
    store, _, embeddings = store_parts
    embeddings.embed_documents = MagicMock(return_value=[])

    with pytest.raises(ValueError, match="unexpected number"):
        store.add_texts(["one"])


def test_from_texts_requires_collection_and_inserts() -> None:
    embeddings = FakeEmbeddings()
    with pytest.raises(ValueError, match="collection"):
        DocumentDBVectorStore.from_texts(["one"], embeddings)

    collection = MagicMock()
    store = DocumentDBVectorStore.from_texts(
        ["one"],
        embeddings,
        collection=cast("Collection[dict[str, Any]]", collection),
        ids=["1"],
    )

    assert isinstance(store, DocumentDBVectorStore)
    collection.bulk_write.assert_called_once()


def test_similarity_search_builds_documentdb_pipeline_and_maps_results(
    store_parts: tuple[DocumentDBVectorStore, MagicMock, FakeEmbeddings],
) -> None:
    store, collection, embeddings = store_parts
    result = _result("one", "first", [5.0, 1.0], 0.91, {"topic": "db"})
    result["document"]["__cosmos_meta__"] = {"score": 0.91}
    collection.aggregate.return_value = [result]
    post_filter = [{"$limit": 1}]

    results = store.similarity_search_with_score(
        "query",
        k=3,
        pre_filter={"metadata.active": {"$eq": True}},
        search_options={"lSearch": 80},
        post_filter_pipeline=post_filter,
        comment="unit-test",
    )

    assert embeddings.query_calls == ["query"]
    assert results == [
        (Document(id="one", page_content="first", metadata={"topic": "db"}), 0.91)
    ]
    collection.aggregate.assert_called_once_with(
        [
            {
                "$search": {
                    "cosmosSearch": {
                        "vector": [5.0, 1.0],
                        "path": "embedding",
                        "k": 3,
                        "filter": {"metadata.active": {"$eq": True}},
                        "lSearch": 80,
                    },
                    "returnStoredSource": True,
                }
            },
            {
                "$project": {
                    "similarityScore": {"$meta": "searchScore"},
                    "document": "$$ROOT",
                }
            },
            {"$limit": 1},
        ],
        comment="unit-test",
    )
    assert post_filter == [{"$limit": 1}]


def test_similarity_search_by_vector_and_relevance_threshold(
    store_parts: tuple[DocumentDBVectorStore, MagicMock, FakeEmbeddings],
) -> None:
    store, collection, _ = store_parts
    collection.aggregate.return_value = [
        _result("one", "first", [1.0, 0.0], 0.9),
        _result("two", "second", [0.0, 1.0], 0.4),
    ]

    documents = store.similarity_search_by_vector([1.0, 0.0], k=2)
    relevant = store.similarity_search_with_relevance_scores(
        "query", k=2, score_threshold=0.5
    )

    assert [document.id for document in documents] == ["one", "two"]
    assert [(document.id, score) for document, score in relevant] == [("one", 0.9)]


def test_search_validates_arguments_and_reserved_options(
    store_parts: tuple[DocumentDBVectorStore, MagicMock, FakeEmbeddings],
) -> None:
    store, _, _ = store_parts

    with pytest.raises(ValueError, match="k"):
        store.similarity_search_by_vector([1.0], k=0)
    with pytest.raises(ValueError, match="embedding"):
        store.similarity_search_by_vector([], k=1)
    with pytest.raises(ValueError, match="reserved fields"):
        store.similarity_search_by_vector(
            [1.0], search_options={"k": 100, "path": "other"}
        )


def test_max_marginal_relevance_selects_diverse_results(
    store_parts: tuple[DocumentDBVectorStore, MagicMock, FakeEmbeddings],
) -> None:
    store, collection, embeddings = store_parts
    collection.aggregate.return_value = [
        _result("near", "near", [1.0, 0.0], 1.0),
        _result("similar", "similar", [0.9, 0.1], 0.9),
        _result("diverse", "diverse", [0.0, 1.0], 0.5),
    ]

    documents = store.max_marginal_relevance_search(
        "q", k=2, fetch_k=3, lambda_mult=0.0
    )

    assert embeddings.query_calls == ["q"]
    assert [document.id for document in documents] == ["similar", "diverse"]
    with pytest.raises(ValueError, match="lambda_mult"):
        store.max_marginal_relevance_search_by_vector([1.0], lambda_mult=1.1)


def test_get_and_delete_by_ids(
    store_parts: tuple[DocumentDBVectorStore, MagicMock, FakeEmbeddings],
) -> None:
    store, collection, _ = store_parts
    collection.find.return_value = [
        {
            "_id": "one",
            "text": "first",
            "embedding": [1.0],
            "metadata": {"topic": "db"},
        }
    ]
    collection.delete_many.return_value.acknowledged = True

    assert store.get_by_ids([]) == []
    assert store.get_by_ids(["one"])[0] == Document(
        id="one", page_content="first", metadata={"topic": "db"}
    )
    collection.find.assert_called_once_with({"_id": {"$in": ["one"]}})
    assert store.delete() is None
    assert store.delete([]) is True
    assert store.delete(["one"], comment="test") is True
    collection.delete_many.assert_called_once_with(
        {"_id": {"$in": ["one"]}}, comment="test"
    )


def test_nested_fields_and_extra_stored_fields_are_preserved_as_metadata() -> None:
    collection = MagicMock()
    collection.find.return_value = [
        {
            "_id": "one",
            "content": {"text": "first", "vector": [1.0]},
            "attributes": {"topic": "db"},
            "tenant": "example",
        }
    ]
    store = DocumentDBVectorStore(
        cast("Collection[dict[str, Any]]", collection),
        FakeEmbeddings(),
        text_key="content.text",
        embedding_key="content.vector",
        metadata_key="attributes",
    )

    document = store.get_by_ids(["one"])[0]

    assert document.page_content == "first"
    assert document.metadata == {
        "topic": "db",
        "tenant": "example",
    }


def test_invalid_store_and_stored_documents_are_rejected(
    store_parts: tuple[DocumentDBVectorStore, MagicMock, FakeEmbeddings],
) -> None:
    store, collection, embeddings = store_parts
    with pytest.raises(ValueError, match="must not be empty"):
        DocumentDBVectorStore(
            cast("Collection[dict[str, Any]]", collection), embeddings, text_key=""
        )
    with pytest.raises(ValueError, match="distinct"):
        DocumentDBVectorStore(
            cast("Collection[dict[str, Any]]", collection),
            embeddings,
            text_key="same",
            embedding_key="same",
        )

    collection.find.return_value = [{"_id": "one", "metadata": {}}]
    with pytest.raises(ValueError, match="no string text"):
        store.get_by_ids(["one"])
    collection.find.return_value = [
        {"_id": "one", "text": "text", "metadata": "invalid"}
    ]
    with pytest.raises(ValueError, match="non-object metadata"):
        store.get_by_ids(["one"])


def test_store_delegates_vector_index_management(
    store_parts: tuple[DocumentDBVectorStore, MagicMock, FakeEmbeddings],
) -> None:
    store, collection, _ = store_parts
    collection.database.command.return_value = {"ok": 1}

    assert store.create_vector_index(
        3,
        kind=VectorIndexKind.IVF,
        similarity=VectorSimilarity.EUCLIDEAN,
        num_lists=2,
    ) == {"ok": 1}
    store.delete_vector_index()

    collection.database.command.assert_called_once()
    collection.drop_index.assert_called_once_with("vectorSearchIndex")
