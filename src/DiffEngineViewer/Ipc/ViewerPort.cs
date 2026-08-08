static class ViewerPort
{
    public const int Default = 3493;

    /// <summary>
    /// The tray's piper sits on 3492. Tests override this so a run never talks to a live viewer,
    /// mirroring how PiperTest reassigns PiperClient.Port.
    /// </summary>
    public const string Variable = "DiffEngine_ViewerPort";

    public static int Resolve()
    {
        var value = Environment.GetEnvironmentVariable(Variable);
        if (int.TryParse(value, out var port) &&
            port is > 0 and < 65536)
        {
            return port;
        }

        return Default;
    }
}
