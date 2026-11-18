using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SteamKitSDK.ContentDelivery.Models.Downloading;
using SteamKitSDK.ContentDelivery.Exceptions;
using Xunit;

namespace SteamKitSDK.Tests;

public sealed class LegacyDownloadHandlerTests
{
    [Fact]
    public async Task RunAsync_WhenServerRejectsRequest_ReturnsHttpErrorAndKeepsInstalledFile()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "content.bin");
            await File.WriteAllTextAsync(path, "installed");
            using var client = new HttpClient(new StubHttpHandler(HttpStatusCode.Forbidden));
            using var handler = new LegacyDownloadHandler("https://example.invalid/file?token=secret", "content.bin", client);
            var result = await handler.RunAsync(directory.FullName);

            Assert.Equal(DownloadStage.Failed, result.Stage);
            Assert.Equal(DownloadStage.Transfer, result.InterruptedStage);
            var error = Assert.IsType<SteamDownloadException>(result.Error);
            Assert.Contains("HTTP 403", error.Message);
            Assert.DoesNotContain("secret", error.ToString());
            Assert.Equal("installed", await File.ReadAllTextAsync(path));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task RunAsync_ReportsVerificationAndTransferDespiteThrowingSubscriber()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            using var client = new HttpClient(new StubHttpHandler(HttpStatusCode.OK));
            using var handler = new LegacyDownloadHandler("https://example.invalid/content", "content.bin", client);
            handler.ProgressChanged += (_, _) => throw new InvalidOperationException("observer failure");
            var updates = new System.Collections.Generic.List<DownloadProgress>();
            var result = await handler.RunAsync(directory.FullName, progress: new CaptureProgress(updates));

            Assert.True(result.IsSuccess);
            Assert.Contains(updates, update => update.Stage == DownloadStage.Verification && update.RequiresDownload == true);
            Assert.Contains(updates, update => update.Stage == DownloadStage.Transfer && update.TransferBytesWritten > 0);
            Assert.Equal(DownloadStage.Completed, updates[^1].Stage);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void FromSteamWorkshop_RejectsUnapprovedEndpoint()
    {
        Assert.Throws<SteamDownloadException>(() =>
            LegacyDownloadHandler.FromSteamWorkshop("https://example.invalid/file", "content.bin", _ => false));
    }

    private sealed class CaptureProgress(System.Collections.Generic.List<DownloadProgress> updates) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => updates.Add(value);
    }

    private sealed class StubHttpHandler(HttpStatusCode status, byte[]? bytes = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = bytes == null ? new StringContent("sample content") : new ByteArrayContent(bytes)
            });
    }

    [Fact]
    public async Task DownloadAsync_WhenCancelled_PropagatesCancellationAndResetsState()
    {
        using var handler = new LegacyDownloadHandler("https://example.invalid/content", "content.bin");
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await handler.SetupAsync(Path.GetTempPath(), _ => true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handler.DownloadAsync(cancellationTokenSource.Token));

        Assert.Equal(DownloadHandlerStateEnum.SetUp, handler.State);
    }

    [Fact]
    public async Task SetupAsync_RejectsFileNameOutsideDownloadDirectory()
    {
        var pathOutsideDownloadDirectory = Path.Combine("..", "outside.bin");
        using var handler = new LegacyDownloadHandler("https://example.invalid/content", pathOutsideDownloadDirectory);

        await Assert.ThrowsAsync<ArgumentException>(
            () => handler.SetupAsync(Path.GetTempPath(), _ => true));
    }

    [Fact]
    public async Task DownloadAsync_DownloadsContentAndRaisesFileDownloaded()
    {
        const string fileName = "content.bin";
        var content = Encoding.UTF8.GetBytes("Steam content downloaded through HttpClient.");
        var downloadDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var fileDownloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            using var client = new HttpClient(new StubHttpHandler(HttpStatusCode.OK, content));
            using var handler = new LegacyDownloadHandler($"https://example.invalid/{fileName}", fileName, client);
            handler.FileDownloaded += (_, _) => fileDownloaded.TrySetResult();

            await handler.SetupAsync(downloadDirectory, _ => true);
            await handler.VerifyAsync();
            await handler.DownloadAsync();

            await fileDownloaded.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var downloadedContent = await File.ReadAllBytesAsync(Path.Combine(downloadDirectory, fileName));
            Assert.Equal(content, downloadedContent);
            Assert.Equal(1, handler.TotalProgress);
            Assert.Equal((ulong)content.Length, handler.TotalFileSize);
            Assert.Equal(DownloadHandlerStateEnum.Downloaded, handler.State);
        }
        finally
        {
            if (Directory.Exists(downloadDirectory))
            {
                Directory.Delete(downloadDirectory, recursive: true);
            }
        }
    }

}
