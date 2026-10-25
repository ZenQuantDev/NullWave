using System;

namespace NullWave.Models;

public class PlaylistFolder : NotifyBase
{
    private bool _isExpanded = true;
    private string _name = string.Empty;

    public Guid Id { get; set; } = Guid.NewGuid();
    
    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }
    
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
    
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }
}