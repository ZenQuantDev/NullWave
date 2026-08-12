using Avalonia;
using Avalonia.Controls;

namespace NullWave.Helpers.Attached;

/// <summary>
/// Allows binding a boolean (or MultiBinding) to dynamically toggle the 'active' pseudo-class.
/// Usage: attached:PseudoClass.Active="{Binding ...}"
/// </summary>
public static class PseudoClass
{
    public static readonly AttachedProperty<bool> ActiveProperty =
        AvaloniaProperty.RegisterAttached<StyledElement, bool>(
            "Active", 
            typeof(PseudoClass), 
            defaultValue: false);

    static PseudoClass()
    {
        // Listen for changes to the attached property and update the Classes collection
        ActiveProperty.Changed.AddClassHandler<StyledElement>((element, args) =>
        {
            if (args.NewValue is bool isActive)
            {
                element.Classes.Set("active", isActive);
            }
        });
    }

    public static bool GetActive(StyledElement element) => element.GetValue(ActiveProperty);
    public static void SetActive(StyledElement element, bool value) => element.SetValue(ActiveProperty, value);
}
