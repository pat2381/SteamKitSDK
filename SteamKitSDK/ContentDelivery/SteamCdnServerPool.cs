using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamKit2;
using SteamKit2.CDN;
using SteamKitSDK.ContentDelivery.Exceptions;
using SteamKitSDK.Core.Structs;

namespace SteamKitSDK.ContentDelivery;

#nullable enable

public class SteamCdnServerPool : IDisposable
{
    private const int MinimumPoolSize = 10;
    private readonly AppId _appId;
    private readonly SteamContentClient _steamContentClient;
    private readonly CancellationTokenSource _lifetime;
    private readonly CancellationToken _lifetimeToken;
    private readonly Task _populatorTask;
    private readonly Channel<Server> _servers = Channel.CreateUnbounded<Server>();
    private int _closed;

    public ILogger? Logger { get; set; }
    public Client CdnClient { get; }
    public Server? DesignatedProxyServer { get; private set; }
    public bool IsExhausted { get; private set; }

    public SteamCdnServerPool(SteamContentClient steamContentClient, AppId appId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(steamContentClient);
        _steamContentClient = steamContentClient;
        _appId = appId;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, steamContentClient.CancellationToken);
        _lifetimeToken = _lifetime.Token;
        CdnClient = new Client(steamContentClient.SteamClient.InternalClient);
        _populatorTask = PopulateAsync();
    }

    public Server GetServer(CancellationToken cancellationToken = default)
        => GetServerAsync(cancellationToken).AsTask().GetAwaiter().GetResult();

    public async ValueTask<Server> GetServerAsync(CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            deadline.Token.ThrowIfCancellationRequested();
            return await _servers.Reader.ReadAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (ChannelClosedException exception) when (!deadline.Token.IsCancellationRequested)
        {
            throw new SteamNoContentServerFoundException(_appId, exception.InnerException ?? exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested && !_lifetimeToken.IsCancellationRequested)
        {
            throw new SteamNoContentServerFoundException(_appId, exception);
        }
    }

    public void ReturnServer(Server server, bool isFaulty)
    {
        if (!isFaulty && !_lifetimeToken.IsCancellationRequested) _servers.Writer.TryWrite(server);
    }

    private async Task PopulateAsync()
    {
        try
        {
            while (!_lifetimeToken.IsCancellationRequested)
            {
                if (_servers.Reader.Count < MinimumPoolSize)
                {
                    var servers = await GetServerListAsync().ConfigureAwait(false);
                    var suitable = servers.Where(server =>
                            (server.Type == "SteamCache" || server.Type == "CDN")
                            && (server.AllowedAppIds.Length == 0 || server.AllowedAppIds.Contains(_appId)))
                        .OrderBy(server => server.WeightedLoad).ToList();
                    if (suitable.Count == 0) throw new SteamNoContentServerFoundException(_appId);

                    DesignatedProxyServer = servers.FirstOrDefault(server => server.UseAsProxy);
                    foreach (var server in suitable) _servers.Writer.TryWrite(server);
                }

                await Task.Delay(TimeSpan.FromSeconds(5), _lifetimeToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            IsExhausted = true;
            _servers.Writer.TryComplete(exception);
        }
        finally
        {
            _servers.Writer.TryComplete();
        }
    }

    private async Task<IReadOnlyCollection<Server>> GetServerListAsync()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Logger?.LogTrace("Discovering Steam CDN servers for app {AppId}, attempt {Attempt}", _appId, attempt);
                return await ContentServerDirectoryService.LoadAsync(
                    _steamContentClient.SteamClient.InternalClient.Configuration,
                    _steamContentClient.SteamClient.ActiveCellId, deadline.Token)
                    .WaitAsync(deadline.Token).ConfigureAwait(false)
                    ?? throw new SteamNoContentServerFoundException(_appId);
            }
            catch (SteamKitWebRequestException exception) when (exception.StatusCode == HttpStatusCode.TooManyRequests && attempt < 3)
            {
                await Task.Delay(TimeSpan.FromSeconds(5 * attempt), deadline.Token).ConfigureAwait(false);
            }
        }
    }

    public void Dispose() => Close();

    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        _lifetime.Cancel();
        CdnClient.Dispose();
        _ = DisposeLifetimeAsync();
    }

    private async Task DisposeLifetimeAsync()
    {
        await _populatorTask.ConfigureAwait(false);
        _lifetime.Dispose();
    }
}
