using NullWave.ViewModels.Base;

namespace NullWave.ViewModels;

public class PlaceholderPageViewModel : ViewModelBase
{
    public string Icon { get; }
    public string Title { get; }
    public string Message { get; }

    public PlaceholderPageViewModel(string icon, string title, string message)
    {
        Icon = icon;
        Title = title;
        Message = message;
    }
}