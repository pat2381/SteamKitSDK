using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SteamKitSDK.ContentDelivery.Enumerations;
using SteamKitSDK.ContentDelivery.Exceptions;

namespace SteamKitSDK.ContentDelivery.Models.Downloading;

#nullable enable

public class LegacyDownloadHandler : IDownloadHandler, IDownloadProgressSource
{
    private static readonly HttpClient DirectHttpClient = new();
    private static readonly HttpClient SteamWorkshopHttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false
    });
    private readonly HttpClient _httpClient;
    private readonly DownloadOperation _operation = new();

    public string FileUrl { get; }
    public string FileName { get; }
    public string DownloadDirectory { get; protected set; } = string.Empty;
    public Func<ManifestFile, bool> FileCondition { get; protected set; } = _ => true;
    public DownloadHandlerStateEnum State { get; protected set; }
    public IReadOnlyList<ManifestFile> Files { get; protected set; } = Array.Empty<ManifestFile>();

    public LegacyDownloadHandler(string fileUrl, string fileName) : this(fileUrl, fileName, DirectHttpClient)
    {
    }

    public LegacyDownloadHandler(string fileUrl, string fileName, HttpClient httpClient)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
        FileUrl = fileUrl;
        FileName = fileName;
        State = DownloadHandlerStateEnum.Created;
    }

    internal static LegacyDownloadHandler FromSteamWorkshop(string url, string fileName, Func<Uri, bool> isAllowedHost)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || uri.UserInfo.Length != 0 || !uri.IsDefaultPort || !isAllowedHost(uri))
        {
            throw new SteamDownloadException($"Steam Workshop returned an untrusted download endpoint for '{fileName}'.");
        }
        return new LegacyDownloadHandler(uri.AbsoluteUri, fileName, SteamWorkshopHttpClient);
    }

    public double TotalProgress { get; private set; }
    public int TotalFileCount => State == DownloadHandlerStateEnum.Created ? 1 : Files.Count;
    public ulong TotalFileSize { get; private set; }

    public event EventHandler<ManifestFile>? FileDownloaded;
    public event EventHandler<FileVerifiedArgs>? FileVerified;
    public event EventHandler<VerificationCompletedArgs>? VerificationCompleted;
    public event EventHandler<EventArgs>? DownloadComplete;
    public event EventHandler<DownloadProgress>? ProgressChanged;

    public Task SetupAsync(
        string directory,
        Func<ManifestFile, bool> condition,
        CancellationToken cancellationToken = default)
    {
        // Allow setup when not setup yet or already setup, but no state past it
        using var operation = _operation.Enter();
        if (State != DownloadHandlerStateEnum.Created && State != DownloadHandlerStateEnum.SetUp &&
            State != DownloadHandlerStateEnum.Verified)
        {
            throw new DownloadHandlerStateMismatchException(DownloadHandlerStateEnum.Created, State);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(condition);
        cancellationToken.ThrowIfCancellationRequested();

        _ = DownloadPathResolver.ResolveFilePath(directory, FileName);

        var file = new ManifestFile(FileName, [], default, 0, []);
        Files = condition(file) ? [file] : [];
        DownloadDirectory = Path.GetFullPath(directory);
        FileCondition = condition;
        TotalFileSize = 0;

        State = DownloadHandlerStateEnum.Verified;

        return Task.CompletedTask;
    }

    public Task VerifyAsync(CancellationToken cancellationToken = default)
    {
        using var operation = _operation.Enter();
        cancellationToken.ThrowIfCancellationRequested();
        if (State != DownloadHandlerStateEnum.Verified)
        {
            throw new DownloadHandlerStateMismatchException(DownloadHandlerStateEnum.Verified, State);
        }
        foreach (var file in Files)
        {
            DownloadNotifications.Publish(this, FileVerified, new FileVerifiedArgs(file, true));
            DownloadNotifications.Publish(this, ProgressChanged, new DownloadProgress(DownloadStage.Verification, 1,
                file.FileName, RequiresDownload: true));
        }
        DownloadNotifications.Publish(this, VerificationCompleted, new VerificationCompletedArgs(Files));
        return Task.CompletedTask;
    }

    public async Task DownloadAsync(CancellationToken cancellationToken = default)
    {
        using var operation = _operation.Enter();
        if (State != DownloadHandlerStateEnum.Verified)
        {
            throw new DownloadHandlerStateMismatchException(DownloadHandlerStateEnum.Verified, State);
        }

        State = DownloadHandlerStateEnum.Downloading;
        TotalProgress = 0;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(30));
        var transferToken = timeout.Token;
        string? temporaryPath = null;
        try
        {
            transferToken.ThrowIfCancellationRequested();
            if (Files.Count == 0)
            {
                TotalProgress = 1;
                State = DownloadHandlerStateEnum.Downloaded;
                DownloadNotifications.Publish(this, DownloadComplete, EventArgs.Empty);
                return;
            }

            var path = DownloadPathResolver.ResolveFilePath(DownloadDirectory, FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            using var response = await _httpClient.GetAsync(
                FileUrl,
                HttpCompletionOption.ResponseHeadersRead,
                transferToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Workshop server returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);
            }

            TotalFileSize = (ulong) (response.Content.Headers.ContentLength ?? 0);

            await using var source = await response.Content.ReadAsStreamAsync(transferToken).ConfigureAwait(false);
            var stagingPath = Path.Combine(Path.GetDirectoryName(path)!, $".steam-download-{Guid.NewGuid():N}.tmp");
            var destination = new FileStream(
                stagingPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);

            temporaryPath = stagingPath;
            long downloadedBytes = 0;
            await using (destination.ConfigureAwait(false))
            {
                var buffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = await source.ReadAsync(buffer, transferToken).ConfigureAwait(false)) != 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, bytesRead), transferToken).ConfigureAwait(false);
                    downloadedBytes += bytesRead;
                    TotalProgress = TotalFileSize == 0 ? 0 : (double) downloadedBytes / TotalFileSize;
                    PublishProgress((ulong)downloadedBytes);
                }
            }

            transferToken.ThrowIfCancellationRequested();
            File.Move(stagingPath, DownloadPathResolver.ResolveFilePath(DownloadDirectory, FileName), overwrite: true);
            temporaryPath = null;
            TotalFileSize = TotalFileSize == 0 ? (ulong) downloadedBytes : TotalFileSize;
            TotalProgress = 1;

            State = DownloadHandlerStateEnum.Downloaded;

            OnFileDownloaded();

            DownloadNotifications.Publish(this, DownloadComplete, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            State = DownloadHandlerStateEnum.SetUp;
            throw;
        }
        catch (Exception exception)
        {
            State = DownloadHandlerStateEnum.Verified;
            var detail = exception switch
            {
                OperationCanceledException => "the HTTP transfer timed out",
                HttpRequestException { StatusCode: { } status } => $"the server returned HTTP {(int)status} ({status})",
                HttpRequestException httpError => $"the HTTP connection failed ({httpError.HttpRequestError})",
                IOException or UnauthorizedAccessException => "the transfer or destination file could not be read or written",
                _ => "the HTTP transfer could not be started or completed"
            };
            throw new SteamDownloadException($"Workshop download for '{FileName}' failed because {detail}.");
        }
        finally
        {
            if (temporaryPath != null)
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _operation.StartDisposal();
        return default;
    }

    public void Dispose()
    {
        DisposeAsync().GetAwaiter().GetResult();
    }

    private void OnFileDownloaded()
    {
        if (FileDownloaded != null)
        {
            DownloadNotifications.Publish(this, FileDownloaded,
                    new ManifestFile(
                        FileName,
                        [],
                        default,
                        TotalFileSize,
                        []));
        }
    }

    private void PublishProgress(ulong writtenBytes)
        => DownloadNotifications.Publish(this, ProgressChanged,
            new DownloadProgress(DownloadStage.Transfer, TotalProgress, FileName, writtenBytes,
                TotalFileSize == 0 ? null : TotalFileSize, TransferBytesWritten: writtenBytes,
                TransferSize: TotalFileSize == 0 ? null : TotalFileSize));

}
