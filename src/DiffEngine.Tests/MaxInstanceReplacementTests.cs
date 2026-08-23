#if NET10_0
/// <summary>
/// Relaunching a pair that is already open does not spend an instance slot.
/// <para>
/// InnerLaunch killed the tool already showing the pair and then asked MaxInstance whether it was
/// allowed to launch. Once the per process counter was spent that answer was no - so a re-failing
/// test closed its own diff window and opened nothing in its place, and reported
/// TooManyRunningDiffTools with the move sent as processId null. The number of open tools had gone
/// down, not up.
/// </para>
/// </summary>
[NotInParallel]
[RunOn(TUnit.Core.Enums.OS.Windows)]
public class MaxInstanceReplacementTests :
    IDisposable
{
    [Test]
    public async Task RelaunchingTheSamePairIsNotANewInstance()
    {
        LimitTo(1);

        ProcessCleanup.Refresh();
        var first = await DiffRunner.LaunchAsync(temp, target);
        await Assert.That(first).IsEqualTo(LaunchResult.StartedNewInstance);

        await WaitForRunning();
        ProcessCleanup.Refresh();

        // The slot is spent, but this pair is already open - so this is a replacement
        var second = await DiffRunner.LaunchAsync(temp, target);
        await Assert.That(second).IsEqualTo(LaunchResult.StartedNewInstance);
    }

    /// <summary>
    /// And a different pair still runs into the limit, which is what the limit is for.
    /// </summary>
    [Test]
    public async Task ADifferentPairStillHitsTheLimit()
    {
        LimitTo(1);

        ProcessCleanup.Refresh();
        await Assert.That(await DiffRunner.LaunchAsync(temp, target)).IsEqualTo(LaunchResult.StartedNewInstance);

        ProcessCleanup.Refresh();
        await Assert.That(await DiffRunner.LaunchAsync(otherTemp, otherTarget)).IsEqualTo(LaunchResult.TooManyRunningDiffTools);
    }

    /// <summary>
    /// Through the environment variable, because that is what MaxInstance reads first and this
    /// machine may well have one set - DiffEngine_MaxInstances persists per user, so the app
    /// domain setting alone silently loses to it. Process scoped, so nothing outlives the run.
    /// </summary>
    static void LimitTo(int value)
    {
        Environment.SetEnvironmentVariable(variable, value.ToString());
        // Forces MaxInstance to re-read, since it caches the first answer
        DiffRunner.MaxInstancesToLaunch(value);
        MaxInstance.ResetCount();
    }

    async Task WaitForRunning()
    {
        var command = tool.BuildCommand(temp, target);
        for (var attempt = 0; attempt < 40; attempt++)
        {
            ProcessCleanup.Refresh();
            if (ProcessCleanup.IsRunning(command))
            {
                return;
            }

            await Task.Delay(250);
        }
    }

    public MaxInstanceReplacementTests()
    {
        Directory.CreateDirectory(directory);

        temp = Write("first.received.zzmax");
        target = Write("first.verified.zzmax");
        otherTemp = Write("second.received.zzmax");
        otherTarget = Write("second.verified.zzmax");
        tool = DiffTools.AddTool(
            name: $"MaxProbe{Guid.NewGuid():N}",
            autoRefresh: false,
            isMdi: false,
            supportsText: false,
            requiresTarget: true,
            useShellExecute: false,
            launchArguments: new(
                Left: (t, g) => $"\"{t}\" \"{g}\"",
                Right: (t, g) => $"\"{g}\" \"{t}\""),
            exePath: FakeDiffTool.Exe,
            binaryExtensions: [".zzmax"])!;
    }

    string Write(string name)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, name);
        return path;
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(variable, original);
        DiffRunner.MaxInstancesToLaunch(5);
        MaxInstance.ResetCount();
        try
        {
            DiffRunner.Kill(temp, target);
            DiffRunner.Kill(otherTemp, otherTarget);
            Directory.Delete(directory, true);
        }
        catch
        {
            // Best effort: the fake tool exits on its own anyway
        }
    }

    // Per test, not static: two tests sharing paths means the second one's first launch finds
    // the first one's tool still open and is treated as a replacement
    const string variable = "DiffEngine_MaxInstances";
    string? original = Environment.GetEnvironmentVariable(variable);
    string directory = Path.Combine(Path.GetTempPath(), $"DiffEngine.MaxInstance.{Guid.NewGuid():N}");
    ResolvedTool tool;
    string temp;
    string target;
    string otherTemp;
    string otherTarget;
}
#endif
