using System;
using System.Threading.Tasks;
using Xunit;

public class AsyncTimerTests
{
    [Fact]
    public async Task It_calls_error_callback()
    {
        TaskCompletionSource<bool> errorCallbackInvoked = new();

        AsyncTimer timer = new(
            callback: (_, _) => throw new("Simulated!"),
            interval: TimeSpan.Zero,
            errorCallback: _ => { errorCallbackInvoked.SetResult(true); });

        Assert.True(await errorCallbackInvoked.Task);
    }

    [Fact]
    public async Task It_continues_to_run_after_an_error()
    {
        TaskCompletionSource<bool> callbackInvokedAfterError = new();

        var fail = true;
        var exceptionThrown = false;
        AsyncTimer timer = new(
            callback: (_, _) =>
            {
                if (fail)
                {
                    fail = false;
                    throw new("Simulated!");
                }

                Assert.True(exceptionThrown);
                callbackInvokedAfterError.SetResult(true);
                return Task.FromResult(0);
            },
            interval: TimeSpan.Zero,
            errorCallback: _ => { exceptionThrown = true; });

        Assert.True(await callbackInvokedAfterError.Task);
    }

    [Fact]
    public async Task Stop_cancels_token_while_waiting()
    {
        var waitCanceled = false;
        TaskCompletionSource<bool> delayStarted = new();
        AsyncTimer timer = new(
            callback: (_, _) => throw new("Simulated!"),
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
        TaskCompletionSource<bool> callbackStarted = new();
        TaskCompletionSource<bool> stopInitiated = new();
        AsyncTimer timer = new(
            callback: async (_, token) =>
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
        TaskCompletionSource<bool> callbackCompleted = new();
        TaskCompletionSource<bool> callbackTaskStarted = new();
        AsyncTimer timer = new(
            callback: (_, _) =>
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