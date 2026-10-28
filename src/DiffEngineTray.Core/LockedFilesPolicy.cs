/// <summary>
/// Whether a move whose target is locked kills what holds it without asking: the
/// AlwaysKillLockingProcesses setting, set at startup and from the Options form, and by the
/// question's own "always" box. Apart from the question, which is a WinForms form in the head, so
/// the tracker can ask this of a head that has none.
/// </summary>
static class LockedFilesPolicy
{
    public static bool AlwaysKill { get; set; }
}
