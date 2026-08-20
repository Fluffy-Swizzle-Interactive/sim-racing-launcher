using iRacingLauncher.Models;
using iRacingLauncher.Services;
using Xunit;

namespace iRacingLauncher.Tests;

public class FakeProcessGateway : IProcessGateway
{
    public HashSet<string> RunningProcessNames { get; } = new();
    public List<string> StartedPaths { get; } = new();
    public List<string> KilledProcessNames { get; } = new();

    /// <summary>Paths for which Start() reports failure, simulating a missing/invalid exe.</summary>
    public HashSet<string> FailingPaths { get; } = new();

    public bool IsRunning(string processName) => RunningProcessNames.Contains(processName);

    public bool Start(string exePath)
    {
        StartedPaths.Add(exePath);
        return !FailingPaths.Contains(exePath);
    }

    public void Kill(string processName) => KilledProcessNames.Add(processName);
}

public class ProcessServiceTests
{
    private static List<AppEntry> ThreeApps() => new()
    {
        new AppEntry { Name = "A", ProcessName = "procA", Path = @"C:\a.exe", Selected = true },
        new AppEntry { Name = "B", ProcessName = "procB", Path = @"C:\b.exe", Selected = true },
        new AppEntry { Name = "C", ProcessName = "procC", Path = @"C:\c.exe", Selected = true },
    };

    [Fact]
    public async Task LaunchSelectedAsync_SkipsAlreadyRunningApps()
    {
        var gateway = new FakeProcessGateway();
        gateway.RunningProcessNames.Add("procB");
        var delays = new List<int>();
        var service = new ProcessService(gateway, seconds => { delays.Add(seconds); return Task.CompletedTask; });

        var launched = await service.LaunchSelectedAsync(ThreeApps(), delaySeconds: 2);

        Assert.Equal(new[] { "A", "C" }, launched);
        Assert.Equal(new[] { @"C:\a.exe", @"C:\c.exe" }, gateway.StartedPaths);
    }

    [Fact]
    public async Task LaunchSelectedAsync_SkipsUnselectedApps()
    {
        var apps = ThreeApps();
        apps[1].Selected = false;
        var gateway = new FakeProcessGateway();
        var service = new ProcessService(gateway, _ => Task.CompletedTask);

        var launched = await service.LaunchSelectedAsync(apps, delaySeconds: 2);

        Assert.Equal(new[] { "A", "C" }, launched);
    }

    [Fact]
    public async Task LaunchSelectedAsync_DelaysAfterEachStart()
    {
        var gateway = new FakeProcessGateway();
        var delayCalls = new List<int>();
        var service = new ProcessService(gateway, seconds => { delayCalls.Add(seconds); return Task.CompletedTask; });

        await service.LaunchSelectedAsync(ThreeApps(), delaySeconds: 3);

        Assert.Equal(new[] { 3, 3, 3 }, delayCalls);
    }

    [Fact]
    public async Task LaunchSelectedAsync_ContinuesBatchWhenOneAppFailsToStart()
    {
        // Spec contract: "one bad path doesn't block the batch".
        var gateway = new FakeProcessGateway();
        gateway.FailingPaths.Add(@"C:\b.exe");
        var delays = new List<int>();
        var service = new ProcessService(gateway, seconds => { delays.Add(seconds); return Task.CompletedTask; });

        var launched = await service.LaunchSelectedAsync(ThreeApps(), delaySeconds: 2);

        // The app that failed to start is not reported as launched...
        Assert.Equal(new[] { "A", "C" }, launched);
        // ...but the batch kept going: C was still attempted after B failed.
        Assert.Equal(new[] { @"C:\a.exe", @"C:\b.exe", @"C:\c.exe" }, gateway.StartedPaths);
        // And no stagger delay is burned waiting on an app that never started.
        Assert.Equal(new[] { 2, 2 }, delays);
    }

    [Fact]
    public void StopApp_KillsByProcessName()
    {
        var gateway = new FakeProcessGateway();
        var service = new ProcessService(gateway);

        service.StopApp(new AppEntry { Name = "A", ProcessName = "procA", Path = @"C:\a.exe" });

        Assert.Contains("procA", gateway.KilledProcessNames);
    }

    [Fact]
    public void IsRunning_ReturnsTrueWhenGatewayReportsRunning()
    {
        var gateway = new FakeProcessGateway();
        gateway.RunningProcessNames.Add("procA");
        var service = new ProcessService(gateway);

        var result = service.IsRunning(new AppEntry { Name = "A", ProcessName = "procA", Path = @"C:\a.exe" });

        Assert.True(result);
    }

    [Fact]
    public void StartApp_StartsByPath()
    {
        var gateway = new FakeProcessGateway();
        var service = new ProcessService(gateway);

        service.StartApp(new AppEntry { Name = "A", ProcessName = "procA", Path = @"C:\a.exe" });

        Assert.Contains(@"C:\a.exe", gateway.StartedPaths);
    }
}
