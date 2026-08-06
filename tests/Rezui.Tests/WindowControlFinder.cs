using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using StyledElement = Avalonia.StyledElement;

namespace Rezui.Tests;

/// <summary>
/// Lookup helpers for named controls inside the layered MainWindow.
///
/// After the shell was split into per-page UserControls, <see cref="StyledElement.FindControl"/>
/// no longer reaches elements hosted inside StartupView / HomePage / DetailsPage /
/// PlayerPage because it walks only the window's own logical tree, not the nested
/// UserControls' name scopes. These extensions walk the full logical tree (which is
/// built before the window is shown) and then the visual tree (which covers anything
/// only materialized after rendering), so callers keep working both before and after
/// <c>Window.Show</c>.
/// </summary>
internal static class WindowControlFinder
{
    /// <summary>
    /// Finds a named control of <typeparamref name="T"/> anywhere in the window's
    /// logical or visual tree, including inside nested page UserControls. Returns
    /// null when absent so callers can assert/branch as they did with FindControl.
    /// </summary>
    public static T? FindNamed<T>(this Window window, string name)
        where T : class
    {
        foreach (var descendant in window.GetLogicalDescendants())
        {
            if (descendant is StyledElement styled &&
                styled.Name == name &&
                styled is T typed)
            {
                return typed;
            }
        }

        foreach (var descendant in window.GetVisualDescendants())
        {
            if (descendant is T typed && descendant.Name == name)
            {
                return typed;
            }
        }

        return null;
    }
}
