/// <summary>
/// Skips a test that shells out to the .NET SDK. The SDK is what built these tests, so it is
/// there in every arrangement that matters; the skip exists for the one where the test assembly
/// was carried somewhere the CLI is not.
/// </summary>
public sealed class RequiresDotnetAttribute() :
    SkipAttribute("The dotnet CLI was not found, so the F# compiler cannot be run.")
{
    public static string? DotnetPath { get; } = Find();

    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(DotnetPath is null);

    static string? Find()
    {
        var name = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "dotnet.exe" : "dotnet";
        var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(root))
        {
            // ReSharper disable once RedundantSuppressNullableWarningExpression
            var candidate = Path.Combine(root!, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var paths = Environment.GetEnvironmentVariable("PATH");
        if (paths == null)
        {
            return null;
        }

        foreach (var directory in paths.Split(Path.PathSeparator))
        {
            if (directory.Length == 0)
            {
                continue;
            }

            try
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // An invalid directory on PATH is not this test's problem
            }
        }

        return null;
    }
}
