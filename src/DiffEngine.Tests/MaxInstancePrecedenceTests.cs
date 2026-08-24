/// <summary>
/// DiffEngine_MaxInstances was read ahead of the app domain value, and it persists per user -
/// DiffEngineTray writes it to the user environment on every options save. So on a machine that
/// had ever saved Options, <see cref="DiffRunner.MaxInstancesToLaunch" /> silently did nothing,
/// and a test calling it to keep diff windows shut still had them open.
/// </summary>
[NotInParallel]
public class MaxInstancePrecedenceTests
{
    const string variable = "DiffEngine_MaxInstances";

    [Test]
    public async Task Setting_it_beats_the_environment()
    {
        Environment.SetEnvironmentVariable(variable, "10");
        MaxInstance.ResetAppDomainValue();

        DiffRunner.MaxInstancesToLaunch(0);

        await Assert.That(MaxInstance.MaxInstancesToLaunch).IsEqualTo(0);
    }

    /// <summary>
    /// The environment is still what an app domain that sets nothing gets, which is how the tray
    /// and DiffEngine_MaxInstances go on working.
    /// </summary>
    [Test]
    public async Task The_environment_is_the_default_when_nothing_sets_it()
    {
        Environment.SetEnvironmentVariable(variable, "10");
        MaxInstance.ResetAppDomainValue();

        await Assert.That(MaxInstance.MaxInstancesToLaunch).IsEqualTo(10);
    }

    /// <summary>
    /// A user scope save is the later explicit set, so it takes the value back off the app domain
    /// rather than losing to it. This is what the tray does on an options save.
    /// </summary>
    [Test]
    public async Task A_user_set_after_an_app_domain_set_wins()
    {
        // Not 3, so the save is a change and SetForUser does not skip it. Whatever this machine
        // happens to have set is not allowed to decide that.
        Environment.SetEnvironmentVariable(variable, "10");
        DiffRunner.MaxInstancesToLaunch(0);

        MaxInstance.SetForUser(3);

        await Assert.That(MaxInstance.MaxInstancesToLaunch).IsEqualTo(3);
    }

    string? original = Environment.GetEnvironmentVariable(variable);

    [After(Test)]
    public void Restore()
    {
        Environment.SetEnvironmentVariable(variable, original);
        MaxInstance.ResetAppDomainValue();
        MaxInstance.ResetCount();
    }
}
