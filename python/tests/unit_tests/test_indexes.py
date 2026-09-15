from typing import Any, cast
from unittest.mock import MagicMock

import pytest
from pymongo.collection import Collection

from langchain_documentdb.indexes import (
    VectorIndexKind,
    VectorSimilarity,
    create_vector_index,
    delete_vector_index,
)


def _collection() -> tuple[Collection[dict[str, Any]], MagicMock]:
    mock = MagicMock()
    mock.name = "documents"
    mock.database.command.return_value = {"ok": 1}
    return cast("Collection[dict[str, Any]]", mock), mock


@pytest.mark.parametrize(
    ("kind", "kwargs", "expected"),
    [
        (VectorIndexKind.IVF, {"num_lists": 12}, {"numLists": 12}),
        (
            VectorIndexKind.HNSW,
            {"m": 20, "ef_construction": 80},
            {"m": 20, "efConstruction": 80},
        ),
        (
            VectorIndexKind.DISKANN,
            {"max_degree": 64, "l_build": 100},
            {"maxDegree": 64, "lBuild": 100},
        ),
    ],
)
def test_create_vector_index_builds_documentdb_command(
    kind: VectorIndexKind, kwargs: dict[str, int], expected: dict[str, int]
) -> None:
    collection, mock = _collection()

    response = create_vector_index(
        collection,
        index_name="vectors",
        embedding_key="content.vector",
        dimensions=1536,
        kind=kind,
        similarity=VectorSimilarity.COSINE,
        **kwargs,
    )

    assert response == {"ok": 1}
    options = {
        "kind": kind.value,
        "dimensions": 1536,
        "similarity": "COS",
        **expected,
    }
    mock.database.command.assert_called_once_with(
        {
            "createIndexes": "documents",
            "indexes": [
                {
                    "name": "vectors",
                    "key": {"content.vector": "cosmosSearch"},
                    "cosmosSearchOptions": options,
                }
            ],
        }
    )


@pytest.mark.parametrize(
    ("kwargs", "message"),
    [
        ({"index_name": ""}, "index_name"),
        ({"embedding_key": ""}, "embedding_key"),
        ({"dimensions": 0}, "dimensions"),
        ({"kind": VectorIndexKind.IVF, "num_lists": 0}, "num_lists"),
        ({"kind": VectorIndexKind.HNSW, "m": 1}, "HNSW"),
        (
            {"kind": VectorIndexKind.HNSW, "m": 20, "ef_construction": 30},
            "twice",
        ),
        ({"kind": VectorIndexKind.DISKANN, "max_degree": 19}, "max_degree"),
        ({"kind": VectorIndexKind.DISKANN, "l_build": 501}, "l_build"),
    ],
)
def test_create_vector_index_validates_options(
    kwargs: dict[str, Any], message: str
) -> None:
    collection, _ = _collection()
    arguments: dict[str, Any] = {
        "index_name": "vectors",
        "embedding_key": "embedding",
        "dimensions": 3,
        **kwargs,
    }

    with pytest.raises(ValueError, match=message):
        create_vector_index(collection, **arguments)


def test_delete_vector_index() -> None:
    collection, mock = _collection()

    delete_vector_index(collection, "vectors")

    mock.drop_index.assert_called_once_with("vectors")
    with pytest.raises(ValueError, match="index_name"):
        delete_vector_index(collection, "")
