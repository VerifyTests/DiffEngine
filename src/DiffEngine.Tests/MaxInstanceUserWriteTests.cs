/// <summary>
/// The tray persisted DiffEngine_MaxInstances on every settings save, whether or not the limit was
/// part of what changed - the tray's SettingsHelper.Write calls SetForUser unconditionally, and
/// the value it passes is whatever the options form was populated with, which is the value already
/// in effect. Saves of unrelated settings go through the same path, including the "always kill
/// locking processes" prompt, which has no options form at all. So opening the tray once was enough
/// to leave a user scope variable behind on a machine that had never chosen a limit.
/// </summary>
[NotInParallel]
public class MaxInstanceUserWriteTests
{
    const string variable = "DiffEngine_MaxInstances";

    /// <summary>
    /// The case that put the variable on machines that never asked for it: nothing set, so the
    /// form opens at the default and saves it straight back.
    /// </summary>
    [Test]
    public async Task Saving_the_default_on_a_machine_with_nothing_set_writes_nothing()
    {
        Environment.SetEnvironmentVariable(variable, null);
        MaxInstance.ResetAppDomainValue();

        MaxInstance.SetForUser(MaxInstance.MaxInstancesToLaunch);

        await Assert.That(Environment.GetEnvironmentVariable(variable)).IsNull();
    }

    [Test]
    public async Task Saving_the_value_already_set_writes_nothing()
    {
        Environment.SetEnvironmentVariable(variable, "10");
        MaxInstance.ResetAppDomainValue();

        MaxInstance.SetForUser(10);

        await Assert.That(Environment.GetEnvironmentVariable(variable)).IsEqualTo("10");
        await Assert.That(MaxInstance.MaxInstancesToLaunch).IsEqualTo(10);
    }

    /// <summary>
    /// Choosing the default is a way back to an unset machine, rather than a pin at whatever the
    /// default happens to be today. Same as SetTargetOnLeft with false.
    /// </summary>
    [Test]
    public async Task Saving_the_default_clears_a_value_that_was_set()
    {
        var @default = Default();
        Environment.SetEnvironmentVariable(variable, "10");
        MaxInstance.ResetAppDomainValue();

        MaxInstance.SetForUser(@default);

        await Assert.That(Environment.GetEnvironmentVariable(variable)).IsNull();
        await Assert.That(MaxInstance.MaxInstancesToLaunch).IsEqualTo(@default);
    }

    /// <summary>
    /// Including a value that was pinned at the default explicitly - it says nothing the absent
    /// variable does not, and leaving it would keep the machine pinned.
    /// </summary>
    [Test]
    public async Task Saving_the_default_over_a_redundant_pin_clears_it()
    {
        var @default = Default();
        Environment.SetEnvironmentVariable(variable, @default.ToString());
        MaxInstance.ResetAppDomainValue();

        MaxInstance.SetForUser(@default);

        await Assert.That(Environment.GetEnvironmentVariable(variable)).IsNull();
    }

    /// <summary>
    /// What the limit is with nothing set, which is what defaultMax is, without reaching into it.
    /// </summary>
    static int Default()
    {
        Environment.SetEnvironmentVariable(variable, null);
        MaxInstance.ResetAppDomainValue();
        return MaxInstance.MaxInstancesToLaunch;
    }

    /// <summary>
    /// And a save that does change the limit still lands, which is the point of the setting.
    /// </summary>
    [Test]
    public async Task Saving_a_different_value_writes_it()
    {
        Environment.SetEnvironmentVariable(variable, "10");
        MaxInstance.ResetAppDomainValue();

        MaxInstance.SetForUser(7);

        await Assert.That(Environment.GetEnvironmentVariable(variable)).IsEqualTo("7");
        await Assert.That(MaxInstance.MaxInstancesToLaunch).IsEqualTo(7);
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
