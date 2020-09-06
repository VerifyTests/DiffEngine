using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

class Tracker :
    IAsyncDisposable
{
    Action active;
    Action inactive;
    ConcurrentDictionary<string, TrackedMove> moves = new ConcurrentDictionary<string, TrackedMove>(StringComparer.OrdinalIgnoreCase);
    ConcurrentDictionary<string, TrackedDelete> deletes = new ConcurrentDictionary<string, TrackedDelete>(StringComparer.OrdinalIgnoreCase);
    AsyncTimer timer;
    int lastScanCount;

    public Tracker(Action active, Action inactive)
    {
        this.active = active;
        this.inactive = inactive;
        timer = new AsyncTimer(
            ScanFiles,
            TimeSpan.FromSeconds(2),
            exception =>
            {
                ExceptionHandler.Handle("Failed to scan files", exception);
            });
    }

    Task ScanFiles(DateTime dateTime, CancellationToken cancellationToken)
    {
        foreach (var delete in deletes.ToList()
            .Where(delete => !File.Exists(delete.Value.File)))
        {
            deletes.TryRemove(delete.Key, out _);
        }

        var newCount = moves.Count + deletes.Count;
        if (lastScanCount != newCount)
        {
            ToggleActive();
        }

        lastScanCount = newCount;
        return Task.WhenAll(moves.Select(HandleScanMove));
    }

    async Task HandleScanMove(KeyValuePair<string, TrackedMove> pair)
    {
        void RemoveAndKill(KeyValuePair<string, TrackedMove> keyValuePair)
        {
            if (moves.TryRemove(keyValuePair.Key, out var removed))
            {
                KillProcesses(removed);
            }
        }

        var move = pair.Value;
        if (!File.Exists(move.Temp))
        {
            RemoveAndKill(pair);
            return;
        }

        if (!File.Exists(move.Target))
        {
            return;
        }

        if (await FileComparer.FilesAreEqual(move.Temp, move.Target))
        {
            RemoveAndKill(pair);
            return;
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
        get => moves.Any() ||
               deletes.Any();
    }

    public TrackedMove AddMove(
        string temp,
        string target,
        string exe,
        string arguments,
        bool canKill,
        int? processId)
    {
        Process? process = null;
        if (processId != null)
        {
            ProcessEx.TryGet(processId.Value, out process);
        }

        var move = new TrackedMove(temp, target, exe, arguments, canKill, process);

        return moves.AddOrUpdate(
            target,
            addValueFactory: s => move,
            updateValueFactory: (s, existing) =>
            {
                existing.Process?.Dispose();
                return move;
            });
    }

    public TrackedDelete AddDelete(string file)
    {
        return deletes.AddOrUpdate(
            file,
            addValueFactory: s => new TrackedDelete(file),
            updateValueFactory: (s, existing) => existing);
    }

    public void Accept(TrackedDelete delete)
    {
        if (deletes.Remove(delete.File, out var removed))
        {
            File.Delete(removed.File);
        }
    }

    public void Accept(TrackedMove move)
    {
        if (moves.Remove(move.Target, out var removed))
        {
            InnerMove(removed);
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

        if (move.Process == null)
        {
            Log.Information($"No processes to kill for `{move.Temp}`");
            return;
        }

        KillProcess(move, move.Process);
    }

    static void KillProcess(TrackedMove move, Process process)
    {
        try
        {
            process.Kill();
        }
        catch (Exception exception)
        {
            ExceptionHandler.Handle($"Failed to kill {process.Id}. Command: {move.Exe} {move.Arguments}", exception);
        }
        finally
        {
            process.Dispose();
        }
    }

    public void Clear()
    {
        deletes.Clear();
        moves.Clear();
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
    }

    public void AcceptAllDeletes()
    {
        foreach (var delete in deletes.Values)
        {
            File.Delete(delete.File);
        }

        deletes.Clear();
    }

    public void AcceptAllMoves()
    {
        foreach (var move in moves.Values)
        {
            InnerMove(move);
        }

        moves.Clear();
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

static class ProcessEx
{
    public static bool TryGet(int id, [NotNullWhen(true)] out Process? process)
    {
        try
        {
            process = Process.GetProcessById(id);
            return true;
        }
        catch (ArgumentException)
        {
            //If process doesnt exists
            process = null;
            return false;
        }
    }
}