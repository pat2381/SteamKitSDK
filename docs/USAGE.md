# BytexDigital.Steam: Nutzungsanleitung

Diese Anleitung beschreibt die APIs von Version 0.9.0. Das SDK greift auf Steam über SteamKit2 zu. Es darf nur für Inhalte eingesetzt werden, für die das verwendete Steam-Konto eine Berechtigung besitzt.

## Installation

```powershell
dotnet add package BytexDigital.Steam --version 0.9.0
```

Das Projekt benötigt .NET 10. Für Downloads von besitzpflichtigen Spielen oder Workshop-Inhalten ist ein angemeldetes Steam-Konto erforderlich.

## Grundprinzip

Jeder Download folgt immer derselben Reihenfolge:

1. `SteamClient` mit Zugangsdaten und Authenticator erzeugen.
2. Mit `ConnectAsync` verbinden.
3. Über `SteamContentClient` einen `IDownloadHandler` abrufen.
4. Den Handler mit `SetupAsync` konfigurieren.
5. Mit `VerifyAsync` lokale Dateien gegen das Manifest prüfen.
6. Mit `DownloadAsync` fehlende oder abweichende Dateien herunterladen.
7. Handler, Content-Client und Steam-Client beenden bzw. entsorgen.

`VerifyAsync` ist kein optionaler Schritt: Erst danach befindet sich ein Download-Handler im gültigen Zustand für `DownloadAsync`.

## Anmelden und Steam Guard

`ConsoleSteamAuthenticator` ist für eine lokale, interaktive Anwendung gedacht. Er fragt bei Bedarf nach einem E-Mail- oder Steam-Guard-Code und nach der Bestätigung in der Steam-Mobile-App.

```csharp
using BytexDigital.Steam.Core;

var username = Environment.GetEnvironmentVariable("STEAM_USERNAME")
    ?? throw new InvalidOperationException("STEAM_USERNAME is not set.");
var password = Environment.GetEnvironmentVariable("STEAM_PASSWORD")
    ?? throw new InvalidOperationException("STEAM_PASSWORD is not set.");

var credentials = new SteamCredentials(username, password);

// Ohne persistenceDirectory: kein Schreiben von Token auf die Festplatte.
var authenticator = new ConsoleSteamAuthenticator(username);

using var steamClient = new SteamClient(credentials, authenticator);

steamClient.InternalClientConnected += () => Console.WriteLine("Mit Steam verbunden.");
steamClient.InternalClientLoggedOn += () => Console.WriteLine("Anmeldung abgeschlossen.");
steamClient.InternalClientDisconnected += () => Console.WriteLine("Steam-Verbindung getrennt.");

await steamClient.ConnectAsync();
```

Die Umgebungsvariablen verhindern, dass das Passwort in Quellcode oder Shell-Historie landet. Für produktive Dienste sollte eine eigene Klasse von `SteamAuthenticator` abgeleitet werden. Diese implementiert die sechs abstrakten Methoden für Code-Eingabe, Mobile-Bestätigung, Access-Token und Guard-Daten und speichert die beiden letzten Werte ausschließlich in einem betriebssystemgestützten Secret Store (beispielsweise Windows Credential Manager, macOS Keychain oder Linux Secret Service).

> **Wichtig:** Übergibt man `ConsoleSteamAuthenticator` ein `persistenceDirectory`, schreibt die Beispielimplementierung Access-Token und Guard-Daten als Dateien. Das ist nur für einen lokal geschützten Entwicklungsrechner geeignet, nicht für Server oder gemeinsam genutzte Geräte.

## Spiel- bzw. Depot-Download

Das folgende Beispiel lädt alle erreichbaren Depots der öffentlichen Branch einer App. `appId` muss durch die Steam-App-ID des gewünschten Spiels ersetzt werden. Ein Steam-Konto kann nur Depots laden, für die es eine Lizenz besitzt.

```csharp
using BytexDigital.Steam.ContentDelivery;
using BytexDigital.Steam.ContentDelivery.Exceptions;
using BytexDigital.Steam.ContentDelivery.Models.Downloading;
using BytexDigital.Steam.Core;

const uint appId = 440; // Beispiel: Team Fortress 2
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
    Console.WriteLine($"Geprüft: {result.ManifestFile.FileName}; Download nötig: {result.RequiresDownload}");
handler.FileDownloaded += (_, file) =>
    Console.WriteLine($"{handler.TotalProgress:P1} – {file.FileName}");
handler.VerificationCompleted += (_, result) =>
    Console.WriteLine($"{result.QueuedFiles.Count} Dateien werden geladen.");
handler.DownloadComplete += (_, _) => Console.WriteLine("Download abgeschlossen.");

await handler.SetupAsync(
    downloadDirectory,
    file => !file.FileName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase),
    cancellationToken);
await handler.VerifyAsync(cancellationToken);
await handler.DownloadAsync(cancellationToken);

steamClient.Shutdown();
```

Der Dateifilter wird vor der Verifikation angewendet. Die SDK-interne Pfadauflösung verhindert, dass Namen aus einem Manifest das konfigurierte Download-Verzeichnis verlassen.

### Ein einzelnes Depot oder ein bekanntes Manifest

```csharp
// Ein Depot innerhalb der öffentlichen Branch laden.
await using var depotHandler = await contentClient.GetAppDataAsync(
    appId: 440,
    depotId: 441,
    branch: "public",
    cancellationToken: cancellationToken);

await depotHandler.SetupAsync(downloadDirectory, _ => true, cancellationToken);
await depotHandler.VerifyAsync(cancellationToken);
await depotHandler.DownloadAsync(cancellationToken);

// Alternativ: eine bereits bekannte Manifest-ID verwenden.
await using var manifestHandler = await contentClient.GetAppDataAsync(
    appId: 440,
    depotId: 441,
    manifestId: 12345678901234567890UL,
    cancellationToken: cancellationToken);
```

Die Überladung mit einer Manifest-ID ist nützlich für reproduzierbare Builds. Eine Manifest-ID umgeht jedoch keine Steam-Berechtigungsprüfung beim Abruf des Depot-Schlüssels.

### Passwortgeschützte Branches

Zunächst können die Branches abgefragt werden. Anschließend wird das Passwort ausschließlich an den API-Aufruf übergeben; es darf nicht geloggt werden.

```csharp
var branches = await contentClient.GetBranchesAsync(appId, cancellationToken);
foreach (var branch in branches)
{
    Console.WriteLine($"{branch.Name}: Passwort nötig = {branch.RequiresPassword}");
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

Aktuelle V2-Manifeste werden unterstützt. Alte V1-verschlüsselte Manifest-IDs werden mit `NotSupportedException` abgelehnt, weil SteamKit2 3.4 die notwendige alte Hilfsfunktion nicht mehr anbietet.

## Workshop-Mod herunterladen

Ein Workshop-Element wird über seine `PublishedFileId` geladen. Die Methode ermittelt Metadaten, Lizenz-/Zugriffsstatus und bei Steam-CDN-Inhalten das passende Manifest selbst.

```csharp
using BytexDigital.Steam.ContentDelivery.Models.Downloading;

const ulong workshopItemId = 3417789720; // Beispiel-ID; durch eigene PublishedFileId ersetzen
var workshopDirectory = Path.Combine(AppContext.BaseDirectory, "downloads", "workshop", workshopItemId.ToString());

var details = await contentClient.GetPublishedFileDetailsAsync(workshopItemId, cancellationToken);
Console.WriteLine($"Workshop-Titel: {details.title}");

await using IDownloadHandler workshopHandler = await contentClient.GetPublishedFileDataAsync(
    workshopItemId,
    cancellationToken: cancellationToken);

workshopHandler.FileDownloaded += (_, file) =>
    Console.WriteLine($"{workshopHandler.TotalProgress:P1} – {file.FileName}");

await workshopHandler.SetupAsync(workshopDirectory, _ => true, cancellationToken);
await workshopHandler.VerifyAsync(cancellationToken);
await workshopHandler.DownloadAsync(cancellationToken);
```

Nicht jedes Workshop-Element ist ein herunterladbarer Community-Inhalt. In diesem Fall wirft die SDK-Methode `SteamPublishedFileIsNotDownloadableException`. Fehlende Lizenz oder eingeschränkter Zugriff führen zu den entsprechenden Zugriffs- bzw. Lizenz-Exceptions.

Für Workshop-Kataloge sollte immer eine maximale Ergebniszahl festgelegt werden, damit die Abfrage nicht unbeschränkt Speicher belegt:

```csharp
var newestItems = await contentClient.GetPublishedFilesForAppIdRawAsync(
    appId,
    maximumResults: 250,
    cancellationToken);
```

## Abbruch, Fehlerbehandlung und Aufräumen

Ein `CancellationToken` wird in alle potenziell lang laufenden Aufrufe weitergereicht. Bei einem Abbruch wird `OperationCanceledException` weitergegeben und der Handler bleibt erneut einrichtbar.

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
    Console.WriteLine("Der Download wurde abgebrochen.");
}
catch (SteamDownloadException exception)
{
    Console.Error.WriteLine($"Steam-Download fehlgeschlagen: {exception.Message}");
}
finally
{
    steamClient.Shutdown();
}
```

Die wichtigen Ausnahmegruppen befinden sich in `BytexDigital.Steam.Core.Exceptions` und `BytexDigital.Steam.ContentDelivery.Exceptions`. Anwendungen sollten erwartete Zugriffs-, Lizenz-, Branch- und Downloadfehler gezielt behandeln und niemals Zugangsdaten, Branch-Passwörter oder Access-Token protokollieren.

## Anonyme Nutzung

Öffentliche Daten können – soweit Steam dies erlaubt – ohne Anmeldung abgefragt werden. Anonyme Zugänge besitzen keine Lizenzen und eignen sich deshalb nicht für geschützte Spiel- oder Workshop-Downloads.

```csharp
using var steamClient = new SteamClient();
await using var contentClient = new SteamContentClient(steamClient);

await steamClient.ConnectAsync(cancellationToken);
var branches = await contentClient.GetBranchesAsync(appId, cancellationToken);
```

## Betriebshinweise

- Begrenze `maxConcurrentDownloadsPerTask` passend zu Bandbreite, Speicher und Steam-Ratenlimits; 8 ist ein vernünftiger Startwert.
- Wiederverwende einen verbundenen `SteamClient` für mehrere Vorgänge und rufe `Shutdown()` beim Beenden auf.
- Verwende einen eigenen, pro App/Account getrennten Download-Ordner.
- Verwahre Passwörter, Refresh-Token und Guard-Daten in einem Secret Store. Keine Klartext-Dateien, Repository-Commits oder Kommandozeilenargumente verwenden.
- Prüfe vor Releases die in diesem Repository dokumentierten Tests und den Sicherheitsbericht.

## Integrierter Test-Client

Das Projekt `BytexDigital.Steam.TestClient` ist ein kleiner Konsolenclient für reale, kontrollierte Smoke-Tests. Er verwendet keine Zugangsdaten aus Argumenten und speichert keine Token. Steam Guard und die Mobile-Bestätigung werden interaktiv über `ConsoleSteamAuthenticator` abgefragt.

Vor dem Start müssen die Zugangsdaten nur für den aktuellen Prozess als Umgebungsvariablen verfügbar sein:

```powershell
$env:STEAM_USERNAME = "dein-steam-name"
$env:STEAM_PASSWORD = "dein-passwort"
```

Ein sicherer Testablauf für ein Spiel ist:

```powershell
# 1. Verfügbare Branches prüfen.
dotnet run --project BytexDigital.Steam.TestClient -- --app-id 440 --list-branches

# 2. Manifest und vorhandene Dateien prüfen, ohne einen Download zu starten.
dotnet run --project BytexDigital.Steam.TestClient -- --app-id 440 --verify-only

# 3. Download nach expliziter Bestätigung starten.
dotnet run --project BytexDigital.Steam.TestClient -- --app-id 440 `
  --directory .\downloads\tf2 --max-concurrency 8
```

`--yes` überspringt die interaktive Bestätigung und ist deshalb nur für bewusst automatisierte Testläufe gedacht. `Ctrl+C` bricht Verifikation oder Download ab. Für einen passwortgeschützten Branch wird das Passwort ebenfalls nicht als Argument übergeben:

```powershell
$env:STEAM_BRANCH_PASSWORD = "branch-passwort"
dotnet run --project BytexDigital.Steam.TestClient -- --app-id 440 --branch beta --verify-only
```

Ein einzelnes Depot lässt sich mit `--depot-id` begrenzen. Workshop-Elemente werden über ihre PublishedFileId getestet:

```powershell
dotnet run --project BytexDigital.Steam.TestClient -- --workshop-id 3417789720 `
  --directory .\downloads\workshop-test
```

Nach dem Test sollten die Umgebungsvariablen aus der aktuellen Shell entfernt werden:

```powershell
Remove-Item Env:STEAM_USERNAME, Env:STEAM_PASSWORD, Env:STEAM_WEB_API_KEY, Env:STEAM_BRANCH_PASSWORD -ErrorAction SilentlyContinue
```
