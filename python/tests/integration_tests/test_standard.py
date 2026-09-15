from __future__ import annotations

import logging
import os
from collections.abc import Generator
from uuid import uuid4

import pytest
from langchain_core.vectorstores import VectorStore
from langchain_tests.integration_tests import VectorStoreIntegrationTests
from pymongo import MongoClient
from pymongo.auth_oidc import OIDCCallback, OIDCCallbackContext, OIDCCallbackResult

from langchain_documentdb import DocumentDBVectorStore, VectorIndexKind

pytestmark = [
    pytest.mark.integration,
    pytest.mark.skipif(
        os.getenv("DOCUMENTDB_RUN_INTEGRATION_TESTS") != "1",
        reason="Set DOCUMENTDB_RUN_INTEGRATION_TESTS=1 to run live tests",
    ),
]

_DOCUMENTDB_SCOPE = "https://ossrdbms-aad.database.windows.net/.default"


class AzureIdentityTokenCallback(OIDCCallback):
    def __init__(self, access_token: str) -> None:
        self._access_token = access_token

    def fetch(self, context: OIDCCallbackContext) -> OIDCCallbackResult:
        del context
        return OIDCCallbackResult(access_token=self._access_token)


class TestDocumentDBVectorStore(VectorStoreIntegrationTests):
    @property
    def has_async(self) -> bool:
        return False

    @pytest.fixture
    def vectorstore(self) -> Generator[VectorStore, None, None]:
        database_name = os.environ["DOCUMENTDB_DATABASE"]
        collection_prefix = os.getenv("DOCUMENTDB_COLLECTION", "langchain_tests")
        collection_name = f"{collection_prefix}_{uuid4().hex}"
        index_name = os.getenv("DOCUMENTDB_VECTOR_INDEX", "vectorSearchIndex")
        if os.getenv("DOCUMENTDB_AUTH_MODE") == "entra":
            endpoint = os.environ["DOCUMENTDB_ENDPOINT"]
            access_token = os.environ["DOCUMENTDB_OIDC_TOKEN"]
            client: MongoClient[dict[str, object]] = MongoClient(
                endpoint,
                tls=True,
                retryWrites=False,
                maxIdleTimeMS=120_000,
                authMechanism="MONGODB-OIDC",
                authMechanismProperties={
                    "OIDC_CALLBACK": AzureIdentityTokenCallback(access_token)
                },
            )
        else:
            connection_string = os.environ["DOCUMENTDB_CONNECTION_STRING"]
            logging.getLogger(__name__).warning(
                "INTEGRATION TEST ONLY: using a connection string from the environment"
            )
            client = MongoClient(connection_string)
        database = client[database_name]
        database.create_collection(collection_name)
        collection = database[collection_name]
        store = DocumentDBVectorStore(
            collection,
            self.get_embeddings(),
            index_name=index_name,
        )
        store.create_vector_index(6, kind=VectorIndexKind.IVF)

        try:
            yield store
        finally:
            database.drop_collection(collection_name)
            client.close()
