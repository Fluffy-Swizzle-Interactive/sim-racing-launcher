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

    public bool IsRunning(AppEntry app) => _gateway.IsRunning(app.ProcessName);

    /// <summary>
    /// Starts a single app. The gateway's success/failure result is discarded here:
    /// callers refresh statuses afterwards, which correctly leaves a failed launch
    /// showing as "Path not found" / "Not running".
    /// </summary>
    public void StartApp(AppEntry app) => _gateway.Start(app.Path);

    public void StopApp(AppEntry app) => _gateway.Kill(app.ProcessName);

    /// <summary>
    /// Launches every selected app not already running, staggered by <paramref name="delaySeconds"/>.
    /// </summary>
    /// <param name="progress">
    /// Reports (current, total) position through the selected batch as each app is
    /// reached — including ones skipped as already-running — so a caller can show
    /// "Launching N of M..." for the whole selection, not just successful starts.
    /// </param>
    public async Task<List<string>> LaunchSelectedAsync(
        IEnumerable<AppEntry> apps,
        int delaySeconds,
        IProgress<(int Current, int Total)>? progress = null)
    {
        var launched = new List<string>();
        var selected = apps.Where(a => a.Selected).ToList();
        for (var i = 0; i < selected.Count; i++)
        {
            var app = selected[i];
            progress?.Report((i + 1, selected.Count));

            if (_gateway.IsRunning(app.ProcessName))
            {
                continue;
            }
            // A bad path must not block the batch: skip the bookkeeping and the
            // stagger delay for anything that failed to start, but keep going.
            if (!_gateway.Start(app.Path))
            {
                continue;
            }
            launched.Add(app.Name);
            await _delay(delaySeconds);
        }
        return launched;
    }
}
