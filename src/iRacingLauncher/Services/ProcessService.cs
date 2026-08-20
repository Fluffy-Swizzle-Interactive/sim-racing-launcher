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

    public async Task<List<string>> LaunchSelectedAsync(IEnumerable<AppEntry> apps, int delaySeconds)
    {
        var launched = new List<string>();
        foreach (var app in apps.Where(a => a.Selected))
        {
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
