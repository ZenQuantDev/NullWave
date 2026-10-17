using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace NullWave.Helpers;

/// <summary>
/// Safe wrapper around RelayCommand that catches exceptions from async operations
/// and routes them through ErrorReporter. Also tracks execution state to prevent
/// double-clicks and disable buttons during async work.
/// 
/// Use this INSTEAD of RelayCommand when the command body is async:
///   BAD:  new RelayCommand(async () => await DoWork())     // swallows exceptions
///   GOOD: new SafeRelayCommand(async () => await DoWork()) // catches and reports
/// </summary>
public class SafeRelayCommand : ICommand
{
    private readonly Action? _execute;
    private readonly Func<Task>? _executeAsync;
    private readonly Func<bool>? _canExecute;
    private readonly string? _errorScope;
    private bool _isExecuting;

    public SafeRelayCommand(Action execute, Func<bool>? canExecute = null, string? errorScope = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _errorScope = errorScope;
    }

    public SafeRelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null, string? errorScope = null)
    {
        _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        _canExecute = canExecute;
        _errorScope = errorScope;
    }

    public bool CanExecute(object? parameter) 
        => !_isExecuting && (_canExecute?.Invoke() ?? true);

    // async void is acceptable ONLY for event handlers and ICommand.Execute
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;

        try
        {
            _isExecuting = true;
            RaiseCanExecuteChanged();

            if (_executeAsync != null)
                await _executeAsync();
            else
                _execute?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorReporter.Report(ex, "An unexpected error occurred.", _errorScope);
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged()
        => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Generic version of SafeRelayCommand for commands that take a parameter.
/// </summary>
public class SafeRelayCommand<T> : ICommand
{
    private readonly Action<T?>? _execute;
    private readonly Func<T?, Task>? _executeAsync;
    private readonly Func<T?, bool>? _canExecute;
    private readonly string? _errorScope;
    private bool _isExecuting;

    public SafeRelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null, string? errorScope = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _errorScope = errorScope;
    }

    public SafeRelayCommand(Func<T?, Task> executeAsync, Func<T?, bool>? canExecute = null, string? errorScope = null)
    {
        _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        _canExecute = canExecute;
        _errorScope = errorScope;
    }

    public bool CanExecute(object? parameter)
        => !_isExecuting && (_canExecute?.Invoke((T?)parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;

        T? typedParam = default;
        try
        {
            typedParam = (T?)parameter;
        }
        catch (InvalidCastException)
        {
            // Parameter type mismatch, skip execution
            return;
        }

        try
        {
            _isExecuting = true;
            RaiseCanExecuteChanged();

            if (_executeAsync != null)
                await _executeAsync(typedParam);
            else
                _execute?.Invoke(typedParam);
        }
        catch (Exception ex)
        {
            ErrorReporter.Report(ex, "An unexpected error occurred.", _errorScope);
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged()
        => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}