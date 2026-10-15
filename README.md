![Nuget](https://img.shields.io/nuget/vpre/BytexDigital.Steam.svg?style=flat-square)

# BytexDigital.Steam

This library is a wrapper for the popular SteamKit2 library. It's primary goal is to simplify specific APIs of it to
make them easier to use.

As of now, this library's main purpose is to allow for simple content downloading (apps and workshop items) off Steam.

## Requirements

- .NET 10 SDK or runtime
- A Steam account and an authenticator for authenticated downloads; anonymous access is supported where Steam permits it
- SteamKit2 3.4.0 is included transitively by this package

## Development

The repository uses locked NuGet restores. Run the following before contributing:

```powershell
dotnet restore --locked-mode
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
```

## Compatibility notes

- The package now targets .NET 10 and uses SteamKit2 3.4.0.
- Current encrypted depot manifests (V2) use Steam's branch-password API. Legacy V1 encrypted manifest identifiers are intentionally rejected because the required SteamKit helper no longer exists in SteamKit2 3.4; applications should resolve protected branches through the current branch-password flow instead.
- This is a pre-1.0 release. Version 0.9.0 contains framework and dependency updates and therefore requires consumers to test their integration before upgrading.

> :warning: **Please note that this library is in early beta. APIs may change heavily between versions.**

## Download

[nuget package](https://www.nuget.org/packages/BytexDigital.Steam/)

### Simple item download usage example

See the [test client project](https://github.com/BytexDigital/BytexDigital.Steam/tree/master/BytexDigital.Steam.TestClient)

#### Filter which files to download

You can use a condition to limit your download to specific files.

```csharp
var downloadTask = downloadHandler.DownloadToFolderAsync(@".\downloads", x => x.FileName.EndsWith(".exe"));
```
