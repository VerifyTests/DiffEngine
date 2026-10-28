/// <param name="StartTime">
/// When the process started, as Restart Manager reported it, in UTC. What makes the id mean this
/// process rather than whichever one holds the id when it is acted on: the kill can come after a
/// dialog that waits on the user for as long as they like, and Windows reuses ids. Null when it
/// could not be read, which nothing is then killed on the strength of.
/// </param>
record LockingProcess(int ProcessId, string Name, DateTime? StartTime = null);