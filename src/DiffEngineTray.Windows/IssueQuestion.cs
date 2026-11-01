/// <summary>
/// How the Windows head asks whether to open an issue for a failure: a modal box, which the core's
/// <see cref="IssueLauncher" /> has no toolkit to show. Handed over in <see cref="Program" />'s
/// Main, before anything can fail.
/// </summary>
static class IssueQuestion
{
    public static bool Declined(string text)
    {
        var result = MessageBox.Show(
            text,
            "DiffEngineTray Error",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Error);
        return result == DialogResult.No;
    }
}
