using System;
using System.Threading.Tasks;
using NullWave.Helpers;
using Xunit;

namespace NullWave.Tests;

public class SafeRelayCommandTests
{
    [Fact]
    public async Task Execute_async_command_completes_successfully()
    {
        var tcs = new TaskCompletionSource<bool>();
        var command = new SafeRelayCommand(async () =>
        {
            await Task.Delay(10);
            tcs.SetResult(true);
        });

        command.Execute(null);
        
        // Wait for the async operation to complete (with timeout)
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(1000));
        
        Assert.True(tcs.Task.IsCompleted, "Async command should complete within 1 second");
        Assert.True(await tcs.Task, "Command should set result to true");
    }

    [Fact]
    public async Task Execute_catches_exception_and_does_not_throw()
    {
        var command = new SafeRelayCommand(async () =>
        {
            await Task.Delay(10);
            throw new InvalidOperationException("Test exception");
        });

        // Should not throw
        var ex = Record.Exception(() => command.Execute(null));
        Assert.Null(ex);

        // Wait for async completion
        await Task.Delay(50);
    }

    [Fact]
    public void CanExecute_returns_false_while_executing()
    {
        var tcs = new TaskCompletionSource<bool>();
        var command = new SafeRelayCommand(async () => await tcs.Task);

        Assert.True(command.CanExecute(null));

        command.Execute(null);
        Assert.False(command.CanExecute(null)); // Disabled during execution

        tcs.SetResult(true);
    }

    [Fact]
    public async Task CanExecute_returns_true_after_completion()
    {
        var tcs = new TaskCompletionSource<bool>();
        var command = new SafeRelayCommand(async () => await tcs.Task);

        command.Execute(null);
        Assert.False(command.CanExecute(null));

        tcs.SetResult(true);
        await Task.Delay(50); // Wait for async completion

        Assert.True(command.CanExecute(null)); // Re-enabled after completion
    }

    [Fact]
    public void Sync_command_executes_successfully()
    {
        bool executed = false;
        var command = new SafeRelayCommand(() => executed = true);

        command.Execute(null);

        Assert.True(executed);
    }

    [Fact]
    public void Sync_command_catches_exception()
    {
        var command = new SafeRelayCommand(() => throw new InvalidOperationException("Test"));

        var ex = Record.Exception(() => command.Execute(null));
        Assert.Null(ex); // Should not throw
    }
}

public class ErrorReporterTests
{
    [Fact]
    public void Report_does_not_throw()
    {
        var ex = new InvalidOperationException("Test exception");

        var thrown = Record.Exception(() => ErrorReporter.Report(ex, "Test message", "test"));
        Assert.Null(thrown);
    }

    [Fact]
    public void Report_without_exception_does_not_throw()
    {
        var thrown = Record.Exception(() => ErrorReporter.Report("Test message", "test"));
        Assert.Null(thrown);
    }
}