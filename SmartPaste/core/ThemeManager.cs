using System;
using System.Windows;

namespace SmartPaste
{
    /// <summary>
    /// Loads and swaps the active theme ResourceDictionary at runtime.
    /// Themes live in /Theming/Theme.&lt;Brand&gt;.&lt;Mode&gt;.xaml and expose the same brush keys,
    /// so switching a theme re-colors the whole app through DynamicResource lookups.
    /// </summary>
    public static class ThemeManager
    {
        public static string CurrentBrand { get; private set; } = "Aubergine";
        public static string CurrentMode { get; private set; } = "Light";

        /// <summary>Raised after the active theme dictionary has been swapped.</summary>
        public static event EventHandler? ThemeChanged;

        private const string ThemeMarker = "/Theming/Theme.";

        /// <summary>Applies the theme for the given brand ("Aubergine"/"Prune") and mode ("Light"/"Dark").</summary>
        public static void Apply(string brand, string mode)
        {
            brand = NormalizeBrand(brand);
            mode = NormalizeMode(mode);

            var uri = new Uri($"pack://application:,,,/Theming/Theme.{brand}.{mode}.xaml", UriKind.Absolute);
            var themeDict = new ResourceDictionary { Source = uri };

            var merged = Application.Current.Resources.MergedDictionaries;
            for (int i = merged.Count - 1; i >= 0; i--)
            {
                string? src = merged[i].Source?.OriginalString;
                if (src != null && src.Contains(ThemeMarker, StringComparison.OrdinalIgnoreCase))
                    merged.RemoveAt(i);
            }
            merged.Insert(0, themeDict);

            CurrentBrand = brand;
            CurrentMode = mode;
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        public static void ToggleMode() => Apply(CurrentBrand, CurrentMode == "Dark" ? "Light" : "Dark");

        public static void SetBrand(string brand) => Apply(brand, CurrentMode);

        /// <summary>Pack URI of the active theme's emblem (mark-aubergine / mark-prune).</summary>
        public static Uri EmblemUri =>
            new Uri($"pack://application:,,,/assets/mark-{CurrentBrand.ToLowerInvariant()}.png", UriKind.Absolute);

        private static string NormalizeBrand(string v) =>
            string.Equals(v, "Prune", StringComparison.OrdinalIgnoreCase) ? "Prune" : "Aubergine";

        private static string NormalizeMode(string v) =>
            string.Equals(v, "Dark", StringComparison.OrdinalIgnoreCase) ? "Dark" : "Light";
    }
}
