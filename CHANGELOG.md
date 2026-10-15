# Changelog

All notable changes are documented here.

## 0.9.0 - Modernization release

- Migrated the library and companion applications to .NET 10.
- Updated SteamKit2 to 3.4.0 and refreshed direct dependencies.
- Added locked dependency restores, CI package validation, test-result publishing, and code-coverage artifacts.
- Improved download cancellation, file-target lifecycle handling, and null-safety checks in core client paths.
- Added unit and loopback integration coverage for value objects, download handling, cancellation, and file targets.
- Retired the unsupported V1 encrypted depot-manifest decryptor. SteamKit2 3.4 no longer exposes the required helper; V2/current branch-password handling remains supported.

## Upgrade notes

- .NET runtimes older than .NET 10 are no longer supported.
- Consumers that still rely on V1 encrypted manifest identifiers must migrate to Steam's current protected-branch manifest flow.
