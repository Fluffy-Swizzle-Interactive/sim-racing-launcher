namespace iRacingLauncher.Services;

public interface IProcessGateway
{
    bool IsRunning(string processName);

    /// <summary>
    /// Attempts to start the executable at <paramref name="exePath"/>.
    /// Returns true if the process was started, false if it could not be
    /// launched for any reason (missing/blank path, UAC declined, etc.).
    /// Implementations must not throw for an unlaunchable path.
    /// </summary>
    bool Start(string exePath);
    void Kill(string processName);
}
