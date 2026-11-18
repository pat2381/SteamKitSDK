using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SteamKitSDK.ContentDelivery.Exceptions;
using SteamKitSDK.Core.Exceptions;
using SteamKitSDK.Core.Regional;
using Nito.AsyncEx;
using SteamKit2.Authentication;
using SteamKit2.Discovery;
using SteamKit2.Internal;
using SteamKit = SteamKit2;

namespace SteamKitSDK.Core
{
#nullable enable

    public class SteamClient : IDisposable
    {
        public enum SteamClientState
        {
            Created,
            Running,
            ShutDown
        }

        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly AsyncManualResetEvent _clientReadyEvent = new AsyncManualResetEvent(false);
        internal readonly SteamKit.SteamApps _steamAppsHandler;
        private readonly SteamAuthenticator? _steamAuthenticator;
        internal readonly SteamKit.SteamContent _steamContentHandler;
        internal readonly SteamKit.SteamUser _steamUserHandler;
        internal AsyncManualResetEvent _licensesReceived = new AsyncManualResetEvent(false);
        private int _logonAttemptsCounter;
        private int _cmAttemptsCounter;
        private int _connectStarted;
        private int _disposed;
        private int _reconnectScheduled;
        private int _reconnectAttempts;
        private readonly object _notificationLock = new();
        private Task _lastNotification = Task.CompletedTask;

        public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>
        ///     Credentials that this instance will use to log in after connecting.
        /// </summary>
        public SteamCredentials Credentials { get; }

        /// <summary>
        ///     Internal client provided by the SteamKit library.
        /// </summary>
        public SteamKit.SteamClient InternalClient { get; }

        /// <summary>
        ///     Server that the client will be forced to connect to.
        ///     <para>If unavailable, the client will attempt reconnecting until a shutdown is forced.</para>
        /// </summary>
        public ServerRecord? ForcedServer { get; set; }

        public CancellationToken CancellationToken => _cancellationTokenSource.Token;

        /// <summary>
        ///     Indicates whether the client is ready for any operation.
        /// </summary>
        public bool IsConnected => _clientReadyEvent.IsSet && _licensesReceived.IsSet && !CancellationToken.IsCancellationRequested;

        /// <summary>
        ///     Indicates whether the client is faulted. See <see cref="FaultException" /> for more information.
        /// </summary>
        public bool IsFaulted => FaultException != null;

        /// <summary>
        ///     State of the client. Once in the <see cref="SteamClientState.ShutDown" /> state, it cannot be used anymore.
        /// </summary>
        public SteamClientState State { get; protected set; } = SteamClientState.Created;

        public int MaximumLogonAttempts { get; set; } = 1;
        public int MaximumCmServerAttempts { get; set; } = 15;
        public uint ActiveCellId { get; private set; }
        public Exception? FaultException { get; private set; }

        internal SteamKit.CallbackManager CallbackManager { get; set; }
        internal IList<SteamKit.SteamApps.LicenseListCallback.License> Licenses { get; set; } =
            new List<SteamKit.SteamApps.LicenseListCallback.License>();

        public SteamClient(SteamCredentials? credentials = null) : this(
            credentials,
            default,
            default)
        {
        }

        public SteamClient(
            SteamCredentials? credentials,
            SteamAuthenticator? steamAuthenticator,
            Action<SteamKit.ISteamConfigurationBuilder>? steamConfigurationBuilder = null,
            IRegionSpecificServerListProvider? serverListProvider = null)
        {
            // Default values
            credentials ??= SteamCredentials.Anonymous;
            steamConfigurationBuilder ??= builder => { };

            Credentials = credentials;
            _steamAuthenticator = steamAuthenticator;

            if (!Credentials.IsAnonymous && _steamAuthenticator is null)
            {
                throw new SteamAuthenticatorRequiredException();
            }

            // If the caller has provided a region specific server list,
            // make sure we also use it in our SteamConfiguration object.
            if (serverListProvider != null)
            {
                var userPassedBuilder = steamConfigurationBuilder;

                steamConfigurationBuilder = builder =>
                {
                    builder.WithServerListProvider(serverListProvider);

                    // Invoke the user passed config builder afterwards
                    userPassedBuilder.Invoke(builder);
                };
            }

            // SteamKit2 SteamConfiguration object
            var configuration = SteamKit.SteamConfiguration.Create(steamConfigurationBuilder);

            // Region specific server list providers are provided the SteamConfiguration object to make API calls,
            // for example to ISteamDirectory.
            serverListProvider?.SetSteamConfiguration(configuration);

            // Initialize SteamClient with the configuration object
            InternalClient = new SteamKit.SteamClient(configuration);

            _cancellationTokenSource = new CancellationTokenSource();
            CallbackManager = new SteamKit.CallbackManager(InternalClient);

            _steamUserHandler = InternalClient.GetHandler<SteamKit.SteamUser>()
                ?? throw new InvalidOperationException("SteamKit did not provide a SteamUser handler.");
            _steamAppsHandler = InternalClient.GetHandler<SteamKit.SteamApps>()
                ?? throw new InvalidOperationException("SteamKit did not provide a SteamApps handler.");
            _steamContentHandler = InternalClient.GetHandler<SteamKit.SteamContent>()
                ?? throw new InvalidOperationException("SteamKit did not provide a SteamContent handler.");

            CallbackManager.Subscribe<SteamKit.SteamClient.ConnectedCallback>(OnConnected);
            CallbackManager.Subscribe<SteamKit.SteamClient.DisconnectedCallback>(OnDisconnected);
            CallbackManager.Subscribe<SteamKit.SteamUser.LoggedOnCallback>(OnLoggedOn);
            CallbackManager.Subscribe<SteamKit.SteamUser.LoggedOffCallback>(OnLoggedOff);
            CallbackManager.Subscribe<SteamKit.SteamApps.LicenseListCallback>(OnLicenseList);

            _ = Task.Run(CallbackManagerHandlerAsync);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            State = SteamClientState.ShutDown;
            
            FaultException ??= new SteamClientDisposedException();
            
            _clientReadyEvent.Reset();
            _cancellationTokenSource.Cancel();
            
            InternalClient.Disconnect();
        }

        /// <summary>
        ///     Event handler that fires upon exception that are produced by user code.
        /// </summary>
        public event EventHandler<Exception>? OnUnhandledException;

        public event Action? InternalClientAttemptingConnect;
        public event Action? InternalClientConnected;
        public event Action? InternalClientDisconnected;
        public event Action? InternalClientLoggedOn;
        public event Action? InternalClientLoggedOff;
        public event EventHandler<SteamConnectionProgress>? ConnectionProgressChanged;

        public void Shutdown()
        {
            Dispose();
        }

        /// <summary>
        ///     Asks the underlying client to connect to Steam and perform a login with the given <see cref="Credentials" />.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <exception cref="SteamClientFaultedException">Client is faulted.</exception>
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CancellationToken.IsCancellationRequested) throw new SteamClientDisposedException();
            using var timeout = new CancellationTokenSource(ConnectionTimeout);
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            if (Interlocked.Exchange(ref _connectStarted, 1) != 0) throw new SteamClientAlreadyRunningException();
            State = SteamClientState.Running;
            if (Volatile.Read(ref _disposed) != 0)
            {
                State = SteamClientState.ShutDown;
                throw new SteamClientDisposedException();
            }

            try
            {
                PublishConnection(new(SteamConnectionStage.Connecting), InternalClientAttemptingConnect);
                if (CancellationToken.IsCancellationRequested) throw new SteamClientDisposedException();
                InternalClient.Connect(ForcedServer);
                if (CancellationToken.IsCancellationRequested)
                {
                    InternalClient.Disconnect();
                }

                await AwaitReadyCoreAsync(lifetime.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
            {
                var exception = new TimeoutException("Steam connection, logon or license retrieval exceeded the configured connection timeout.");
                SetFaultedAndShutdown(exception);
                throw exception;
            }
            catch
            {
                Shutdown();
                throw;
            }
        }

        /// <summary>
        ///     Awaits client to be ready for operational use. Throws exception in any other case.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Awaitable.</returns>
        /// <exception cref="SteamClientFaultedException"></exception>
        /// <exception cref="SteamClientNotReadyException"></exception>
        public async Task AwaitReadyAsync(CancellationToken cancellationToken)
        {
            using var timeout = new CancellationTokenSource(ConnectionTimeout);
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            try
            {
                await AwaitReadyCoreAsync(lifetime.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
            {
                throw new TimeoutException("Steam did not become ready within the configured connection timeout.");
            }
        }

        private async Task AwaitReadyCoreAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (State == SteamClientState.Created) throw new SteamClientNotReadyException();
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, CancellationToken);
            try
            {
                await _clientReadyEvent.WaitAsync(lifetime.Token).ConfigureAwait(false);
                await _licensesReceived.WaitAsync(lifetime.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && IsFaulted)
            {
                throw new SteamClientFaultedException(FaultException!);
            }
            if (IsFaulted) throw new SteamClientFaultedException(FaultException!);
            if (!IsConnected) throw new SteamClientNotReadyException();
        }

        private void AttemptLogin()
        {
            if (!Credentials.IsAnonymous)
            {
                _ = Task.Run(
                    async () =>
                    {
                        if (Interlocked.Increment(ref _logonAttemptsCounter) > MaximumLogonAttempts)
                        {
                            SetFaultedAndShutdown(new SteamConnectException());
                            return;
                        }
                        var accessToken = string.Empty;
                        
                        try
                        {
                            var authenticator = _steamAuthenticator ?? throw new SteamAuthenticatorRequiredException();
                            accessToken = await authenticator.GetAccessTokenAsync(_cancellationTokenSource.Token) ?? string.Empty;

                            if (string.IsNullOrEmpty(accessToken))
                            {
                                var guardData = await authenticator.GetGuardDataAsync(
                                    _cancellationTokenSource
                                        .Token);

                                // Begin authenticating via credentials
                                var authSession = await InternalClient.Authentication
                                    .BeginAuthSessionViaCredentialsAsync(
                                        new AuthSessionDetails
                                        {
                                            Username = Credentials.Username,
                                            Password = Credentials.Password,
                                            IsPersistentSession = false,
                                            Authenticator = new SteamAuthenticatorAdapter(authenticator, CancellationToken),
                                            GuardData = string.IsNullOrEmpty(guardData) ? null : guardData
                                        })
                                    .WaitAsync(_cancellationTokenSource.Token);

                                var pollResponse = await authSession.PollingWaitForResultAsync(CancellationToken)
                                    .WaitAsync(CancellationToken);

                                accessToken = pollResponse.RefreshToken;

                                await authenticator.PersistAccessTokenAsync(accessToken,
                                    _cancellationTokenSource.Token);

                                if (!string.IsNullOrEmpty(pollResponse.NewGuardData))
                                {
                                    await authenticator.PersistGuardDataAsync(pollResponse.NewGuardData,
                                        _cancellationTokenSource.Token);
                                }
                            }
                        }
                        catch (AuthenticationException ex)
                        {
                            SetFaultedAndShutdown(new SteamAuthenticationException(ex.Result));

                            return;
                        }
                        catch (Exception ex)
                        {
                            if (CancellationToken.IsCancellationRequested) return;
                            SetFaultedAndShutdown(new InvalidOperationException("Steam token retrieval or persistence failed.", ex));
                            return;
                        }
                        
                        if (CancellationToken.IsCancellationRequested) return;
                        try
                        {
                            _steamUserHandler.LogOn(
                                new SteamKit.SteamUser.LogOnDetails
                                {
                                    Username = Credentials.Username,
                                    AccessToken = accessToken,
                                    LoginID = (uint)Random.Shared.Next(100000000, 999999999)
                                });
                        }
                        catch (Exception ex)
                        {
                            SetFaultedAndShutdown(new InvalidOperationException("SteamKit could not submit the logon request.", ex));
                        }
                    });
            }
            else
            {
                _steamUserHandler.LogOnAnonymous();
            }
        }

        private async Task CallbackManagerHandlerAsync()
        {
            try
            {
                while (!CancellationToken.IsCancellationRequested)
                {
                    await CallbackManager.RunWaitCallbackAsync(CancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                SetFaultedAndShutdown(new InvalidOperationException("The SteamKit callback loop failed.", ex));
            }
        }

        private void OnLicenseList(SteamKit.SteamApps.LicenseListCallback callback)
        {
            if (callback.Result != SteamKit.EResult.OK)
            {
                PublishConnection(new(SteamConnectionStage.LicenseResponse, callback.Result));
                SetFaultedAndShutdown(new SteamAuthenticationException(callback.Result));
                return;
            }
            Licenses = new List<SteamKit.SteamApps.LicenseListCallback.License>(callback.LicenseList);

            _licensesReceived.Set();
            PublishConnection(new(SteamConnectionStage.LicenseResponse, callback.Result, LicenseCount: Licenses.Count));
        }

        private void OnConnected(SteamKit.SteamClient.ConnectedCallback callback)
        {
            PublishConnection(new(SteamConnectionStage.Connected), InternalClientConnected);
            if (CancellationToken.IsCancellationRequested) return;
            AttemptLogin();
        }

        private void OnDisconnected(SteamKit.SteamClient.DisconnectedCallback callback)
        {
            _clientReadyEvent.Reset();
            _licensesReceived.Reset();
            PublishConnection(new(SteamConnectionStage.Disconnected, UserInitiated: callback.UserInitiated), InternalClientDisconnected);

            if (callback.UserInitiated) return;

            if (CancellationToken.IsCancellationRequested) return;
            if (Interlocked.Exchange(ref _reconnectScheduled, 1) != 0) return;
            _ = ReconnectAsync();
        }

        private async Task ReconnectAsync()
        {
            try
            {
                var attempt = Interlocked.Increment(ref _reconnectAttempts);
                if (attempt > MaximumCmServerAttempts)
                {
                    SetFaultedAndShutdown(new SteamNoCmServerFoundException());
                    return;
                }
                await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken).ConfigureAwait(false);
                Interlocked.Exchange(ref _reconnectScheduled, 0);
                PublishConnection(new(SteamConnectionStage.Connecting, ReconnectAttempt: attempt), InternalClientAttemptingConnect);
                if (CancellationToken.IsCancellationRequested) return;
                InternalClient.Connect(ForcedServer);
                if (CancellationToken.IsCancellationRequested) InternalClient.Disconnect();
            }
            catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                SetFaultedAndShutdown(new InvalidOperationException("SteamKit could not reconnect.", ex));
            }
        }

        private void OnLoggedOn(SteamKit.SteamUser.LoggedOnCallback callback)
        {
            if (CancellationToken.IsCancellationRequested) return;
            PublishConnection(new(SteamConnectionStage.LogonResponse, callback.Result, callback.CellID), InternalClientLoggedOn);

            if (callback.Result == SteamKit.EResult.OK)
            {
                // Set the cell ID our Steam Client actually connected to
                ActiveCellId = callback.CellID;

                _logonAttemptsCounter = 0;
                _cmAttemptsCounter = 0;
                Interlocked.Exchange(ref _reconnectAttempts, 0);

                if (Credentials.IsAnonymous) _licensesReceived.Set();
                _clientReadyEvent.Set();
            }
            else
            {
                if (callback.Result == SteamKit.EResult.AccountLogonDenied ||
                    callback.Result == SteamKit.EResult.AccountLoginDeniedNeedTwoFactor)
                {
                    if (_logonAttemptsCounter >= MaximumLogonAttempts)
                        SetFaultedAndShutdown(new SteamAuthenticationException(callback.Result));
                    return;
                }

                if (callback.Result == SteamKit.EResult.TryAnotherCM)
                {
                    // If we get this error code, we should try another CM server to connect to. This one might just be
                    // down for maintenance. Keep track of how many times we attempted to connect to different CM
                    // servers to set a limit though.
                    _cmAttemptsCounter += 1;

                    if (_cmAttemptsCounter < MaximumCmServerAttempts)
                    {
                        // Returning here will result in a new connect attempt. It's important we DONT create a new
                        // SteamKit.SteamClient as it already keeps track of which CM servers were attempted.
                        return;
                    }
                    // ReSharper disable once RedundantIfElseBlock
                    else
                    {
                        SetFaultedAndShutdown(new SteamNoCmServerFoundException());

                        return;
                    }
                }
                
                // Don't spam unsuccessful logon attempts as this might get us rate limited
                if (_logonAttemptsCounter > MaximumLogonAttempts)
                {
                    SetFaultedAndShutdown(new SteamAuthenticationException(callback.Result));

                    return;
                }

                SetFaultedAndShutdown(new SteamAuthenticationException(callback.Result));
            }
        }

        private void SetFaultedAndShutdown(Exception exception)
        {
            if (CancellationToken.IsCancellationRequested) return;
            FaultException = exception;
            
            Shutdown();
        }

        private void OnLoggedOff(SteamKit.SteamUser.LoggedOffCallback callback)
        {
            _clientReadyEvent.Reset();
            _licensesReceived.Reset();
            PublishConnection(new(SteamConnectionStage.LogoffResponse, callback.Result), InternalClientLoggedOff);
        }

        private void PublishConnection(SteamConnectionProgress progress, Action? callbacks = null)
        {
            var observers = ConnectionProgressChanged;
            if (observers == null && callbacks == null) return;
            lock (_notificationLock)
            {
                _lastNotification = _lastNotification.ContinueWith(_ =>
                {
                    if (observers != null)
                    {
                        foreach (EventHandler<SteamConnectionProgress> observer in observers.GetInvocationList())
                        {
                            try { observer(this, progress); }
                            catch (Exception ex) { NotifyObserverError(ex); }
                        }
                    }
                    Notify(callbacks);
                }, CancellationToken.None, TaskContinuationOptions.DenyChildAttach, TaskScheduler.Default);
            }
        }

        private void Notify(Action? callbacks)
        {
            if (callbacks is null) return;
            foreach (Action callback in callbacks.GetInvocationList())
            {
                try
                {
                    callback();
                }
                catch (Exception ex)
                {
                    NotifyObserverError(ex);
                }
            }
        }

        private void NotifyObserverError(Exception exception)
        {
            if (OnUnhandledException is not { } observers) return;
            foreach (EventHandler<Exception> observer in observers.GetInvocationList())
            {
                try { observer(this, exception); }
                catch (Exception) { }
            }
        }
    }
}
