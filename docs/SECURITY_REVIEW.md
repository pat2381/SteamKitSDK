# Security Review

Status: October 16, 2026, version 0.9.3.

## Checks performed

- Locked restore, Release build with warnings treated as errors, .NET analyzers enabled, and a complete test run.
- NuGet vulnerability scan including transitive dependencies.
- Search for common secret markers and a manual review of authentication, download, and file-system paths.
- NuGet package build.

## Result

At the time of review, no known vulnerable NuGet packages were found. The analyzer build produced no warnings or errors. The test suite covers downloads through a local HTTP test server, cancellation behavior, file writes, client lifetime, unsupported legacy manifests, and security regressions in path resolution and error messages. A search for common secret markers in the source tree also found no results.

This review resolved the following findings:

1. Download filenames are canonicalized before writing and cannot escape the configured destination directory. This protects against `..` and absolute paths from manifests or legacy Workshop metadata.
2. `SteamInvalidBranchPasswordException` no longer includes a branch password in its error message.

## Residual risks and operating rules

- `ConsoleSteamAuthenticator` is an interactive sample implementation. When configured with a `persistenceDirectory`, it stores access tokens and guard data as plaintext. Production applications must use a dedicated `SteamAuthenticator` implementation with a secret store. Windows desktop applications can use the DPAPI-backed store in `SteamKitSDK.Windows`.
- Automated tests against real Steam accounts, licenses, or Workshop content are intentionally not part of CI. They require credentials and paid or external resources. Perform a manual smoke test with a dedicated test account before production use.
- Workshop URLs and Steam CDN endpoints are external data sources. The SDK validates destination paths but cannot guarantee entitlement or the availability of external Steam services.
- Never log credentials, access tokens, guard data, or branch passwords; never commit them to source control or pass them as command-line arguments.
