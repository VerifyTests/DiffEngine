/// <summary>
/// Which command a key is, by <see cref="ViewerForm.Map" />: the key with the modifiers held, as
/// <c>ProcessCmdKey</c> is handed it.
/// </summary>
public class KeyMapTests
{
    /// <summary>
    /// Every key that is a command on its own, with Alt held. None of them is one then: the three
    /// that change something were Alt+A accepting, Alt+D discarding and Alt+Q quitting.
    /// </summary>
    [Test]
    public async Task AnAltChordIsNoCommand()
    {
        foreach (var key in Enum.GetValues<Keys>().Distinct())
        {
            if (ViewerForm.Map(key) == CommandKind.None)
            {
                continue;
            }

            await Assert.That(ViewerForm.Map(Keys.Alt | key)).IsEqualTo(CommandKind.None).Because($"Alt+{key}");
            await Assert.That(ViewerForm.Map(Keys.Alt | Keys.Shift | key)).IsEqualTo(CommandKind.None).Because($"Alt+Shift+{key}");
        }
    }

    /// <summary>
    /// Alt Gr is Control and Alt together on the layouts that have it, and what it types is a
    /// character: Alt Gr+C is not copy and Alt Gr+0, a closing brace on a German keyboard, is not
    /// a zoom reset.
    /// </summary>
    [Test]
    [Arguments(Keys.C)]
    [Arguments(Keys.A)]
    [Arguments(Keys.D0)]
    [Arguments(Keys.Oemplus)]
    public async Task AltGrIsNoCommand(Keys key) =>
        await Assert.That(ViewerForm.Map(Keys.Control | Keys.Alt | key)).IsEqualTo(CommandKind.None);

    /// <summary>
    /// What the chords were before, which Alt being answered first must not have moved.
    /// </summary>
    [Test]
    public async Task TheOtherChordsAreWhatTheyWere()
    {
        await Assert.That(ViewerForm.Map(Keys.A)).IsEqualTo(CommandKind.Accept);
        await Assert.That(ViewerForm.Map(Keys.Shift | Keys.A)).IsEqualTo(CommandKind.AcceptAll);
        await Assert.That(ViewerForm.Map(Keys.Control | Keys.A)).IsEqualTo(CommandKind.SelectAll);
        await Assert.That(ViewerForm.Map(Keys.Control | Keys.C)).IsEqualTo(CommandKind.Copy);
        await Assert.That(ViewerForm.Map(Keys.D)).IsEqualTo(CommandKind.Discard);
        await Assert.That(ViewerForm.Map(Keys.Q)).IsEqualTo(CommandKind.Quit);
    }
}
