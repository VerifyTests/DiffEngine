using NumericUpDown = System.Windows.Forms.NumericUpDown;

/// <summary>
/// The form's range has to be the setting's range. DiffEngine_MaxInstances takes any ushort, and
/// the spinner was left at NumericUpDown's default maximum of 100, so anything above that threw
/// out of the constructor and Options could never be opened to lower it again.
/// </summary>
[TUnit.Core.Executors.STAThreadExecutor]
public class OptionsFormMaxInstancesTests
{
    [Test]
    public async Task Opens_at_the_highest_max_instances_that_can_be_set()
    {
        using var form = new OptionsForm(
            new()
            {
                MaxInstancesToLaunch = ushort.MaxValue
            },
            _ => Task.FromResult<IReadOnlyCollection<string>>([]));

        var spinner = form.Controls
            .Find("maxInstancesNumericUpDown", true)
            .OfType<NumericUpDown>()
            .Single();
        await Assert.That(spinner.Value).IsEqualTo(ushort.MaxValue);
    }
}
