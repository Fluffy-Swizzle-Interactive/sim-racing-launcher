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

    public bool Start(string exePath)
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
