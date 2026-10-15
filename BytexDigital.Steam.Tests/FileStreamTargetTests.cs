using System;
using System.IO;
using System.Threading.Tasks;
using BytexDigital.Steam.ContentDelivery.Models.Downloading;
using Xunit;

namespace BytexDigital.Steam.Tests;

public sealed class FileStreamTargetTests
{
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
