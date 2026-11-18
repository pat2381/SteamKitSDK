using System;
using System.IO;
using System.Threading.Tasks;
using System.Security.Cryptography;
using SteamKitSDK.ContentDelivery;
using SteamKitSDK.ContentDelivery.Models;
using SteamKitSDK.Core;
using SteamKitSDK.ContentDelivery.Models.Downloading;
using Xunit;

namespace SteamKitSDK.Tests;

public sealed class FileStreamTargetTests
{
    [Fact]
    public async Task RunAsync_MaterializesEmptyDepotFileWithoutCdnConnection()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            using var steamClient = new SteamClient();
            await using var contentClient = new SteamContentClient(steamClient);
            var file = new ManifestFile("empty.bin", [], default, 0, SHA1.HashData([]));
            var manifest = new Manifest([file], 480, 1, DateTime.UtcNow, 0, 0);
            await using var handler = new DefaultDownloadHandler(contentClient, manifest, 480, 480);
            var result = await handler.RunAsync(directory.FullName);

            Assert.True(result.IsSuccess, result.Error?.ToString());
            Assert.Equal(0, new FileInfo(Path.Combine(directory.FullName, file.FileName)).Length);
            Assert.Equal(1, handler.TotalProgress);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task CancelAndCompleteBeforeWrite_AreNoOps()
    {
        var filePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var target = new FileStreamTarget(filePath, 4);

        try
        {
            await target.CancelAsync();
            await target.CompleteAsync();

            Assert.False(File.Exists(filePath));
        }
        finally
        {
            await target.DisposeAsync();
        }
    }

    [Fact]
    public async Task WriteAsync_WritesAtOffsetAndTracksProgress()
    {
        var filePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var target = new FileStreamTarget(filePath, 8);

        try
        {
            await target.WriteAsync(2, [1, 2, 3, 4]);
            await target.WriteAsync(0, [9, 8]);
            await target.CompleteAsync();

            var content = await File.ReadAllBytesAsync(filePath);
            Assert.Equal(new byte[] { 9, 8, 1, 2, 3, 4, 0, 0 }, content);
            Assert.Equal(6UL, target.WrittenBytes);
            Assert.Equal(8UL, target.TotalBytes);
            Assert.Equal(0.75, target.Progress, precision: 3);
        }
        finally
        {
            await target.DisposeAsync();

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }
}
