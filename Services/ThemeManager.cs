using System;
using System.Windows;

namespace IrrModWorkbench.Services
{
    public static class ThemeManager
    {
        public static void ApplyTheme(string themeName)
        {
            var app = Application.Current;
            var dictionaries = app.Resources.MergedDictionaries;
            dictionaries.Clear();

            var uri = themeName.Equals("Base16 Light", StringComparison.Ordinal) ? new Uri("Themes/Base16Light.xaml", UriKind.Relative) : new Uri("Themes/Base16Light.xaml");


            dictionaries.Add(new ResourceDictionary { Source = uri });
        }
    }
}