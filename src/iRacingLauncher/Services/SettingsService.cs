using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using iRacingLauncher.Models;

namespace iRacingLauncher.Services;

public class SettingsService
{
    // The design doc documents the on-disk format as camelCase, so serialize to it
    // and read it back case-insensitively (a PascalCase file written by an older
    // build still loads).
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string _configPath;

    public SettingsService(string configPath)
    {
        _configPath = configPath;
    }

    public static string GetDefaultConfigPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "iRacingLauncher");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "config.json");
    }

    /// <summary>
    /// Loads the config, falling back to defaults for a missing, unreadable or
    /// unparseable file. This runs during startup before any window exists, so it
    /// never throws — a failure to even write the regenerated defaults still yields
    /// a usable in-memory config.
    /// </summary>
    public LauncherConfig Load()
    {
        if (!File.Exists(_configPath))
        {
            return ResetToDefaults();
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            var config = JsonSerializer.Deserialize<LauncherConfig>(json, SerializerOptions);
            if (config is null || config.Apps is null)
            {
                return ResetToDefaults();
            }
            return config;
        }
        catch (JsonException)
        {
            return ResetToDefaults();
        }
        catch (IOException)
        {
            // Locked or otherwise unreadable file.
            return ResetToDefaults();
        }
        catch (UnauthorizedAccessException)
        {
            return ResetToDefaults();
        }
    }

    private LauncherConfig ResetToDefaults()
    {
        var defaults = CreateDefaultConfig();
        try
        {
            Save(defaults);
        }
        catch (IOException)
        {
            // Read-only or locked config location — carry on with in-memory defaults.
        }
        catch (UnauthorizedAccessException)
        {
        }
        return defaults;
    }

    public void Save(LauncherConfig config)
    {
        var json = JsonSerializer.Serialize(config, SerializerOptions);
        var dir = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(_configPath, json);
    }

    public static LauncherConfig CreateDefaultConfig()
    {
        return new LauncherConfig
        {
            LaunchDelaySeconds = 2,
            LaunchAtWindowsStartup = false,
            Theme = "Dark",
            Apps = new List<AppEntry>
            {
                new() { Name = "iRacing", ProcessName = "iRacingUI", Path = "", Selected = true },
                new() { Name = "CrewChiefV4", ProcessName = "CrewChiefV4", Path = "", Selected = true },
                new() { Name = "TradingPaints", ProcessName = "Trading Paints", Path = "", Selected = true },
                new() { Name = "RaceLab", ProcessName = "RacelabApps", Path = "", Selected = true },
                new() { Name = "Coach David", ProcessName = "Coach Dave Delta", Path = "", Selected = true },
            }
        };
    }
}
