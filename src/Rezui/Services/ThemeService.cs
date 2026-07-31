using Avalonia;
using Avalonia.Styling;
using Rezui.Models;

namespace Rezui.Services;

public sealed class ThemeService
{
    public void Apply(ThemePreference preference)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = preference switch
        {
            ThemePreference.Light => ThemeVariant.Light,
            ThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }
}
