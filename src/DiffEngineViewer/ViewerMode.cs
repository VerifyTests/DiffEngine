enum ViewerMode
{
    /// <summary>
    /// Two files passed on the command line. One window per invocation.
    /// </summary>
    File,

    /// <summary>
    /// Inline snapshot review. Single instance, queued, patches arrive on stdin or the socket.
    /// </summary>
    Inline
}
