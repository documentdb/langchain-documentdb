# Examples

These examples exercise the same DocumentDB vector-store workflow in each
supported LangChain ecosystem. Each example:

- accepts an authenticated, caller-owned database object;
- accepts an embedding implementation or precomputed vectors;
- creates the collection and vector index when the API supports it;
- writes synthetic documents with stable IDs and metadata;
- performs a filtered similarity search; and
- leaves client and credential lifetime with the application.

| Ecosystem                 | Example                                                        | Provider documentation                               |
| ------------------------- | -------------------------------------------------------------- | ---------------------------------------------------- |
| Python / LangChain        | [`python/vector_store.py`](python/vector_store.py)             | [`../python/README.md`](../python/README.md)         |
| JavaScript / LangChain.js | [`javascript/vector-store.ts`](javascript/vector-store.ts)     | [`../javascript/README.md`](../javascript/README.md) |
| Java / LangChain4j        | [`java/VectorStoreExample.java`](java/VectorStoreExample.java) | [`../java/README.md`](../java/README.md)             |
| Go / langchaingo          | [`go/vector_store.go`](go/vector_store.go)                     | [`../go/README.md`](../go/README.md)                 |
| .NET / MEVA               | [`csharp/VectorStoreExample.cs`](csharp/VectorStoreExample.cs) | [`../csharp/README.md`](../csharp/README.md)         |

The examples deliberately do not construct database clients from connection
strings. Production applications should create the injected client with
managed or workload identity using the authentication flow supported by their
DocumentDB deployment. The live test suites document an explicitly gated
connection-string fallback for local and end-to-end testing only.

Package names and APIs are still pre-release. Build or install the corresponding
repository-local package before incorporating an example into an application.

## Validate the examples

The examples expose a `run` or `Run` function so the host application can
provide its authenticated client and embedding implementation. Validate the
checked-in source from the repository root with:

```powershell
python -m py_compile examples/python/vector_store.py

Set-Location javascript
npx tsc --project ../examples/javascript/tsconfig.json
Set-Location ..

dotnet build examples/csharp/VectorStoreExample.csproj

Set-Location go
go test ../examples/go/vector_store.go
Set-Location ..
```

The Java example uses the repository module's Java 17 and Maven dependencies.
Its API calls mirror the compiled usage in the Java test suite; copy the class
into an application that depends on the locally installed
`io.documentdb:langchain4j-documentdb:0.1.0-SNAPSHOT` artifact.
