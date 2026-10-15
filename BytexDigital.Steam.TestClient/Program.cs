using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BytexDigital.Steam.ContentDelivery;
using BytexDigital.Steam.ContentDelivery.Models;
using BytexDigital.Steam.ContentDelivery.Models.Downloading;
using BytexDigital.Steam.Core;
using SteamClient = BytexDigital.Steam.Core.SteamClient;

namespace BytexDigital.Steam.TestClient;

public static class Program
{
    private const string BranchPasswordEnvironmentVariable = "STEAM_BRANCH_PASSWORD";

    private static async Task<int> Main(string[] args)
    {
        if (!TryParseOptions(args, out var options, out var error))
        {
            Console.Error.WriteLine(error);
            PrintUsage();
            return 64;
        }

        if (options.ShowHelp)
        {
            PrintUsage();
            return 0;
        }

        var username = Environment.GetEnvironmentVariable("STEAM_USERNAME");
        var password = Environment.GetEnvironmentVariable("STEAM_PASSWORD");
        var webApiKey = Environment.GetEnvironmentVariable("STEAM_WEB_API_KEY");

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            Console.Error.WriteLine("Set STEAM_USERNAME and STEAM_PASSWORD before starting the test client.");
            return 64;
        }

        var steamCredentials = new SteamCredentials(username, password, webApiKey);

        using var cancellationTokenSource = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            if (!cancellationTokenSource.IsCancellationRequested)
            {
                Console.WriteLine("\nAbbruch angefordert. Aktive Operationen werden beendet …");
                cancellationTokenSource.Cancel();
            }
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            using var steamClient = new SteamClient(
                steamCredentials,
                new ConsoleSteamAuthenticator(steamCredentials.Username));
            await using var steamContentClient = new SteamContentClient(
                steamClient,
                options.MaxConcurrency);

            steamClient.InternalClientAttemptingConnect += () => Console.WriteLine("Steam: Verbindung wird aufgebaut …");
            steamClient.InternalClientConnected += () => Console.WriteLine("Steam: verbunden.");
            steamClient.InternalClientLoggedOn += () => Console.WriteLine("Steam: Anmeldung erfolgreich.");
            steamClient.InternalClientDisconnected += () => Console.WriteLine("Steam: Verbindung getrennt.");

            await steamClient.ConnectAsync(cancellationTokenSource.Token);

            if (options.ListBranches)
            {
                await PrintBranchesAsync(steamContentClient, options.AppId!.Value, cancellationTokenSource.Token);
                return 0;
            }

            var handler = await CreateHandlerAsync(steamContentClient, options, cancellationTokenSource.Token);
            await using (handler)
            {
                var targetDirectory = options.DownloadDirectory
                    ?? Path.Combine(
                        AppContext.BaseDirectory,
                        "downloads",
                        options.WorkshopId.HasValue
                            ? $"workshop-{options.WorkshopId.Value}"
                            : $"app-{options.AppId!.Value}");

                Console.WriteLine($"Zielordner: {Path.GetFullPath(targetDirectory)}");
                Console.WriteLine($"Parallelität: {options.MaxConcurrency}");

                handler.FileVerified += (_, result) =>
                {
                    var action = result.RequiresDownload ? "wird geladen" : "bereits aktuell";
                    Console.WriteLine($"Prüfung: {result.ManifestFile.FileName} – {action}");
                };
                handler.VerificationCompleted += (_, result) =>
                    Console.WriteLine($"Verifikation abgeschlossen: {result.QueuedFiles.Count} Dateien werden geladen.");
                handler.FileDownloaded += (_, file) =>
                    Console.WriteLine($"Datei fertig: {file.FileName}");
                handler.DownloadComplete += (_, _) => Console.WriteLine("Download abgeschlossen.");

                await handler.SetupAsync(targetDirectory, _ => true, cancellationTokenSource.Token);
                Console.WriteLine($"{handler.Files.Count} Dateien im ausgewählten Inhalt.");

                await handler.VerifyAsync(cancellationTokenSource.Token);

                if (options.VerifyOnly)
                {
                    Console.WriteLine("Verify-only abgeschlossen; es wurden keine Dateien heruntergeladen.");
                    return 0;
                }

                if (!options.AssumeYes && !AskForConfirmation())
                {
                    Console.WriteLine("Download nicht gestartet.");
                    return 0;
                }

                await ShowProgressUntilCompletedAsync(handler, cancellationTokenSource.Token);
            }

            steamClient.Shutdown();
            return 0;
        }
        catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
        {
            Console.Error.WriteLine("Vorgang wurde abgebrochen.");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Test fehlgeschlagen: {exception.Message}");
            Console.Error.WriteLine("Hinweis: Zugangsdaten, Token und Branch-Passwörter werden nicht ausgegeben.");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static async Task<IDownloadHandler> CreateHandlerAsync(
        SteamContentClient contentClient,
        TestClientOptions options,
        CancellationToken cancellationToken)
    {
        if (options.WorkshopId.HasValue)
        {
            var details = await contentClient.GetPublishedFileDetailsAsync(options.WorkshopId.Value, cancellationToken);
            Console.WriteLine($"Workshop-Element: {details.title}");
            return await contentClient.GetPublishedFileDataAsync(options.WorkshopId.Value, cancellationToken: cancellationToken);
        }

        var branchPassword = Environment.GetEnvironmentVariable(BranchPasswordEnvironmentVariable);
        return options.DepotId.HasValue
            ? await contentClient.GetAppDataAsync(
                options.AppId!.Value,
                options.DepotId.Value,
                options.Branch,
                branchPassword,
                cancellationToken: cancellationToken)
            : await contentClient.GetAppDataAsync(
                options.AppId!.Value,
                options.Branch,
                branchPassword,
                cancellationToken: cancellationToken);
    }

    private static async Task PrintBranchesAsync(
        SteamContentClient contentClient,
        uint appId,
        CancellationToken cancellationToken)
    {
        var branches = await contentClient.GetBranchesAsync(appId, cancellationToken);
        foreach (var branch in branches)
        {
            Console.WriteLine(
                $"{branch.Name,-20} Passwort: {(branch.RequiresPassword ? "ja" : "nein"),-3} Build: {branch.BuildId}  {branch.Description}");
        }
    }

    private static async Task ShowProgressUntilCompletedAsync(IDownloadHandler handler, CancellationToken cancellationToken)
    {
        var downloadTask = handler.DownloadAsync(cancellationToken);

        while (!downloadTask.IsCompleted)
        {
            Console.Write($"\rFortschritt: {handler.TotalProgress:P1} | Dateien: {handler.TotalFileCount} | Größe: {FormatBytes(handler.TotalFileSize)}   ");
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        await downloadTask;
        Console.WriteLine($"\rFortschritt: {handler.TotalProgress:P1} | Dateien: {handler.TotalFileCount} | Größe: {FormatBytes(handler.TotalFileSize)}   ");
    }

    private static bool AskForConfirmation()
    {
        Console.Write("Download in den angegebenen Ordner starten? [y/N] ");
        return string.Equals(Console.ReadLine(), "y", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatBytes(ulong bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        var value = (double)bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value.ToString("0.##", CultureInfo.InvariantCulture)} {units[unitIndex]}";
    }

    private static bool TryParseOptions(string[] args, out TestClientOptions options, out string? error)
    {
        options = new TestClientOptions();
        error = null;

        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];
            switch (option)
            {
                case "--help" or "-h":
                    options = options with { ShowHelp = true };
                    break;
                case "--app-id":
                    if (!TryReadUInt(args, ref index, option, out var appId, out error)) return false;
                    options = options with { AppId = appId };
                    break;
                case "--workshop-id":
                    if (!TryReadULong(args, ref index, option, out var workshopId, out error)) return false;
                    options = options with { WorkshopId = workshopId };
                    break;
                case "--depot-id":
                    if (!TryReadUInt(args, ref index, option, out var depotId, out error)) return false;
                    options = options with { DepotId = depotId };
                    break;
                case "--branch":
                    if (!TryReadString(args, ref index, option, out var branch, out error)) return false;
                    options = options with { Branch = branch };
                    break;
                case "--directory":
                    if (!TryReadString(args, ref index, option, out var directory, out error)) return false;
                    options = options with { DownloadDirectory = directory };
                    break;
                case "--max-concurrency":
                    if (!TryReadInt(args, ref index, option, out var maxConcurrency, out error) || maxConcurrency < 1)
                    {
                        error ??= "--max-concurrency must be at least 1.";
                        return false;
                    }

                    options = options with { MaxConcurrency = maxConcurrency };
                    break;
                case "--verify-only":
                    options = options with { VerifyOnly = true };
                    break;
                case "--list-branches":
                    options = options with { ListBranches = true };
                    break;
                case "--yes":
                    options = options with { AssumeYes = true };
                    break;
                default:
                    error = $"Unknown option: {option}";
                    return false;
            }
        }

        if (options.ShowHelp)
        {
            return true;
        }

        if (options.AppId.HasValue == options.WorkshopId.HasValue)
        {
            error = "Specify exactly one of --app-id or --workshop-id.";
            return false;
        }

        if (options.ListBranches && !options.AppId.HasValue)
        {
            error = "--list-branches requires --app-id.";
            return false;
        }

        if (options.DepotId.HasValue && !options.AppId.HasValue)
        {
            error = "--depot-id requires --app-id.";
            return false;
        }

        return true;
    }

    private static bool TryReadString(string[] args, ref int index, string option, out string value, out string? error)
    {
        value = string.Empty;
        error = null;
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
        {
            error = $"{option} requires a value.";
            return false;
        }

        value = args[index];
        return true;
    }

    private static bool TryReadUInt(string[] args, ref int index, string option, out uint value, out string? error)
    {
        value = 0;
        if (!TryReadString(args, ref index, option, out var text, out error)) return false;
        if (uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value)) return true;
        error = $"{option} requires an unsigned integer.";
        return false;
    }

    private static bool TryReadULong(string[] args, ref int index, string option, out ulong value, out string? error)
    {
        value = 0;
        if (!TryReadString(args, ref index, option, out var text, out error)) return false;
        if (ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value)) return true;
        error = $"{option} requires an unsigned integer.";
        return false;
    }

    private static bool TryReadInt(string[] args, ref int index, string option, out int value, out string? error)
    {
        value = 0;
        if (!TryReadString(args, ref index, option, out var text, out error)) return false;
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value)) return true;
        error = $"{option} requires an integer.";
        return false;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            BytexDigital.Steam Test Client

            Required environment variables:
              STEAM_USERNAME, STEAM_PASSWORD

            Optional environment variables:
              STEAM_WEB_API_KEY, STEAM_BRANCH_PASSWORD

            Game download:
              dotnet run --project BytexDigital.Steam.TestClient -- --app-id <id> [--depot-id <id>]
                [--branch <name>] [--directory <path>] [--max-concurrency <count>] [--verify-only] [--yes]

            Workshop download:
              dotnet run --project BytexDigital.Steam.TestClient -- --workshop-id <id>
                [--directory <path>] [--max-concurrency <count>] [--verify-only] [--yes]

            Inspect branches:
              dotnet run --project BytexDigital.Steam.TestClient -- --app-id <id> --list-branches

            Ctrl+C cancels a running verification or download safely.
            """);
    }

    private sealed record TestClientOptions(
        uint? AppId = null,
        ulong? WorkshopId = null,
        uint? DepotId = null,
        string Branch = "public",
        string? DownloadDirectory = null,
        int MaxConcurrency = 8,
        bool VerifyOnly = false,
        bool ListBranches = false,
        bool AssumeYes = false,
        bool ShowHelp = false);
}
