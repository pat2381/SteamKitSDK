using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BytexDigital.Steam.ContentDelivery.Models;
using BytexDigital.Steam.ContentDelivery.Models.Downloading;
using Xunit;

namespace BytexDigital.Steam.Tests;

public sealed class MultiDepotDownloadHandlerTests
{
    [Fact]
    public void Constructor_RejectsEmptyHandlerList()
    {
        Assert.Throws<ArgumentException>(() => new MultiDepotDownloadHandler([]));
    }

    [Fact]
    public async Task DownloadAsync_WhenAllChildrenSucceed_MarksDownloadAsCompleted()
    {
        var child = new TestDownloadHandler();
        await using var handler = new MultiDepotDownloadHandler([child]);

        await handler.SetupAsync("downloads", _ => true);
        await handler.VerifyAsync();
        await handler.DownloadAsync();

        Assert.Equal(DownloadHandlerStateEnum.Downloaded, handler.State);
        Assert.Equal(1, child.DownloadCalls);
    }

    [Fact]
    public async Task DownloadAsync_WhenAChildFails_PropagatesFailureAndMarksHandlerFailed()
    {
        var child = new TestDownloadHandler { DownloadFailure = new InvalidOperationException("download failed") };
        await using var handler = new MultiDepotDownloadHandler([child]);

        await handler.SetupAsync("downloads", _ => true);
        await handler.VerifyAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.DownloadAsync());

        Assert.Equal(DownloadHandlerStateEnum.Failed, handler.State);
    }

    private sealed class TestDownloadHandler : IDownloadHandler
    {
        public int DownloadCalls { get; private set; }
        public Exception? DownloadFailure { get; init; }
        public double TotalProgress { get; private set; }
        public int TotalFileCount => 1;
        public ulong TotalFileSize => 1;
        public string DownloadDirectory { get; private set; } = string.Empty;
        public Func<ManifestFile, bool> FileCondition { get; private set; } = _ => true;
        public DownloadHandlerStateEnum State { get; private set; }
        public IReadOnlyList<ManifestFile> Files { get; } = Array.Empty<ManifestFile>();

        public event EventHandler<FileVerifiedArgs>? FileVerified
        {
            add { }
            remove { }
        }

        public event EventHandler<VerificationCompletedArgs>? VerificationCompleted;
        public event EventHandler<ManifestFile>? FileDownloaded
        {
            add { }
            remove { }
        }

        public event EventHandler<EventArgs>? DownloadComplete;

        public Task SetupAsync(string directory, Func<ManifestFile, bool> condition, CancellationToken cancellationToken = default)
        {
            DownloadDirectory = directory;
            FileCondition = condition;
            State = DownloadHandlerStateEnum.SetUp;
            return Task.CompletedTask;
        }

        public Task VerifyAsync(CancellationToken cancellationToken = default)
        {
            State = DownloadHandlerStateEnum.Verified;
            VerificationCompleted?.Invoke(this, new VerificationCompletedArgs([]));
            return Task.CompletedTask;
        }

        public Task DownloadAsync(CancellationToken cancellationToken = default)
        {
            DownloadCalls++;
            if (DownloadFailure is not null)
            {
                throw DownloadFailure;
            }

            State = DownloadHandlerStateEnum.Downloaded;
            TotalProgress = 1;
            DownloadComplete?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }
}
