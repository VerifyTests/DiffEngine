using System;
using System.Threading.Tasks;
using Xunit;

public class AsyncTimerTests
{
    [Fact]
    public async Task It_calls_error_callback()
    {
        var errorCallbackInvoked = new TaskCompletionSource<bool>();

        var timer = new AsyncTimer(
            callback: (time, token) => throw new Exception("Simulated!"),
            interval: TimeSpan.Zero,
            errorCallback: e => { errorCallbackInvoked.SetResult(true); });

        Assert.True(await errorCallbackInvoked.Task);
    }

    [Fact]
    public async Task It_continues_to_run_after_an_error()
    {
        var callbackInvokedAfterError = new TaskCompletionSource<bool>();

        var fail = true;
        var exceptionThrown = false;
        var timer = new AsyncTimer(
            callback: (time, token) =>
            {
                if (fail)
                {
                    fail = false;
                    throw new Exception("Simulated!");
                }

                Assert.True(exceptionThrown);
                callbackInvokedAfterError.SetResult(true);
                return Task.FromResult(0);
            },
            interval: TimeSpan.Zero,
            errorCallback: e => { exceptionThrown = true; });

        Assert.True(await callbackInvokedAfterError.Task);
    }

    [Fact]
    public async Task Stop_cancels_token_while_waiting()
    {
        var waitCanceled = false;
        var delayStarted = new TaskCompletionSource<bool>();
        var timer = new AsyncTimer(
            callback: (time, token) => throw new Exception("Simulated!"),
            interval: TimeSpan.FromDays(7),
            delayStrategy: async (delayTime, token) =>
            {
                delayStarted.SetResult(true);
                try
                {
                    await Task.Delay(delayTime, token);
                }
                catch (OperationCanceledException)
                {
                    waitCanceled = true;
                }
            });

        await delayStarted.Task;
        await timer.DisposeAsync();

        Assert.True(waitCanceled);
    }

    [Fact]
    public async Task Stop_cancels_token_while_in_callback()
    {
        var callbackCanceled = false;
        var callbackStarted = new TaskCompletionSource<bool>();
        var stopInitiated = new TaskCompletionSource<bool>();
        var timer = new AsyncTimer(
            callback: async (time, token) =>
            {
                callbackStarted.SetResult(true);
                await stopInitiated.Task;
                if (token.IsCancellationRequested)
                {
                    callbackCanceled = true;
                }
            },
            interval: TimeSpan.Zero);

        await callbackStarted.Task;
        var stopTask = timer.DisposeAsync();
        stopInitiated.SetResult(true);
        await stopTask;
        Assert.True(callbackCanceled);
    }

    [Fact]
    public async Task Stop_waits_for_callback_to_complete()
    {
        var callbackCompleted = new TaskCompletionSource<bool>();
        var callbackTaskStarted = new TaskCompletionSource<bool>();
        var timer = new AsyncTimer(
            callback: (time, token) =>
            {
                callbackTaskStarted.SetResult(true);
                return callbackCompleted.Task;
            },
            interval: TimeSpan.Zero);

        await callbackTaskStarted.Task;

        var stopTask = timer.DisposeAsync().AsTask();
        var delayTask = Task.Delay(1000);

        var firstToComplete = await Task.WhenAny(stopTask, delayTask);
        Assert.Equal(delayTask, firstToComplete);
        callbackCompleted.SetResult(true);

        await stopTask;
    }
}