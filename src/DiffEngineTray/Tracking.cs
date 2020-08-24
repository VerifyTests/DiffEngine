using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

static class Tracking
{
    static ConcurrentDictionary<string, TrackedMove> moves = new ConcurrentDictionary<string, TrackedMove>(StringComparer.OrdinalIgnoreCase);
    static ConcurrentDictionary<string, TrackedDelete> deletes = new ConcurrentDictionary<string, TrackedDelete>(StringComparer.OrdinalIgnoreCase);

    public static bool TrackingAny
    {
        get => moves.Any() || deletes.Any();
    }

    public static void AddMove(
        string temp,
        string target,
        bool isMdi,
        bool autoRefresh,
        int processId)
    {
        moves[target] = new TrackedMove(temp, target, isMdi, autoRefresh, processId);
    }

    public static void AddDelete(string file)
    {
        deletes[file] = new TrackedDelete(file);
    }

    public static void Delete(TrackedDelete delete)
    {
        if (deletes.Remove(delete.File, out _))
        {
            File.Delete(delete.File);
        }
    }

    public static void Move(TrackedMove move)
    {
        if (moves.Remove(move.Target, out _))
        {
            if (File.Exists(move.Temp))
            {
                File.Move(move.Temp, move.Target, true);
            }
        }
    }

    public static void ApproveAll()
    {
        foreach (var delete in deletes.Values)
        {
            File.Delete(delete.File);
        }

        deletes.Clear();
        foreach (var move in moves.Values)
        {
            if (File.Exists(move.Temp))
            {
                File.Move(move.Temp, move.Target, true);
            }
        }

        moves.Clear();
    }

    public static ICollection<TrackedDelete> Deletes
    {
        get => deletes.Values;
    }

    public static ICollection<TrackedMove> Moves
    {
        get => moves.Values;
    }
}