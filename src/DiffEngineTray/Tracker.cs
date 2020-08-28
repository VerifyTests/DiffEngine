using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
        int? processId,
        DateTime? processStartTime)
    {
        var updated = false;
        var wasActive = TrackingAny;
        var update = moves.AddOrUpdate(
            target,
            addValueFactory: s =>
            {
                updated = true;
                return new TrackedMove(temp, target, exe, arguments, canKill, processId, processStartTime);
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

    public static void Launch(TrackedMove move)
    {
        var startInfo = new ProcessStartInfo(move.Exe, move.Arguments)
        {
            UseShellExecute = true
        };

        using var process = Process.Start(startInfo);
        if (process != null)
        {
            //TODO: should probably add to a list of process ids
            move.ProcessId = process.Id;
            move.ProcessStartTime = process.StartTime;
            return;
        }
        var message = $@"Failed to launch diff tool.
{move.Exe} {move.Arguments}";
        Log.Error(message);
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
        if (!move.CanKill)
        {
            Log.Information($"Did not kill for `{move.Temp}` since CanKill=false");
            return;
        }

        if (move.ProcessId == null)
        {
            Log.Information($"Did not kill for `{move.Temp}` since ProcessId=null");
            return;
        }

        if (move.ProcessStartTime == null)
        {
            Log.Information($"Did not kill for `{move.Temp}` since ProcessStartTime=null");
            return;
        }

        var processId = move.ProcessId.Value;
        if (!ProcessEx.TryGet(processId, out var process))
        {
            Log.Information($"Did not kill for `{move.Temp}` since processId {processId} not found");
            return;
        }

        if (move.ProcessStartTime.Value != process.StartTime)
        {
            Log.Information($"Did not kill {processId}  for `{move.Temp}` since move.ProcessStartTime ({move.ProcessStartTime.Value}) does not equal process.StartTime ({process.StartTime})");
            return;
        }

        try
        {
            process.Kill();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    public void Clear()
    {
        deletes.Clear();
        moves.Clear();
        ToggleActive(true);
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

    public void AcceptAllDeletes()
    {
        var wasActive = TrackingAny;
        foreach (var delete in deletes.Values)
        {
            File.Delete(delete.File);
        }

        deletes.Clear();
        ToggleActive(wasActive);
    }

    public void AcceptAllMoves()
    {
        var wasActive = TrackingAny;
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