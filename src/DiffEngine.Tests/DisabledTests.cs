/// <summary>
/// <see cref="DiffRunner.Disabled" /> was captured at type initialisation, so a build server or AI
/// CLI reported after that - which is when a test host reports one, having only just loaded - left
/// diff tools launching anyway.
/// </summary>
[NotInParallel]
public class DisabledTests
{
    [Test]
    public async Task A_build_server_detected_after_first_use_still_disables()
    {
        // As the module initializer left it, and as any consumer that ever set it leaves it
        DiffRunner.Disabled = false;

        DiffRunner.ResetDisabled();
        BuildServerDetector.Detected = true;

        await Assert.That(DiffRunner.Disabled).IsTrue();
    }

    [Test]
    public async Task Setting_it_pins_it()
    {
        DiffRunner.ResetDisabled();
        BuildServerDetector.Detected = true;

        DiffRunner.Disabled = false;

        await Assert.That(DiffRunner.Disabled).IsFalse();
    }

    /// <summary>
    /// Every other test in this assembly runs with it off, which the module initializer does once.
    /// </summary>
    [After(Test)]
    public void Restore() =>
        DiffRunner.Disabled = false;
}
