using Avalonia.Platform;

namespace DiffEngineTray;

static class AppIcons
{
    public static WindowIcon Default { get; } = Load("default.png");
    public static WindowIcon Active { get; } = Load("active.png");

    static WindowIcon Load(string name)
    {
        using var stream = AssetLoader.Open(new($"avares://DiffEngineTray.Avalonia/Assets/{name}"));
        return new(stream);
    }
}
