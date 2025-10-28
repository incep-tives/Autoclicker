using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Autoclicker;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    // Windows API imports for mouse simulation
    [DllImport("user32.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall)]
    public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint cButtons, uint dwExtraInfo);
    
    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);
    
    // Windows API imports for global hotkey registration
    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    
    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    
    // Windows API for window theming
    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    
    private static int SetWindowAttribute(IntPtr hwnd, int attribute, int[] value, int size)
    {
        int val = value[0];
        return DwmSetWindowAttribute(hwnd, attribute, ref val, size);
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }
    
    // Mouse event flags
    private const uint MOUSEEVENTF_LEFTDOWN = 0x02;
    private const uint MOUSEEVENTF_LEFTUP = 0x04;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x08;
    private const uint MOUSEEVENTF_RIGHTUP = 0x10;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x20;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x80;
    
    // Hotkey constants
    private const int HOTKEY_ID = 9000;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const int WM_HOTKEY = 0x0312;
    
    // Mouse button enum
    public enum MouseButton
    {
        Left = 0,
        Right = 1,
        Middle = 2
    }
    
    private List<System.Threading.Timer> _clickTimers = new();
    private bool _isRunning = false;
    private bool _isDarkTheme = false;
    private Key? _hotkeyKey = null;
    private ModifierKeys _hotkeyModifiers = ModifierKeys.None;
    private bool _isWaitingForHotkey = false;
    private bool _isGlobalHotkeyRegistered = false;
    private readonly object _clickLock = new object();
    private bool _isDoubleClick = false;
    private MouseButton _selectedMouseButton = MouseButton.Left;
    private Config _config = new Config();
    
    public MainWindow()
    {
        InitializeComponent();
        
        // Load configuration
        LoadConfiguration();
        
        // Validate CPS input as user types
        CpsTextBox.PreviewTextInput += CpsTextBox_PreviewTextInput;
        CpsTextBox.TextChanged += CpsTextBox_TextChanged;
        
        // Save configuration when settings change
        CpsTextBox.LostFocus += (s, e) => SaveConfiguration();
        MouseButtonComboBox.SelectionChanged += (s, e) => SaveConfiguration();
        ClickTypeComboBox.SelectionChanged += (s, e) => SaveConfiguration();
        
        // Set up hotkey handling
        this.KeyDown += MainWindow_KeyDown;
        
        // Set up window message handling for global hotkeys
        this.SourceInitialized += MainWindow_SourceInitialized;
        this.Loaded += MainWindow_Loaded;
    }
    
    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        // Add hook for window messages (needed for global hotkeys)
        var helper = new WindowInteropHelper(this);
        var source = HwndSource.FromHwnd(helper.Handle);
        source?.AddHook(WndProc);
    }
    
    private void LoadConfiguration()
    {
        _config = Config.Load();
        
        // Apply loaded settings to UI
        CpsTextBox.Text = _config.ClicksPerSecond.ToString();
        MouseButtonComboBox.SelectedIndex = _config.MouseButton;
        ClickTypeComboBox.SelectedIndex = _config.ClickType;
        _isDarkTheme = _config.IsDarkTheme;
        
        // Load hotkey if exists
        if (!string.IsNullOrEmpty(_config.HotkeyKey) && Enum.TryParse(_config.HotkeyKey, out Key key))
        {
            _hotkeyKey = key;
            _hotkeyModifiers = (ModifierKeys)_config.HotkeyModifiers;
            
            // Update hotkey display
            string hotkeyText = "";
            if (_hotkeyModifiers.HasFlag(ModifierKeys.Control))
                hotkeyText += "Ctrl+";
            if (_hotkeyModifiers.HasFlag(ModifierKeys.Alt))
                hotkeyText += "Alt+";
            if (_hotkeyModifiers.HasFlag(ModifierKeys.Shift))
                hotkeyText += "Shift+";
            if (_hotkeyModifiers.HasFlag(ModifierKeys.Windows))
                hotkeyText += "Win+";
            hotkeyText += key.ToString();
            
            HotkeyLabel.Content = hotkeyText;
        }
    }
    
    private void SaveConfiguration()
    {
        _config.ClicksPerSecond = int.TryParse(CpsTextBox.Text, out int cps) ? cps : 10;
        _config.MouseButton = MouseButtonComboBox.SelectedIndex;
        _config.ClickType = ClickTypeComboBox.SelectedIndex;
        _config.IsDarkTheme = _isDarkTheme;
        _config.HotkeyKey = _hotkeyKey?.ToString();
        _config.HotkeyModifiers = (int)_hotkeyModifiers;
        
        _config.Save();
    }
    
    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Apply initial theme after window is fully loaded
        ApplyTheme();
        
        // Register hotkey after window is loaded
        if (_hotkeyKey.HasValue)
        {
            RegisterGlobalHotkey();
        }
    }
    
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            // Global hotkey was pressed
            Dispatcher.Invoke(() =>
            {
                if (_isRunning)
                    StopClicking();
                else
                    StartClicking();
            });
            handled = true;
        }
        return IntPtr.Zero;
    }
    
    private void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            StopClicking();
        }
        else
        {
            StartClicking();
        }
    }
    
    private void StartClicking()
    {
        // Validate CPS input
        if (!int.TryParse(CpsTextBox.Text, out int cps) || cps < 1 || cps > 1000)
        {
            MessageBox.Show("Please enter a valid CPS value between 1 and 1000.", "Invalid CPS", 
                          MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        // Capture UI state before starting threads
        _isDoubleClick = ClickTypeComboBox.SelectedIndex == 1;
        _selectedMouseButton = (MouseButton)MouseButtonComboBox.SelectedIndex;
        _isRunning = true;
        
        // Calculate how many threads we need for high CPS
        // Use multiple threads for CPS > 50 to overcome system timer limitations
        // Each thread can handle roughly 50-64 CPS reliably
        int threadCount = cps > 50 ? Math.Max(1, (cps / 50)) : 1;
        int cpsPerThread = cps / threadCount;
        int remainderCps = cps % threadCount;
        
        // Create multiple high-resolution timers
        for (int i = 0; i < threadCount; i++)
        {
            int currentThreadCps = cpsPerThread + (i < remainderCps ? 1 : 0);
            if (currentThreadCps > 0)
            {
                double intervalMs = 1000.0 / currentThreadCps;
                
                var timer = new System.Threading.Timer(
                    PerformClickCallback, 
                    null, 
                    TimeSpan.Zero, 
                    TimeSpan.FromMilliseconds(intervalMs)
                );
                
                _clickTimers.Add(timer);
            }
        }
        
        // Update UI (we're already on the UI thread)
        StartStopButton.Content = "Stop";
        StartStopButton.Background = new SolidColorBrush(Colors.Red);
        StatusLabel.Content = $"Running ({threadCount} threads)";
        StatusLabel.Foreground = new SolidColorBrush(Colors.Green);
        
        // Disable controls while running
        MouseButtonComboBox.IsEnabled = false;
        ClickTypeComboBox.IsEnabled = false;
        CpsTextBox.IsEnabled = false;
    }
    
    private void StopClicking()
    {
        _isRunning = false;
        
        // Stop and dispose all timers
        foreach (var timer in _clickTimers)
        {
            timer?.Dispose();
        }
        _clickTimers.Clear();
        
        // Update UI
        StartStopButton.Content = "Start";
        StartStopButton.Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // #4CAF50
        StatusLabel.Content = "Stopped";
        StatusLabel.Foreground = new SolidColorBrush(Colors.Red);
        
        // Re-enable controls
        MouseButtonComboBox.IsEnabled = true;
        ClickTypeComboBox.IsEnabled = true;
        CpsTextBox.IsEnabled = true;
    }
    
    private void PerformClickCallback(object? state)
    {
        if (!_isRunning) return;
        
        lock (_clickLock)
        {
            if (!_isRunning) return;
            PerformClick();
        }
    }
    
    private void PerformClick()
    {
        // Get current mouse position
        GetCursorPos(out POINT point);
        
        // Determine mouse button events based on selection
        uint downFlag, upFlag;
        uint wheelData = 0;
        
        switch (_selectedMouseButton)
        {
            case MouseButton.Left:
                downFlag = MOUSEEVENTF_LEFTDOWN;
                upFlag = MOUSEEVENTF_LEFTUP;
                break;
            case MouseButton.Right:
                downFlag = MOUSEEVENTF_RIGHTDOWN;
                upFlag = MOUSEEVENTF_RIGHTUP;
                break;
            case MouseButton.Middle:
                downFlag = MOUSEEVENTF_MIDDLEDOWN;
                upFlag = MOUSEEVENTF_MIDDLEUP;
                break;
            default:
                downFlag = MOUSEEVENTF_LEFTDOWN;
                upFlag = MOUSEEVENTF_LEFTUP;
                break;
        }
        
        // Perform single click
        mouse_event(downFlag, (uint)point.X, (uint)point.Y, wheelData, 0);
        mouse_event(upFlag, (uint)point.X, (uint)point.Y, wheelData, 0);
        
        // Double click - perform second click immediately if enabled
        if (_isDoubleClick)
        {
            mouse_event(downFlag, (uint)point.X, (uint)point.Y, wheelData, 0);
            mouse_event(upFlag, (uint)point.X, (uint)point.Y, wheelData, 0);
        }
    }
    
    private void CpsTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // Only allow digits
        e.Handled = !char.IsDigit(e.Text, 0);
    }
    
    private void CpsTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Validate range
        if (int.TryParse(CpsTextBox.Text, out int value))
        {
            if (value > 1000)
            {
                CpsTextBox.Text = "1000";
                CpsTextBox.CaretIndex = CpsTextBox.Text.Length;
            }
            else if (value < 1 && CpsTextBox.Text.Length > 0)
            {
                CpsTextBox.Text = "1";
                CpsTextBox.CaretIndex = CpsTextBox.Text.Length;
            }
        }
    }
    
    protected override void OnClosed(EventArgs e)
    {
        // Stop clicking when window is closed
        StopClicking();
        
        // Unregister global hotkey
        UnregisterGlobalHotkey();
        
        // Save configuration before closing
        SaveConfiguration();
        
        base.OnClosed(e);
    }
    
    // Menu Event Handlers
    private void ToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        _isDarkTheme = !_isDarkTheme;
        ApplyTheme();
        SaveConfiguration();
    }
    
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }
    
    private void SetHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (_isWaitingForHotkey)
            return;
            
        _isWaitingForHotkey = true;
        HotkeyLabel.Content = "Press a key... (ESC to cancel)";
        HotkeyLabel.Foreground = new SolidColorBrush(Colors.Orange);
        
        // Focus the window to capture key events
        this.Focus();
    }
    
    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("An autoclicker", "About AutoClicker", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    
    // Theme Management
    private void ApplyTheme()
    {
        if (_isDarkTheme)
        {
            // Dark theme
            this.Background = new SolidColorBrush(Color.FromRgb(45, 45, 48));
            
            // Apply dark window chrome
            try
            {
                var helper = new WindowInteropHelper(this);
                if (helper.Handle != IntPtr.Zero)
                {
                    SetWindowAttribute(helper.Handle, 20, new[] { 1 }, 4); // DWMWA_USE_IMMERSIVE_DARK_MODE
                }
            }
            catch { /* Ignore if fails on older Windows versions */ }
            
            // Update all labels and text
            foreach (var child in FindVisualChildren<Label>(this))
            {
                if (child.Name == "StatusLabel")
                {
                    child.Foreground = _isRunning ? new SolidColorBrush(Colors.LightGreen) : new SolidColorBrush(Colors.LightCoral);
                }
                else if (child.Name == "HotkeyLabel")
                {
                    child.Foreground = _hotkeyKey.HasValue ? new SolidColorBrush(Colors.LightBlue) : new SolidColorBrush(Colors.Gray);
                }
                else
                {
                    child.Foreground = new SolidColorBrush(Colors.White);
                }
            }
            
            foreach (var child in FindVisualChildren<TextBox>(this))
            {
                child.Background = new SolidColorBrush(Color.FromRgb(60, 60, 60));
                child.Foreground = new SolidColorBrush(Colors.White);
                child.BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100));
            }
            
            foreach (var child in FindVisualChildren<ComboBox>(this))
            {
                child.Background = new SolidColorBrush(Color.FromRgb(60, 60, 60));
                child.Foreground = new SolidColorBrush(Colors.White);
                child.BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100));
            }
            
            // Update menu styles for dark theme
            var darkMenuStyle = new Style(typeof(Menu));
            darkMenuStyle.Setters.Add(new Setter(Menu.BackgroundProperty, new SolidColorBrush(Color.FromRgb(60, 60, 60))));
            darkMenuStyle.Setters.Add(new Setter(Menu.ForegroundProperty, new SolidColorBrush(Colors.White)));
            this.Resources["MenuStyle"] = darkMenuStyle;
            
            var darkMenuItemStyle = new Style(typeof(MenuItem));
            darkMenuItemStyle.Setters.Add(new Setter(MenuItem.BackgroundProperty, new SolidColorBrush(Color.FromRgb(60, 60, 60))));
            darkMenuItemStyle.Setters.Add(new Setter(MenuItem.ForegroundProperty, new SolidColorBrush(Colors.White)));
            var darkTrigger = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
            darkTrigger.Setters.Add(new Setter(MenuItem.BackgroundProperty, new SolidColorBrush(Color.FromRgb(80, 80, 80))));
            darkMenuItemStyle.Triggers.Add(darkTrigger);
            this.Resources["MenuItemStyle"] = darkMenuItemStyle;
        }
        else
        {
            // Light theme (default)
            this.Background = new SolidColorBrush(Colors.White);
            
            // Apply light window chrome
            try
            {
                var helper = new WindowInteropHelper(this);
                if (helper.Handle != IntPtr.Zero)
                {
                    SetWindowAttribute(helper.Handle, 20, new[] { 0 }, 4); // DWMWA_USE_IMMERSIVE_DARK_MODE = false
                }
            }
            catch { /* Ignore if fails on older Windows versions */ }
            
            foreach (var child in FindVisualChildren<Label>(this))
            {
                if (child.Name == "StatusLabel")
                {
                    child.Foreground = _isRunning ? new SolidColorBrush(Colors.Green) : new SolidColorBrush(Colors.Red);
                }
                else if (child.Name == "HotkeyLabel")
                {
                    child.Foreground = _hotkeyKey.HasValue ? new SolidColorBrush(Colors.Blue) : new SolidColorBrush(Colors.Gray);
                }
                else
                {
                    child.Foreground = new SolidColorBrush(Colors.Black);
                }
            }
            
            foreach (var child in FindVisualChildren<TextBox>(this))
            {
                child.Background = new SolidColorBrush(Colors.White);
                child.Foreground = new SolidColorBrush(Colors.Black);
                child.BorderBrush = new SolidColorBrush(Color.FromRgb(171, 173, 179));
            }
            
            foreach (var child in FindVisualChildren<ComboBox>(this))
            {
                child.Background = new SolidColorBrush(Colors.White);
                child.Foreground = new SolidColorBrush(Colors.Black);
                child.BorderBrush = new SolidColorBrush(Color.FromRgb(171, 173, 179));
            }
            
            // Update menu styles for light theme
            var lightMenuStyle = new Style(typeof(Menu));
            lightMenuStyle.Setters.Add(new Setter(Menu.BackgroundProperty, new SolidColorBrush(Colors.White)));
            lightMenuStyle.Setters.Add(new Setter(Menu.ForegroundProperty, new SolidColorBrush(Colors.Black)));
            this.Resources["MenuStyle"] = lightMenuStyle;
            
            var lightMenuItemStyle = new Style(typeof(MenuItem));
            lightMenuItemStyle.Setters.Add(new Setter(MenuItem.BackgroundProperty, new SolidColorBrush(Colors.White)));
            lightMenuItemStyle.Setters.Add(new Setter(MenuItem.ForegroundProperty, new SolidColorBrush(Colors.Black)));
            var lightTrigger = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
            lightTrigger.Setters.Add(new Setter(MenuItem.BackgroundProperty, new SolidColorBrush(Color.FromRgb(225, 225, 225))));
            lightMenuItemStyle.Triggers.Add(lightTrigger);
            this.Resources["MenuItemStyle"] = lightMenuItemStyle;
        }
    }
    
    // Helper method to find visual children
    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
    {
        if (depObj != null)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                if (child != null && child is T)
                {
                    yield return (T)child;
                }
                
                foreach (T childOfChild in FindVisualChildren<T>(child))
                {
                    yield return childOfChild;
                }
            }
        }
    }
    
    // Hotkey Management
    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (!_isWaitingForHotkey)
        {
            // Check if the pressed key matches our hotkey
            if (_hotkeyKey.HasValue && e.Key == _hotkeyKey.Value && 
                Keyboard.Modifiers == _hotkeyModifiers)
            {
                // Toggle autoclicker with hotkey
                if (_isRunning)
                    StopClicking();
                else
                    StartClicking();
                
                e.Handled = true;
                return;
            }
            return;
        }
        
        // We're waiting for hotkey input
        if (e.Key == Key.Escape)
        {
            // Cancel hotkey setting
            UnregisterGlobalHotkey();
            _hotkeyKey = null;
            _hotkeyModifiers = ModifierKeys.None;
            HotkeyLabel.Content = "Not bound";
            HotkeyLabel.Foreground = new SolidColorBrush(Colors.Gray);
            
            // Save configuration
            SaveConfiguration();
        }
        else
        {
            // Set new hotkey
            _hotkeyKey = e.Key;
            _hotkeyModifiers = Keyboard.Modifiers;
            
            string hotkeyText = "";
            if (_hotkeyModifiers.HasFlag(ModifierKeys.Control))
                hotkeyText += "Ctrl+";
            if (_hotkeyModifiers.HasFlag(ModifierKeys.Alt))
                hotkeyText += "Alt+";
            if (_hotkeyModifiers.HasFlag(ModifierKeys.Shift))
                hotkeyText += "Shift+";
            if (_hotkeyModifiers.HasFlag(ModifierKeys.Windows))
                hotkeyText += "Win+";
                
            hotkeyText += e.Key.ToString();
            
            HotkeyLabel.Content = hotkeyText;
            HotkeyLabel.Foreground = new SolidColorBrush(Colors.Blue);
            
            // Register global hotkey
            RegisterGlobalHotkey();
            
            // Save configuration
            SaveConfiguration();
        }
        
        _isWaitingForHotkey = false;
        e.Handled = true;
    }
    
    private void RegisterGlobalHotkey()
    {
        // Unregister existing hotkey first
        UnregisterGlobalHotkey();
        
        if (!_hotkeyKey.HasValue)
            return;
        
        // Convert WPF modifiers to Win32 modifiers
        uint modifiers = 0;
        if (_hotkeyModifiers.HasFlag(ModifierKeys.Control))
            modifiers |= MOD_CONTROL;
        if (_hotkeyModifiers.HasFlag(ModifierKeys.Alt))
            modifiers |= MOD_ALT;
        if (_hotkeyModifiers.HasFlag(ModifierKeys.Shift))
            modifiers |= MOD_SHIFT;
        if (_hotkeyModifiers.HasFlag(ModifierKeys.Windows))
            modifiers |= MOD_WIN;
        
        // Convert WPF key to virtual key code
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(_hotkeyKey.Value);
        
        // Get window handle
        var helper = new WindowInteropHelper(this);
        
        // Register the hotkey
        _isGlobalHotkeyRegistered = RegisterHotKey(helper.Handle, HOTKEY_ID, modifiers, vk);
        
        if (!_isGlobalHotkeyRegistered)
        {
            MessageBox.Show("Failed to register global hotkey. The key combination might already be in use by another application.", 
                          "Hotkey Registration Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    
    private void UnregisterGlobalHotkey()
    {
        if (_isGlobalHotkeyRegistered)
        {
            var helper = new WindowInteropHelper(this);
            UnregisterHotKey(helper.Handle, HOTKEY_ID);
            _isGlobalHotkeyRegistered = false;
        }
    }
}