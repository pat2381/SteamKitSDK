using SteamKit2;

namespace SteamKitSDK.Core;

public enum SteamConnectionStage { Connecting, Connected, LogonResponse, LicenseResponse, Disconnected, LogoffResponse }

public sealed record SteamConnectionProgress(
    SteamConnectionStage Stage,
    EResult? Result = null,
    uint? CellId = null,
    int? LicenseCount = null,
    int? ReconnectAttempt = null,
    bool? UserInitiated = null);
