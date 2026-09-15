"""DocumentDB vector-store workflow with caller-owned dependencies."""

from langchain_core.documents import Document
from langchain_core.embeddings import Embeddings
from pymongo.collection import Collection

from langchain_documentdb import (
    DocumentDBVectorStore,
    VectorIndexKind,
    VectorSimilarity,
)


def run(
    collection: Collection[dict[str, object]], embeddings: Embeddings
) -> list[Document]:
    """Create the index, write synthetic content, and run a filtered search."""
    store = DocumentDBVectorStore(collection, embeddings)
    store.create_vector_index(
        dimensions=1536,
        kind=VectorIndexKind.DISKANN,
        similarity=VectorSimilarity.COSINE,
        max_degree=32,
        l_build=50,
    )
    store.add_texts(
        texts=[
            "DocumentDB provides a MongoDB-compatible API.",
            "LangChain composes retrieval with language models.",
        ],
        metadatas=[
            {"category": "database", "published": True},
            {"category": "framework", "published": True},
        ],
        ids=["documentdb-overview", "langchain-overview"],
    )
    return store.similarity_search(
        "Which database works with MongoDB drivers?",
        k=4,
        pre_filter={"metadata.category": {"$eq": "database"}},
        search_options={"lSearch": 80},
    )