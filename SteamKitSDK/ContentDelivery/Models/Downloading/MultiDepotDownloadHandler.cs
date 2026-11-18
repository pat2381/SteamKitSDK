using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SteamKitSDK.ContentDelivery.Exceptions;

namespace SteamKitSDK.ContentDelivery.Models.Downloading;

public class MultiDepotDownloadHandler : IDownloadHandler, IDownloadProgressSource
{
    private readonly IReadOnlyList<IDownloadHandler> _multipleFilesHandlers;
    private int _completedVerifications;
    private readonly DownloadOperation _operation = new();

    /// <summary>
    /// Returns all <see cref="IDownloadHandler"/> that were directly passed to its constructors.
    /// </summary>
    public IReadOnlyList<IDownloadHandler> Handlers => _multipleFilesHandlers;

    public MultiDepotDownloadHandler(List<IDownloadHandler> multipleFilesHandlers)
    {
        ArgumentNullException.ThrowIfNull(multipleFilesHandlers);
        if (multipleFilesHandlers.Count == 0)
        {
            throw new ArgumentException("At least one download handler is required.", nameof(multipleFilesHandlers));
        }

        _multipleFilesHandlers = Array.AsReadOnly(multipleFilesHandlers.ToArray());

        foreach (var handler in _multipleFilesHandlers)
        {
            handler.FileVerified += OnFileVerified;
            handler.FileDownloaded += OnFileDownloaded;
            if (handler is IDownloadProgressSource source) source.ProgressChanged += OnProgressChanged;
        }
    }

    public string DownloadDirectory { get; protected set; }
    public Func<ManifestFile, bool> FileCondition { get; protected set; }
    public DownloadHandlerStateEnum State { get; protected set; }

    public double TotalProgress
    {
        get
        {
            if (State == DownloadHandlerStateEnum.Downloaded) return 1;
            var total = TotalFileSize;
            return total == 0
                ? Handlers.Average(handler => handler.TotalProgress)
                : Handlers.Sum(handler => handler.TotalFileSize * handler.TotalProgress) / total;
        }
    }
    public int TotalFileCount => _multipleFilesHandlers.Sum(x => x.TotalFileCount);
    public ulong TotalFileSize => _multipleFilesHandlers.Aggregate(0UL, (total, handler) => total + handler.TotalFileSize);

    public IReadOnlyList<ManifestFile> Files
    {
        get { return Handlers.Count == 0 ? [] : Handlers.SelectMany(h => h.Files).ToList(); }
    }

    public event EventHandler<FileVerifiedArgs> FileVerified;
    public event EventHandler<VerificationCompletedArgs> VerificationCompleted;
    public event EventHandler<ManifestFile> FileDownloaded;
    public event EventHandler<EventArgs> DownloadComplete;
    public event EventHandler<DownloadProgress> ProgressChanged;

    private void OnFileVerified(object sender, FileVerifiedArgs args)
        => DownloadNotifications.Publish(sender, FileVerified, args);

    private void OnFileDownloaded(object sender, ManifestFile file)
        => DownloadNotifications.Publish(sender, FileDownloaded, file);

    private void OnProgressChanged(object sender, DownloadProgress progress)
        => DownloadNotifications.Publish(this, ProgressChanged, progress with
        {
            Fraction = progress.Stage == DownloadStage.Verification
                ? (_completedVerifications + progress.Fraction) / Handlers.Count
                : TotalProgress
        });

    /// <summary>
    /// Returns all <see cref="IDownloadHandler"/> nested within any child that are either of type <see cref="DefaultDownloadHandler"/> or <see cref="LegacyDownloadHandler"/>.
    /// </summary>
    public IReadOnlyList<IDownloadHandler> GetActualHandlers()
    {
        return _multipleFilesHandlers.SelectMany(x =>
            {
                if (x is MultiDepotDownloadHandler multiDepotDownloadHandler)
                {
                    return multiDepotDownloadHandler.GetActualHandlers();
                }
                else
                {
                    return new List<IDownloadHandler> { x };
                }
            })
            .ToList();
    }
    
    public async Task SetupAsync(
        string directory,
        Func<ManifestFile, bool> condition,
        CancellationToken cancellationToken = default)
    {
        using var operation = _operation.Enter();
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(condition);

        if (State != DownloadHandlerStateEnum.Created)
        {
            throw new DownloadHandlerStateMismatchException(DownloadHandlerStateEnum.Created, State);
        }

        DownloadDirectory = directory;
        FileCondition = condition;

        foreach (var handler in _multipleFilesHandlers)
        {
            await handler.SetupAsync(directory, condition, cancellationToken).ConfigureAwait(false);
        }

        State = DownloadHandlerStateEnum.SetUp;
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
            _completedVerifications = 0;
            var filesToDownload = new System.Collections.Concurrent.ConcurrentBag<ManifestFile>();

            foreach (var handler in _multipleFilesHandlers)
            {
                void Collect(object sender, FileVerifiedArgs args)
                {
                    if (args.RequiresDownload)
                    {
                        filesToDownload.Add(args.ManifestFile);
                    }
                }

                handler.FileVerified += Collect;
                try { await handler.VerifyAsync(cancellationToken).ConfigureAwait(false); }
                finally { handler.FileVerified -= Collect; }
                _completedVerifications++;
            }

            State = DownloadHandlerStateEnum.Verified;

            DownloadNotifications.Publish(this, VerificationCompleted, new VerificationCompletedArgs(filesToDownload.ToList()));
        }
        catch
        {
            State = DownloadHandlerStateEnum.Failed;
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
            foreach (var handler in _multipleFilesHandlers)
            {
                // Start download
                await handler.DownloadAsync(cancellationToken).ConfigureAwait(false);
            }

            State = DownloadHandlerStateEnum.Downloaded;
            DownloadNotifications.Publish(this, DownloadComplete, EventArgs.Empty);
        }
        catch
        {
            State = DownloadHandlerStateEnum.Failed;
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_operation.StartDisposal()) return;
        foreach (var handler in _multipleFilesHandlers)
        {
            try
            {
                handler.FileVerified -= OnFileVerified;
                handler.FileDownloaded -= OnFileDownloaded;
                if (handler is IDownloadProgressSource source) source.ProgressChanged -= OnProgressChanged;
                await handler.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // ignore..
            }
        }
    }

    public void Dispose()
    {
        DisposeAsync().GetAwaiter().GetResult();
    }
}
