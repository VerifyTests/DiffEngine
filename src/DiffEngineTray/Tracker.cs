using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

class Tracker :
    IAsyncDisposable
{
    protected Action active;
    protected Action inactive;
    ConcurrentDictionary<string, TrackedMove> moves = new ConcurrentDictionary<string, TrackedMove>(StringComparer.OrdinalIgnoreCase);
    ConcurrentDictionary<string, TrackedDelete> deletes = new ConcurrentDictionary<string, TrackedDelete>(StringComparer.OrdinalIgnoreCase);
    Timer timer;

    public Tracker(Action active, Action inactive)
    {
        this.active = active;
        this.inactive = inactive;
        timer = new Timer(ScanFiles);
    }

    void ScanFiles()
    {
        var changed = false;
        timer.Pause();
        var wasActive = TrackingAny;
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
        timer.Resume();
        if (changed)
        {
            ToggleActive(wasActive);
        }
    }

    void ToggleActive(bool wasActive)
    {
        if (TrackingAny)
        {
            if (!wasActive)
            {
                active();
            }
        }
        else if (!TrackingAny)
        {
            if (wasActive)
            {
                inactive();
            }
        }
    }

    public bool TrackingAny
    {
        get => moves.Any() || deletes.Any();
    }

    public TrackedMove AddMove(
        string temp,
        string target,
        bool canKill,
        int? processId)
    {
        var updated = false;
        var wasActive = TrackingAny;
        var update = moves.AddOrUpdate(
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
            ToggleActive(wasActive);
        }

        return update;
    }

    public TrackedDelete AddDelete(string file)
    {
        var updated = false;
        var wasActive = TrackingAny;
        var delete = deletes.AddOrUpdate(
            file,
            addValueFactory: s =>
            {
                updated = true;
                return new TrackedDelete(file);
            },
            updateValueFactory: (s, existing) => existing);
        if (updated)
        {
            ToggleActive(wasActive);
        }

        return delete;
    }

    public void Accept(TrackedDelete delete)
    {
        var wasActive = TrackingAny;
        if (deletes.Remove(delete.File, out var removed))
        {
            File.Delete(removed.File);
            ToggleActive(wasActive);
        }
    }

    public void Accept(TrackedMove move)
    {
        var wasActive = TrackingAny;
        if (moves.Remove(move.Target, out var removed))
        {
            InnerMove(removed);
            ToggleActive(wasActive);
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
            //TODO verify command line matches
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
        var wasActive = TrackingAny;
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
        ToggleActive(wasActive);
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