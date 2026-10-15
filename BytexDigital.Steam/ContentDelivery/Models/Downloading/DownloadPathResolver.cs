using System;
using System.IO;

namespace BytexDigital.Steam.ContentDelivery.Models.Downloading;

internal static class DownloadPathResolver
{
    internal static string ResolveFilePath(string downloadDirectory, string relativeFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeFileName);

        var rootDirectory = Path.GetFullPath(downloadDirectory);
        var targetPath = Path.GetFullPath(Path.Combine(rootDirectory, relativeFileName));
        var rootDirectoryWithSeparator = rootDirectory.EndsWith(Path.DirectorySeparatorChar)
            || rootDirectory.EndsWith(Path.AltDirectorySeparatorChar)
            ? rootDirectory
            : rootDirectory + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!targetPath.StartsWith(rootDirectoryWithSeparator, comparison))
        {
            throw new ArgumentException(
                "The download file name must resolve to a path inside the configured download directory.",
                nameof(relativeFileName));
        }

        return targetPath;
    }
}
