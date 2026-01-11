public static class Program
{
    public static int Main()
    {
        try
        {
            TestDiffToolsAccess();
            TestDefinitionsAccess();
            TestToolResolution();

            Console.WriteLine("All DiffEngine AOT tests passed!");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Test failed: {ex}");
            return 1;
        }
    }

    static void TestDiffToolsAccess()
    {
        // Test that we can access DiffTools static properties without reflection issues
        var isDetected = DiffTools.IsDetectedForExtension(DiffTool.VisualStudioCode, "txt");
        Console.WriteLine($"DiffTools.IsDetectedForExtension(VSCode, 'txt'): {isDetected}");

        var allTools = DiffTools.Resolved.ToList();
        Console.WriteLine($"DiffTools.Resolved count: {allTools.Count}");
    }

    static void TestDefinitionsAccess()
    {
        // Test that tool definitions can be accessed
        var definitions = Definitions.Tools.ToList();
        Console.WriteLine($"Definitions.Tools count: {definitions.Count}");

        foreach (var def in definitions.Take(3))
        {
            Console.WriteLine($"  Tool: {def.Tool}, Url: {def.Url}");
        }
    }

    static void TestToolResolution()
    {
        // Test tool resolution
        var found = DiffTools.TryFindByExtension("txt", out var resolved);
        Console.WriteLine($"TryFindByExtension('txt'): found={found}");

        if (found && resolved != null)
        {
            Console.WriteLine($"  Resolved tool: {resolved.Tool}");
        }

        // Test by name
        var foundByName = DiffTools.TryFindByName(DiffTool.VisualStudioCode, out _);
        Console.WriteLine($"TryFindByName(VisualStudioCode): found={foundByName}");
    }
}
