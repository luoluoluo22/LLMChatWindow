using System;
using System.Configuration;
using System.Data;
using System.Windows;
using ControlzEx.Theming;
using System.Linq; // For Linq operations like string.Join
using System.Runtime.InteropServices; // Added for IPC
using System.Text; // Added for Encoding
using System.Threading; // Added for Mutex
using System.Diagnostics; // Added for Debug

namespace LLMChatWindow;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    // Unique Mutex name (using a GUID is recommended)
    private const string MutexName = "##LLMChatWindow_SingleInstance_Mutex##"; // Example name
    private Mutex? _mutex;
    private bool _isFirstInstance;

    // --- P/Invoke for IPC ---
    internal const int WM_COPYDATA = 0x004A;

    [StructLayout(LayoutKind.Sequential)]
    internal struct COPYDATASTRUCT
    {
        public IntPtr dwData; // User-defined data
        public int cbData;    // Size of data in lpData
        public IntPtr lpData; // Pointer to data to be passed
    }

    // FindWindow (to find the existing instance's window)
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    // SendMessage
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, ref COPYDATASTRUCT lParam);

    // Optional: For activating existing window
    //[DllImport("user32.dll")]
    //[return: MarshalAs(UnmanagedType.Bool)]
    //private static extern bool SetForegroundWindow(IntPtr hWnd);
    //[DllImport("user32.dll")]
    //private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    //private const int SW_RESTORE = 9;
    // -------------------------

    protected override void OnStartup(StartupEventArgs e)
    {
        // Try to create the mutex.
        _mutex = new Mutex(true, MutexName, out _isFirstInstance);

        if (!_isFirstInstance)
        {
            // Another instance is already running.
            Debug.WriteLine("Another instance detected. Sending arguments and shutting down.");

            // Combine arguments from the new instance
            string messageToSend = string.Empty;
            if (e.Args.Length > 0)
            {
                messageToSend = string.Join(" ", e.Args);
                Debug.WriteLine($"Arguments to send: {messageToSend}");

                // Find the window of the first instance (using the exact Title from MainWindow.xaml)
                IntPtr hWnd = FindWindow(null, "AI Input Box");

                if (hWnd != IntPtr.Zero)
                {
                    // Prepare data for WM_COPYDATA
                    byte[] dataBytes = Encoding.Unicode.GetBytes(messageToSend); // Use Unicode

                    COPYDATASTRUCT cds = new COPYDATASTRUCT();
                    cds.dwData = IntPtr.Zero; // Identifier (optional)
                    // Size in bytes (Unicode chars are 2 bytes typically on Windows)
                    cds.cbData = dataBytes.Length; // Let receiver handle encoding based on byte length

                    // Allocate memory and copy data
                    cds.lpData = Marshal.AllocHGlobal(cds.cbData);
                    try // Ensure memory is freed even if Marshal fails
                    {
                        Marshal.Copy(dataBytes, 0, cds.lpData, dataBytes.Length); // Copy byte array

                        // Send the message
                        SendMessage(hWnd, WM_COPYDATA, IntPtr.Zero, ref cds);
                        Debug.WriteLine($"Sent WM_COPYDATA to HWND {hWnd}");
                    }
                    finally
                    {
                        // Free allocated memory
                        Marshal.FreeHGlobal(cds.lpData);
                    }
                }
                else
                {
                    Debug.WriteLine("Could not find existing window handle by title 'AI Input Box'.");
                    // Optionally show an error to the user
                }
            }
            else
            {
                Debug.WriteLine("No arguments to send. Activating existing window (if found).");
                // Optionally activate the existing window anyway
                IntPtr hWnd = FindWindow(null, "AI Input Box");
                if(hWnd != IntPtr.Zero)
                {
                    // Implement ShowWindow/SetForegroundWindow if needed
                    // ShowWindow(hWnd, SW_RESTORE);
                    // SetForegroundWindow(hWnd);
                    Debug.WriteLine("Existing window found, attempted to activate (logic not fully implemented).");
                }
                else
                {
                    Debug.WriteLine("Could not find existing window handle to activate.");
                }
            }

            // Shut down the new instance
            this.Shutdown();
            return;
        }

        // --- This is the first instance, proceed with normal startup ---
        Debug.WriteLine("First instance starting up.");
        base.OnStartup(e);

        // Handle unhandled exceptions to release Mutex
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        this.Exit += App_Exit; // Handle normal exit

        // Set the theme
        try
        {
            ThemeManager.Current.ChangeTheme(this, "Dark.Steel");
        }
        catch(Exception themeEx)
        {
            Debug.WriteLine($"Error setting theme: {themeEx.Message}");
        }

        string initialMessage = string.Empty;
        if (e.Args.Length > 0)
        {
            initialMessage = string.Join(" ", e.Args);
        }

        var mainWindow = new MainWindow(initialMessage);
        this.MainWindow = mainWindow;
        mainWindow.Show();
    }

    private void App_Exit(object sender, ExitEventArgs e)
    {
        // Release the mutex on normal exit
        ReleaseMutex();
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        // Release the mutex on unhandled exception
        ReleaseMutex();
        // Optionally log the exception
        Debug.WriteLine($"Unhandled exception: {e.Exception}");
        // Prevent default shutdown (optional, depending on desired behavior)
        // e.Handled = true;
    }

    private void ReleaseMutex()
    {
        if (_isFirstInstance && _mutex != null)
        {
            try
            {
                _mutex.ReleaseMutex();
                _mutex.Dispose();
                _mutex = null;
                Debug.WriteLine("Mutex released.");
            }
            catch (Exception ex)
            {
                // Catch potential ObjectDisposedException if called multiple times
                Debug.WriteLine($"Error releasing mutex: {ex.Message}");
            }
        }
    }
}

