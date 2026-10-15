using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SteamKitSDK.Windows.Authentication;
using Xunit;

namespace SteamKitSDK.Windows.Tests;

public sealed class DesktopSteamAuthenticatorTests
{
    [Fact]
    public async Task Authenticator_UsesDialogsAndStoreForTheConfiguredAccount()
    {
        var dialogs = new FakeDialogService();
        var store = new MemoryStore();
        var authenticator = new DesktopSteamAuthenticator("alice", dialogs, store);

        Assert.Equal("device-code", await authenticator.GetTwoFactorAuthenticationCodeAsync(false));
        Assert.Equal("email-code", await authenticator.GetEmailAuthenticationCodeAsync("example.com", true));
        Assert.True(await authenticator.NotifyMobileNotificationAsync());

        await authenticator.PersistAccessTokenAsync("access-token");
        await authenticator.PersistGuardDataAsync("guard-data");

        Assert.Equal("access-token", await authenticator.GetAccessTokenAsync());
        Assert.Equal("guard-data", await authenticator.GetGuardDataAsync());
        Assert.Equal("alice", store.LastAccountName);
        Assert.Equal("example.com", dialogs.LastEmailDomain);
        Assert.True(dialogs.LastEmailCodeWasRetry);
    }

    [Fact]
    public async Task DpapiStore_RoundTripsAndDeletesProtectedValues()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SteamKitSDK.Windows.Tests", Guid.NewGuid().ToString("N"));

        try
        {
            var store = new DpapiSteamAuthenticationStore(directory);

            await store.SaveAccessTokenAsync("alice", "access-token");
            await store.SaveGuardDataAsync("alice", "guard-data");

            Assert.Equal("access-token", await store.GetAccessTokenAsync("alice"));
            Assert.Equal("guard-data", await store.GetGuardDataAsync("alice"));
            var persistedContents = string.Concat(Directory.EnumerateFiles(directory).Select(File.ReadAllText));
            Assert.DoesNotContain("access-token", persistedContents);

            await store.DeleteAsync("alice");

            Assert.Null(await store.GetAccessTokenAsync("alice"));
            Assert.Null(await store.GetGuardDataAsync("alice"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class FakeDialogService : ISteamAuthenticationDialogService
    {
        public string? LastEmailDomain { get; private set; }
        public bool LastEmailCodeWasRetry { get; private set; }

        public Task<string> RequestTwoFactorAuthenticationCodeAsync(bool previousCodeWasIncorrect, CancellationToken cancellationToken = default) =>
            Task.FromResult("device-code");

        public Task<string> RequestEmailAuthenticationCodeAsync(
            string accountEmail,
            bool previousCodeWasIncorrect,
            CancellationToken cancellationToken = default)
        {
            LastEmailDomain = accountEmail;
            LastEmailCodeWasRetry = previousCodeWasIncorrect;
            return Task.FromResult("email-code");
        }

        public Task<bool> RequestMobileConfirmationAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class MemoryStore : ISteamAuthenticationStore
    {
        private readonly ConcurrentDictionary<string, string> _values = new();

        public string? LastAccountName { get; private set; }

        public Task SaveAccessTokenAsync(string accountName, string token, CancellationToken cancellationToken = default)
        {
            LastAccountName = accountName;
            _values[GetKey(accountName, "access")] = token;
            return Task.CompletedTask;
        }

        public Task<string?> GetAccessTokenAsync(string accountName, CancellationToken cancellationToken = default)
        {
            LastAccountName = accountName;
            return Task.FromResult(_values.TryGetValue(GetKey(accountName, "access"), out var value) ? value : null);
        }

        public Task SaveGuardDataAsync(string accountName, string guardData, CancellationToken cancellationToken = default)
        {
            LastAccountName = accountName;
            _values[GetKey(accountName, "guard")] = guardData;
            return Task.CompletedTask;
        }

        public Task<string?> GetGuardDataAsync(string accountName, CancellationToken cancellationToken = default)
        {
            LastAccountName = accountName;
            return Task.FromResult(_values.TryGetValue(GetKey(accountName, "guard"), out var value) ? value : null);
        }

        public Task DeleteAsync(string accountName, CancellationToken cancellationToken = default)
        {
            LastAccountName = accountName;
            _values.TryRemove(GetKey(accountName, "access"), out _);
            _values.TryRemove(GetKey(accountName, "guard"), out _);
            return Task.CompletedTask;
        }

        private static string GetKey(string accountName, string kind) => $"{accountName}:{kind}";
    }
}
