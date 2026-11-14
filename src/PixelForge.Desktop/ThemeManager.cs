using System.Windows;

namespace PixelForge.Desktop;

// Presentation resources only; switching does not recreate the window or asset state.
public static class ThemeManager
{
    public static string Current { get; private set; } = "Dark";
    public static void Apply(string? theme)
    {
        Current = theme == "Light" ? "Light" : "Dark";
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var next = new ResourceDictionary { Source = new Uri($"pack://application:,,,/PixelForge.Desktop;component/Themes/{Current}.xaml", UriKind.Absolute) };
        if (dictionaries.Count == 0) dictionaries.Add(next); else dictionaries[0] = next;
    }
}
