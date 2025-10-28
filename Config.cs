using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace Autoclicker;

public class Config
{
    public int ClicksPerSecond { get; set; } = 10;
    public int MouseButton { get; set; } = 0; // 0=Left, 1=Right, 2=Middle
    public int ClickType { get; set; } = 0; // 0=Single, 1=Double
    public bool IsDarkTheme { get; set; } = false;
    public string? HotkeyKey { get; set; } = null;
    public int HotkeyModifiers { get; set; } = 0; // ModifierKeys as int
    
    private static readonly string ConfigFileName = ".config";
    
    public static string GetConfigPath()
    {
        // Get the directory where the executable is located
        string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(exeDirectory, ConfigFileName);
    }
    
    public static Config Load()
    {
        try
        {
            string configPath = GetConfigPath();
            if (File.Exists(configPath))
            {
                string json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<Config>(json);
                return config ?? new Config();
            }
        }
        catch (Exception ex)
        {
            // If loading fails, create a new config with defaults
            System.Diagnostics.Debug.WriteLine($"Failed to load config: {ex.Message}");
        }
        
        return new Config();
    }
    
    public void Save()
    {
        try
        {
            string configPath = GetConfigPath();
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            string json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(configPath, json);
        }
        catch (Exception ex)
        {
            // Silently fail if saving doesn't work
            System.Diagnostics.Debug.WriteLine($"Failed to save config: {ex.Message}");
        }
    }
}
