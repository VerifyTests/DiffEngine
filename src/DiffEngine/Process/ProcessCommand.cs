using System;
using System.Diagnostics;

namespace DiffEngine
{
    [DebuggerDisplay("{Command} | Process = {Process}")]
    public readonly struct ProcessCommand
    {
        /// <summary>
        /// The command line used to launch the process.
        /// </summary>
        public string Command { get; }

        /// <summary>
        /// The process Id.
        /// </summary>
        public int Process { get; }

        public ProcessCommand(string command, in int process)
        {
            Guard.AgainstNullOrEmpty(command, nameof(command));
            Command = command;
            Process = process;
        }
    }
}