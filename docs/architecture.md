# Architecture

## Separate DocumentDB integrations

DocumentDB is MongoDB-compatible, but it is an independently developed open
source document database with its own implementation, deployment models,
support boundaries, and release lifecycle. Separate LangChain integrations let
the project:

- test behavior directly against DocumentDB;
- document supported capabilities without implying complete MongoDB parity;
- evolve connection, indexing, and operational guidance with DocumentDB; and
- release fixes on a schedule owned by the DocumentDB community.

The integrations should follow established MongoDB LangChain concepts when
those concepts fit, while avoiding source copying or assumptions about
undocumented behavior.

## MongoDB compatibility expectations

DocumentDB aims to work with MongoDB applications, drivers, and tools. Each
language integration is therefore expected to use an appropriate
MongoDB-compatible driver unless a DocumentDB-specific client becomes the
preferred supported path.

Compatibility must be demonstrated by tests. Driver compatibility does not by
itself guarantee identical query semantics, vector index configuration,
filtering behavior, error handling, or performance. Differences must be
documented in the relevant language package.

## Vector store scope

The shared scope is vector store support: storing embedded
documents, running similarity searches, applying supported metadata filters,
and returning LangChain-native document types. Retriever support should build
on the vector store interfaces where the target ecosystem already provides
that composition.

Broader integrations such as chat history, loaders, caches, and graph features
are outside the current scope and should be proposed separately.

## Cross-language consistency

Language packages should align on core concepts where their host ecosystems
permit:

- clear collection and vector index configuration;
- explicit embedding field, text field, and metadata mapping;
- similarity search and score behavior;
- metadata filter documentation;
- consistent environment variable names for integration tests;
- equivalent example scenarios and terminology; and
- explicit capability and compatibility notes.

Consistency does not require identical APIs. Each package should follow the
idioms and standard interfaces of its LangChain ecosystem.
