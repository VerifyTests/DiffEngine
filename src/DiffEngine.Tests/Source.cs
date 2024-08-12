public static class Source
{
    public static void Init([CallerFilePath] string file = "") =>
        Directory = Path.GetDirectoryName(file)!;

    public static string File([CallerFilePath] string file = "") =>
        file;

    public static string Directory { get; private set; } = null!;
}