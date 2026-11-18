# Security Review

Status: November 18, 2026, working tree based on version 0.9.4.

## Working-tree review

| Area | Finding | Change |
| --- | --- | --- |
| Download destination | Canonical path containment alone allowed existing symbolic links or Windows reparse points to redirect file access. | Validate existing path components and reject rooted filenames, Windows alternate data streams and ambiguous Windows names. |
| Workshop HTTP | URLs supplied by Workshop metadata could drive outbound HTTP requests, and failed HTTP transfers could finish without propagating an error. | Require trusted HTTPS hosts for SDK-resolved Workshop URLs, disable automatic redirects, and return contextual HTTP errors without copying signed URLs into the diagnostic. |
| Local file integrity | Verification deleted mismatched files before downloading a replacement, and interrupted transfers could leave a partial file at the final path. | Write to a unique temporary file, validate the chunk layout and publish only after the file is complete. Replacement is per file. |
| Client lifecycle | Authenticator errors and failed callbacks could stop the background work while other calls kept waiting. A concurrent depot-key cache insertion could spin indefinitely. | Propagate failures, use SteamKit's asynchronous callback wait, bound connection and CDN discovery waits, and use atomic cache insertion. |
| Concurrency | Failed workers could leave other workers running during file cleanup. Concurrent calls on one handler could mutate the same operation state. | Await the parallel workers before cleanup. Reject overlapping operations and scope notifications to the relevant operation. |

The locked restore used cached packages. A current direct and transitive NuGet vulnerability check was attempted, but the NuGet feed could not be reached in this environment. The dependency-scan result below applies to the earlier review.

### Remaining boundaries

- Use a private download directory. Reparse-point checks examine the filesystem at the time of each check. Another local process with write access can race filesystem changes, and the SDK does not provide a transaction across an entire depot or installation.
- Directly constructing a `LegacyDownloadHandler` accepts the caller's URL and optionally their HTTP client. Apply your own outbound policy when this entry point receives untrusted input. Keep any additional SDK Workshop host allowlist limited to trusted endpoints.
- SteamKit2 3.5.0 exposes no caller cancellation token on its CDN manifest or chunk download methods. The SDK bounds its wait and discards abandoned chunk buffers. SteamKit2 controls the underlying requests and their own timeouts.
- Custom callbacks and event handlers run application code. Keep download observers quick and thread-safe and use asynchronous dispatch for UI updates. Use cancellable implementations for Steam Guard prompts.

## Checks performed for version 0.9.4

- Locked restore, Release build with warnings treated as errors, .NET analyzers enabled, and a complete test run.
- NuGet vulnerability scan including transitive dependencies.
- Search for common secret markers and a manual review of authentication, download, and file-system paths.
- NuGet package build.

## Result for version 0.9.4

At the time of review, no known vulnerable NuGet packages were found. The analyzer build produced no warnings or errors. The test suite covers downloads through a local HTTP test server, cancellation behavior, file writes, client lifetime, unsupported legacy manifests, and security regressions in path resolution and error messages. A search for common secret markers in the source tree also found no results.

This review resolved the following findings:

1. Download filenames are canonicalized before writing and cannot escape the configured destination directory. This protects against `..` and absolute paths from manifests or legacy Workshop metadata.
2. `SteamInvalidBranchPasswordException` no longer includes a branch password in its error message.

## Residual risks and operating rules

- `ConsoleSteamAuthenticator` is an interactive sample implementation. When configured with a `persistenceDirectory`, it stores access tokens and guard data as plaintext. Production applications must use a dedicated `SteamAuthenticator` implementation with a secret store. Windows desktop applications can use the DPAPI-backed store in `SteamKitSDK.Windows`.
- Automated tests against real Steam accounts, licenses, or Workshop content are intentionally not part of CI. They require credentials and paid or external resources. Perform a manual smoke test with a dedicated test account before production use.
- Workshop URLs and Steam CDN endpoints are external data sources. The SDK validates destination paths but cannot guarantee entitlement or the availability of external Steam services.
- Never log credentials, access tokens, guard data, or branch passwords; never commit them to source control or pass them as command-line arguments.
