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
            return PiperClient.SendDelete(file, cancellation);
        }

        public static Task AddMove(
            string tempFile,
            string targetFile,
            bool isMdi,
            bool autoRefresh,
            int processId,
            CancellationToken cancellation = default)
        {
            return PiperClient.SendMove(tempFile, targetFile, isMdi, autoRefresh, processId, cancellation);
        }
    }
}