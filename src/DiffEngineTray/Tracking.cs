using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

class Tracking:IAsyncDisposable
{
    Action active;
    Action inactive;
    ConcurrentDictionary<string, TrackedMove> moves = new ConcurrentDictionary<string, TrackedMove>(StringComparer.OrdinalIgnoreCase);
    ConcurrentDictionary<string, TrackedDelete> deletes = new ConcurrentDictionary<string, TrackedDelete>(StringComparer.OrdinalIgnoreCase);
    Timer timer;

    TimeSpan timeSpan = TimeSpan.FromSeconds(2);
    public Tracking(Action active, Action inactive)
    {
        this.active = active;
        this.inactive = inactive;
        timer = new Timer(state => { ScanFiles(); }, null, timeSpan, timeSpan);
    }

    void ScanFiles()
    {
        var changed = false;
        timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        foreach (var delete in deletes.ToList())
        {
            if (!File.Exists(delete.Value.File))
            {
                deletes.TryRemove(delete.Key, out _);
                changed = true;
            }
        }
        foreach (var move in moves.ToList())
        {
            if (!File.Exists(move.Value.Temp))
            {
                changed = true;
                if (moves.TryRemove(move.Key, out var removed))
                {
                    KillProcess(removed);
                }
            }
        }
        timer.Change(timeSpan, timeSpan);
        if (changed)
        {
            ToggleActive();
        }
    }

    void ToggleActive()
    {
        if (TrackingAny)
        {
            active();
        }
        else
        {
            inactive();
        }
    }

    public bool TrackingAny
    {
        get => moves.Any() || deletes.Any();
    }

    public void AddMove(
        string temp,
        string target,
        bool canKill,
        int? processId)
    {
        var updated = false;
        moves.AddOrUpdate(
            target,
            addValueFactory: s =>
            {
                updated = true;
                return new TrackedMove(temp, target, canKill, processId);
            },
            updateValueFactory: (s, existing) =>
            {
                existing.ProcessId ??= processId;
                return existing;
            });
        if (updated)
        {
            ToggleActive();
        }
    }

    public void AddDelete(string file)
    {
        deletes[file] = new TrackedDelete(file);
        ToggleActive();
    }

    public void Delete(TrackedDelete delete)
    {
        if (deletes.Remove(delete.File, out var removed))
        {
            File.Delete(removed.File);
            ToggleActive();
        }
    }

    public void Move(TrackedMove move)
    {
        if (moves.Remove(move.Target, out var removed))
        {
            InnerMove(removed);
            ToggleActive();
        }
    }

    static void InnerMove(TrackedMove move)
    {
        if (File.Exists(move.Temp))
        {
            File.Move(move.Temp, move.Target, true);
        }

        KillProcess(move);
    }

    static void KillProcess(TrackedMove move)
    {
        if (!move.CanKill || move.ProcessId == null)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(move.ProcessId.Value);
            process.Kill();
        }
        catch (ArgumentException)
        {
            //If process doesnt exists
        }
    }

    public void AcceptAll()
    {
        foreach (var delete in deletes.Values)
        {
            File.Delete(delete.File);
        }

        deletes.Clear();
        foreach (var move in moves.Values)
        {
            InnerMove(move);
        }

        moves.Clear();
        ToggleActive();
    }

    public ICollection<TrackedDelete> Deletes
    {
        get => deletes.Values;
    }

    public ICollection<TrackedMove> Moves
    {
        get => moves.Values;
    }

    public ValueTask DisposeAsync()
    {
        return timer.DisposeAsync();
    }
}