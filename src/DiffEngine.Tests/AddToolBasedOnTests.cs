/// <summary>
/// What a derived tool inherits.
/// <para>
/// Every flag on AddToolBasedOn is a nullable that falls back to the tool being derived from, and
/// useShellExecute defaulted to true rather than null - so its `?? existing` was unreachable, and a
/// caller who said nothing about it got true rather than what they derived from. For the five
/// definitions that set it false (the bundled viewer, VS Code, Cursor, MsWordDiff, MsExcelDiff)
/// that silently changed how the tool launches, and took the inherited CreateNoWindow with it,
/// which is the console flash the viewer definition exists to prevent.
/// </para>
/// <para>
/// Asserted on the defaults rather than on a derived tool, because AddToolBasedOn resolves through
/// ToolLookup and so only works for a tool installed on the machine running the test. A behavioural
/// test would quietly do nothing wherever the tool it names is absent - which, for the viewer, is
/// this machine and CI both.
/// </para>
/// </summary>
public class AddToolBasedOnTests
{
    [Test]
    public async Task EveryOptionalFlagFallsBackToTheToolItIsBasedOn()
    {
        var method = typeof(DiffTools).GetMethod(nameof(DiffTools.AddToolBasedOn))!;

        var withDefaults = method
            .GetParameters()
            .Where(_ => _.ParameterType == typeof(bool?))
            .ToList();

        // The flags, so a new one cannot be added without this noticing
        await Assert.That(withDefaults.Select(_ => _.Name!).ToList()).IsEquivalentTo(
        [
            "autoRefresh",
            "isMdi",
            "supportsText",
            "requiresTarget",
            "useShellExecute",
            "createNoWindow",
            "killLockingProcess"
        ]);

        foreach (var parameter in withDefaults)
        {
            await Assert.That(parameter.HasDefaultValue).IsTrue();
            await Assert.That(parameter.DefaultValue).IsNull();
        }
    }
}
