using System.IO;
using System.Text.Json;
using iRacingLauncher.Models;
using iRacingLauncher.Services;
using Xunit;

namespace iRacingLauncher.Tests;

public class SettingsServiceTests
{
    private static string TempConfigPath() =>
        Path.Combine(Path.GetTempPath(), $"iRacingLauncherTest_{Guid.NewGuid():N}.json");

    [Fact]
    public void Load_WhenFileMissing_CreatesAndReturnsDefaults()
    {
        var path = TempConfigPath();
        try
        {
            var service = new SettingsService(path);
            var config = service.Load();

            Assert.Equal(2, config.LaunchDelaySeconds);
            Assert.Equal(5, config.Apps.Count);
            Assert.True(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SaveThenLoad_RoundTripsConfig()
    {
        var path = TempConfigPath();
        try
        {
            var service = new SettingsService(path);
            var config = SettingsService.CreateDefaultConfig();
            config.LaunchDelaySeconds = 7;
            config.Apps[0].Selected = false;

            service.Save(config);
            var loaded = service.Load();

            Assert.Equal(7, loaded.LaunchDelaySeconds);
            Assert.False(loaded.Apps[0].Selected);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_WhenFileIsCorruptJson_FallsBackToDefaultsAndOverwrites()
    {
        var path = TempConfigPath();
        try
        {
            File.WriteAllText(path, "{ not valid json ");
            var service = new SettingsService(path);

            var config = service.Load();

            Assert.Equal(5, config.Apps.Count);

            // Verify the corrupt file on disk was actually overwritten, not just
            // that Load() returns in-memory defaults regardless of disk state.
            var rawContentAfterFirstLoad = File.ReadAllText(path);
            Assert.DoesNotContain("not valid json", rawContentAfterFirstLoad);
            var reparsed = JsonSerializer.Deserialize<LauncherConfig>(rawContentAfterFirstLoad);
            Assert.NotNull(reparsed);

            var reloaded = service.Load();
            Assert.Equal(5, reloaded.Apps.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_ReadsCamelCaseConfigInTheDocumentedOnDiskFormat()
    {
        var path = TempConfigPath();
        try
        {
            // Exactly the shape documented in the design spec's Data & Config section.
            File.WriteAllText(path, """
                {
                  "launchDelaySeconds": 9,
                  "launchAtWindowsStartup": true,
                  "theme": "Light",
                  "apps": [
                    {
                      "name": "iRacing",
                      "processName": "iRacingUI",
                      "path": "C:\\iRacing\\ui\\iRacingUI.exe",
                      "selected": false
                    }
                  ]
                }
                """);
            var service = new SettingsService(path);

            var config = service.Load();

            Assert.Equal(9, config.LaunchDelaySeconds);
            Assert.True(config.LaunchAtWindowsStartup);
            Assert.Equal("Light", config.Theme);
            var app = Assert.Single(config.Apps);
            Assert.Equal("iRacing", app.Name);
            Assert.Equal("iRacingUI", app.ProcessName);
            Assert.Equal(@"C:\iRacing\ui\iRacingUI.exe", app.Path);
            Assert.False(app.Selected);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_WritesCamelCaseJson()
    {
        var path = TempConfigPath();
        try
        {
            var service = new SettingsService(path);
            service.Save(SettingsService.CreateDefaultConfig());

            var raw = File.ReadAllText(path);

            Assert.Contains("\"launchDelaySeconds\"", raw);
            Assert.Contains("\"apps\"", raw);
            Assert.Contains("\"processName\"", raw);
            Assert.DoesNotContain("\"LaunchDelaySeconds\"", raw);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetDefaultConfigPath_ReturnsAppDataPathAndCreatesDirectory()
    {
        var path = SettingsService.GetDefaultConfigPath();

        var expectedSuffix = Path.Combine("iRacingLauncher", "config.json");
        Assert.EndsWith(expectedSuffix, path);
        Assert.True(Directory.Exists(Path.GetDirectoryName(path)));
    }
}
