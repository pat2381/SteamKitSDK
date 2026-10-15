using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BytexDigital.Steam.ContentDelivery.Models.Downloading;
using Xunit;

namespace BytexDigital.Steam.Tests;

public sealed class LegacyDownloadHandlerTests
{
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
        using var handler = new LegacyDownloadHandler("https://example.invalid/content", "..\\outside.bin");

        await Assert.ThrowsAsync<ArgumentException>(
            () => handler.SetupAsync(Path.GetTempPath(), _ => true));
    }

    [Fact]
    public async Task DownloadAsync_DownloadsContentAndRaisesFileDownloaded()
    {
        const string fileName = "content.bin";
        var content = Encoding.UTF8.GetBytes("Steam content downloaded through HttpClient.");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serveRequest = ServeOnceAsync(listener, content);
        var downloadDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var fileDownloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            using var handler = new LegacyDownloadHandler($"http://127.0.0.1:{endpoint.Port}/{fileName}", fileName);
            handler.FileDownloaded += (_, _) => fileDownloaded.TrySetResult();

            await handler.SetupAsync(downloadDirectory, _ => true);
            await handler.VerifyAsync();
            await handler.DownloadAsync();

            await serveRequest;
            await fileDownloaded.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var downloadedContent = await File.ReadAllBytesAsync(Path.Combine(downloadDirectory, fileName));
            Assert.Equal(content, downloadedContent);
            Assert.Equal(1, handler.TotalProgress);
            Assert.Equal((ulong)content.Length, handler.TotalFileSize);
            Assert.Equal(DownloadHandlerStateEnum.Downloaded, handler.State);
        }
        finally
        {
            listener.Stop();

            if (Directory.Exists(downloadDirectory))
            {
                Directory.Delete(downloadDirectory, recursive: true);
            }
        }
    }

    private static async Task ServeOnceAsync(TcpListener listener, byte[] content)
    {
        using var client = await listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();

        var requestBuffer = new byte[4096];
        _ = await stream.ReadAsync(requestBuffer);

        var headers = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Length: {content.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(headers);
        await stream.WriteAsync(content);
    }
}
