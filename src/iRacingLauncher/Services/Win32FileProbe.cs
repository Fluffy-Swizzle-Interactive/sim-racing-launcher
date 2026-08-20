using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IWshRuntimeLibrary;

namespace iRacingLauncher.Services;

public class Win32FileProbe : IFileProbe
{
    public bool FileExists(string path) => System.IO.File.Exists(path);

    public IEnumerable<string> FindExecutablesUnder(string rootDir, string appName)
    {
        if (!Directory.Exists(rootDir))
        {
            return Enumerable.Empty<string>();
        }

        var normalizedTarget = appName.Replace(" ", string.Empty);
        var matches = new List<string>();
        foreach (var exe in SafeEnumerateFiles(rootDir, "*.exe"))
        {
            var normalizedName = Path.GetFileNameWithoutExtension(exe).Replace(" ", string.Empty);
            if (normalizedName.Contains(normalizedTarget, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(exe);
            }
        }
        return matches;
    }

    public IEnumerable<string> ResolveStartMenuShortcuts(string appName)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        };

        var results = new List<string>();

        WshShellClass shell;
        try
        {
            shell = new WshShellClass();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Windows Script Host unavailable/disabled — no shortcut resolution.
            return results;
        }

        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                continue;
            }
            foreach (var lnk in SafeEnumerateFiles(root, "*.lnk"))
            {
                var fileName = Path.GetFileNameWithoutExtension(lnk);
                if (!fileName.Contains(appName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                try
                {
                    if (shell.CreateShortcut(lnk) is IWshShortcut shortcut &&
                        !string.IsNullOrEmpty(shortcut.TargetPath) &&
                        System.IO.File.Exists(shortcut.TargetPath))
                    {
                        results.Add(shortcut.TargetPath);
                    }
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    // Malformed or unreadable .lnk — skip just this one.
                }
            }
        }
        return results;
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root, string pattern)
    {
        var found = new List<string>();
        try
        {
            // Append one item at a time: EnumerateFiles is lazy, so it can throw
            // partway through the walk. Collecting incrementally keeps everything
            // found before the bad directory instead of discarding the whole result.
            foreach (var file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
            {
                found.Add(file);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Directory we can't read — keep what we already found.
        }
        catch (IOException)
        {
            // Covers PathTooLongException and transient I/O failures mid-walk.
        }
        return found;
    }
}
