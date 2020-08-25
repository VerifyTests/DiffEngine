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
            bool canKill,
            int? processId,
            CancellationToken cancellation = default)
        {
            if (!IsRunning)
            {
                return Task.CompletedTask;
            }

            return PiperClient.SendMove(tempFile, targetFile, canKill, processId, cancellation);
        }
    }
}