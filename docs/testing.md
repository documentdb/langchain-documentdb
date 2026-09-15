# Testing

## Unit tests

Every new behavior requires unit coverage. Unit tests must be deterministic,
must not contact external services, and should cover validation, document
mapping, query construction, filtering, score conversion, and error handling as
applicable. Follow the native test layout and naming conventions in each
language folder.

## Integration tests

Behavior that depends on database capabilities requires tests against a real,
compatible DocumentDB deployment. Integration tests must:

- be opt-in and separate from the default unit test command;
- create uniquely named, isolated test data;
- clean up collections and indexes they create;
- avoid assumptions about pre-existing data;
- never print connection strings or credentials; and
- document any required server capability or version.

Standard LangChain or ecosystem-specific integration suites should be used
where available.

The C# unit and gated integration tests run with:

```powershell
dotnet restore csharp/DocumentDB.LangChain.sln
dotnet test csharp/DocumentDB.LangChain.sln --no-restore
```

The JavaScript/TypeScript unit and gated integration tests run with:

```powershell
Set-Location javascript
npm ci
npm run typecheck
npm run test:coverage
```

The Java unit and gated integration tests run with:

```powershell
Set-Location java
.\mvnw.cmd spotless:check
.\mvnw.cmd verify
```

The Go unit and gated integration tests run with:

```powershell
Set-Location go
go fmt ./...
go vet ./...
go test -race -cover ./...
go build ./...
```

## Environment variables

Integration tests are expected to use the following environment variables. Do
not place real values in the repository.

```text
DOCUMENTDB_CONNECTION_STRING=<MongoDB-compatible DocumentDB connection string>
DOCUMENTDB_DATABASE=<isolated test database name>
DOCUMENTDB_COLLECTION=<isolated test collection name>
DOCUMENTDB_VECTOR_INDEX=<test vector index name>
DOCUMENTDB_RUN_INTEGRATION_TESTS=1
```

`DOCUMENTDB_RUN_INTEGRATION_TESTS` is an explicit safety switch; live tests stay
skipped unless it is exactly `1`. Connection strings are for explicitly
configured local or integration-test environments only. Production examples and
hosted-service guidance should use identity-based authentication when supported
by the target DocumentDB service and driver.

## DocumentDB Local E2E

The repository can deploy the official open-source DocumentDB Local image and
run all five live suites with one command:

```powershell
.\scripts\run-local-e2e.ps1
```

The runner binds DocumentDB to loopback port `10260`, generates disposable test
credentials, waits for the official readiness banner, and removes the container
and temporary Java truststore when it finishes. Use `-Port <port>` when `10260`
is occupied or `-KeepContainer` to leave the instance running for inspection.

The Python live suite currently reports 13 synchronous contracts passed and 12
asynchronous contracts skipped. The skips are intentional because the Python
provider does not yet implement native async database operations. This is a
documented provider gap rather than a DocumentDB Local failure; see the Python
package README for the current scope.

DocumentDB Local currently supports HNSW and IVFFlat vector indexes. The live
fixtures select IVF for local and managed-service portability; DiskANN command
generation remains covered by deterministic unit tests for Azure DocumentDB.
The local image uses a generated TLS certificate, so the runner applies each
driver's test-only local certificate option and imports that certificate into a
disposable Java truststore. These connection-string and certificate exceptions
must not be copied into production authentication paths.

Prerequisites are Docker Desktop plus the language dependencies documented
above. Install each repository package's dependencies before invoking the
runner. The image can be overridden when validating a release candidate:

```powershell
.\scripts\run-local-e2e.ps1 -Image ghcr.io/documentdb/documentdb/documentdb-local:<tag>
```

## Azure DocumentDB E2E

Use the Azure runner to validate all five integrations against an existing
Azure DocumentDB deployment. Use a dedicated integration-test database because
the suites create and delete uniquely named collections.

The preferred mode uses the active Azure CLI identity and MongoDB OIDC. The
cluster must allow Microsoft Entra ID authentication, and that identity must be
registered on the cluster with sufficient DocumentDB data-plane roles:

```powershell
az login
$env:DOCUMENTDB_DATABASE = "langchain_e2e"
.\scripts\run-azure-e2e.ps1 `
	-ConfirmLiveService `
	-AuthMode Entra `
	-ClusterName <cluster-name>
Remove-Item Env:DOCUMENTDB_DATABASE
```

The runner requests a short-lived token for
`https://ossrdbms-aad.database.windows.net/.default`, passes it only to the test
processes, and clears it afterward. Running this command locally validates a
developer's Entra identity. Validating an actual system-assigned or
user-assigned managed identity requires running the same OIDC client path on an
Azure compute resource carrying that identity.

Connection-string authentication remains an explicit E2E fallback. Enter the
value through a secure terminal prompt so it is not stored in shell history:

```powershell
$secureConnectionString = Read-Host "Azure DocumentDB connection string" -AsSecureString
$env:DOCUMENTDB_CONNECTION_STRING = [Net.NetworkCredential]::new("", $secureConnectionString).Password
$env:DOCUMENTDB_DATABASE = "langchain_e2e"
.\scripts\run-azure-e2e.ps1 -ConfirmLiveService
Remove-Item Env:DOCUMENTDB_CONNECTION_STRING, Env:DOCUMENTDB_DATABASE
```

The runner never accepts a connection string as a command-line parameter and
rejects options that disable TLS certificate validation. It preserves the
caller's test environment values. Connection strings are allowed only for this
explicit E2E fallback and emit warning logs; production applications must use
caller-owned clients configured with managed or workload identity.
