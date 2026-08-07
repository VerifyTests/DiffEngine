public class TrackerInlineMoveTest
{
    static string sourceContent = "class C\n{\n    void M() => VerifyInline(value, \"old\");\n}";

    static (string temp, string patch, string verified, string cs) WriteStaging(string newContent = "new")
    {
        var directory = Path.Combine(Path.GetTempPath(), $"TrackerInlineMoveTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var cs = Path.Combine(directory, "Tests.cs");
        File.WriteAllText(cs, sourceContent);
        var temp = Path.Combine(directory, "Tests.Test.received.txt");
        File.WriteAllText(temp, newContent);
        var verified = Path.Combine(directory, "Tests.Test.expected.txt");
        File.WriteAllText(verified, "old");
        var patch = Path.Combine(directory, "Tests.Test.inlinepatch");
        InlinePatchFile.Write(patch, new(cs, 3, "\"old\"", newContent));
        return (temp, patch, verified, cs);
    }

    static void Cleanup(string temp)
    {
        var directory = Path.GetDirectoryName(temp)!;
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public async Task AddSingle()
    {
        var (temp, patch, verified, _) = WriteStaging();
        try
        {
            await using var tracker = new RecordingTracker();
            tracker.AddInlineMove(temp, "Tests.cs", patch, verified);
            await Assert.That(tracker.InlineMoves).HasSingleItem();
            await Assert.That(tracker.TrackingAny).IsTrue();
        }
        finally
        {
            Cleanup(temp);
        }
    }

    [Test]
    public async Task AddSameUpdates()
    {
        var (temp, patch, verified, _) = WriteStaging();
        try
        {
            await using var tracker = new RecordingTracker();
            tracker.AddInlineMove(temp, "Tests.cs", patch, verified);
            tracker.AddInlineMove(temp, "Tests.cs", patch, verified);
            await Assert.That(tracker.InlineMoves).HasSingleItem();
        }
        finally
        {
            Cleanup(temp);
        }
    }

    [Test]
    public async Task AcceptAppliesPatchAndCleansStaging()
    {
        var (temp, patch, verified, cs) = WriteStaging();
        try
        {
            await using var tracker = new RecordingTracker();
            var move = tracker.AddInlineMove(temp, cs, patch, verified);
            tracker.Accept(move);
            await tracker.AssertEmpty();
            await Assert.That(File.ReadAllText(cs)).Contains("new");
            await Assert.That(File.Exists(temp)).IsFalse();
            await Assert.That(File.Exists(patch)).IsFalse();
            await Assert.That(File.Exists(verified)).IsFalse();
        }
        finally
        {
            Cleanup(temp);
        }
    }

    [Test]
    public async Task AcceptStalePatchDiscardsAndNotifies()
    {
        var (temp, patch, verified, cs) = WriteStaging();
        try
        {
            // Simulate the source changing after the test run
            File.WriteAllText(cs, "class C { }");
            string? message = null;
            await using var tracker = new RecordingTracker(inlineFailed: (_, m) => message = m);
            var move = tracker.AddInlineMove(temp, cs, patch, verified);
            tracker.Accept(move);
            await tracker.AssertEmpty();
            await Assert.That(message!).Contains("Re-run the test");
            await Assert.That(File.Exists(temp)).IsFalse();
        }
        finally
        {
            Cleanup(temp);
        }
    }

    [Test]
    public async Task DiscardCleansStaging()
    {
        var (temp, patch, verified, cs) = WriteStaging();
        try
        {
            await using var tracker = new RecordingTracker();
            var move = tracker.AddInlineMove(temp, cs, patch, verified);
            tracker.Discard(move);
            await tracker.AssertEmpty();
            await Assert.That(File.Exists(temp)).IsFalse();
            await Assert.That(File.Exists(patch)).IsFalse();
            // Source untouched
            await Assert.That(File.ReadAllText(cs)).Contains("old");
        }
        finally
        {
            Cleanup(temp);
        }
    }

    [Test]
    public async Task AcceptAllMixed()
    {
        var (temp, patch, verified, cs) = WriteStaging();
        try
        {
            await using var tracker = new RecordingTracker();
            tracker.AddInlineMove(temp, cs, patch, verified);
            tracker.AcceptAll();
            await tracker.AssertEmpty();
            await Assert.That(File.ReadAllText(cs)).Contains("new");
        }
        finally
        {
            Cleanup(temp);
        }
    }

    [Test]
    public async Task ClearRemovesInlineMoves()
    {
        var (temp, patch, verified, cs) = WriteStaging();
        try
        {
            await using var tracker = new RecordingTracker();
            tracker.AddInlineMove(temp, cs, patch, verified);
            tracker.Clear();
            await tracker.AssertEmpty();
            // Clear does not delete staging files (matches TrackedMove behavior)
            await Assert.That(File.Exists(temp)).IsTrue();
        }
        finally
        {
            Cleanup(temp);
        }
    }
}

public class TrayVersionFileTest
{
    [Test]
    public async Task RoundTrip()
    {
        TrayVersionFile.Write("20.1.3+abc123");
        try
        {
            var read = TrayVersionFile.TryRead(out var version);
            await Assert.That(read).IsTrue();
            await Assert.That(version).IsEqualTo(new Version(20, 1, 3));
        }
        finally
        {
            TrayVersionFile.Delete();
        }
    }

    [Test]
    public async Task PrereleaseSuffixStripped()
    {
        TrayVersionFile.Write("21.0.0-beta.1");
        try
        {
            var read = TrayVersionFile.TryRead(out var version);
            await Assert.That(read).IsTrue();
            await Assert.That(version).IsEqualTo(new Version(21, 0, 0));
        }
        finally
        {
            TrayVersionFile.Delete();
        }
    }

    [Test]
    public async Task MissingFileFails()
    {
        TrayVersionFile.Delete();
        var read = TrayVersionFile.TryRead(out _);
        await Assert.That(read).IsFalse();
    }

    [Test]
    public async Task GarbageFails()
    {
        TrayVersionFile.Write("garbage");
        try
        {
            var read = TrayVersionFile.TryRead(out _);
            await Assert.That(read).IsFalse();
        }
        finally
        {
            TrayVersionFile.Delete();
        }
    }
}
