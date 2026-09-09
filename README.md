# DocumentDB LangChain integrations

This repository is the development home for DocumentDB integrations across the
LangChain ecosystem. It is organized as a multi-language monorepo so each
integration can follow the conventions and release lifecycle of its native
LangChain community.

DocumentDB is an open source document database designed to be compatible with
MongoDB applications, drivers, and tools. These integrations build on that
compatibility while presenting DocumentDB-specific packages, documentation, and
tests. See [DocumentDB](https://documentdb.io/) and the
[Azure DocumentDB documentation](https://learn.microsoft.com/en-us/azure/documentdb/)
for project and service details.

## Scope

The current implementation focus is vector store and retriever support. The
language packages align with established MongoDB LangChain integration
concepts where appropriate, without assuming that every behavior or capability
is identical.

## Repository layout

| Path                         | Ecosystem                                | Package/module                                                                                       |
| ---------------------------- | ---------------------------------------- | ---------------------------------------------------------------------------------------------------- |
| [`python/`](python/)         | Python / LangChain                       | `langchain-documentdb`                                                                               |
| [`javascript/`](javascript/) | JavaScript and TypeScript / LangChain.js | `@documentdb/langchain-documentdb`                                                                   |
| [`go/`](go/)                 | Go / langchaingo                         | `github.com/documentdb/langchain-documentdb/go`                                                      |
| [`java/`](java/)             | Java / LangChain4j                       | `io.documentdb:langchain4j-documentdb`                                                               |
| [`csharp/`](csharp/)         | .NET                                     | `DocumentDB.LangChain`                                                                               |
| [`docs/`](docs/)             | Shared guidance                          | [Architecture](docs/architecture.md), [testing](docs/testing.md), and [releasing](docs/releasing.md) |
| [`examples/`](examples/)     | Cross-language examples                  | [Example index](examples/README.md)                                                                  |

## Implementation status

- Python: LangChain vector store with synchronous search, filtering, MMR,
  retriever composition, and index management.
- JavaScript/TypeScript: LangChain.js vector store with typed document,
  search, deletion, and index lifecycle APIs.
- Go: langchaingo vector store with document and direct-vector APIs.
- Java: LangChain4j `EmbeddingStore` implementation.
- .NET: Microsoft.Extensions.VectorData vector store implementation.

Each package README documents its supported operations and current limitations.

## Package status

**No packages from this repository are published yet.** All implementations
are pre-release, and their public APIs may change before the first stable
release. The coordinates above are the names declared by the package manifests,
and release status does not change those names.

Complete workflows for every implementation are available in
[`examples/`](examples/). They use caller-owned authenticated database clients
and do not contain credentials or production connection-string paths.

Run every live integration suite against the official open-source DocumentDB
Local container with `./scripts/run-local-e2e.ps1`. See
[`docs/testing.md`](docs/testing.md) for prerequisites, TLS handling, and image
version overrides. The same guide documents validation against an existing
Azure DocumentDB deployment with `./scripts/run-azure-e2e.ps1`, including the
preferred passwordless Microsoft Entra ID workflow.

## Contributing

Contributions are welcome. Read [`CONTRIBUTING.md`](CONTRIBUTING.md) for
development, testing, and pull-request expectations. Participation is governed
by the [`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md), and vulnerabilities must be
reported according to [`SECURITY.md`](SECURITY.md).

## License

This project is licensed under the [MIT License](LICENSE).
