"""DocumentDB vector index definitions."""

from __future__ import annotations

from enum import Enum
from typing import Any

from pymongo.collection import Collection


class VectorIndexKind(str, Enum):
    """Vector index algorithms supported by DocumentDB."""

    IVF = "vector-ivf"
    HNSW = "vector-hnsw"
    DISKANN = "vector-diskann"


class VectorSimilarity(str, Enum):
    """Similarity metrics supported by DocumentDB vector indexes."""

    COSINE = "COS"
    EUCLIDEAN = "L2"
    INNER_PRODUCT = "IP"


def create_vector_index(
    collection: Collection[dict[str, Any]],
    *,
    index_name: str,
    embedding_key: str,
    dimensions: int,
    kind: VectorIndexKind = VectorIndexKind.DISKANN,
    similarity: VectorSimilarity = VectorSimilarity.COSINE,
    num_lists: int = 1,
    m: int = 16,
    ef_construction: int = 64,
    max_degree: int = 32,
    l_build: int = 50,
) -> dict[str, Any]:
    """Create a DocumentDB vector index and return the database response."""
    if not index_name:
        msg = "index_name must not be empty"
        raise ValueError(msg)
    if not embedding_key:
        msg = "embedding_key must not be empty"
        raise ValueError(msg)
    if dimensions <= 0:
        msg = "dimensions must be greater than zero"
        raise ValueError(msg)

    options: dict[str, Any] = {
        "kind": kind.value,
        "dimensions": dimensions,
        "similarity": similarity.value,
    }
    if kind is VectorIndexKind.IVF:
        if num_lists <= 0:
            msg = "num_lists must be greater than zero"
            raise ValueError(msg)
        options["numLists"] = num_lists
    elif kind is VectorIndexKind.HNSW:
        if not 2 <= m <= 100 or not 4 <= ef_construction <= 1000:
            msg = "HNSW requires 2 <= m <= 100 and 4 <= ef_construction <= 1000"
            raise ValueError(msg)
        if ef_construction < 2 * m:
            msg = "ef_construction must be at least twice m"
            raise ValueError(msg)
        options.update({"m": m, "efConstruction": ef_construction})
    else:
        if not 20 <= max_degree <= 2048:
            msg = "max_degree must be between 20 and 2048"
            raise ValueError(msg)
        if not 10 <= l_build <= 500:
            msg = "l_build must be between 10 and 500"
            raise ValueError(msg)
        options.update({"maxDegree": max_degree, "lBuild": l_build})

    command = {
        "createIndexes": collection.name,
        "indexes": [
            {
                "name": index_name,
                "key": {embedding_key: "cosmosSearch"},
                "cosmosSearchOptions": options,
            }
        ],
    }
    return collection.database.command(command)


def delete_vector_index(
    collection: Collection[dict[str, Any]], index_name: str
) -> None:
    """Delete a DocumentDB vector index by name."""
    if not index_name:
        msg = "index_name must not be empty"
        raise ValueError(msg)
    collection.drop_index(index_name)
