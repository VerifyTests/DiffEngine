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

        public static void AddDelete(string file)
        {
            if (!IsRunning)
            {
                return;
            }

            PiperClient.SendDelete(file);
        }

        public static void AddMove(
            string tempFile,
            string targetFile,
            string? exe,
            string? arguments,
            bool canKill,
            int? processId)
        {
            if (!IsRunning)
            {
                return;
            }

            PiperClient.SendMove(tempFile, targetFile, exe, arguments, canKill, processId);
        }

        public static Task AddDeleteAsync(string file, CancellationToken cancellation = default)
        {
            if (!IsRunning)
            {
                return Task.CompletedTask;
            }

            return PiperClient.SendDeleteAsync(file, cancellation);
        }

        public static Task AddMoveAsync(
            string tempFile,
            string targetFile,
            string? exe,
            string? arguments,
            bool canKill,
            int? processId,
            CancellationToken cancellation = default)
        {
            if (!IsRunning)
            {
                return Task.CompletedTask;
            }

            return PiperClient.SendMoveAsync(tempFile, targetFile, exe, arguments, canKill, processId, cancellation);
        }
    }
}