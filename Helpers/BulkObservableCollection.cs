using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace NullWave.Helpers;

public class BulkObservableCollection<T> : ObservableCollection<T>
{
    private bool _suppressNotification = false;

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!_suppressNotification)
        {
            base.OnCollectionChanged(e);
        }
    }

    public void ReplaceAll(IEnumerable<T> items)
    {
        _suppressNotification = true;
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }
        _suppressNotification = false;
        
        // Notify layout controls to reload items
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        
        // Notify binding strings evaluating collection totals
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
    }
}