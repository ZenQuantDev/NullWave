using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NullWave.Services;

namespace NullWave.ViewModels.Base;

public class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private static readonly List<WeakReference<ViewModelBase>> _live = new();
    private static readonly object _liveLock = new();

    static ViewModelBase()
    {
        LocalizationService.Instance.PropertyChanged += OnLocalizationPropertyChanged;
    }

    private static void OnLocalizationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (null or "Item[]"))
            return;

        var snapshot = new List<ViewModelBase>();

        lock (_liveLock)
        {
            _live.RemoveAll(w =>
            {
                if (!w.TryGetTarget(out ViewModelBase? target))
                    return true;

                _ = target;
                return false;
            });

            foreach (var w in _live)
            {
                if (w.TryGetTarget(out ViewModelBase? vm) && vm is not null)
                    snapshot.Add(vm);
            }
        }

        Serilog.Log.Debug("[Locale] broadcasting to {Count} VMs", snapshot.Count);
        foreach (var vm in snapshot)
        {
            try
            {
                vm.OnPropertyChanged(null);
                vm.OnLocaleChanged();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "[Locale] refresh failed for {Vm}", vm.GetType().Name);
            }
        }
    }

    protected ViewModelBase()
    {
        lock (_liveLock)
            _live.Add(new WeakReference<ViewModelBase>(this));
    }

    protected virtual void OnLocaleChanged()
    {
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}