using System;
using System.Linq;

namespace iRacingLauncher.Services;

public class AppFinderService
{
    private readonly IFileProbe _fileProbe;
    private readonly string[] _searchRoots;

    public AppFinderService(IFileProbe fileProbe, string[] searchRoots)
    {
        _fileProbe = fileProbe;
        _searchRoots = searchRoots;
    }

    public static string[] GetDefaultSearchRoots() => new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetEnvironmentVariable("ProgramData") ?? @"C:\ProgramData",
    };

    public string? FindPath(string appName, string? existingPath)
    {
        if (!string.IsNullOrWhiteSpace(existingPath) && _fileProbe.FileExists(existingPath))
        {
            return existingPath;
        }

        foreach (var root in _searchRoots)
        {
            var match = _fileProbe.FindExecutablesUnder(root, appName).FirstOrDefault();
            if (match is not null)
            {
                return match;
            }
        }

        return _fileProbe.ResolveStartMenuShortcuts(appName).FirstOrDefault();
    }
}
