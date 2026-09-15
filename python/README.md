# DocumentDB LangChain integration for Python

This package implements a LangChain vector store for DocumentDB's
MongoDB-compatible vector search. It supports document insertion and upsert,
similarity search, relevance thresholds, metadata prefilters, maximal marginal
relevance (MMR), ID retrieval and deletion, retrievers, and vector index
management.

The `langchain-documentdb` distribution exposes the `langchain_documentdb`
package. It is not published; install it from this repository while the API
remains alpha.

## Requirements

- Python 3.10 or newer
- `langchain-core` 1.x
- PyMongo 4.x
- A DocumentDB deployment with vector search support

## Development installation

From the `python` folder, create a virtual environment and install the package
with test dependencies:

```powershell
python -m venv .venv
.venv\Scripts\python.exe -m pip install -e ".[test]"
```

## Usage

The vector store accepts an existing PyMongo collection. Application code owns
client construction, authentication, connection lifetime, and collection
creation. Use the identity-based authentication mechanism supported by your
DocumentDB deployment; do not embed credentials or connection strings in code.

```python
from langchain_core.embeddings import Embeddings
from pymongo.collection import Collection

from langchain_documentdb import DocumentDBVectorStore


def create_store(
	collection: Collection[dict], embeddings: Embeddings
) -> DocumentDBVectorStore:
	return DocumentDBVectorStore(
		collection,
		embeddings,
		text_key="text",
		embedding_key="embedding",
		metadata_key="metadata",
		index_name="vectorSearchIndex",
	)
```

Add or replace documents using stable string IDs:

```python
ids = store.add_texts(
	["DocumentDB is MongoDB-compatible."],
	metadatas=[{"category": "database", "published": True}],
	ids=["documentdb-overview"],
)
```

Search with an optional DocumentDB prefilter and index-specific search options:

```python
results = store.similarity_search_with_score(
	"Which database works with MongoDB drivers?",
	k=4,
	pre_filter={"metadata.published": {"$eq": True}},
	search_options={"lSearch": 80},
)
```

`search_options` maps directly into `cosmosSearch`. Typical tuning parameters
are `nProbes` for IVF, `efSearch` for HNSW, and `lSearch` for DiskANN. Reserved
fields such as `vector`, `path`, `k`, and `filter` cannot be overridden.

Every LangChain vector store can also provide a retriever:

```python
retriever = store.as_retriever(
	search_type="mmr",
	search_kwargs={"k": 4, "fetch_k": 20, "lambda_mult": 0.5},
)
documents = retriever.invoke("DocumentDB vector search")
```

## Vector indexes

Create an IVF, HNSW, or DiskANN index with documented DocumentDB defaults and
validation:

```python
from langchain_documentdb import VectorIndexKind, VectorSimilarity

store.create_vector_index(
	dimensions=1536,
	kind=VectorIndexKind.DISKANN,
	similarity=VectorSimilarity.COSINE,
	max_degree=32,
	l_build=50,
)
```

Use `num_lists` with IVF, `m` and `ef_construction` with HNSW, or `max_degree`
and `l_build` with DiskANN. Metadata fields used by a prefilter also require an
appropriate standard DocumentDB index.

Delete the configured vector index with `store.delete_vector_index()`.

## Stored document shape

The default stored shape is:

```text
{
  "_id": "stable-string-id",
  "text": "original page content",
  "embedding": [0.1, 0.2, ...],
  "metadata": {"source": "example"}
}
```

The text, embedding, and metadata paths are configurable and support dotted
paths. Search results exclude the stored embedding from LangChain document
metadata. Additional top-level fields returned by DocumentDB are preserved as
metadata.

## Tests

Run deterministic unit tests, coverage, formatting, and typing checks:

```powershell
.venv\Scripts\python.exe -m pytest tests/unit_tests --cov=langchain_documentdb --cov-report=term-missing
.venv\Scripts\python.exe -m ruff check src tests
.venv\Scripts\python.exe -m mypy src
```

The live suite subclasses LangChain's standard `VectorStoreIntegrationTests`.
It creates and removes an isolated collection for every test. It is disabled by
default and runs only when `DOCUMENTDB_RUN_INTEGRATION_TESTS=1` and the
environment variables in [`../docs/testing.md`](../docs/testing.md) are set.

```powershell
$env:DOCUMENTDB_RUN_INTEGRATION_TESTS = "1"
.venv\Scripts\python.exe -m pytest tests/integration_tests
```

Connection-string authentication is accepted only by this explicit integration
test path and emits a warning. Production applications should inject a
collection configured with the identity-based authentication supported by their
DocumentDB environment.

## Known gaps

The Python provider currently implements synchronous vector-store operations
only. LangChain's standard integration suite therefore skips its 12 asynchronous
contracts. Native async support using PyMongo's `AsyncMongoClient` and
`AsyncCollection` is planned future work; applications should not treat the
current synchronous methods as non-blocking or call them directly on an event
loop.

## Example

See [`../examples/python/vector_store.py`](../examples/python/vector_store.py)
for a complete index, write, and filtered-search workflow using caller-owned
collection and embedding dependencies.
