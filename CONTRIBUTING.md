# Contributing

Thank you for helping build DocumentDB integrations for the LangChain
ecosystem. Keep changes focused and follow the conventions of the language
package being modified.

## Development guidelines

- Limit a pull request to one language package where possible. Shared
  documentation or repository configuration may be updated with that package.
- Add or update tests for every behavioral change.
- Follow the patterns, formatting tools, and dependency conventions established
  in the language folder you modify.
- Do not commit secrets, credentials, certificates, tokens, or populated
  environment files.
- Use managed or workload identity for production authentication. Connection
  strings are permitted only for explicitly gated local and integration tests,
  must be supplied through environment variables or a secure prompt, and must
  never appear in source, fixtures, examples, or command history.
- Treat public API changes as compatibility decisions and explain them in the
  pull request.
- Do not add publishing or release automation without maintainer approval.

Each language folder contains its own setup and validation commands. Shared
integration-test requirements are documented in
[`docs/testing.md`](docs/testing.md).

## Before submitting a change

1. Run the unit tests and formatter for the language package you changed.
2. Run integration tests when behavior communicates with DocumentDB.
3. Document any new environment variables without including real values.
4. Update the package README when scope, dependencies, or test commands change.

## Developer Certificate of Origin

DocumentDB requires every commit to include a Developer Certificate of Origin
sign-off. Use your real identity and create signed-off commits with:

```text
git commit -s -m "Describe the change"
```

The sign-off certifies the
[Developer Certificate of Origin](https://developercertificate.org/).

## AI-assisted contributions

Contributors are responsible for every submitted change, including work created
with AI tools. Disclose substantial AI assistance in the pull request, verify
the result, and be prepared to explain and maintain it. Issues and pull requests
must describe behavior that the contributor has actually reproduced or tested.

## Pull requests

Describe the user-visible behavior, test coverage, and any compatibility or
security implications. Keep generated files, unrelated formatting changes, and
dependency updates outside the pull request unless they are required by the
change.

Participation in this project is governed by the
[`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md). Report vulnerabilities according to
[`SECURITY.md`](SECURITY.md).
