using iRacingLauncher.Services;
using Xunit;

namespace iRacingLauncher.Tests;

public class FakeFileProbe : IFileProbe
{
    public HashSet<string> ExistingFiles { get; } = new();
    public Dictionary<string, List<string>> ExecutablesByRoot { get; } = new();
    public List<string> StartMenuMatches { get; } = new();
    public List<string> SearchedRoots { get; } = new();

    public bool FileExists(string path) => ExistingFiles.Contains(path);

    public IEnumerable<string> FindExecutablesUnder(string rootDir, string appName)
    {
        SearchedRoots.Add(rootDir);
        return ExecutablesByRoot.TryGetValue(rootDir, out var files) ? files : Enumerable.Empty<string>();
    }

    public IEnumerable<string> ResolveStartMenuShortcuts(string appName) => StartMenuMatches;
}

public class AppFinderServiceTests
{
    [Fact]
    public void FindPath_ReturnsExistingPathWithoutSearching_WhenStillValid()
    {
        var probe = new FakeFileProbe();
        probe.ExistingFiles.Add(@"C:\already\there.exe");
        var service = new AppFinderService(probe, new[] { @"C:\Root1", @"C:\Root2" });

        var result = service.FindPath("SomeApp", @"C:\already\there.exe");

        Assert.Equal(@"C:\already\there.exe", result);
        Assert.Empty(probe.SearchedRoots);
    }

    [Fact]
    public void FindPath_SearchesRootsInOrder_ReturnsFirstMatch()
    {
        var probe = new FakeFileProbe();
        probe.ExecutablesByRoot[@"C:\Root2"] = new List<string> { @"C:\Root2\App\App.exe" };
        var service = new AppFinderService(probe, new[] { @"C:\Root1", @"C:\Root2", @"C:\Root3" });

        var result = service.FindPath("App", existingPath: null);

        Assert.Equal(@"C:\Root2\App\App.exe", result);
        Assert.Equal(new[] { @"C:\Root1", @"C:\Root2" }, probe.SearchedRoots);
    }

    [Fact]
    public void FindPath_FallsBackToStartMenuShortcuts_WhenNoRootMatch()
    {
        var probe = new FakeFileProbe();
        probe.StartMenuMatches.Add(@"C:\Somewhere\App.exe");
        var service = new AppFinderService(probe, new[] { @"C:\Root1" });

        var result = service.FindPath("App", existingPath: null);

        Assert.Equal(@"C:\Somewhere\App.exe", result);
    }

    [Fact]
    public void FindPath_ReturnsNull_WhenNothingFound()
    {
        var probe = new FakeFileProbe();
        var service = new AppFinderService(probe, new[] { @"C:\Root1" });

        var result = service.FindPath("App", existingPath: @"C:\missing.exe");

        Assert.Null(result);
    }
}
