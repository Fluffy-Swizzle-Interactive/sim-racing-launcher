namespace iRacingLauncher.Services;

public interface IProcessGateway
{
    bool IsRunning(string processName);

    /// <summary>
    /// Attempts to start the executable at <paramref name="exePath"/> with the given
    /// command-line <paramref name="arguments"/> (pass "" for none). Returns true if
    /// the process was started, false if it could not be launched for any reason
    /// (missing/blank path, UAC declined, etc.). Implementations must not throw for
    /// an unlaunchable path.
    /// </summary>
    bool Start(string exePath, string arguments);
    void Kill(string processName);
}
