# Modernization Plan

## 1. Dependencies and platform

- [x] Update SteamKit2 to 3.4.0 and adopt the required upstream changes.
- [x] Replace vulnerable package versions and introduce package lock files.
- [x] Migrate to .NET 10 and define the SDK centrally.

## 2. Reliability and API contracts

- [x] Replace outdated networking, cryptography, and serialization APIs.
- [x] Introduce null-safety for value objects, `SteamClient`, the CDN pool, and file targets.
- [x] Make the prioritized SteamKit adapters (`SteamContentClient`, download handlers) nullable-safe method by method.
- [x] Assess unimplemented legacy manifests and document them explicitly as unsupported when a secure current implementation is not possible.

## 3. Quality assurance

- [x] Introduce meaningful unit and integration tests.
- [x] Extend test coverage for `SteamContentClient` failure paths, downloads, and cancellation cases.
- [x] Add package validation and code-coverage artifacts to CI.

## 4. Package and documentation

- [x] Include the NuGet README in the package.
- [x] Update the README for .NET 10, supported download paths, and current limitations.
- [x] Define package version 0.9.0 and release notes for modernization breaking changes.
- [x] Add the optional `SteamKitSDK.Windows` package for Windows desktop authentication.

## 5. Completion

- [x] Validate locked restore, Release build, tests, vulnerability scan, and NuGet packages.

## Validation status

Successfully validated on October 15, 2026: locked restore, warning-free Release build, test suite with coverage generation, NuGet package build, and vulnerability scan with no known vulnerabilities.
