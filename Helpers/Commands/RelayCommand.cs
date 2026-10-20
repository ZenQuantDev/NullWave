using System;
using System.Threading.Tasks;

namespace NullWave.Helpers;

/// <summary>Kept for source compatibility. Behaves exactly like SafeRelayCommand
/// (exceptions reported via ErrorReporter, no double-execution while running).</summary>
public class RelayCommand : SafeRelayCommand
{
    public RelayCommand(Action execute, Func<bool>? canExecute = null) : base(execute, canExecute) { }
    public RelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null) : base(executeAsync, canExecute) { }
}

public class RelayCommand<T> : SafeRelayCommand<T>
{
    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null) : base(execute, canExecute) { }
    public RelayCommand(Func<T?, Task> executeAsync, Func<T?, bool>? canExecute = null) : base(executeAsync, canExecute) { }
}