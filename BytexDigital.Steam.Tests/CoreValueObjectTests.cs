using System;
using System.Threading.Tasks;
using BytexDigital.Steam.Core;
using BytexDigital.Steam.Core.Enumerations;
using BytexDigital.Steam.Core.Exceptions;
using BytexDigital.Steam.Core.Structs;
using BytexDigital.Steam.ContentDelivery;
using BytexDigital.Steam.ContentDelivery.Models;
using Xunit;

namespace BytexDigital.Steam.Tests;

public sealed class CoreValueObjectTests
{
    [Fact]
    public void AnonymousCredentials_AreRecognized()
    {
        var credentials = SteamCredentials.Anonymous;

        Assert.True(credentials.IsAnonymous);
        Assert.Equal(string.Empty, credentials.Username);
    }

    [Theory]
    [InlineData(null, "password")]
    [InlineData("username", null)]
    [InlineData("username", "")]
    public void Credentials_WithMissingRequiredValue_ThrowArgumentException(string? username, string? password)
    {
        Assert.Throws<ArgumentException>(() => new SteamCredentials(username!, password!));
    }

    [Fact]
    public void IdentifierTypes_PreserveTheirNumericValue()
    {
        AppId appId = 480;
        DepotId depotId = appId;
        ManifestId manifestId = 123456789UL;
        PublishedFileId publishedFileId = 987654321UL;

        Assert.Equal(480U, (uint)appId);
        Assert.Equal(480U, (uint)depotId);
        Assert.Equal(123456789UL, (ulong)manifestId);
        Assert.Equal(987654321UL, (ulong)publishedFileId);
    }

    [Fact]
    public void SteamOs_UsesTheProvidedIdentifier()
    {
        SteamOs steamOs = "steamdeck";

        Assert.Equal("steamdeck", steamOs.Identifier);
        Assert.Equal("steamdeck", steamOs.ToString());
        Assert.Throws<ArgumentNullException>(() => new SteamOs(null!));
    }

    [Fact]
    public void SteamClient_DefaultConstructor_CreatesAnonymousClient()
    {
        using var client = new SteamClient();

        Assert.True(client.Credentials.IsAnonymous);
    }

    [Fact]
    public void SteamClient_NonAnonymousCredentialsRequireAuthenticator()
    {
        var credentials = new SteamCredentials("username", "password");

        Assert.Throws<SteamAuthenticatorRequiredException>(() => new SteamClient(credentials));
    }

    [Fact]
    public async Task SteamContentClient_RejectsInvalidParallelDownloadLimit()
    {
        using var client = new SteamClient();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await using var contentClient = new SteamContentClient(client, 0);
        });
    }

    [Fact]
    public async Task SteamContentClient_CanBeDisposedWithoutConnection()
    {
        using var client = new SteamClient();
        await using var contentClient = new SteamContentClient(client);

        Assert.Same(client, contentClient.SteamClient);
    }

    [Fact]
    public async Task SteamContentClient_RejectsUnsupportedV1EncryptedManifest()
    {
        using var client = new SteamClient();
        await using var contentClient = new SteamContentClient(client);
        var legacyManifest = new DepotEncryptedManifest(
            appId: 480,
            depotId: 480,
            branchName: "legacy",
            encryptedManifestId: "00",
            version: DepotEncryptedManifest.EncryptionVersion.V1);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => contentClient.DecryptDepotManifestAsync(legacyManifest, "password"));
    }
}
