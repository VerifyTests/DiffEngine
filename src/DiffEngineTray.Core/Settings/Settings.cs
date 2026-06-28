public class Settings
{
    public HotKey? AcceptAllHotKey { get; set; }
    public HotKey? DiscardAllHotKey { get; set; }
    public HotKey? AcceptOpenHotKey { get; set; }
    public bool RunAtStartup { get; set; }
    [JsonIgnore]
    public bool TargetOnLeft { get; set; } = TargetPosition.TargetOnLeft;
    [JsonIgnore]
    public int MaxInstancesToLaunch { get; set; } = MaxInstance.MaxInstancesToLaunch;
}