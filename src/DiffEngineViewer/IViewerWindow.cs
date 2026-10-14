/// <summary>
/// One platform's renderer, as the kernel's <see cref="IHeadWindow{TScreen, TInput}"/> has it.
/// The viewer asks nothing of a head beyond that.
/// </summary>
interface IViewerWindow : IHeadWindow<Screen, ViewerInput>;
