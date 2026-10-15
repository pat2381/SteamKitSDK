using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SteamKitSDK.ContentDelivery;
using SteamKitSDK.ContentDelivery.Models;
using SteamKitSDK.ContentDelivery.Models.Downloading;
using SteamKitSDK.Core;
using SteamClient = SteamKitSDK.Core.SteamClient;

namespace SteamKitSDK.TestClient;

public static class Program
{
    private const string BranchPasswordEnvironmentVariable = "STEAM_BRANCH_PASSWORD";

    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            return await RunInteractiveMenuAsync();
        }

        var pauseBeforeExit = Array.Exists(
            args,
            argument => string.Equals(argument, "--pause", StringComparison.OrdinalIgnoreCase));
        var exitCode = await RunAsync(args);

        if (pauseBeforeExit && !Console.IsInputRedirected)
        {
            Console.WriteLine();
            Console.Write("Zum Schließen eine Taste drücken …");
            Console.ReadKey(intercept: true);
            Console.WriteLine();
        }

        return exitCode;
    }

    private static async Task<int> RunInteractiveMenuAsync()
    {
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("=== SteamKitSDK Test Client ===");
            Console.WriteLine("1. Spiel oder Depot prüfen / herunterladen");
            Console.WriteLine("2. Workshop-Mod prüfen / herunterladen");
            Console.WriteLine("3. Branches eines Spiels anzeigen");
            Console.WriteLine("0. Beenden");
            Console.Write("Auswahl: ");

            switch (Console.ReadLine()?.Trim())
            {
                case "1":
                    await RunMenuCommandAsync(CreateGameMenuArguments());
                    break;
                case "2":
                    await RunMenuCommandAsync(CreateWorkshopMenuArguments());
                    break;
                case "3":
                    await RunMenuCommandAsync(CreateBranchMenuArguments());
                    break;
                case "0":
                case null:
                    return 0;
                default:
                    Console.WriteLine("Ungültige Auswahl.");
                    break;
            }
        }
    }

    private static async Task RunMenuCommandAsync(string[] arguments)
    {
        _ = await RunAsync(arguments);
        Console.WriteLine();
        Console.Write("Zum Menü zurückkehren – eine Taste drücken …");
        Console.ReadKey(intercept: true);
        Console.WriteLine();
    }

    private static string[] CreateGameMenuArguments()
    {
        var appId = ReadRequiredValue("Steam-App-ID");
        var arguments = new List<string> { "--app-id", appId };
        AddOptionalArgument(arguments, "--depot-id", ReadOptionalValue("Depot-ID (leer = alle erreichbaren Depots)"));
        AddOptionalArgument(arguments, "--branch", ReadOptionalValue("Branch (leer = public)"));
        arguments.Add("--directory");
        arguments.Add(ReadInstallationDirectory($"app-{appId}"));
        AddOptionalArgument(arguments, "--max-concurrency", ReadOptionalValue("Parallelität (leer = 8)"));

        if (AskYesNo("Nur prüfen, ohne Download?", defaultValue: true))
        {
            arguments.Add("--verify-only");
        }

        return arguments.ToArray();
    }

    private static string[] CreateWorkshopMenuArguments()
    {
        var workshopId = ReadRequiredValue("Workshop-PublishedFileId");
        var arguments = new List<string> { "--workshop-id", workshopId, "--directory", ReadInstallationDirectory($"workshop-{workshopId}") };
        AddOptionalArgument(arguments, "--max-concurrency", ReadOptionalValue("Parallelität (leer = 8)"));

        if (AskYesNo("Nur prüfen, ohne Download?", defaultValue: true))
        {
            arguments.Add("--verify-only");
        }

        return arguments.ToArray();
    }

    private static string[] CreateBranchMenuArguments() =>
        ["--app-id", ReadRequiredValue("Steam-App-ID"), "--list-branches"];

    private static string ReadRequiredValue(string label)
    {
        while (true)
        {
            Console.Write($"{label}: ");
            var value = Console.ReadLine()?.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            Console.WriteLine("Dieser Wert ist erforderlich.");
        }
    }

    private static string? ReadOptionalValue(string label)
    {
        Console.Write($"{label}: ");
        var value = Console.ReadLine()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string ReadInstallationDirectory(string contentIdentifier)
    {
        var defaultDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteamKitSDK",
            "downloads",
            contentIdentifier);

        while (true)
        {
            Console.Write($"Installationsordner [{defaultDirectory}]: ");
            var input = Console.ReadLine()?.Trim().Trim('"');
            var selectedDirectory = string.IsNullOrWhiteSpace(input) ? defaultDirectory : input;

            try
            {
                return Path.GetFullPath(selectedDirectory);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                Console.WriteLine("Der angegebene Installationsordner ist ungültig. Gib den Pfad ohne Anführungszeichen ein.");
            }
        }
    }

    private static void AddOptionalArgument(ICollection<string> arguments, string option, string? value)
    {
        if (value is null)
        {
            return;
        }

        arguments.Add(option);
        arguments.Add(value);
    }

    private static bool AskYesNo(string question, bool defaultValue)
    {
        var suffix = defaultValue ? "[J/n]" : "[j/N]";
        Console.Write($"{question} {suffix}: ");
        var value = Console.ReadLine()?.Trim();

        return string.IsNullOrEmpty(value)
            ? defaultValue
            : string.Equals(value, "j", StringComparison.OrdinalIgnoreCase)
              || string.Equals(value, "y", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadCredential(string environmentVariable, string label, bool secret)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (Console.IsInputRedirected)
        {
            return null;
        }

        while (true)
        {
            Console.Write($"{label}: ");
            value = secret ? ReadPassword() : Console.ReadLine();

            if (!secret)
            {
                Console.WriteLine();
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            Console.WriteLine("Dieser Wert ist erforderlich.");
        }
    }

    private static string ReadPassword()
    {
        var password = new StringBuilder();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return password.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                {
                    password.Length--;
                    Console.Write("\b \b");
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
                Console.Write('*');
            }
        }
    }

    private static async Task<int> RunAsync(string[] args)
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

        SteamCredentials steamCredentials;
        SteamAuthenticator? steamAuthenticator;

        if (options.Anonymous)
        {
            steamCredentials = SteamCredentials.Anonymous;
            steamAuthenticator = null;
            Console.WriteLine("Steam: anonyme Sitzung wird verwendet.");
        }
        else
        {
            var username = ReadCredential("STEAM_USERNAME", "Steam-Benutzername", secret: false);
            var password = ReadCredential("STEAM_PASSWORD", "Steam-Passwort", secret: true);
            var webApiKey = Environment.GetEnvironmentVariable("STEAM_WEB_API_KEY");

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                Console.Error.WriteLine("Steam-Benutzername und Steam-Passwort sind erforderlich. Alternativ kann --anonymous verwendet werden.");
                return 64;
            }

            steamCredentials = new SteamCredentials(username, password, webApiKey);
            steamAuthenticator = new ConsoleSteamAuthenticator(steamCredentials.Username);
        }

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
                steamAuthenticator);
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
                case "--anonymous":
                    options = options with { Anonymous = true };
                    break;
                case "--list-branches":
                    options = options with { ListBranches = true };
                    break;
                case "--yes":
                    options = options with { AssumeYes = true };
                    break;
                case "--pause":
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
            SteamKitSDK Test Client

            Credentials:
              The interactive menu asks for username and password when they are not set below.
              STEAM_USERNAME, STEAM_PASSWORD can be used for command-line automation.

            Optional environment variables:
              STEAM_WEB_API_KEY, STEAM_BRANCH_PASSWORD

            Game download:
              dotnet run --project SteamKitSDK.TestClient -- --app-id <id> [--depot-id <id>]
                [--branch <name>] [--directory <path>] [--max-concurrency <count>] [--verify-only] [--anonymous] [--yes]

            Workshop download:
              dotnet run --project SteamKitSDK.TestClient -- --workshop-id <id>
                [--directory <path>] [--max-concurrency <count>] [--verify-only] [--yes]

            Inspect branches:
              dotnet run --project SteamKitSDK.TestClient -- --app-id <id> --list-branches

            Anonymous server-content check (no credentials):
              dotnet run --project SteamKitSDK.TestClient -- --app-id <id> --depot-id <id> --anonymous --verify-only

            Without arguments, the client starts an interactive menu. --pause keeps the window open after a command-line test run.
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
        bool Anonymous = false,
        bool ListBranches = false,
        bool AssumeYes = false,
        bool ShowHelp = false);
}
