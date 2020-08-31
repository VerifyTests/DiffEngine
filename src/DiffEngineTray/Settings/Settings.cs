public class Settings
{
    public HotKey? HotKey { get; set; }

}
public class HotKey
{
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public bool Control { get; set; }
    public string Key { get; set; } = null!;
}