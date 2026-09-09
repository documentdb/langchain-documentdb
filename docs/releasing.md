# Releasing

No package in this repository is currently published. Release automation must
not be enabled until maintainers approve the package coordinates, registry
ownership, and release permissions for each ecosystem.

## Release checklist

1. Confirm the package version and compatibility policy.
2. Verify package metadata, license information, repository links, and included
   files.
3. Run all unit, formatting, linting, type, and build checks in CI.
4. Run the DocumentDB Local and Azure DocumentDB end-to-end suites described in
   [`testing.md`](testing.md).
5. Inspect the built wheel, npm package, JAR, NuGet package, or Go module tag to
   ensure it contains only intended source, binaries, documentation, and
   license files.
6. Record user-visible changes and known compatibility limitations.
7. Publish from a protected GitHub environment using registry trusted
   publishing or short-lived workload identity where supported.
8. Create a signed release tag and verify the package from its public registry.

Release credentials must not be stored in source, workflow files, or local
configuration committed to the repository. Prefer GitHub OIDC and registry
trusted publishing over long-lived tokens.
