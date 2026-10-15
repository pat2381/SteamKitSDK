using System;
using BytexDigital.Steam.Core;
using BytexDigital.Steam.Core.Enumerations;
using BytexDigital.Steam.Core.Structs;
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
}
