using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace NullWave.Helpers;

public class RelayCommand : ICommand
{
    private readonly Action? _execute;
    private readonly Func<Task>? _executeAsync;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public RelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
    {
        _executeAsync = executeAsync;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter)
    {
        if (_executeAsync != null)
            _ = _executeAsync();
        else
            _execute?.Invoke();
    }

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }
}