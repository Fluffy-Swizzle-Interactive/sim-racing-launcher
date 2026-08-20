using System.Collections.Generic;

namespace iRacingLauncher.Services;

public interface IFileProbe
{
    bool FileExists(string path);
    IEnumerable<string> FindExecutablesUnder(string rootDir, string appName);
    IEnumerable<string> ResolveStartMenuShortcuts(string appName);
}
