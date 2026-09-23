enum ViewerMode
{
    /// <summary>
    /// Two files passed on the command line. One window per invocation.
    /// </summary>
    File,

    /// <summary>
    /// Inline snapshot review. Single instance, queued, patches arrive by launch or over the socket.
    /// </summary>
    Inline
}
