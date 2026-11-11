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
            // A current-format file has both a top-level "apps" registry array and a
            // "profiles" array of AppRefs. Checking "apps" alone isn't enough — the
            // pre-profiles flat-apps legacy shape also has a top-level "apps" key,
            // just with embedded entries and no "profiles" at all. Anything that
            // isn't both — that pre-profiles shape, or today's per-profile
            // embedded-apps shape — needs migrating into the registry.
            var isCurrentFormat = HasProperty(doc.RootElement, "apps") && HasProperty(doc.RootElement, "profiles");
            var needsMigration = !isCurrentFormat;

            LauncherConfig? config;
            if (isCurrentFormat)
            {
                config = JsonSerializer.Deserialize<LauncherConfig>(json, SerializerOptions);
            }
            else if (!HasProperty(doc.RootElement, "profiles"))
            {
                config = MigrateFlatAppsFormat(json);
            }
            else
            {
                config = MigrateEmbeddedPerProfileAppsFormat(json);
            }

            if (config is null || config.Apps is null || config.Profiles is null || config.Profiles.Count == 0)
            {
                return ResetToDefaults();
            }

            if (!config.Profiles.Any(p => p.Name == config.ActiveProfileName))
            {
                config.ActiveProfileName = config.Profiles[0].Name;
            }

            if (needsMigration)
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
        catch (Exception)
        {
            // Catch-all for anything the migration path can throw that the specific
            // catches above don't cover (e.g. a null "profiles"/"apps" array, a null
            // ProcessName on an embedded legacy app, or a non-object JSON root). The
            // design contract is that a bug in the merge logic surfaces as a fallback
            // to defaults rather than a crash — see design spec.
            return ResetToDefaults();
        }
    }

    private static bool HasProperty(JsonElement root, string name) =>
        root.EnumerateObject().Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Pre-profiles shape: a single flat "apps" array, no "profiles" key at all.</summary>
    private static LauncherConfig? MigrateFlatAppsFormat(string json)
    {
        var legacy = JsonSerializer.Deserialize<LegacyFlatConfig>(json, SerializerOptions);
        if (legacy is null)
        {
            return null;
        }

        const string defaultProfileName = "Default";
        return MergeEmbeddedApps(
            legacy.LaunchDelaySeconds,
            legacy.LaunchAtWindowsStartup,
            closeToTray: false,
            legacy.Theme,
            defaultProfileName,
            new List<LegacyProfile> { new() { Name = defaultProfileName, Apps = legacy.Apps } });
    }

    /// <summary>Today's shape: "profiles" with each profile's apps embedded directly.</summary>
    private static LauncherConfig? MigrateEmbeddedPerProfileAppsFormat(string json)
    {
        var legacy = JsonSerializer.Deserialize<LegacyProfilesConfig>(json, SerializerOptions);
        if (legacy is null)
        {
            return null;
        }

        return MergeEmbeddedApps(
            legacy.LaunchDelaySeconds,
            legacy.LaunchAtWindowsStartup,
            legacy.CloseToTray,
            legacy.Theme,
            legacy.ActiveProfileName,
            legacy.Profiles);
    }

    /// <summary>
    /// Groups every embedded app across all legacy profiles by ProcessName
    /// (case-insensitive), creating one AppDefinition per group — Path/Name taken
    /// from the first group member with a non-empty Path, falling back to the first
    /// member overall. Arguments always starts empty: legacy embedded entries never
    /// had an arguments field, so a migrated app has none until the user sets one
    /// afterward in the Apps card. Each profile gets an AppRefs list preserving its
    /// original per-app order and Selected value.
    /// </summary>
    private static LauncherConfig MergeEmbeddedApps(
        int launchDelaySeconds,
        bool launchAtWindowsStartup,
        bool closeToTray,
        string theme,
        string activeProfileName,
        List<LegacyProfile> legacyProfiles)
    {
        var groups = legacyProfiles
            .SelectMany(p => p.Apps)
            .GroupBy(a => a.ProcessName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var appsByProcessName = new Dictionary<string, AppDefinition>(StringComparer.OrdinalIgnoreCase);
        var apps = new List<AppDefinition>();
        foreach (var group in groups)
        {
            var winner = group.FirstOrDefault(a => !string.IsNullOrEmpty(a.Path)) ?? group.First();
            var definition = new AppDefinition
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = winner.Name,
                ProcessName = group.First().ProcessName ?? string.Empty,
                Path = winner.Path,
                Arguments = string.Empty,
            };
            apps.Add(definition);
            appsByProcessName[group.Key] = definition;
        }

        var profiles = legacyProfiles.Select(p => new Profile
        {
            Name = p.Name,
            AppRefs = p.Apps
                .Select(a => new ProfileAppEntry { AppId = appsByProcessName[a.ProcessName ?? string.Empty].Id, Selected = a.Selected })
                .ToList(),
        }).ToList();

        return new LauncherConfig
        {
            LaunchDelaySeconds = launchDelaySeconds,
            LaunchAtWindowsStartup = launchAtWindowsStartup,
            CloseToTray = closeToTray,
            Theme = theme,
            ActiveProfileName = activeProfileName,
            Apps = apps,
            Profiles = profiles,
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

    /// <summary>
    /// Serializes the config and swaps it into place atomically.
    /// </summary>
    /// <remarks>
    /// Writing straight to <see cref="_configPath"/> would truncate it first, so an
    /// interrupted write — a logoff/shutdown force-kill, a power loss — could leave
    /// a half-written file behind. <see cref="Load"/> treats that as corrupt and
    /// resets to defaults, then persists those defaults over it, destroying the
    /// user's real configuration for good. Writing to a sibling temp file and moving
    /// it over the target means the config is only ever replaced by a complete file:
    /// the move is atomic within a volume, so a crash leaves either the old config
    /// or the new one intact, never a truncated hybrid. The temp name carries the
    /// process id so two running instances can't clobber each other's staging file.
    /// </remarks>
    public void Save(LauncherConfig config)
    {
        var json = JsonSerializer.Serialize(config, SerializerOptions);
        var dir = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tempPath = $"{_configPath}.{Environment.ProcessId}.tmp";
        File.WriteAllText(tempPath, json);
        try
        {
            File.Move(tempPath, _configPath, overwrite: true);
        }
        catch
        {
            // Don't leave the staging file behind if the swap itself failed; the
            // existing config is still intact, which is the point of staging.
            try { File.Delete(tempPath); } catch (IOException) { }
            throw;
        }
    }

    public static LauncherConfig CreateDefaultConfig()
    {
        var iRacingSim = Sim("iRacing", "iRacingUI");
        var accSim = Sim("Assetto Corsa Competizione", "AC2-Win64-Shipping");
        var acSim = Sim("Assetto Corsa (Content Manager)", "Content Manager");
        var crewChief = CrewChief();
        var tradingPaints = TradingPaints();
        var raceLab = RaceLab();
        var coachDave = CoachDave();

        const string defaultProfileName = "iRacing";
        return new LauncherConfig
        {
            LaunchDelaySeconds = 2,
            LaunchAtWindowsStartup = false,
            Theme = "Dark",
            ActiveProfileName = defaultProfileName,
            Apps = new List<AppDefinition> { iRacingSim, accSim, acSim, crewChief, tradingPaints, raceLab, coachDave },
            Profiles = new List<Profile>
            {
                new()
                {
                    Name = "iRacing",
                    AppRefs = Refs(iRacingSim, crewChief, tradingPaints, raceLab, coachDave),
                },
                new()
                {
                    // ACC shares iRacing's companion-tool lineup — all four support it.
                    Name = "ACC",
                    AppRefs = Refs(accSim, crewChief, tradingPaints, raceLab, coachDave),
                },
                new()
                {
                    // Vanilla AC has no Coach Dave Delta support, unlike iRacing/ACC.
                    Name = "AC",
                    AppRefs = Refs(acSim, crewChief, tradingPaints, raceLab),
                },
            },
        };

        static AppDefinition Sim(string name, string processName) => new()
        {
            Id = Guid.NewGuid().ToString("N"), Name = name, ProcessName = processName, Path = "", Arguments = "",
        };
        static AppDefinition CrewChief() => new()
        {
            Id = Guid.NewGuid().ToString("N"), Name = "CrewChiefV4", ProcessName = "CrewChiefV4", Path = "", Arguments = "",
        };
        static AppDefinition TradingPaints() => new()
        {
            Id = Guid.NewGuid().ToString("N"), Name = "TradingPaints", ProcessName = "Trading Paints", Path = "", Arguments = "",
        };
        static AppDefinition RaceLab() => new()
        {
            Id = Guid.NewGuid().ToString("N"), Name = "RaceLab", ProcessName = "RacelabApps", Path = "", Arguments = "",
        };
        static AppDefinition CoachDave() => new()
        {
            Id = Guid.NewGuid().ToString("N"), Name = "Coach David", ProcessName = "Coach Dave Delta", Path = "", Arguments = "",
        };
        static List<ProfileAppEntry> Refs(params AppDefinition[] apps) =>
            apps.Select(a => new ProfileAppEntry { AppId = a.Id, Selected = true }).ToList();
    }

    /// <summary>An embedded app entry as it appears in either legacy on-disk shape.</summary>
    private class LegacyAppEntry
    {
        public string Name { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public bool Selected { get; set; } = true;
    }

    private class LegacyProfile
    {
        public string Name { get; set; } = string.Empty;
        public List<LegacyAppEntry> Apps { get; set; } = new();
    }

    /// <summary>Shape of config.json before profiles existed — a single flat app list.</summary>
    private class LegacyFlatConfig
    {
        public int LaunchDelaySeconds { get; set; } = 2;
        public bool LaunchAtWindowsStartup { get; set; } = false;
        public string Theme { get; set; } = "Dark";
        public List<LegacyAppEntry> Apps { get; set; } = new();
    }

    /// <summary>Shape of config.json with profiles but apps still embedded per profile.</summary>
    private class LegacyProfilesConfig
    {
        public int LaunchDelaySeconds { get; set; } = 2;
        public bool LaunchAtWindowsStartup { get; set; } = false;
        public bool CloseToTray { get; set; } = false;
        public string Theme { get; set; } = "Dark";
        public string ActiveProfileName { get; set; } = string.Empty;
        public List<LegacyProfile> Profiles { get; set; } = new();
    }
}
