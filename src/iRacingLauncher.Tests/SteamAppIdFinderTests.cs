using System.IO;
using iRacingLauncher;
using iRacingLauncher.Services;
using Xunit;

namespace iRacingLauncher.Tests;

/// <summary>
/// Working out a Steam game's App ID from where its exe lives, by reading the
/// appmanifest_*.acf files Steam keeps in each library's steamapps folder.
/// </summary>
public class SteamAppIdFinderTests : IDisposable
{
    private readonly string _library = Path.Combine(Path.GetTempPath(), $"SteamLib_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_library)) Directory.Delete(_library, recursive: true);
    }

    private string SteamApps => Path.Combine(_library, "steamapps");

    private string AddGame(string installDir, string relativeExe)
    {
        var exe = Path.Combine(SteamApps, "common", installDir, relativeExe);
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllText(exe, "");
        return exe;
    }

    private void AddManifest(string fileId, string body) =>
        File.WriteAllText(Path.Combine(SteamApps, $"appmanifest_{fileId}.acf"), body);

    private static string Manifest(string appId, string installDir) => $$"""
        "AppState"
        {
        	"appid"		"{{appId}}"
        	"Universe"		"1"
        	"name"		"Some Game"
        	"installdir"		"{{installDir}}"
        	"InstalledDepots"
        	{
        		"2399421"
        		{
        			"manifest"		"123"
        		}
        	}
        }
        """;

    [Fact]
    public void FindAppId_ExeInsideSteamLibrary_ReturnsAppId()
    {
        var exe = AddGame("Le Mans Ultimate", "Le Mans Ultimate.exe");
        AddManifest("2399420", Manifest("2399420", "Le Mans Ultimate"));

        Assert.Equal("2399420", SteamAppIdFinder.FindAppId(exe));
    }

    [Fact]
    public void FindAppId_PicksMatchingManifestAmongSeveral()
    {
        var exe = AddGame("assettocorsa", "AssettoCorsa.exe");
        AddManifest("2399420", Manifest("2399420", "Le Mans Ultimate"));
        AddManifest("244210", Manifest("244210", "assettocorsa"));

        Assert.Equal("244210", SteamAppIdFinder.FindAppId(exe));
    }

    [Fact]
    public void FindAppId_ExeInNestedSubfolder_StillMatches()
    {
        var exe = AddGame("Game", Path.Combine("bin", "x64", "game.exe"));
        AddManifest("111", Manifest("111", "Game"));

        Assert.Equal("111", SteamAppIdFinder.FindAppId(exe));
    }

    [Fact]
    public void FindAppId_MatchesInstallDirCaseInsensitively()
    {
        var exe = AddGame("Le Mans Ultimate", "LMU.exe");
        AddManifest("2399420", Manifest("2399420", "le mans ultimate"));

        Assert.Equal("2399420", SteamAppIdFinder.FindAppId(exe));
    }

    [Fact]
    public void FindAppId_PathOutsideSteam_ReturnsNull()
    {
        Assert.Null(SteamAppIdFinder.FindAppId(@"C:\iRacing\ui\iRacingUI.exe"));
    }

    [Fact]
    public void FindAppId_ManifestMissingAppId_ReturnsNull()
    {
        var exe = AddGame("Game", "game.exe");
        AddManifest("111", "\"AppState\"\n{\n\t\"installdir\"\t\t\"Game\"\n}\n");

        Assert.Null(SteamAppIdFinder.FindAppId(exe));
    }

    [Fact]
    public void FindAppId_GarbageManifest_ReturnsNull()
    {
        var exe = AddGame("Game", "game.exe");
        AddManifest("111", "\0\0 not a manifest {{{");

        Assert.Null(SteamAppIdFinder.FindAppId(exe));
    }

    [Fact]
    public void FindAppId_NonNumericAppIdInManifest_ReturnsNull()
    {
        var exe = AddGame("Game", "game.exe");
        AddManifest("111", Manifest("111/../x", "Game"));

        Assert.Null(SteamAppIdFinder.FindAppId(exe));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a path <>|")]
    public void FindAppId_BlankOrBadPath_ReturnsNull(string path)
    {
        Assert.Null(SteamAppIdFinder.FindAppId(path));
    }

    [Fact]
    public void FindAppId_PathStoppingAtAnotherGamesFolder_ReturnsNull()
    {
        // Typing "...\common\assettocorsacompetizione\acc.exe" passes through
        // "...\common\assettocorsa", another game's folder. That half-typed path must
        // not match, or the wrong ID gets filled in and sticks.
        AddGame("assettocorsa", "AssettoCorsa.exe");
        AddManifest("244210", Manifest("244210", "assettocorsa"));

        Assert.Null(SteamAppIdFinder.FindAppId(Path.Combine(SteamApps, "common", "assettocorsa")));
        Assert.Null(SteamAppIdFinder.FindAppId(Path.Combine(SteamApps, "common", "assettocorsa", "Assetto")));
    }

    [Fact]
    public void Settings_BrowseToSteamExe_FillsIdAndKeepsTheDetectedMessage()
    {
        var exe = AddGame("Le Mans Ultimate", "Le Mans Ultimate.exe");
        AddManifest("2399420", Manifest("2399420", "Le Mans Ultimate"));
        var row = new AppEditRowViewModel(new iRacingLauncher.Models.AppDefinition());

        row.SetPathFromBrowse(exe);

        Assert.Equal("2399420", row.SteamAppId);
        Assert.StartsWith("Steam game detected", row.FindStatus);
    }

    [Fact]
    public void Settings_AutoFindSteamExe_FillsIdAndKeepsTheDetectedMessage()
    {
        var exe = AddGame("Le Mans Ultimate", "Le Mans Ultimate.exe");
        AddManifest("2399420", Manifest("2399420", "Le Mans Ultimate"));
        var row = new AppEditRowViewModel(new iRacingLauncher.Models.AppDefinition());

        row.SetPathFromAutoFind(exe);

        Assert.Equal("2399420", row.SteamAppId);
        Assert.StartsWith("Steam game detected", row.FindStatus);
    }

    [Fact]
    public void Settings_AutoFindNonSteamExe_ShowsTheUsualFoundMessage()
    {
        var row = new AppEditRowViewModel(new iRacingLauncher.Models.AppDefinition());

        row.SetPathFromAutoFind(@"C:\iRacing\ui\iRacingUI.exe");

        Assert.Equal("", row.SteamAppId);
        Assert.Equal("Found — review and Save to confirm.", row.FindStatus);
    }

    private const string AcExe = @"F:\SteamLibrary2\steamapps\common\assettocorsa\AssettoCorsa.exe";

    [Theory]
    [InlineData("", "", AcExe, true)]
    [InlineData("  ", "", AcExe, true)]
    [InlineData("", "--vr", AcExe, false)]
    [InlineData("2399420", "", AcExe, false)]
    [InlineData("", "", @"F:\SteamLibrary2\steamapps\common\assettocorsa\acs.exe", true)]
    [InlineData("", "", @"F:\SteamLibrary2\steamapps\common\assettocorsa\Content Manager.exe", false)]
    [InlineData("", "", @"F:\SteamLibrary2\steamapps\common\assettocorsa\content manager.EXE", false)]
    public void ShouldAutoFill(string steamAppId, string arguments, string exePath, bool expected)
    {
        Assert.Equal(expected, SteamAppIdFinder.ShouldAutoFill(steamAppId, arguments, exePath));
    }

    [Fact]
    public void Settings_BrowseToContentManagerInsideAcFolder_LeavesIdBlank()
    {
        // Content Manager is often installed inside AC's Steam folder. Launching
        // through Steam would bypass it, so it must keep launching directly.
        var cm = AddGame("assettocorsa", "Content Manager.exe");
        AddManifest("244210", Manifest("244210", "assettocorsa"));
        var row = new AppEditRowViewModel(new iRacingLauncher.Models.AppDefinition());

        row.SetPathFromBrowse(cm);

        Assert.Equal("", row.SteamAppId);
    }

    [Fact]
    public void Settings_FindIdOnContentManager_StillFillsWhenAskedExplicitly()
    {
        var cm = AddGame("assettocorsa", "Content Manager.exe");
        AddManifest("244210", Manifest("244210", "assettocorsa"));
        var row = new AppEditRowViewModel(new iRacingLauncher.Models.AppDefinition());
        row.SetPathFromBrowse(cm);

        row.FindSteamAppId();

        Assert.Equal("244210", row.SteamAppId);
    }

    [Fact]
    public void Settings_BrowseToNormalAcExe_AutoFills()
    {
        var ac = AddGame("assettocorsa", "AssettoCorsa.exe");
        AddManifest("244210", Manifest("244210", "assettocorsa"));
        var row = new AppEditRowViewModel(new iRacingLauncher.Models.AppDefinition());

        row.SetPathFromBrowse(ac);

        Assert.Equal("244210", row.SteamAppId);
    }
}
