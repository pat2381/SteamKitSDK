# Changelog

All notable changes are documented here.

## 0.9.1 - Package discovery release

- Refined NuGet package descriptions, authorship, project links, release notes, and discovery tags.
- Updated installation instructions to point to the published `SteamKitSDK` and `SteamKitSDK.Windows` packages.

## Unreleased

## 0.9.0 - Modernization release

- Renamed the solution, projects, assembly, package identifier, and public namespaces from `BytexDigital.Steam` to `SteamKitSDK`. Consumers must update their `using` statements and package reference.
- Added `SteamKitSDK.Windows`, an optional Windows desktop companion package with UI-agnostic Steam Guard dialogs and DPAPI-protected access-token and guard-data storage.
- Migrated the library and companion applications to .NET 10.
- Updated SteamKit2 to 3.4.0 and refreshed direct dependencies.
- Added locked dependency restores, CI package validation, test-result publishing, and code-coverage artifacts.
- Improved download cancellation, file-target lifecycle handling, and null-safety checks in core client paths.
- Added unit and loopback integration coverage for value objects, download handling, cancellation, and file targets.
- Retired the unsupported V1 encrypted depot-manifest decryptor. SteamKit2 3.4 no longer exposes the required helper; V2/current branch-password handling remains supported.
- Hardened download paths against traversal and removed branch passwords from exception messages.
- Changed the test client to read credentials from environment variables and disabled its plaintext token persistence by default.
- Improved download state handling, failure propagation, bounded chunk retries, CDN-pool cancellation, and memory usage of parallel downloads.
- Added bounded workshop-catalog pagination and a performance/reliability review.

## Upgrade notes

- .NET runtimes older than .NET 10 are no longer supported.
- Consumers that still rely on V1 encrypted manifest identifiers must migrate to Steam's current protected-branch manifest flow.
