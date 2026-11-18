using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using SteamKitSDK.ContentDelivery.Enumerations;
using SteamKitSDK.ContentDelivery.Exceptions;
using SteamKitSDK.Core.Structs;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using SteamKit2.CDN;
using static SteamKit2.DepotManifest;

namespace SteamKitSDK.ContentDelivery.Models.Downloading;

public class DefaultDownloadHandler : IDownloadHandler, IDownloadProgressSource
{
    internal byte[] _depotKey;
    private readonly DownloadOperation _operation = new();

    private ConcurrentDictionary<ManifestFile, FileTarget> _fileTargets = new();

    private ConcurrentBag<FileWriter> _fileWriters = new();
    private SteamCdnServerPool _serverPool;
    private ConcurrentBag<ChunkJob> _chunks = new();
    internal SteamContentClient _steamContentClient;

    public Manifest Manifest { get; }
    public AppId AppId { get; }
    public DepotId DepotId { get; }
    public ManifestId ManifestId { get; }
    public DownloadHandlerStateEnum State { get; protected set; }
    public IReadOnlyList<ManifestFile> Files { get; protected set; }
    public string DownloadDirectory { get; protected set; }
    public Func<ManifestFile, bool> FileCondition { get; protected set; }

    public ILogger Logger { get; set; }

    public DefaultDownloadHandler(
        SteamContentClient steamContentClient,
        Manifest manifest,
        AppId appId,
        DepotId depotId)
    {
        _steamContentClient = steamContentClient;

        Manifest = manifest;
        AppId = appId;
        DepotId = depotId;
        ManifestId = manifest.ManifestId;
    }

    public event EventHandler<ManifestFile> FileDownloaded;
    public event EventHandler<FileVerifiedArgs> FileVerified;
    public event EventHandler<VerificationCompletedArgs> VerificationCompleted;
    public event EventHandler<EventArgs> DownloadComplete;
    public event EventHandler<DownloadProgress> ProgressChanged;
    private int _verifiedFiles;
    private long _transferBytesWritten;
    private ulong _transferSize;

    public Task SetupAsync(
        string directory,
        Func<ManifestFile, bool> condition,
        CancellationToken cancellationToken = default)
    {
        // Allow setup when not setup yet or already setup, but no state past it
        using var operation = _operation.Enter();
        if (State != DownloadHandlerStateEnum.Created && State != DownloadHandlerStateEnum.SetUp)
        {
            throw new DownloadHandlerStateMismatchException(DownloadHandlerStateEnum.Created, State);
        }

        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(condition);
        var root = Path.GetFullPath(directory);

        // Filter out files that are directories or that the caller does not want
        IEnumerable<ManifestFile> filteredFiles = Manifest.Files
            .Where(file => !file.Flags.HasFlag(ManifestFileFlag.Directory) && condition(file))
            .OrderBy(x => x.FileName)
            .ToList();

        // Filter all files that are possible duplicates
        Files = filteredFiles.DistinctBy(file => DownloadPathResolver.ResolveFilePath(root, file.FileName),
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToList();
        DownloadDirectory = root;
        FileCondition = condition;

        State = DownloadHandlerStateEnum.SetUp;

        return Task.CompletedTask;
    }

    public int TotalFileCount
    {
        get
        {
            if (State >= DownloadHandlerStateEnum.Verified)
            {
                return _fileTargets.Count;
            }
            else
            {
                return Manifest.Files.Count;
            }
        }
    }

    public ulong TotalFileSize
    {
        get
        {
            if (State >= DownloadHandlerStateEnum.Verified)
            {
                return _fileTargets.Select(x => x.Key.TotalSize).Aggregate(0UL, (a, b) => a + b);
            }
            else
            {
                return Manifest.Files.Select(x => x.TotalSize).Aggregate(0UL, (a, b) => a + b);
            }
        }
    }

    public double TotalProgress
    {
        get
        {
            if (State >= DownloadHandlerStateEnum.Verified)
            {
                return _transferSize > 0 ? (double)Interlocked.Read(ref _transferBytesWritten) / _transferSize :
                    State == DownloadHandlerStateEnum.Failed ? 0 : 1;
            }
            else
            {
                return 0;
            }
        }
    }

    public async Task VerifyAsync(CancellationToken cancellationToken = default)
    {
        using var operation = _operation.Enter();
        if (State != DownloadHandlerStateEnum.SetUp)
        {
            throw new DownloadHandlerStateMismatchException(DownloadHandlerStateEnum.SetUp, State);
        }

        try
        {
            State = DownloadHandlerStateEnum.Verifying;

            _verifiedFiles = 0;
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _steamContentClient.CancellationToken);
            await Parallel.ForEachAsync(Files, ParallelOptions(lifetime.Token), VerifyFileWithContextAsync)
                .ConfigureAwait(false);

            _transferSize = TotalQueuedBytes();
            State = DownloadHandlerStateEnum.Verified;

            Logger?.LogTrace("Verification completed");

            DownloadNotifications.Publish(this, VerificationCompleted, new VerificationCompletedArgs(_fileTargets.Keys.ToList()));
        }
        catch
        {
            await Task.WhenAll(_fileWriters.Select(writer => writer.DisposeAsync().AsTask())).ConfigureAwait(false);
            _fileWriters.Clear();
            _fileTargets.Clear();
            _chunks.Clear();
            State = DownloadHandlerStateEnum.SetUp;
            throw;
        }
    }

    public async Task DownloadAsync(CancellationToken cancellationToken = default)
    {
        using var operation = _operation.Enter();
        if (State != DownloadHandlerStateEnum.Verified)
        {
            throw new DownloadHandlerStateMismatchException(DownloadHandlerStateEnum.Verified, State);
        }

        try
        {
            State = DownloadHandlerStateEnum.Downloading;
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _steamContentClient.CancellationToken);
            var token = lifetime.Token;
            token.ThrowIfCancellationRequested();
            foreach (var writer in _fileWriters.Where(writer => writer.ExpectedBytes == 0))
            {
                await writer.WriteAsync(0, []).ConfigureAwait(false);
            }

            if (!_chunks.IsEmpty)
            {
                _serverPool = new SteamCdnServerPool(_steamContentClient, AppId, token) { Logger = Logger };
                _depotKey = await _steamContentClient.GetDepotKeyAsync(DepotId, AppId, token).ConfigureAwait(false);
                await Parallel.ForEachAsync(_chunks, ParallelOptions(token),
                    (chunk, workerToken) => new ValueTask(DownloadChunkAsync(chunk, workerToken))).ConfigureAwait(false);
            }

            token.ThrowIfCancellationRequested();
            State = DownloadHandlerStateEnum.Downloaded;
            Logger?.LogTrace("Completed download tasks");

            DownloadNotifications.Publish(this, DownloadComplete, EventArgs.Empty);
        }
        catch
        {
            State = DownloadHandlerStateEnum.Failed;
            throw;
        }
        finally
        {
            if (_fileWriters.Count > 0)
            {
                Logger?.LogTrace("Disposing all file writers");

                var disposeTasks = _fileWriters.Select(x => x.DisposeAsync().AsTask());
                await Task.WhenAll(disposeTasks).ConfigureAwait(false);
            }

            Logger?.LogTrace("Closing server pool");
            _serverPool?.Close();

        }

        Logger?.LogTrace("Exiting");
    }

    private ParallelOptions ParallelOptions(CancellationToken cancellationToken) => new()
    {
        MaxDegreeOfParallelism = _steamContentClient.MaxConcurrentDownloadsPerTask,
        CancellationToken = cancellationToken,
        TaskScheduler = TaskScheduler.Default
    };

    private ulong TotalQueuedBytes() => _fileTargets.Keys.Aggregate(0UL, (total, file) => total + file.TotalSize);

    private async ValueTask VerifyFileWithContextAsync(ManifestFile file, CancellationToken cancellationToken)
    {
        try
        {
            await VerifyFileAsync(file, DownloadDirectory, _chunks, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SteamDownloadException($"Could not verify '{file.FileName}' in depot {DepotId}: {exception.Message}", exception);
        }
    }

    private async Task VerifyFileAsync(ManifestFile file, string directory, ConcurrentBag<ChunkJob> chunks,
        CancellationToken cancellationToken)
    {
        var filePath = DownloadPathResolver.ResolveFilePath(directory, file.FileName);
        var fileDirectory = Path.GetDirectoryName(filePath);

        Directory.CreateDirectory(fileDirectory);

        if (File.Exists(filePath))
        {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                // If the files are the same length, verify them using their hash
                if ((ulong) fs.Length == file.TotalSize)
                {
                    using var bs = new BufferedStream(fs);
                    using var sha1 = SHA1.Create();

                    // If both files are identical, dont download the file
                    if ((await sha1.ComputeHashAsync(bs, cancellationToken).ConfigureAwait(false)).SequenceEqual(file.FileHash))
                    {
                        PublishVerified(file, false);
                        return;
                    }
                }
            }

        }

        ulong expectedOffset = 0;
        foreach (var chunk in file.ChunkHeaders.OrderBy(chunk => chunk.Offset))
        {
            if (chunk.Offset != expectedOffset || chunk.UncompressedLength > file.TotalSize - expectedOffset
                || chunk.UncompressedLength == 0 || chunk.UncompressedLength > 64 * 1024 * 1024)
            {
                throw new InvalidDataException($"Invalid Steam chunk layout for '{file.FileName}' in depot {DepotId}.");
            }
            expectedOffset += chunk.UncompressedLength;
        }
        if (expectedOffset != file.TotalSize || file.TotalSize > long.MaxValue)
        {
            throw new InvalidDataException($"Steam chunks do not cover '{file.FileName}' in depot {DepotId}.");
        }

        var target = new FileStreamTarget(filePath, file.TotalSize,
            () => DownloadPathResolver.ResolveFilePath(directory, file.FileName));

        _fileTargets.TryAdd(file, target);

        target.TotalBytes = file.TotalSize;
        target.WrittenBytes = 0;

        var fileWriter = new FileWriter(this, file, target, file.TotalSize);

        _fileWriters.Add(fileWriter);

        foreach (var chunk in file.ChunkHeaders)
        {
            chunks.Add(
                new ChunkJob
                {
                    ManifestFile = file,
                    Chunk = chunk,
                    FileWriter = fileWriter,
                    InternalChunk = new ChunkData
                    {
                        Checksum = chunk.Checksum,
                        ChunkID = chunk.Id,
                        CompressedLength = chunk.CompressedLength,
                        Offset = chunk.Offset,
                        UncompressedLength = chunk.UncompressedLength
                    }
                });
        }

        PublishVerified(file, true);
    }

    private void PublishVerified(ManifestFile file, bool requiresDownload)
    {
        var verified = Interlocked.Increment(ref _verifiedFiles);
        DownloadNotifications.Publish(this, FileVerified, new FileVerifiedArgs(file, requiresDownload));
        DownloadNotifications.Publish(this, ProgressChanged, new DownloadProgress(DownloadStage.Verification,
            (double)verified / Files.Count, file.FileName, FileSize: file.TotalSize, AppId: AppId,
            DepotId: DepotId, ManifestId: ManifestId, RequiresDownload: requiresDownload,
            FilesVerified: verified, FilesToVerify: Files.Count));
    }

    private async Task DownloadChunkAsync(ChunkJob chunkJob, CancellationToken cancellationToken)
    {
        byte[] downloadedData = null;
        var writtenBytes = 0;
        var downloaded = false;
        Exception lastException = null;
        string contentServer = null;
        var attemptUsed = 0;

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Server server = null;

            try
            {
                server = await _serverPool.GetServerAsync(cancellationToken).ConfigureAwait(false);
                contentServer = server.Host;
                attemptUsed = attempt;
                downloadedData = new byte[chunkJob.InternalChunk.UncompressedLength];
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(45));

                writtenBytes = await _serverPool.CdnClient.DownloadDepotChunkAsync(
                        DepotId,
                        chunkJob.InternalChunk,
                        server,
                        downloadedData,
                        _depotKey,
                        _serverPool.DesignatedProxyServer).WaitAsync(timeout.Token)
                    .ConfigureAwait(false);

                if (writtenBytes != downloadedData.Length)
                    throw new InvalidDataException($"Steam CDN returned {writtenBytes} decoded bytes instead of {downloadedData.Length}.");

                _serverPool.ReturnServer(server, false);
                server = null;
                downloaded = true;
                break;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (server != null) _serverPool.ReturnServer(server, true);
                throw;
            }
            catch (SteamNoContentServerFoundException)
            {
                throw;
            }
            catch (Exception exception)
            {
                if (server != null) _serverPool.ReturnServer(server, true);
                lastException = exception;
                Logger?.LogWarning(
                    exception,
                    "Chunk download failed for depot {DepotId}; attempt {Attempt} of {MaximumAttempts}.",
                    DepotId,
                    attempt,
                    5);
            }
        }

        if (!downloaded)
        {
            throw new SteamDownloadException(
                $"Steam CDN could not download '{chunkJob.ManifestFile.FileName}' at offset {chunkJob.InternalChunk.Offset} " +
                $"(app {AppId}, depot {DepotId}, manifest {ManifestId}). Last error: {lastException?.GetType().Name}.",
                lastException);
        }

        Logger?.LogTrace("Writing to FileWriter");
        try
        {
            await chunkJob.FileWriter.WriteAsync(chunkJob.InternalChunk.Offset, downloadedData)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw new SteamDownloadTaskFileTargetWrappedException(chunkJob.FileWriter.Target,
                $"Could not write '{chunkJob.ManifestFile.FileName}' at offset {chunkJob.InternalChunk.Offset} in depot {DepotId}: {exception.Message}",
                exception);
        }

        Logger?.LogTrace("Completed downloading chunk");
        var transferred = (ulong)Interlocked.Add(ref _transferBytesWritten, writtenBytes);
        DownloadNotifications.Publish(this, ProgressChanged, new DownloadProgress(DownloadStage.Transfer,
            TotalProgress, chunkJob.ManifestFile.FileName, chunkJob.FileWriter.Target.WrittenBytes,
            chunkJob.ManifestFile.TotalSize, AppId, DepotId, ManifestId, chunkJob.InternalChunk.Offset,
            chunkJob.InternalChunk.CompressedLength, chunkJob.InternalChunk.UncompressedLength, attemptUsed, contentServer,
            TransferBytesWritten: transferred, TransferSize: _transferSize));
    }

    public async ValueTask DisposeAsync()
    {
        if (!_operation.StartDisposal()) return;
        // Dispose all file writers to release potential file streams
        await Task.WhenAll(_fileWriters.Select(x => x.DisposeAsync().AsTask())).ConfigureAwait(false);

        // Dispose the server pool
        try
        {
            _serverPool?.Dispose();
        }
        catch
        {
            // ignored
        }

        _serverPool = null;
    }

    public void Dispose()
    {
        DisposeAsync().GetAwaiter().GetResult();
    }

    internal class FileWriter : IAsyncDisposable
    {
        private readonly AsyncSemaphore _writeLock = new(1);
        private DefaultDownloadHandler _handler;

        public ulong WrittenBytes { get; private set; }
        public ulong ExpectedBytes { get; }
        public ManifestFile ManifestFile { get; }
        public FileTarget Target { get; }
        public bool IsDone { get; private set; }

        public FileWriter(
            DefaultDownloadHandler handler,
            ManifestFile manifestFile,
            FileTarget target,
            ulong expectedBytes)
        {
            _handler = handler;
            Target = target;
            ManifestFile = manifestFile;
            ExpectedBytes = expectedBytes;
        }

        public async ValueTask DisposeAsync()
        {
            await CancelAsync().ConfigureAwait(false);

            _handler = null;
        }

        public async Task WriteAsync(ulong offset, byte[] data)
        {
            using (await _writeLock.LockAsync().ConfigureAwait(false))
            {
                if (IsDone) return;
                await Target.WriteAsync(offset, data).ConfigureAwait(false);
                WrittenBytes += (ulong)data.Length;
                Target.WrittenBytes = WrittenBytes;
                if (WrittenBytes != ExpectedBytes) return;
                await Target.CompleteAsync().ConfigureAwait(false);
                IsDone = true;
            }
            DownloadNotifications.Publish(_handler, _handler.FileDownloaded, ManifestFile);
        }

        public async Task CancelAsync()
        {
            try
            {
                using var semLock = await _writeLock.LockAsync().ConfigureAwait(false);

                await Target.CancelAsync().ConfigureAwait(false);

                IsDone = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _handler?.Logger?.LogWarning(exception, "Could not remove temporary download data for {FileName}.", ManifestFile.FileName);
            }
        }
    }

    internal class ChunkJob
    {
        public FileWriter FileWriter { get; set; }
        public ManifestFile ManifestFile { get; set; }
        public ManifestFileChunkHeader Chunk { get; set; }
        public ChunkData InternalChunk { get; set; }
    }
}
