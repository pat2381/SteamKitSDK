# Performance and Reliability Review

Status: October 15, 2026, version 0.9.0.

This review combines static analysis, tests, and an inspection of download, CDN, and authentication paths. It does not include a reproducible live benchmark against Steam because that requires an entitled Steam test account, real depots, and variable external CDN conditions.

## Implemented improvements

| Area | Finding | Implementation | Result |
| --- | --- | --- | --- |
| State model | `MultiDepotDownloadHandler` did not remain in the `Downloaded` state after a successful download. | The success state is set; errors set `Failed` and are propagated. | Callers can correctly handle progress and failures. |
| Error handling | Verification and download errors were swallowed in several locations. | Errors are rethrown after the state update. | No apparently successful but incomplete downloads. |
| Chunk retries | Network and file-write errors could retry indefinitely. | A maximum of five attempts per chunk; write errors include context. | No infinite loops and more diagnosable failures. |
| Logging | Retry information was invisible, and one log entry formatted strings too early. | A structured warning is emitted for each failed chunk attempt, with structured chunk count. | Queries by depot and attempt work in structured logging systems. |
| Memory | Every chunk created an extra complete delegate list, then the parallel routine copied it into an array again. | Parallel processing streams the `IEnumerable` and keeps at most `maxConcurrentDownloadsPerTask` tasks active. | Fewer temporary allocations for large games. |
| CDN cancellation | Waiting dequeues from the CDN pool did not react to its internal exhaustion. | Caller and pool cancellation tokens are linked. | Waiting downloads cancel correctly instead of blocking indefinitely. |
| Workshop catalogs | The raw query collected all pages in memory without a limit. | A new overload adds `maximumResults`, cursor protection, and cancellation checks. | Callers can bound memory consumption. |
| Depot selection | An empty filtered depot list was not detected. | A `Count == 0` check was added. | Immediate, clear error rather than an empty handler. |

## Logging and error handling

`DefaultDownloadHandler.Logger` and `SteamCdnServerPool.Logger` accept `Microsoft.Extensions.Logging.ILogger`. For useful logs, record at least app ID, depot ID, manifest ID, file count, downloaded bytes, retry count, and duration. Never include credentials, Steam Guard codes, refresh tokens, or branch passwords.

The existing events (`FileVerified`, `FileDownloaded`, `VerificationCompleted`, `DownloadComplete`) are suitable for UI progress. Do not use them for slow or blocking work because that can affect completion order.

## Recommended next improvements

The following items are deliberately not required in version 0.9.0 because they need larger public API or architectural changes:

1. **Asynchronous, bounded CDN pool** — Replace `BlockingCollection` and the additional `Task.Run` calls that start chunks with a `Channel<Server>`-based pool of unique servers. This reduces thread-pool blocking and prevents duplicate server entries while refilling.
2. **Streaming pipeline for manifests and chunks** — All `ChunkJob` objects are currently retained after verification. A producer/consumer pipeline would further reduce peak memory for very large depots, but requires a larger redesign because of file verification, progress reporting, and ordering guarantees.
3. **Configurable resilience options** — The five chunk attempts should become an options class with exponential backoff, jitter, and an overall timeout. The current fixed limit deliberately prevents infinite retry loops.
4. **Unified observability** — Supply `ILoggerFactory`, `ActivitySource`, and `Meter` centrally through `SteamClient` options. Useful metrics include active downloads, retry count, downloaded bytes, failures by category, CDN latency, and verification duration.
5. **App and branch metadata cache** — `GetDepotsAsync` can retrieve branch and product information repeatedly. A time-bounded, thread-safe cache would reduce API calls; invalidation and entitlement changes must be clearly defined.
6. **Secure token-storage extension point** — The demo authenticator intentionally remains simple. A cross-platform optional secret-store abstraction should be offered as a separate package in a major version.

## Measurement plan for a real load test

1. Use a dedicated Steam test account with one small, one medium, and one large entitled depot.
2. Measure `maxConcurrentDownloadsPerTask` at 2, 4, 8, 16, and 32. Higher parallelism is not automatically faster and can trigger CDN rate limits.
3. For every run, collect throughput, peak working set, GC pauses, open file handles, CDN error rate, retry count, and verification and download duration.
4. Repeat each configuration at least three times in the same network environment; compare the median and 95th percentile.
5. Keep the target directory before a repeat run to assess verification and resume performance separately.

## Validation of this change set

The Release build with .NET analyzers enabled produces no warnings or errors. The test suite includes state and error propagation in the multi-depot handler as well as Windows desktop-authentication tests. CI continues to validate packages, coverage, and known vulnerabilities.
