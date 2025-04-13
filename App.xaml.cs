using System;
using System.Configuration;
using System.Data;
using System.Windows;
using ControlzEx.Theming;
using System.Linq; // For Linq operations like string.Join

namespace LLMChatWindow;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Set the theme (example using MahApps)
        ThemeManager.Current.ChangeTheme(this, "Dark.Steel");

        string initialMessage = string.Empty;
        if (e.Args.Length > 0)
        {
            // Combine all arguments into a single message string
            initialMessage = string.Join(" ", e.Args);
        }

        // Pass the initial message to the MainWindow constructor
        var mainWindow = new MainWindow(initialMessage);
        // The MainWindow is already set as StartupUri in App.xaml,
        // so we might not need to explicitly show it here if using StartupUri.
        // However, passing parameters often requires manual instantiation.
        // Let's remove StartupUri from App.xaml and show manually.
        this.MainWindow = mainWindow;
        mainWindow.Show();
    }
}

