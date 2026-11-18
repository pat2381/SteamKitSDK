using System;
using SteamKitSDK.Core.Structs;

namespace SteamKitSDK.ContentDelivery.Models.Downloading;

public enum DownloadStage { Setup, Verification, Transfer, Completed, Cancelled, Failed }

public sealed record DownloadProgress(
    DownloadStage Stage,
    double Fraction,
    string FileName = null,
    ulong FileBytesWritten = 0,
    ulong? FileSize = null,
    AppId? AppId = null,
    DepotId? DepotId = null,
    ManifestId? ManifestId = null,
    ulong? ChunkOffset = null,
    uint? ChunkCompressedSize = null,
    uint? ChunkSize = null,
    int? Attempt = null,
    string ContentServer = null,
    bool? RequiresDownload = null,
    int? FilesVerified = null,
    int? FilesToVerify = null,
    ulong? TransferBytesWritten = null,
    ulong? TransferSize = null);

public interface IDownloadProgressSource
{
    event EventHandler<DownloadProgress> ProgressChanged;
}

internal static class DownloadNotifications
{
    internal static void Publish<T>(object sender, EventHandler<T> handlers, T value)
    {
        if (handlers == null) return;
        foreach (EventHandler<T> handler in handlers.GetInvocationList())
        {
            try { handler(sender, value); }
            catch (Exception) { }
        }
    }
}
