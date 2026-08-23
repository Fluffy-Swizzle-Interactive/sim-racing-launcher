using iRacingLauncher.Models;
using Xunit;

namespace iRacingLauncher.Tests;

public class LauncherConfigTests
{
    [Fact]
    public void ResolveApps_ReturnsDefinitionsInRefOrder()
    {
        var appA = new AppDefinition { Id = "a", Name = "A" };
        var appB = new AppDefinition { Id = "b", Name = "B" };
        var config = new LauncherConfig { Apps = new() { appA, appB } };
        var refs = new List<ProfileAppEntry>
        {
            new() { AppId = "b", Selected = true },
            new() { AppId = "a", Selected = true },
        };

        var resolved = config.ResolveApps(refs).ToList();

        Assert.Equal(new[] { appB, appA }, resolved);
    }

    [Fact]
    public void ResolveApps_SkipsARefWhoseAppIdIsNotInTheRegistry()
    {
        // Cascading delete in Settings normally keeps refs in sync with the registry,
        // but a hand-edited config could still carry a stale AppId — every other
        // consumer of this join (MainWindow, SettingsWindow) already skips rather
        // than throws, so this shared join point must match rather than crash the
        // Launch/Stop All buttons and the tray menu that all depend on it.
        var appA = new AppDefinition { Id = "a", Name = "A" };
        var config = new LauncherConfig { Apps = new() { appA } };
        var refs = new List<ProfileAppEntry>
        {
            new() { AppId = "a", Selected = true },
            new() { AppId = "stale-id", Selected = true },
        };

        var resolved = config.ResolveApps(refs).ToList();

        Assert.Equal(new[] { appA }, resolved);
    }
}
