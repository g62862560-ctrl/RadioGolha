using Microsoft.Win32;

namespace RadioGolha;

/// <summary>
/// Locates the installed VLC Media Player (the native libvlc.dll plus vlc.exe).
/// Order: the InstallDir recorded by the VLC installer in the registry, then the
/// usual install folders, then every directory on PATH.
/// </summary>
internal static class VlcLocator
{
    private const string RegistryKeyPath = @"SOFTWARE\VideoLAN\VLC";

    /// <summary>
    /// Default VLC install folders (checked after the registry, before PATH).
    /// The explicit "C:\Program Files\VideoLAN\VLC" entry covers machines where
    /// the shell folder APIs report something different from the real location.
    /// </summary>
    private static IEnumerable<string> CandidateDirectories()
    {
        foreach (var directory in RegistryInstallDirectories())
            yield return directory;

        yield return @"C:\Program Files\VideoLAN\VLC";
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VideoLAN", "VLC");
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "VideoLAN", "VLC");

        // ProgramW6432 is only defined for 32-bit processes on 64-bit Windows,
        // but reading it costs nothing and helps when ProgramFiles is redirected.
        var programW6432 = Environment.GetEnvironmentVariable("ProgramW6432");
        if (!string.IsNullOrWhiteSpace(programW6432))
            yield return Path.Combine(programW6432, "VideoLAN", "VLC");
    }

    /// <summary>InstallDir values written by the VLC installer (HKLM + HKCU, both views).</summary>
    private static IEnumerable<string> RegistryInstallDirectories()
    {
        var hives = new[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Registry64),
            (RegistryHive.CurrentUser, RegistryView.Registry32),
        };

        foreach (var (hive, view) in hives)
        {
            string? installDir = null;
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(RegistryKeyPath);
                installDir = key?.GetValue("InstallDir") as string;
            }
            catch
            {
                // unreadable or missing key - try the next candidate
            }

            if (!string.IsNullOrWhiteSpace(installDir))
                yield return installDir.Trim().Trim('"');
        }
    }

    private static IEnumerable<string> AllDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in CandidateDirectories())
        {
            if (string.IsNullOrWhiteSpace(directory) || !seen.Add(directory))
                continue;
            yield return directory;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = entry.Trim().Trim('"');
            if (directory.Length > 0 && seen.Add(directory))
                yield return directory;
        }
    }

    /// <summary>Full path of vlc.exe, or null when VLC is not installed.</summary>
    public static string? FindExecutable()
    {
        foreach (var directory in AllDirectories())
        {
            var candidate = Path.Combine(directory, "vlc.exe");
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    /// <summary>
    /// Folder containing libvlc.dll, or null when VLC is not installed.
    /// Must match the app bitness: a 64-bit build needs 64-bit VLC.
    /// </summary>
    public static string? FindInstallDirectory()
    {
        foreach (var directory in AllDirectories())
        {
            if (File.Exists(Path.Combine(directory, "libvlc.dll")))
                return directory;
        }
        return null;
    }
}
