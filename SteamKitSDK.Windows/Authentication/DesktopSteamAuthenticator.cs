using System;
using System.Threading;
using System.Threading.Tasks;
using SteamKitSDK.Core;

namespace SteamKitSDK.Windows.Authentication;

/// <summary>
/// A <see cref="SteamAuthenticator"/> for WPF and Windows Forms applications.
/// Dialog presentation is supplied by <see cref="ISteamAuthenticationDialogService"/>, keeping this package independent of a UI framework.
/// </summary>
public sealed class DesktopSteamAuthenticator : SteamAuthenticator
{
    private readonly string _accountName;
    private readonly ISteamAuthenticationDialogService _dialogs;
    private readonly ISteamAuthenticationStore _store;

    public DesktopSteamAuthenticator(
        string accountName,
        ISteamAuthenticationDialogService dialogs,
        ISteamAuthenticationStore store)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(store);

        _accountName = accountName;
        _dialogs = dialogs;
        _store = store;
    }

    public override Task<string> GetTwoFactorAuthenticationCodeAsync(
        bool previousCodeWasIncorrect,
        CancellationToken cancellationToken = default) =>
        _dialogs.RequestTwoFactorAuthenticationCodeAsync(previousCodeWasIncorrect, cancellationToken);

    public override Task<string> GetEmailAuthenticationCodeAsync(
        string accountEmail,
        bool previousCodeWasIncorrect,
        CancellationToken cancellationToken = default) =>
        _dialogs.RequestEmailAuthenticationCodeAsync(accountEmail, previousCodeWasIncorrect, cancellationToken);

    public override Task<bool> NotifyMobileNotificationAsync(CancellationToken cancellationToken = default) =>
        _dialogs.RequestMobileConfirmationAsync(cancellationToken);

    public override Task PersistAccessTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        return _store.SaveAccessTokenAsync(_accountName, token, cancellationToken);
    }

    public override async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
        await _store.GetAccessTokenAsync(_accountName, cancellationToken).ConfigureAwait(false) ?? string.Empty;

    public override Task PersistGuardDataAsync(string data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        return _store.SaveGuardDataAsync(_accountName, data, cancellationToken);
    }

    public override async Task<string> GetGuardDataAsync(CancellationToken cancellationToken = default) =>
        await _store.GetGuardDataAsync(_accountName, cancellationToken).ConfigureAwait(false) ?? string.Empty;
}
