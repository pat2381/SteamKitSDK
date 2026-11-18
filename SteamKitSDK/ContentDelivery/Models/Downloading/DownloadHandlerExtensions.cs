using System;
using System.Threading;
using System.Threading.Tasks;

namespace SteamKitSDK.ContentDelivery.Models.Downloading;

public sealed record DownloadResult(DownloadStage Stage, Exception Error, DownloadStage? InterruptedStage = null)
{
    public bool IsSuccess => Stage == DownloadStage.Completed;
}

public static class DownloadHandlerExtensions
{
    /// <summary>Runs the download lifecycle on a new handler. The caller retains ownership of the handler.</summary>
    public static async Task<DownloadResult> RunAsync(
        this IDownloadHandler handler,
        string directory,
        Func<ManifestFile, bool> fileCondition = null,
        IProgress<DownloadProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var source = handler as IDownloadProgressSource;
        EventHandler<DownloadProgress> forward = (_, update) => Report(update);
        if (source != null) source.ProgressChanged += forward;
        var stage = DownloadStage.Setup;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Report(new(stage, 0));
            await handler.SetupAsync(directory, fileCondition ?? (_ => true), cancellationToken).ConfigureAwait(false);
            stage = DownloadStage.Verification;
            Report(new(stage, 0));
            await handler.VerifyAsync(cancellationToken).ConfigureAwait(false);
            stage = DownloadStage.Transfer;
            Report(new(stage, handler.TotalProgress));
            await handler.DownloadAsync(cancellationToken).ConfigureAwait(false);
            Report(new(DownloadStage.Completed, 1));
            return new(DownloadStage.Completed, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Report(new(DownloadStage.Cancelled, 0));
            return new(DownloadStage.Cancelled, null, stage);
        }
        catch (Exception exception)
        {
            Report(new(DownloadStage.Failed, 0));
            return new(DownloadStage.Failed, exception, stage);
        }
        finally
        {
            if (source != null) source.ProgressChanged -= forward;
        }

        void Report(DownloadProgress update)
        {
            try { progress?.Report(update); }
            catch (Exception) { }
        }
    }
}
