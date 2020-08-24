using System.Collections.Concurrent;
using System.IO;
using System.Linq;

static class Tracking
{
    static ConcurrentBag<TrackedMove> trackedMoves = new ConcurrentBag<TrackedMove>();
    static ConcurrentBag<TrackedDelete> trackedDeletes = new ConcurrentBag<TrackedDelete>();

    public static bool TrackingAny
    {
        get => trackedMoves.Any() || trackedDeletes.Any();
    }

    public static void AddMove(
        string temp,
        string target,
        bool isMdi,
        bool autoRefresh,
        int processId)
    {
        trackedMoves.Add(new TrackedMove(temp, target, isMdi, autoRefresh, processId));
    }

    public static void AddDelete(string file)
    {
        trackedDeletes.Add(new TrackedDelete(file));
    }

    public static void ApproveAll()
    {
        foreach (var delete in trackedDeletes)
        {
            File.Delete(delete.File);
        }
        trackedDeletes.Clear();
        foreach (var move in trackedMoves)
        {
            File.Move(move.Temp,move.Target, true);
        }
        trackedMoves.Clear();
    }
}