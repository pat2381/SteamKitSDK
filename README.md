[![Build and test](https://github.com/pat2381/SteamKitSDK/actions/workflows/ci.yml/badge.svg)](https://github.com/pat2381/SteamKitSDK/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/SteamKitSDK.svg?style=flat-square)](https://www.nuget.org/packages/SteamKitSDK/)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg?style=flat-square)](LICENSE.md)

# SteamKitSDK

`SteamKitSDK` is a modern .NET 10 wrapper around [SteamKit2](https://github.com/SteamRE/SteamKit). It provides a focused, asynchronous API for downloading Steam application content and Workshop items, handling Steam Guard, querying branches, and validating local files before downloading them.

> This project was formerly named `BytexDigital.Steam`. Its namespaces and package identity are now `SteamKitSDK`.

## Features

- Download application content, individual depots, and Workshop items
- Verify local files against Steam manifests before downloading
- Report per-file and total download progress
- Authenticate with Steam Guard, including email codes and mobile confirmations
- Query public and password-protected branches
- Use cancellation tokens, structured logging, and async disposal throughout
- Try the functionality with the included interactive console test client

## Requirements

- .NET 10 SDK or runtime
- A Steam account that is entitled to the content you want to access
- A Steam Guard authenticator when Steam requests one

SteamKitSDK does not bypass Steam ownership, licensing, regional, or Workshop access restrictions.

## Installation

```powershell
dotnet add package SteamKitSDK --version 0.9.0
```

The package will be available from NuGet once it has been published. Until then, build it locally with `dotnet pack`.

## Quick start

The following example connects to Steam, verifies an application's files, and downloads missing or changed files. Replace the app ID and destination with values you are authorized to use.

```csharp
using SteamKitSDK.ContentDelivery;
using SteamKitSDK.ContentDelivery.Models.Downloading;
using SteamKitSDK.Core;

var credentials = new SteamCredentials(
    Environment.GetEnvironmentVariable("STEAM_USERNAME")!,
    Environment.GetEnvironmentVariable("STEAM_PASSWORD")!);

using var steamClient = new SteamClient(
    credentials,
    new ConsoleSteamAuthenticator(credentials.Username));
await using var contentClient = new SteamContentClient(steamClient);

await steamClient.ConnectAsync();

await using IDownloadHandler handler = await contentClient.GetAppDataAsync(
    appId: 440,
    branch: "public");

handler.FileDownloaded += (_, file) =>
    Console.WriteLine($"{handler.TotalProgress:P1} – {file.FileName}");

await handler.SetupAsync(@"D:\SteamDownloads\440", _ => true);
await handler.VerifyAsync();
await handler.DownloadAsync();

steamClient.Shutdown();
```

Do not put credentials, Steam Guard tokens, or branch passwords in source code. For long-running services, implement a custom `SteamAuthenticator` backed by the operating system's secret store.

## Interactive test client

The repository includes a console application for real-world validation. It asks for a Steam username, password, Steam Guard information when needed, application or Workshop ID, and an installation directory. Credentials remain only in process memory and are not logged.

```powershell
dotnet run --project SteamKitSDK.TestClient
```

For scripted use, run `dotnet run --project SteamKitSDK.TestClient -- --help`.

## Documentation

- [Complete usage guide](docs/USAGE.md) — game, depot, Workshop, Steam Guard, branches, progress, and error handling
- [Security review](docs/SECURITY_REVIEW.md) — security model and deployment guidance
- [Performance and reliability review](docs/PERFORMANCE_AND_RELIABILITY_REVIEW.md) — design and operational considerations
- [Modernization plan](MODERNIZATION_PLAN.md) — completed upgrade work and remaining release tasks
- [Change log](CHANGELOG.md)

## Development

The repository uses locked NuGet dependency restores. Build and test from the repository root:

```powershell
dotnet restore SteamKitSDK.sln --locked-mode
dotnet build SteamKitSDK.sln --configuration Release --no-restore
dotnet test SteamKitSDK.sln --configuration Release --no-build
dotnet pack SteamKitSDK/SteamKitSDK.csproj --configuration Release --no-build --no-restore --output artifacts
```

Generated output, IDE files, local test results, credentials, and test-client downloads are excluded through `.gitignore` and must not be committed.

## Compatibility and status

- Target framework: .NET 10
- SteamKit2: 3.4.0
- Package status: pre-1.0 (`0.9.0`); public APIs may change before 1.0
- Current encrypted V2 depot manifests are supported through Steam's branch-password flow. Legacy V1 encrypted manifest identifiers are intentionally not supported by SteamKit2 3.4.

## License

Licensed under the [Apache License 2.0](LICENSE.md).
