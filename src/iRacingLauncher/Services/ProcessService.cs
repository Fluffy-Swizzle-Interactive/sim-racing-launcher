using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using iRacingLauncher.Models;

namespace iRacingLauncher.Services;

public class ProcessService
{
    private readonly IProcessGateway _gateway;
    private readonly Func<int, Task> _delay;

    public ProcessService(IProcessGateway gateway, Func<int, Task>? delay = null)
    {
        _gateway = gateway;
        _delay = delay ?? (seconds => Task.Delay(TimeSpan.FromSeconds(seconds)));
    }

    public bool IsRunning(AppDefinition app) => _gateway.IsRunning(app.ProcessName);

    /// <summary>
    /// Starts a single app. The gateway's success/failure result is discarded here:
    /// callers refresh statuses afterwards, which correctly leaves a failed launch
    /// showing as "Path not found" / "Not running".
    /// </summary>
    public void StartApp(AppDefinition app) => _gateway.Start(app.Path, app.Arguments);

    public void StopApp(AppDefinition app) => _gateway.Kill(app.ProcessName);

    /// <summary>
    /// Stops every currently-running app in <paramref name="apps"/> — pass the
    /// profile's full app list, not just the selected ones: "Stop All" means
    /// everything actually running, not just whatever's checked for the next launch.
    /// </summary>
    /// <returns>The names of the apps that were actually running and got stopped.</returns>
    public List<string> StopAll(IEnumerable<AppDefinition> apps)
    {
        var stopped = new List<string>();
        foreach (var app in apps)
        {
            if (!_gateway.IsRunning(app.ProcessName))
            {
                continue;
            }
            _gateway.Kill(app.ProcessName);
            stopped.Add(app.Name);
        }
        return stopped;
    }

    /// <summary>
    /// Launches every app in <paramref name="apps"/> not already running, staggered
    /// by <paramref name="delaySeconds"/>. Pass an already-filtered (e.g. selected)
    /// list — this method launches everything it's given.
    /// </summary>
    /// <param name="progress">
    /// Reports (current, total) position through <paramref name="apps"/> as each is
    /// reached — including ones skipped as already-running — so a caller can show
    /// "Launching N of M..." for the whole batch, not just successful starts.
    /// </param>
    public async Task<LaunchResult> LaunchAppsAsync(
        IEnumerable<AppDefinition> apps,
        int delaySeconds,
        IProgress<(int Current, int Total)>? progress = null)
    {
        var launched = new List<string>();
        var failed = new List<string>();
        var batch = apps.ToList();
        for (var i = 0; i < batch.Count; i++)
        {
            var app = batch[i];
            progress?.Report((i + 1, batch.Count));

            if (_gateway.IsRunning(app.ProcessName))
            {
                continue;
            }
            // A bad path must not block the batch: skip the bookkeeping and the
            // stagger delay for anything that failed to start, but keep going.
            if (!_gateway.Start(app.Path, app.Arguments))
            {
                failed.Add(app.Name);
                continue;
            }
            launched.Add(app.Name);
            await _delay(delaySeconds);
        }
        return new LaunchResult(launched, failed);
    }
}

/// <summary>
/// Outcome of a launch batch: the apps that started successfully, and the apps
/// that were passed in but failed to start (already-running apps are skipped
/// silently and appear in neither list).
/// </summary>
public record LaunchResult(List<string> Launched, List<string> Failed);
