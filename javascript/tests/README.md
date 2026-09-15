# JavaScript and TypeScript tests

`commands.test.ts` and `vectorstore.test.ts` are deterministic unit tests. `integration.test.ts` is
a live DocumentDB test gated by `DOCUMENTDB_RUN_INTEGRATION_TESTS=1`. See
[`../../docs/testing.md`](../../docs/testing.md) for shared safety requirements.

Run the suite from `javascript/` with `npm test` or collect coverage with `npm run test:coverage`.
