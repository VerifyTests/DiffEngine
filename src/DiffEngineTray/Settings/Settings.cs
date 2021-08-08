public class Settings
{
    public HotKey? AcceptAllHotKey { get; set; }
    public HotKey? AcceptOpenHotKey { get; set; }
    public bool RunAtStartup { get; set; }
    public bool TargetOnLeft { get; set; }
    public int MaxInstancesToLaunch { get; set; } = MaxInstance.MaxInstancesToLaunch;
}