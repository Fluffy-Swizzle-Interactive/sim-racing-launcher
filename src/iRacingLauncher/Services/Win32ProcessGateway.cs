using System;
using System.Diagnostics;
using System.IO;

namespace iRacingLauncher.Services;

public class Win32ProcessGateway : IProcessGateway
{
    public bool IsRunning(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }
        return Process.GetProcessesByName(processName).Length > 0;
    }

    public bool Start(string exePath, string arguments)
    {
        // A blank path throws InvalidOperationException and a missing file throws
        // Win32Exception, so screen both out before asking the shell to launch.
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exePath)
            {
                Arguments = arguments,
                // Start the app in its own folder rather than inheriting the
                // launcher's current directory. Sim tools commonly resolve config
                // and data files relative to the working directory, so inheriting
                // ours can break them outright; it also means anything they load by
                // bare filename would search a directory they never chose — which,
                // if the launcher happened to be started from somewhere writable
                // like Downloads, is a side-loading hazard for the app we launch.
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty,
                WindowStyle = ProcessWindowStyle.Minimized,
                UseShellExecute = true,
            });
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // File vanished between the check and the call, no shell association,
            // or the user declined the UAC elevation prompt.
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public bool OpenSteamUrl(string url)
    {
        // Shell-executing an arbitrary string would run any file or URL handler, so
        // only steam:// is ever let through.
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("steam://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // No steam:// handler registered, i.e. Steam isn't installed.
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public void Kill(string processName)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
                // Already exited between enumeration and Kill — ignore.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Access denied (e.g. an elevated sim tool) — nothing we can do; ignore.
            }
        }
    }
}
