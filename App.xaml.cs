using System;
using System.Configuration;
using System.Data;
using System.Windows;
using MahApps.Metro.Theming;

namespace LLMChatWindow;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Explicitly set the dark theme. 
        // Using "Steel" as the accent, adjust if needed.
        // ThemeManager.Current.ChangeTheme(this, "Dark.Steel"); // Build error persists, commenting out again.

        // Keep the previous attempt commented out for reference
        // ThemeManager.Current.DetectTheme(true); 
    }
}

