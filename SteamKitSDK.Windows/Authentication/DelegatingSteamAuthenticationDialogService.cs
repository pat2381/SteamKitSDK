using System;
using System.Threading;
using System.Threading.Tasks;

namespace SteamKitSDK.Windows.Authentication;

/// <summary>
/// Creates a dialog service from application-provided callbacks.
/// The callbacks must marshal their visual work to the WPF or Windows Forms UI thread.
/// </summary>
public sealed class DelegatingSteamAuthenticationDialogService : ISteamAuthenticationDialogService
{
    private readonly Func<bool, CancellationToken, Task<string>> _requestTwoFactorCodeAsync;
    private readonly Func<string, bool, CancellationToken, Task<string>> _requestEmailCodeAsync;
    private readonly Func<CancellationToken, Task<bool>> _requestMobileConfirmationAsync;

    public DelegatingSteamAuthenticationDialogService(
        Func<bool, CancellationToken, Task<string>> requestTwoFactorCodeAsync,
        Func<string, bool, CancellationToken, Task<string>> requestEmailCodeAsync,
        Func<CancellationToken, Task<bool>> requestMobileConfirmationAsync)
    {
        ArgumentNullException.ThrowIfNull(requestTwoFactorCodeAsync);
        ArgumentNullException.ThrowIfNull(requestEmailCodeAsync);
        ArgumentNullException.ThrowIfNull(requestMobileConfirmationAsync);

        _requestTwoFactorCodeAsync = requestTwoFactorCodeAsync;
        _requestEmailCodeAsync = requestEmailCodeAsync;
        _requestMobileConfirmationAsync = requestMobileConfirmationAsync;
    }

    public Task<string> RequestTwoFactorAuthenticationCodeAsync(
        bool previousCodeWasIncorrect,
        CancellationToken cancellationToken = default) =>
        _requestTwoFactorCodeAsync(previousCodeWasIncorrect, cancellationToken);

    public Task<string> RequestEmailAuthenticationCodeAsync(
        string accountEmail,
        bool previousCodeWasIncorrect,
        CancellationToken cancellationToken = default) =>
        _requestEmailCodeAsync(accountEmail, previousCodeWasIncorrect, cancellationToken);

    public Task<bool> RequestMobileConfirmationAsync(CancellationToken cancellationToken = default) =>
        _requestMobileConfirmationAsync(cancellationToken);
}
