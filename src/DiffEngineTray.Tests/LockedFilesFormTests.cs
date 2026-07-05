#if DEBUG
public class LockedFilesFormTests
{
    //[Test]
    //[Explicit]
    //public void Launch()
    //{
    //    using var form = new LockedFilesForm(BuildMove(), BuildLocked());
    //    form.ShowDialog();
    //}

    [Test]
    public async Task Default()
    {
        using var form = new LockedFilesForm(BuildMove(), BuildLocked());
        await Verify(form);
    }

    static TrackedMove BuildMove() =>
        new(
            @"C:\tests\AdviceSummaryTests.BuildWord.received.docx",
            @"C:\tests\AdviceSummaryTests.BuildWord.verified.docx",
            null,
            null,
            false,
            null,
            null,
            "docx");

    static LockedFiles BuildLocked() =>
        new(
            [
                @"C:\tests\AdviceSummaryTests.BuildWord.received.docx",
                @"C:\tests\AdviceSummaryTests.BuildWord.verified.docx"
            ],
            [new(1234, "Microsoft Word")]);
}
#endif