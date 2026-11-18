using System;
using System.IO;

namespace SteamKitSDK.ContentDelivery.Models.Downloading;

internal static class DownloadPathResolver
{
    internal static string ResolveFilePath(string downloadDirectory, string relativeFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeFileName);

        var rootDirectory = Path.GetFullPath(downloadDirectory);
        if (Path.IsPathRooted(relativeFileName) || (OperatingSystem.IsWindows() && relativeFileName.Contains(':')))
        {
            throw new ArgumentException("Download file names must be relative and cannot name a Windows alternate data stream.", nameof(relativeFileName));
        }
        if (OperatingSystem.IsWindows())
        {
            foreach (var segment in relativeFileName.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment.EndsWith(' ') || segment.EndsWith('.') || IsWindowsDeviceName(segment))
                    throw new ArgumentException("The download file name contains an ambiguous Windows path component.", nameof(relativeFileName));
            }
        }
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

        // Check existing components, including ancestors of the configured destination.
        for (var component = targetPath; component != null; component = Path.GetDirectoryName(component))
        {
            try
            {
                if ((File.GetAttributes(component) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException($"The download path contains a symbolic link or reparse point: '{component}'.");
                }
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }

        return targetPath;
    }

    private static bool IsWindowsDeviceName(string segment)
    {
        var name = segment.Split('.', 2)[0].TrimEnd().ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
            || (name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal)
                || name.StartsWith("LPT", StringComparison.Ordinal)) && "123456789���".Contains(name[3]));
    }
}
