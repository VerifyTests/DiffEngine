using System.Collections.Generic;
using System.Linq;
using DiffEngine;
using Serilog;

class Tracker :
    IAsyncDisposable
{
    Action active;
    Action inactive;
    ConcurrentDictionary<string, TrackedMove> moves = new(StringComparer.OrdinalIgnoreCase);
    ConcurrentDictionary<string, TrackedDelete> deletes = new(StringComparer.OrdinalIgnoreCase);
    AsyncTimer timer;
    int lastScanCount;

    public Tracker(Action active, Action inactive)
    {
        this.active = active;
        this.inactive = inactive;
        timer = new(
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
        string? exe,
        string? arguments,
        bool canKill,
        int? processId)
    {
        var exeFile = Path.GetFileName(exe);
        var targetFile = Path.GetFileName(target);
        return moves.AddOrUpdate(
            target,
            addValueFactory: target =>
            {
                Process? process = null;
                if (processId != null)
                {
                    ProcessEx.TryGet(processId.Value, out process);
                }

                var move = BuildTrackedMove(temp, exe, arguments, canKill, target, process);

                if (exeFile == null)
                {
                    Log.Information("MoveAdded. Target:{target}, CanKill:{canKill}, Process:{process}", targetFile, move.CanKill, processId);
                }
                else
                {
                    Log.Information("MoveAdded. Target:{target}, CanKill:{canKill}, Process:{process}, Command:{command}", targetFile, move.CanKill, processId, $"{exeFile} {arguments}");
                }

                return move;
            },
            updateValueFactory: (target, existing) =>
            {
                Process? process;
                if (processId == null)
                {
                    process = existing.Process;
                }
                else
                {
                    existing.Process?.Dispose();
                    ProcessEx.TryGet(processId.Value, out process);
                }

                var move = BuildTrackedMove(temp, exe, arguments, canKill, target, process);

                if (exeFile == null)
                {
                    Log.Information("MoveUpdated. Target:{target}, CanKill:{canKill}, Process:{process}", targetFile, move.CanKill, processId);
                }
                else
                {
                    Log.Information("MoveUpdated. Target:{target}, CanKill:{canKill}, Process:{process}, Command:{command}", targetFile, move.CanKill, processId, $"{exeFile} {arguments}");
                }

                return move;
            });
    }

    static TrackedMove BuildTrackedMove(string temp, string? exe, string? arguments, bool? canKill, string target, Process? process)
    {
        var solution = SolutionDirectoryFinder.Find(target);
        var extension = Path.GetExtension(target).TrimStart('.');
        if (exe == null)
        {
            if(DiffTools.TryFind(extension, out var tool))
            {
                arguments = tool.GetArguments(temp, target);
                exe = tool.ExePath;
                canKill = !tool.IsMdi;
            }
        }
        else if (canKill == null)
        {
            if (DiffTools.TryFindByPath(exe, out var tool))
            {
                canKill = !tool.IsMdi;
            }
            else
            {
                canKill = false;
            }
        }

        return new(temp, target, exe, arguments, canKill.GetValueOrDefault(false), process, solution, extension);
    }

    public TrackedDelete AddDelete(string file)
    {
        return deletes.AddOrUpdate(
            file,
            addValueFactory: key =>
            {
                Log.Information("DeleteAdded. File:{file}", file);
                var solution = SolutionDirectoryFinder.Find(key);
                return new(key, solution);
            },
            updateValueFactory: (_, existing) =>
            {
                Log.Information("DeleteUpdated. File:{file}", file);
                return existing;
            });
    }

    public void Accept(TrackedDelete delete)
    {
        if (deletes.Remove(delete.File, out var removed))
        {
            File.Delete(removed.File);
        }
    }

    public void Accept(IEnumerable<TrackedDelete> toAccept)
    {
        foreach (var delete in toAccept)
        {
            if (deletes.Remove(delete.File, out var removed))
            {
                File.Delete(removed.File);
            }
        }
    }

    public void Accept(IEnumerable<TrackedMove> toAccept)
    {
        foreach (var move in toAccept)
        {
            if (moves.Remove(move.Target, out var removed))
            {
                InnerMove(removed);
            }
        }
    }

    public void Accept(TrackedMove move)
    {
        if (moves.Remove(move.Target, out var removed))
        {
            InnerMove(removed);
        }
    }

    public void Discard(TrackedMove move)
    {
        if (moves.Remove(move.Target, out var removed))
        {
            InnerDiscard(removed);
        }
    }

    static void InnerMove(TrackedMove move)
    {
        KillProcesses(move);

        FileEx.SafeMove(move.Temp, move.Target);
    }

    static void InnerDiscard(TrackedMove move)
    {
        KillProcesses(move);

        FileEx.SafeDelete(move.Temp);
    }

    static void KillProcesses(TrackedMove move)
    {
        if (!move.CanKill)
        {
            Log.Information($"Did not kill for `{move.Name}` since CanKill=false");
            return;
        }

        if (move.Process == null)
        {
            Log.Information($"No processes to kill for `{move.Name}`");
            return;
        }

        move.Process.KillAndDispose();
    }

    public void Clear()
    {
        deletes.Clear();

        foreach (var move in moves.Values)
        {
            KillProcesses(move);
        }

        moves.Clear();
    }

    public void AcceptOpen()
    {
        AcceptAllDeletes();

        foreach (var (key, move) in moves)
        {
            if (move.Process == null)
            {
                continue;
            }

            if (move.Process.HasExited)
            {
                continue;
            }

            InnerMove(move);
            moves.Remove(key, out _);
        }
    }

    public void AcceptAll()
    {
        AcceptAllDeletes();

        AcceptAllMoves();
    }

    void AcceptAllDeletes()
    {
        foreach (var delete in deletes.Values)
        {
            File.Delete(delete.File);
        }

        deletes.Clear();
    }

    void AcceptAllMoves()
    {
        foreach (var move in moves.Values)
        {
            InnerMove(move);
        }

        moves.Clear();
    }

    public ICollection<TrackedDelete> Deletes { get => deletes.Values; }

    public ICollection<TrackedMove> Moves { get => moves.Values; }

    public ValueTask DisposeAsync()
    {
        Clear();
        return timer.DisposeAsync();
    }
}