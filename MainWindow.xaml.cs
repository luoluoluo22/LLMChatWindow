﻿using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel; // Added for ObservableCollection
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using MahApps.Metro.Controls;
using System.IO; // For StreamReader
using System.Diagnostics;
using System.Windows.Threading; // Required for DispatcherPriority
using System.ComponentModel; // Added for INotifyPropertyChanged
using System.Runtime.CompilerServices; // Added for CallerMemberName
using Hardcodet.Wpf.TaskbarNotification; // Add this namespace
using Microsoft.Win32; // For Registry access
using System.Runtime.InteropServices; // Needed for DllImport
using System.Windows.Interop; // Needed for HwndSource

namespace LLMChatWindow;

// --- Application Settings Class ---
public class AppSettings
{
    public string ApiKey { get; set; } = ""; // Default to empty
    public string BaseUrl { get; set; } = "https://api.siliconflow.cn/v1"; // Keep a default
    public string ModelName { get; set; } = "Qwen/Qwen2.5-7B-Instruct"; // Keep a default
}

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : MetroWindow
{
    // --- ChatMessage Class implements INotifyPropertyChanged ---
    public class ChatMessage : INotifyPropertyChanged
    {
        private string _role = "";
        private string _content = "";

        public string Role
        {
            get => _role;
            set => SetProperty(ref _role, value);
        }

        public string Content
        {
            get => _content;
            set => SetProperty(ref _content, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        // Parameterless constructor for deserialization
        public ChatMessage() { }

        public ChatMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    // ----------------------------------------------------------

    // --- File Path Constants (Using AppData) ---
    private static readonly string AppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static readonly string AppDirectory = System.IO.Path.Combine(AppDataPath, "LLMChatWindow");
    private static readonly string SettingsFilePath = System.IO.Path.Combine(AppDirectory, "settings.json");
    private static readonly string HistoryFilePath = System.IO.Path.Combine(AppDirectory, "chathistory.json");
    // -------------------------------------------

    // --- Settings Handling ---
    private AppSettings _currentSettings = new AppSettings();

    // Make ModelName an instance property for binding/access
    public string ModelName => _currentSettings.ModelName;

    private readonly HttpClient _httpClient;
    private readonly List<string> _watermarkSuggestions;
    private readonly Random _random = new Random();
    // Use ObservableCollection for automatic UI updates when items are added/removed
    private ObservableCollection<ChatMessage> _chatHistory = new ObservableCollection<ChatMessage>();
    private bool _isExplicitClose = false;

    // --- Global Hotkey Definitions ---
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int HOTKEY_ID = 9000; // Unique ID for our hotkey

    // Modifiers:
    private const uint MOD_NONE = 0x0000;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CTRL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    // Virtual Key Codes (Example: VK_SPACE for Spacebar)
    private const uint VK_SPACE = 0x20;
    // --------------------------------

    private HwndSource? _source;

    public MainWindow()
    {
        EnsureAppDirectoryExists(); // Ensure directory exists before loading/saving
        LoadSettings(); // Load settings first
        InitializeComponent();
        this.MouseLeftButtonDown += Window_MouseLeftButtonDown;
        this.Loaded += MainWindow_Loaded; // Register Loaded event handler

        // --- Set Icon based on Theme ---
        BitmapImage iconSource = GetCurrentThemeIconSource();
        this.Icon = iconSource; // Set window icon
        if (MyNotifyIcon != null)
        {
            MyNotifyIcon.IconSource = iconSource; // Set tray icon
        }
        // -------------------------------

        // --- Initialize HttpClient with loaded settings ---
        _httpClient = new HttpClient();
        UpdateHttpClientAuthHeader(); // Set auth header initially
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        // ----------------------------------------------------

        // Initialize watermark suggestions
        _watermarkSuggestions = new List<string>
        {
            "写一首关于夏夜的诗",
            "将 'Good morning' 翻译成西班牙语",
            "解释什么是黑洞",
            "给我推荐一部科幻电影",
            "今天有什么新闻？",
            "如何学习 C#？"
        };

        LoadChatHistory(); // Load history after components are initialized
        // Bind the ObservableCollection directly to the ItemsControl
        ChatHistoryItemsControl.ItemsSource = _chatHistory;
        // Removed call to DisplayHistory() as binding handles it.
        UpdateWatermark(); // Set initial watermark

        // 让输入框在启动时获得焦点
        InputTextBox.Focus();

        // Scroll to end after loading history (ensure layout is updated first)
        Dispatcher.InvokeAsync(() => ResponseScrollViewer.ScrollToEnd(), DispatcherPriority.Loaded);
    }

    private void EnsureAppDirectoryExists()
    {
        try
        {
            if (!Directory.Exists(AppDirectory))
            {
                Directory.CreateDirectory(AppDirectory);
                Debug.WriteLine($"Created application data directory: {AppDirectory}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error creating application data directory: {ex.Message}");
            // Consider notifying the user or disabling save functionality
        }
    }

    private void UpdateHttpClientAuthHeader()
    {
        _httpClient.DefaultRequestHeaders.Authorization = string.IsNullOrEmpty(_currentSettings.ApiKey)
            ? null // No auth if key is empty
            : new AuthenticationHeaderValue("Bearer", _currentSettings.ApiKey);
    }

    private void LoadSettings()
    {
        EnsureAppDirectoryExists(); // Ensure directory exists before attempting to load
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                string json = File.ReadAllText(SettingsFilePath);
                _currentSettings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                Debug.WriteLine("Settings loaded successfully.");
            }
            else
            {
                _currentSettings = new AppSettings(); // Use defaults if file doesn't exist
                Debug.WriteLine($"{SettingsFilePath} not found, using default settings.");
                // Optionally save default settings immediately
                // SaveSettings();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading settings: {ex.Message}. Using default settings.");
            _currentSettings = new AppSettings(); // Use defaults on error
            // Consider showing an error message to the user
        }
    }

    private void SaveSettings()
    {
        EnsureAppDirectoryExists(); // Ensure directory exists before attempting to save
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(_currentSettings, options);
            File.WriteAllText(SettingsFilePath, json);
            Debug.WriteLine($"Settings saved successfully to {SettingsFilePath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error saving settings to {SettingsFilePath}: {ex.Message}");
            // Consider showing an error message to the user
        }
    }

    private void LoadChatHistory()
    {
        EnsureAppDirectoryExists(); // Ensure directory exists
        try
        {
            if (File.Exists(HistoryFilePath))
            {
                string json = File.ReadAllText(HistoryFilePath);
                var loadedMessages = JsonSerializer.Deserialize<List<ChatMessage>>(json);
                // Use the constructor that takes an IEnumerable to populate the ObservableCollection
                _chatHistory = loadedMessages != null
                                ? new ObservableCollection<ChatMessage>(loadedMessages)
                                : new ObservableCollection<ChatMessage>();
                Debug.WriteLine($"Loaded {_chatHistory.Count} messages from {HistoryFilePath}");
            }
            else
            {
                _chatHistory = new ObservableCollection<ChatMessage>(); // Initialize if file doesn't exist
                Debug.WriteLine($"{HistoryFilePath} not found, starting new history.");
            }
        }
        catch (JsonException jsonEx) // Catch specific JSON errors
        {
             Debug.WriteLine($"Error deserializing chat history from {HistoryFilePath}: {jsonEx.Message}. Starting fresh.");
             _chatHistory = new ObservableCollection<ChatMessage>();
        }
        catch (Exception ex) // Catch other potential errors (IO, etc.)
        {
            Debug.WriteLine($"Error loading chat history from {HistoryFilePath}: {ex.Message}");
            _chatHistory = new ObservableCollection<ChatMessage>(); // Start fresh on error
        }
    }

    private async Task SaveChatHistoryAsync()
    {
        EnsureAppDirectoryExists(); // Ensure directory exists
        try
        {
            List<ChatMessage> historyToSave = new List<ChatMessage>(_chatHistory);
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(historyToSave, options);
            await File.WriteAllTextAsync(HistoryFilePath, json);
            Debug.WriteLine($"Saved {historyToSave.Count} messages to {HistoryFilePath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error saving chat history to {HistoryFilePath}: {ex.Message}");
            // Optionally notify user or handle error
        }
    }

    // Removed DisplayHistory method as it's replaced by data binding

    // Method to update the watermark with a random suggestion
    private void UpdateWatermark()
    {
        if (_watermarkSuggestions != null && _watermarkSuggestions.Count > 0)
        {
            int index = _random.Next(_watermarkSuggestions.Count);
            string suggestion = _watermarkSuggestions[index];
            // Use MahApps helper to set Watermark
            TextBoxHelper.SetWatermark(InputTextBox, suggestion);
        }
    }

    // 输入框文本变化事件处理 - Can be removed if not used
    private void InputTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // No longer need to filter ListBox
        // We could add logic here if needed, e.g., hide/show response area based on input
    }

    // 输入框按键事件处理 - Make async void to use await
    private async void InputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            string inputText = InputTextBox.Text.Trim();
            if (string.IsNullOrEmpty(inputText)) return;

            ChatMessage userMessage = new ChatMessage("user", inputText);
            // Initialize placeholder with empty content, it will be filled incrementally
            ChatMessage assistantMessagePlaceholder = new ChatMessage("assistant", "");

            // Add messages and save on UI thread
            await Dispatcher.InvokeAsync(async () =>
            {
                _chatHistory.Add(userMessage);
                ResponseScrollViewer.ScrollToEnd();
                InputTextBox.IsEnabled = false; // Disable input while waiting
                InputTextBox.Text = "";
                _chatHistory.Add(assistantMessagePlaceholder); // Add empty placeholder
                ResponseScrollViewer.ScrollToEnd();
            });

            var historyCopyForAI = _chatHistory.Take(_chatHistory.Count - 1).ToList();

            // --- Background AI Task --- Updates UI via Dispatcher
            _ = Task.Run(async () =>
            {
                string? errorMessage = null;

                try
                {
                    // Action to append delta to placeholder content on UI thread
                    Action<string> appendDeltaAction = async (delta) =>
                    {
                         if (!string.IsNullOrEmpty(delta))
                         {
                             await Dispatcher.InvokeAsync(() =>
                             {
                                 assistantMessagePlaceholder.Content += delta; // Append content
                                 // Auto-scrolling during streaming can be disruptive, scroll at end?
                                 // Or scroll only if the scrollbar is already near the bottom.
                                 if (ResponseScrollViewer.VerticalOffset + ResponseScrollViewer.ViewportHeight >= ResponseScrollViewer.ExtentHeight - 30) // Check if near bottom
                                 {
                                      ResponseScrollViewer.ScrollToEnd();
                                 }
                             });
                         }
                    };

                    await GetStreamingAiResponseAsync(historyCopyForAI, appendDeltaAction);

                    // ADD SaveChatHistoryAsync() here, after response is complete
                    if (!string.IsNullOrEmpty(assistantMessagePlaceholder.Content))
                    {
                        await SaveChatHistoryAsync();
                        Debug.WriteLine("Chat history saved after AI response completion.");
                    }
                    else if (string.IsNullOrEmpty(assistantMessagePlaceholder.Content))
                    {
                         // Handle cases where AI returned nothing or failed silently
                         // Maybe remove the empty placeholder?
                         await Dispatcher.InvokeAsync(() => _chatHistory.Remove(assistantMessagePlaceholder));
                         Debug.WriteLine("Removed empty assistant placeholder as no response received.");
                         // Optionally save again if you removed the placeholder
                         // await SaveChatHistoryAsync();
                    }

                }
                catch (HttpRequestException httpEx)
                {
                    errorMessage = $"Network error: {httpEx.Message}";
                }
                catch (JsonException jsonEx)
                {
                    errorMessage = $"Error parsing response: {jsonEx.Message}";
                }
                catch (Exception ex)
                {
                    errorMessage = $"An error occurred: {ex.Message}";
                }
                finally
                {
                    // --- Final UI Update/Cleanup on UI Thread ---
                    await Dispatcher.InvokeAsync(() =>
                    {
                        // If error occurred OR no response content was received, show error/message
                        if (errorMessage != null)
                        {
                             // If placeholder is still empty, set error. Otherwise, append error.
                             if (string.IsNullOrEmpty(assistantMessagePlaceholder.Content))
                                assistantMessagePlaceholder.Content = $"Error: {errorMessage}";
                             else
                                assistantMessagePlaceholder.Content += $"\nError: {errorMessage}";
                        }
                        else if (string.IsNullOrEmpty(assistantMessagePlaceholder.Content))
                        {
                             // Handle case where stream finished but no content/error was processed
                             assistantMessagePlaceholder.Content = "[No response or empty response received]";
                        }

                        ResponseScrollViewer.ScrollToEnd(); // Final scroll to end
                        InputTextBox.IsEnabled = true;
                        InputTextBox.Focus();
                        UpdateWatermark();
                    });
                }
            });
        }
    }


    private async Task GetStreamingAiResponseAsync(List<ChatMessage> messages, Action<string> onDeltaReceived)
    {
        if (string.IsNullOrEmpty(_currentSettings.ApiKey))
        {
            onDeltaReceived("[Error: API Key is not configured in settings.]");
            return;
        }
        if (string.IsNullOrEmpty(_currentSettings.BaseUrl))
        {
            onDeltaReceived("[Error: Base URL is not configured in settings.]");
            return;
        }

        var requestData = new
        {
            model = _currentSettings.ModelName,
            messages = messages.Select(m => new { m.Role, m.Content }).ToList(),
            stream = true
        };

        string jsonPayload = JsonSerializer.Serialize(requestData);
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, $"{_currentSettings.BaseUrl}/chat/completions")
        {
            Content = content
        };

        UpdateHttpClientAuthHeader();

        try
        {
            using (HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
            {
                if (!response.IsSuccessStatusCode)
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    string errorMessage = $"[Error: API request failed with status code {response.StatusCode}. Details: {errorContent}]";
                    Debug.WriteLine(errorMessage);
                    onDeltaReceived(errorMessage);
                    return;
                }

                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var reader = new StreamReader(stream))
                {
                    string? line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        Debug.WriteLine($"Raw stream line: {line}");

                        if (line.StartsWith("data: "))
                        {
                            string jsonData = line.Substring(6).Trim();
                            if (jsonData.Equals("[DONE]", StringComparison.OrdinalIgnoreCase))
                            {
                                break;
                            }

                            try
                            {
                                using (JsonDocument jsonDoc = JsonDocument.Parse(jsonData))
                                {
                                    if (jsonDoc.RootElement.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                                    {
                                        if (choices[0].TryGetProperty("delta", out var deltaElement))
                                        {
                                            if (deltaElement.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String)
                                            {
                                                string? delta = contentElement.GetString();
                                                if (!string.IsNullOrEmpty(delta))
                                                {
                                                    onDeltaReceived(delta);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            catch (JsonException jsonEx)
                            {
                                string parseErrorMessage = $"[Error parsing JSON delta: {jsonEx.Message}]";
                                Debug.WriteLine($"{parseErrorMessage} | Raw line: {line}");
                                onDeltaReceived(parseErrorMessage);
                            }
                        }
                    }
                }
            }
            Debug.WriteLine("Finished receiving streaming response normally.");
        }
        catch (Exception ex)
        {
            string exceptionMessage = $"[Error: An exception occurred during the API call: {ex.Message}]";
            Debug.WriteLine(exceptionMessage);
            onDeltaReceived(exceptionMessage);
        }
    }


    // 窗口拖动
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            this.DragMove();
        }
    }

    // --- Tray Icon Event Handlers ---
    private void MyNotifyIcon_TrayLeftMouseDown(object sender, RoutedEventArgs e)
    {
        // Show/Hide on left click
        ToggleWindowVisibility();
    }

    private void ShowHideMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ToggleWindowVisibility();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _isExplicitClose = true; // Set flag to allow closing
        this.Close(); // Trigger OnClosing
    }

    private void ToggleWindowVisibility()
    {
        if (this.IsVisible)
        {
            this.Hide();
        }
        else
        {
            this.Show();
            this.Activate(); // Bring to front
            this.WindowState = WindowState.Normal; // Ensure it's not minimized
            InputTextBox.Focus(); // Focus input box when shown
        }
    }

    // --- Override Window Closing Behavior & Hotkey Unregistration ---
    protected override void OnClosing(CancelEventArgs e)
    {
        // Unregister hotkey before closing
        _source?.RemoveHook(HwndHook);
        UnregisterHotKey(new WindowInteropHelper(this).Handle, HOTKEY_ID);

        if (!_isExplicitClose) // If not closing via Exit menu
        {
            e.Cancel = true; // Cancel the close operation
            this.Hide(); // Hide the window instead
        }
        else
        {
            MyNotifyIcon?.Dispose(); // Dispose TaskbarIcon if necessary before closing
        }
        base.OnClosing(e);
    }

    // --- Hotkey Registration on Load ---
    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(HwndHook);

        // Register Alt + Space as the hotkey
        if (!RegisterHotKey(handle, HOTKEY_ID, MOD_ALT, VK_SPACE))
        {
            // Handle error, maybe hotkey already registered
             MessageBox.Show("Could not register hotkey. It might be in use by another application.", "Hotkey Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // --- Hotkey Message Handling ---
    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        switch (msg)
        {
            case WM_HOTKEY:
                switch (wParam.ToInt32())
                {
                    case HOTKEY_ID:
                        ToggleWindowVisibility(); // Call our existing toggle function
                        handled = true;
                        break;
                }
                break;
        }
        return IntPtr.Zero;
    }

    // --- Helper to get Icon based on Windows Theme ---
    private BitmapImage GetCurrentThemeIconSource()
    {
        string iconName = "app_icon.ico"; // Default to icon for light theme
        try
        {
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                object? registryValue = key?.GetValue("AppsUseLightTheme");
                if (registryValue is int appsUseLightTheme && appsUseLightTheme == 0)
                {
                    // Dark theme is active, use the light icon
                    iconName = "app_icon_light.ico";
                }
            }
        }
        catch (Exception ex)
        {
            // Log error or handle cases where registry access fails
            Debug.WriteLine($"Error reading theme setting from registry: {ex.Message}. Using default icon.");
            // Fallback to default (app_icon.ico)
        }

        // Create BitmapImage using Pack URI syntax for resources
        Uri iconUri = new Uri($"pack://application:,,,/{iconName}", UriKind.RelativeOrAbsolute);
        return new BitmapImage(iconUri);
    }

    // --- Clear Chat History Button Click Handler ---
    private async void ClearChatButton_Click(object sender, RoutedEventArgs e)
    {
        // Consider adding a confirmation dialog here for better UX
        // Example: if (MessageBox.Show("Are you sure you want to clear the chat history?", "Confirm Clear", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        // {
             _chatHistory.Clear();
             await SaveChatHistoryAsync(); // Save the cleared history
             Debug.WriteLine("Chat history cleared.");
        // }
    }

    // --- Settings Button Click Handler ---
    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        // Create a deep copy if you want cancel to truly discard changes
        // For simplicity now, we pass the live object
        var settingsWindow = new SettingsWindow(_currentSettings);

        // Show the window modally and check the result
        bool? result = settingsWindow.ShowDialog();

        if (result == true)
        {
            // Settings were saved in SettingsWindow, now save them to file
            SaveSettings();

            // Update things that depend on settings, e.g., HttpClient header
            UpdateHttpClientAuthHeader();

            // Optional: Notify UI elements if they need to update (e.g., ModelName display)
            // This requires implementing INotifyPropertyChanged on MainWindow or finding another update mechanism.
            // For now, the RolePrefix binding uses RelativeSource, it *might* update automatically,
            // but explicit notification is more robust.
            Debug.WriteLine("Settings saved and applied.");
        }
        else
        {
            // Settings were cancelled, reload from file to discard changes made in the settings object
            // (Only necessary if we passed the live object and want true cancel)
            LoadSettings();
             Debug.WriteLine("Settings changes cancelled.");
        }
    }
}

// Helper class for JSON deserialization (if needed, but inline anonymous type is used currently)
// public class StreamChoice
// {
//     public StreamDelta Delta { get; set; }
//     public int Index { get; set; }
//     public string FinishReason { get; set; }
// }

// public class StreamDelta
// {
//     public string Role { get; set; }
//     public string Content { get; set; }
// }

// public class StreamResponse
// {
//     public string Id { get; set; }
//     public string Object { get; set; }
//     public long Created { get; set; }
//     public string Model { get; set; }
//     public List<StreamChoice> Choices { get; set; }
// }
// }