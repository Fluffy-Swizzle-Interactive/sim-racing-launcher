using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            using var doc = JsonDocument.Parse(json);
            // Pre-profiles config files have a top-level "apps" array and no
            // "profiles" key. Detect that shape and migrate it into a single
            // profile rather than losing the user's existing app list.
            var isLegacyFormat = !HasProperty(doc.RootElement, "profiles");

            var config = isLegacyFormat
                ? MigrateLegacyFormat(json)
                : JsonSerializer.Deserialize<LauncherConfig>(json, SerializerOptions);

            if (config is null || config.Profiles is null || config.Profiles.Count == 0)
            {
                return ResetToDefaults();
            }

            if (!config.Profiles.Any(p => p.Name == config.ActiveProfileName))
            {
                config.ActiveProfileName = config.Profiles[0].Name;
            }

            if (isLegacyFormat)
            {
                // Persist the migrated shape now so future loads read the new
                // format directly instead of re-migrating every startup.
                TrySave(config);
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

    private static bool HasProperty(JsonElement root, string name) =>
        root.EnumerateObject().Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    private static LauncherConfig? MigrateLegacyFormat(string json)
    {
        var legacy = JsonSerializer.Deserialize<LegacyLauncherConfig>(json, SerializerOptions);
        if (legacy is null)
        {
            return null;
        }

        const string defaultProfileName = "Default";
        return new LauncherConfig
        {
            LaunchDelaySeconds = legacy.LaunchDelaySeconds,
            LaunchAtWindowsStartup = legacy.LaunchAtWindowsStartup,
            Theme = legacy.Theme,
            ActiveProfileName = defaultProfileName,
            Profiles = new List<Profile>
            {
                new() { Name = defaultProfileName, Apps = legacy.Apps },
            },
        };
    }

    private LauncherConfig ResetToDefaults()
    {
        var defaults = CreateDefaultConfig();
        TrySave(defaults);
        return defaults;
    }

    private void TrySave(LauncherConfig config)
    {
        try
        {
            Save(config);
        }
        catch (IOException)
        {
            // Read-only or locked config location — carry on with in-memory config.
        }
        catch (UnauthorizedAccessException)
        {
        }
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
        const string defaultProfileName = "iRacing";
        return new LauncherConfig
        {
            LaunchDelaySeconds = 2,
            LaunchAtWindowsStartup = false,
            Theme = "Dark",
            ActiveProfileName = defaultProfileName,
            Profiles = new List<Profile>
            {
                new()
                {
                    Name = "iRacing",
                    Apps = new List<AppEntry>
                    {
                        Sim("iRacing", "iRacingUI"),
                        CrewChief(), TradingPaints(), RaceLab(), CoachDave(),
                    },
                },
                new()
                {
                    // ACC shares iRacing's companion-tool lineup — all four support it.
                    Name = "ACC",
                    Apps = new List<AppEntry>
                    {
                        Sim("Assetto Corsa Competizione", "AC2-Win64-Shipping"),
                        CrewChief(), TradingPaints(), RaceLab(), CoachDave(),
                    },
                },
                new()
                {
                    // Vanilla AC has no Coach Dave Delta support, unlike iRacing/ACC.
                    Name = "AC",
                    Apps = new List<AppEntry>
                    {
                        Sim("Assetto Corsa", "acs"),
                        CrewChief(), TradingPaints(), RaceLab(),
                    },
                },
            },
        };

        static AppEntry Sim(string name, string processName) =>
            new() { Name = name, ProcessName = processName, Path = "", Selected = true };
        static AppEntry CrewChief() => new() { Name = "CrewChiefV4", ProcessName = "CrewChiefV4", Path = "", Selected = true };
        static AppEntry TradingPaints() => new() { Name = "TradingPaints", ProcessName = "Trading Paints", Path = "", Selected = true };
        static AppEntry RaceLab() => new() { Name = "RaceLab", ProcessName = "RacelabApps", Path = "", Selected = true };
        static AppEntry CoachDave() => new() { Name = "Coach David", ProcessName = "Coach Dave Delta", Path = "", Selected = true };
    }

    /// <summary>Shape of config.json before profiles existed — a single flat app list.</summary>
    private class LegacyLauncherConfig
    {
        public int LaunchDelaySeconds { get; set; } = 2;
        public bool LaunchAtWindowsStartup { get; set; } = false;
        public string Theme { get; set; } = "Dark";
        public List<AppEntry> Apps { get; set; } = new();
    }
}
