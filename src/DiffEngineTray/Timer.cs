using System;
using System.Threading;
using System.Threading.Tasks;

class Timer :
    IAsyncDisposable
{
    System.Threading.Timer timer;

    TimeSpan timeSpan = TimeSpan.FromSeconds(2);

    public Timer(Action action)
    {
        timer = new System.Threading.Timer(state => { action(); }, null, timeSpan, timeSpan);
    }

    public ValueTask DisposeAsync()
    {
        return timer.DisposeAsync();
    }

    public void Pause()
    {
        timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Resume()
    {
        timer.Change(timeSpan, timeSpan);
    }
}