using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MahApps.Metro.Controls;

namespace LLMChatWindow
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : MetroWindow
    {
        private AppSettings _settings;

        // Constructor accepts the current settings from MainWindow
        public SettingsWindow(AppSettings currentSettings)
        {
            InitializeComponent();
            _settings = currentSettings; // Store the settings object
            LoadSettingsIntoUI(); // Load values into textboxes

            // Set the owner to the main window if possible, for modality
            if (Application.Current.MainWindow != this)
            {
                this.Owner = Application.Current.MainWindow;
            }
        }

        private void LoadSettingsIntoUI()
        {
            ApiKeyTextBox.Text = _settings.ApiKey;
            BaseUrlTextBox.Text = _settings.BaseUrl;
            ModelNameTextBox.Text = _settings.ModelName;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Update the settings object with values from the UI
            _settings.ApiKey = ApiKeyTextBox.Text.Trim();
            _settings.BaseUrl = BaseUrlTextBox.Text.Trim();
            _settings.ModelName = ModelNameTextBox.Text.Trim();

            this.DialogResult = true; // Indicate that settings were saved
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false; // Indicate that settings were not saved
            this.Close();
        }
    }
} 