using System;
using System.Threading;
using System.Threading.Tasks;

namespace DiffEngine
{
    public static class DiffEngineTray
    {
        static DiffEngineTray()
        {
            if (Mutex.TryOpenExisting("DiffEngine", out var mutex))
            {
                IsRunning = true;
                mutex.Dispose();
            }
        }

        public static bool IsRunning { get; }

        public static Task AddDelete(string file, CancellationToken cancellation = default)
        {
            if (!IsRunning)
            {
                return Task.CompletedTask;
            }

            return PiperClient.SendDelete(file, cancellation);
        }

        public static Task AddMove(
            string tempFile,
            string targetFile,
            string exe,
            string arguments,
            bool canKill,
            int? processId,
            DateTime? processStartTime,
            CancellationToken cancellation = default)
        {
            if (!IsRunning)
            {
                return Task.CompletedTask;
            }

            return PiperClient.SendMove(tempFile, targetFile, exe, arguments, canKill, processId, processStartTime, cancellation);
        }
    }
}