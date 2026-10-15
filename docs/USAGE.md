# SteamKitSDK Usage Guide

This guide covers the APIs in version 0.9.0. The SDK communicates with Steam through SteamKit2. Use it only for content that the Steam account is entitled to access.

## Installation

```powershell
dotnet add package SteamKitSDK --version 0.9.0
```

The project requires .NET 10. A signed-in Steam account is required to download owned games or Workshop content.

## Download lifecycle

Every download follows the same sequence:

1. Create a `SteamClient` with credentials and an authenticator.
2. Connect with `ConnectAsync`.
3. Obtain an `IDownloadHandler` through `SteamContentClient`.
4. Configure the handler with `SetupAsync`.
5. Check local files against the manifest with `VerifyAsync`.
6. Download missing or changed files with `DownloadAsync`.
7. Shut down or dispose the handler, content client, and Steam client.

`VerifyAsync` is not optional: it moves a download handler into the valid state for `DownloadAsync`.

## Sign-in and Steam Guard

`ConsoleSteamAuthenticator` is intended for a local interactive console application. It prompts for an email or Steam Guard code when necessary, as well as confirmation in the Steam mobile app.

```csharp
using SteamKitSDK.Core;

var username = Environment.GetEnvironmentVariable("STEAM_USERNAME")
    ?? throw new InvalidOperationException("STEAM_USERNAME is not set.");
var password = Environment.GetEnvironmentVariable("STEAM_PASSWORD")
    ?? throw new InvalidOperationException("STEAM_PASSWORD is not set.");

var credentials = new SteamCredentials(username, password);

// Without a persistenceDirectory, no token is written to disk.
var authenticator = new ConsoleSteamAuthenticator(username);

using var steamClient = new SteamClient(credentials, authenticator);

steamClient.InternalClientConnected += () => Console.WriteLine("Connected to Steam.");
steamClient.InternalClientLoggedOn += () => Console.WriteLine("Sign-in completed.");
steamClient.InternalClientDisconnected += () => Console.WriteLine("Steam connection closed.");

await steamClient.ConnectAsync();
```

Environment variables keep the password out of source code and shell history. Production services should derive their own `SteamAuthenticator`. It implements the seven abstract callbacks for code entry, mobile confirmation, access tokens, and guard data, and stores the latter two only in an operating-system-backed secret store such as Windows Credential Manager, macOS Keychain, or Linux Secret Service.

> **Important:** When passed a `persistenceDirectory`, `ConsoleSteamAuthenticator` writes access tokens and guard data as files. Use this only on a protected local development machine, never on a server or shared device.

## WPF and Windows Forms

For desktop applications, use the optional `SteamKitSDK.Windows` package. It does not reference WPF or Windows Forms, so it can support either UI framework. `DesktopSteamAuthenticator` connects SDK sign-in to three UI callbacks; `DpapiSteamAuthenticationStore` encrypts access tokens and guard data with Windows DPAPI in the context of the signed-in Windows user.

```powershell
dotnet add package SteamKitSDK.Windows --version 0.9.0
```

Dialogs must be displayed on the application's UI thread: use `Dispatcher.InvokeAsync` in WPF and `Control.InvokeAsync` in Windows Forms. The following callbacks represent your own dialogs and must honor that threading requirement:

```csharp
using SteamKitSDK.Core;
using SteamKitSDK.Windows.Authentication;

var storageDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "MyApplication",
    "steam-auth");

var dialogs = new DelegatingSteamAuthenticationDialogService(
    requestTwoFactorCodeAsync: (wasIncorrect, cancellationToken) =>
        ShowTwoFactorDialogOnUiThreadAsync(wasIncorrect, cancellationToken),
    requestEmailCodeAsync: (emailDomain, wasIncorrect, cancellationToken) =>
        ShowEmailCodeDialogOnUiThreadAsync(emailDomain, wasIncorrect, cancellationToken),
    requestMobileConfirmationAsync: cancellationToken =>
        ShowMobileConfirmationDialogOnUiThreadAsync(cancellationToken));

var authenticator = new DesktopSteamAuthenticator(
    username,
    dialogs,
    new DpapiSteamAuthenticationStore(storageDirectory));

using var steamClient = new SteamClient(
    new SteamCredentials(username, password),
    authenticator);

await steamClient.ConnectAsync(cancellationToken);
```

`DpapiSteamAuthenticationStore` does not store passwords; it stores only access tokens and guard data issued by Steam. The files are bound to the current Windows user profile. Remove them when a user explicitly signs out:

```csharp
await new DpapiSteamAuthenticationStore(storageDirectory)
    .DeleteAsync(username, cancellationToken);
```

`SteamKitSDK.Windows` is not suitable for server-side or cross-platform applications. In those scenarios, provide an `ISteamAuthenticationStore` implementation backed by an appropriate secret store.

## Game or depot download

The following example downloads every accessible depot from an app's public branch. Replace `appId` with the Steam app ID of the game you are authorized to download.

```csharp
using SteamKitSDK.ContentDelivery;
using SteamKitSDK.ContentDelivery.Exceptions;
using SteamKitSDK.ContentDelivery.Models.Downloading;
using SteamKitSDK.Core;

const uint appId = 440; // Example: Team Fortress 2
var downloadDirectory = Path.Combine(AppContext.BaseDirectory, "downloads", appId.ToString());

using var steamClient = new SteamClient(
    new SteamCredentials(username, password),
    new ConsoleSteamAuthenticator(username));
await using var contentClient = new SteamContentClient(steamClient, maxConcurrentDownloadsPerTask: 8);

await steamClient.ConnectAsync(cancellationToken);

await using IDownloadHandler handler = await contentClient.GetAppDataAsync(
    appId,
    branch: "public",
    skipInaccessibleDepots: true,
    cancellationToken: cancellationToken);

handler.FileVerified += (_, result) =>
    Console.WriteLine($"Verified: {result.ManifestFile.FileName}; download required: {result.RequiresDownload}");
handler.FileDownloaded += (_, file) =>
    Console.WriteLine($"{handler.TotalProgress:P1} – {file.FileName}");
handler.VerificationCompleted += (_, result) =>
    Console.WriteLine($"{result.QueuedFiles.Count} files are queued for download.");
handler.DownloadComplete += (_, _) => Console.WriteLine("Download complete.");

await handler.SetupAsync(
    downloadDirectory,
    file => !file.FileName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase),
    cancellationToken);
await handler.VerifyAsync(cancellationToken);
await handler.DownloadAsync(cancellationToken);

steamClient.Shutdown();
```

The file filter is applied before verification. The SDK's internal path resolution prevents manifest names from escaping the configured download directory.

### A single depot or a known manifest

```csharp
// Download one depot from the public branch.
await using var depotHandler = await contentClient.GetAppDataAsync(
    appId: 440,
    depotId: 441,
    branch: "public",
    cancellationToken: cancellationToken);

await depotHandler.SetupAsync(downloadDirectory, _ => true, cancellationToken);
await depotHandler.VerifyAsync(cancellationToken);
await depotHandler.DownloadAsync(cancellationToken);

// Alternatively, use an already known manifest ID.
await using var manifestHandler = await contentClient.GetAppDataAsync(
    appId: 440,
    depotId: 441,
    manifestId: 12345678901234567890UL,
    cancellationToken: cancellationToken);
```

The manifest-ID overload is useful for reproducible builds. It does not bypass Steam authorization when retrieving the depot key.

### Password-protected branches

First query the available branches. Pass the password only to the API call; never write it to logs.

```csharp
var branches = await contentClient.GetBranchesAsync(appId, cancellationToken);
foreach (var branch in branches)
{
    Console.WriteLine($"{branch.Name}: password required = {branch.RequiresPassword}");
}

var branchPassword = GetBranchPasswordFromSecretStore();

await contentClient.ValidateBranchPasswordAsync(
    await contentClient.GetDepotsAsync(appId, "beta", cancellationToken: cancellationToken),
    branch: "beta",
    branchPassword: branchPassword);

await using var betaHandler = await contentClient.GetAppDataAsync(
    appId,
    branch: "beta",
    branchPassword: branchPassword,
    cancellationToken: cancellationToken);
```

Current V2 manifests are supported. Legacy V1 encrypted manifest IDs are rejected with `NotSupportedException` because SteamKit2 3.4 no longer provides the required helper.

## Downloading a Workshop item

A Workshop item is downloaded through its `PublishedFileId`. The SDK obtains metadata, license and access status, and the matching manifest for Steam CDN content automatically.

```csharp
using SteamKitSDK.ContentDelivery.Models.Downloading;

const ulong workshopItemId = 3417789720; // Replace with your own PublishedFileId
var workshopDirectory = Path.Combine(AppContext.BaseDirectory, "downloads", "workshop", workshopItemId.ToString());

var details = await contentClient.GetPublishedFileDetailsAsync(workshopItemId, cancellationToken);
Console.WriteLine($"Workshop title: {details.title}");

await using IDownloadHandler workshopHandler = await contentClient.GetPublishedFileDataAsync(
    workshopItemId,
    cancellationToken: cancellationToken);

workshopHandler.FileDownloaded += (_, file) =>
    Console.WriteLine($"{workshopHandler.TotalProgress:P1} – {file.FileName}");

await workshopHandler.SetupAsync(workshopDirectory, _ => true, cancellationToken);
await workshopHandler.VerifyAsync(cancellationToken);
await workshopHandler.DownloadAsync(cancellationToken);
```

Not every Workshop item is downloadable community content. In that case, the SDK throws `SteamPublishedFileIsNotDownloadableException`. Missing licenses or restricted access result in the corresponding access or license exceptions.

For Workshop catalogs, always set a maximum result count so that the query does not consume unbounded memory:

```csharp
var newestItems = await contentClient.GetPublishedFilesForAppIdRawAsync(
    appId,
    maximumResults: 250,
    cancellationToken);
```

## Cancellation, error handling, and cleanup

Pass a `CancellationToken` to every potentially long-running operation. On cancellation, `OperationCanceledException` is propagated and the handler can be configured again.

```csharp
using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMinutes(10));

try
{
    await handler.SetupAsync(downloadDirectory, _ => true, cancellationSource.Token);
    await handler.VerifyAsync(cancellationSource.Token);
    await handler.DownloadAsync(cancellationSource.Token);
}
catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
{
    Console.WriteLine("Download cancelled.");
}
catch (SteamDownloadException exception)
{
    Console.Error.WriteLine($"Steam download failed: {exception.Message}");
}
finally
{
    steamClient.Shutdown();
}
```

The main exception groups are in `SteamKitSDK.Core.Exceptions` and `SteamKitSDK.ContentDelivery.Exceptions`. Applications should handle expected access, license, branch, and download failures explicitly, and must never log credentials, branch passwords, or access tokens.

## Anonymous use

Public data can be queried without sign-in where Steam permits it. Anonymous accounts have no licenses, so they cannot download protected game or Workshop content.

```csharp
using var steamClient = new SteamClient();
await using var contentClient = new SteamContentClient(steamClient);

await steamClient.ConnectAsync(cancellationToken);
var branches = await contentClient.GetBranchesAsync(appId, cancellationToken);
```

## Operational guidance

- Set `maxConcurrentDownloadsPerTask` according to bandwidth, memory, and Steam rate limits; 8 is a reasonable starting value.
- Reuse a connected `SteamClient` for multiple operations and call `Shutdown()` when the application exits.
- Use a separate download directory for each application and account.
- Keep passwords, refresh tokens, and guard data in a secret store. Never use plaintext files, repository commits, or command-line arguments.
- Before releases, run the tests documented in this repository and review the security report.

## Included test client

`SteamKitSDK.TestClient` is a small console client for controlled real-world smoke tests. It accepts no credentials via command-line arguments and stores no tokens. Steam Guard and mobile confirmation are prompted interactively through `ConsoleSteamAuthenticator`.

The interactive menu asks for the Steam username and password when the environment variables below are not set. Password input is masked and is neither persisted nor displayed.

Before starting, credentials only need to be available as environment variables for the current process:

```powershell
$env:STEAM_USERNAME = "your-steam-name"
$env:STEAM_PASSWORD = "your-password"
```

A safe game-test workflow is:

```powershell
# 1. List available branches.
dotnet run --project SteamKitSDK.TestClient -- --app-id 440 --list-branches

# 2. Check the manifest and local files without downloading.
dotnet run --project SteamKitSDK.TestClient -- --app-id 440 --verify-only

# 3. Start the download after explicit confirmation.
dotnet run --project SteamKitSDK.TestClient -- --app-id 440 `
  --directory .\downloads\tf2 --max-concurrency 8 --pause
```

`--yes` skips interactive confirmation and should therefore be used only for deliberate automated test runs. `Ctrl+C` cancels verification or download. For a password-protected branch, the password is also not passed as an argument:

```powershell
$env:STEAM_BRANCH_PASSWORD = "branch-password"
dotnet run --project SteamKitSDK.TestClient -- --app-id 440 --branch beta --verify-only
```

Double-clicking the client without arguments opens an interactive menu for game, Workshop, and branch tests. `--pause` keeps the console open after a command-line test finishes.

The menu asks for the app or Workshop ID and then explicitly for the installation directory. An empty value uses the displayed default under `%LOCALAPPDATA%\SteamKitSDK\downloads`; you can enter an absolute or relative path. Surrounding quotation marks are removed automatically.

Use `--depot-id` to limit a test to one depot. Test Workshop items with their PublishedFileId:

```powershell
dotnet run --project SteamKitSDK.TestClient -- --workshop-id 3417789720 `
  --directory .\downloads\workshop-test
```

After testing, remove environment variables from the current shell:

```powershell
Remove-Item Env:STEAM_USERNAME, Env:STEAM_PASSWORD, Env:STEAM_WEB_API_KEY, Env:STEAM_BRANCH_PASSWORD -ErrorAction SilentlyContinue
```
