using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Serilog;

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
        timer.Pause();
        try
        {
            var changed = false;
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
                        KillProcesses(removed);
                    }
                }
            }

            timer.Resume();
            if (changed)
            {
                ToggleActive(wasActive);
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Failed to scan files");
        }
        finally
        {
            timer.Resume();
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
        string exe,
        string arguments,
        bool canKill,
        (int id, DateTime startTime)? process)
    {
        timer.Pause();
        try
        {

            var updated = false;
            var wasActive = TrackingAny;
            var update = moves.AddOrUpdate(
                target,
                addValueFactory: s =>
                {
                    updated = true;
                    var move = new TrackedMove(temp, target, exe, arguments, canKill);
                    if (process.HasValue)
                    {
                        move.AddProcess(process.Value);
                    }

                    return move;
                },
                updateValueFactory: (s, existing) =>
                {
                    if (process.HasValue)
                    {
                        existing.AddProcess(process.Value);
                    }

                    return existing;
                });
            if (updated)
            {
                ToggleActive(wasActive);
            }

            return update;
        }
        finally
        {
            timer.Resume();
        }
    }

    public TrackedDelete AddDelete(string file)
    {
        timer.Pause();
        try
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
        finally
        {
            timer.Resume();
        }
    }

    public void Accept(TrackedDelete delete)
    {
        timer.Pause();
        try
        {
            var wasActive = TrackingAny;
            if (deletes.Remove(delete.File, out var removed))
            {
                File.Delete(removed.File);
                ToggleActive(wasActive);
            }
        }
        finally
        {
            timer.Resume();
        }
    }

    public void Accept(TrackedMove move)
    {
        timer.Pause();
        try
        {
            var wasActive = TrackingAny;
            if (moves.Remove(move.Target, out var removed))
            {
                InnerMove(removed);
                ToggleActive(wasActive);
            }
        }
        finally
        {
            timer.Resume();
        }
    }

    static void InnerMove(TrackedMove move)
    {
        if (File.Exists(move.Temp))
        {
            File.Move(move.Temp, move.Target, true);
        }

        KillProcesses(move);
    }

    static void KillProcesses(TrackedMove move)
    {
        if (!move.CanKill)
        {
            Log.Information($"Did not kill for `{move.Temp}` since CanKill=false");
            return;
        }

        if (!move.Processes.Any())
        {
            Log.Information($"No processes to kill for  for `{move.Temp}`");
            return;
        }

        foreach (var (processId, startTime) in move.Processes)
        {
            KillProcess(move, processId, startTime);
        }
    }

    static void KillProcess(TrackedMove move, int id, DateTime startTime)
    {
        if (!ProcessEx.TryGet(id, out var process))
        {
            Log.Information($"Did not kill for `{move.Temp}` since processId {id} not found");
            return;
        }

        if (startTime != process.StartTime)
        {
            Log.Information($"Did not kill {id}  for `{move.Temp}` since move.ProcessStartTime ({startTime}) does not equal process.StartTime ({process.StartTime})");
            return;
        }

        try
        {
            process.Kill();
        }
        catch (Exception exception)
        {
            Log.Logger.Error(exception, $"Failed to kill {id}. Command: {move.Exe} {move.Arguments}");
        }
    }

    public void Clear()
    {
        timer.Pause();
        try
        {
            deletes.Clear();
            moves.Clear();
            ToggleActive(true);
        }
        finally
        {
            timer.Resume();
        }
    }

    public void AcceptAll()
    {
        timer.Pause();
        try
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
        finally
        {
            timer.Resume();
        }
    }

    public void AcceptAllDeletes()
    {
        timer.Pause();
        try
        {
            var wasActive = TrackingAny;
            foreach (var delete in deletes.Values)
            {
                File.Delete(delete.File);
            }

            deletes.Clear();
            ToggleActive(wasActive);
        }
        finally
        {
            timer.Resume();
        }
    }

    public void AcceptAllMoves()
    {
        timer.Pause();
        try
        {
            var wasActive = TrackingAny;
            foreach (var move in moves.Values)
            {
                InnerMove(move);
            }

            moves.Clear();
            ToggleActive(wasActive);
        }
        finally
        {
            timer.Resume();
        }
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