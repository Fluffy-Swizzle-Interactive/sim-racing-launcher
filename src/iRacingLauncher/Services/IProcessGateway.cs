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

    /// <summary>
    /// Hands a steam:// URL to Steam (e.g. "steam://launch/2399420/vr"). Returns
    /// false, without launching anything, for any URL that isn't steam://, and
    /// false when it can't be opened (Steam not installed, etc.). Must not throw.
    /// </summary>
    bool OpenSteamUrl(string url);

    void Kill(string processName);
}
