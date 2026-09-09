# Go integration tests

`integration_test.go` exercises collection lifecycle and document writes against
a live DocumentDB deployment. It is skipped unless
`DOCUMENTDB_RUN_INTEGRATION_TESTS=1` and creates a uniquely named collection
that is deleted during cleanup.

The connection string accepted by this test is an explicit local testing
fallback only. Production applications must use managed or workload identity.
See [`../../docs/testing.md`](../../docs/testing.md) for the environment and
commands.
