using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VoxAiGo.Core.Managers;

public class SettingsManager
{
    private static readonly Lazy<SettingsManager> _instance = new(() => new SettingsManager());
    public static SettingsManager Shared => _instance.Value;

    private readonly string _settingsDir;
    private readonly string _settingsPath;
    private Dictionary<string, JsonElement> _settings = new();

    // --- Supabase Config (shared with macOS) ---
    public const string SupabaseUrl = "https://bvdbpyjudmkkspcxevlp.supabase.co";
    public const string SupabaseAnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImJ2ZGJweWp1ZG1ra3NwY3hldmxwIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzEzNDA2MzMsImV4cCI6MjA4NjkxNjYzM30.hRaoAXKTesJarVvg8cBky2Umtb1R7R824gJwgEle77w";
    public const string GoogleOAuthClientId = "728500023005-atpmf2bjhibklarulbpc4qgugao3s4l0.apps.googleusercontent.com";

    // --- Keys ---
    public static class Keys
    {
        public const string GeminiApiKey = "gemini_api_key";
        public const string SelectedMode = "selected_mode";
        public const string SelectedLanguage = "selected_language";
        public const string SelectedMicrophone = "selected_microphone";
        public const string UseLocalWhisper = "use_local_whisper";
        public const string ForceOfflineMode = "force_offline_mode";
        public const string PlaySounds = "play_sounds";
        public const string LaunchAtStartup = "launch_at_startup";
        public const string ShowInTray = "show_in_tray";
        public const string WakeWordEnabled = "wake_word_enabled";
        public const string WakeWord = "wake_word";
        public const string WritingStyleEnabled = "writing_style_enabled";
        public const string AppLanguage = "app_language";
        public const string FavoriteLanguages = "favorite_languages";
        public const string HasCompletedSetup = "has_completed_setup";
        public const string JwtToken = "jwt_token";
        public const string RefreshToken = "refresh_token";

        // SaaS-specific keys
        public const string ClarifyText = "clarify_text";
        public const string ByokEnabled = "byok_enabled";
        public const string ByokApiKey = "byok_api_key";
        public const string OfflineMode = "offline_mode";
    }

    private SettingsManager()
    {
        _settingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoxAiGo");
        _settingsPath = Path.Combine(_settingsDir, "settings.json");
        Directory.CreateDirectory(_settingsDir);
        Load();
    }

    public string AppDataDir => _settingsDir;

    // --- Typed getters ---
    public string GetString(string key, string defaultValue = "")
    {
        if (_settings.TryGetValue(key, out var val) && val.ValueKind == JsonValueKind.String)
            return val.GetString() ?? defaultValue;
        return defaultValue;
    }

    public bool GetBool(string key, bool defaultValue = false)
    {
        if (_settings.TryGetValue(key, out var val))
        {
            if (val.ValueKind == JsonValueKind.True) return true;
            if (val.ValueKind == JsonValueKind.False) return false;
        }
        return defaultValue;
    }

    public int GetInt(string key, int defaultValue = 0)
    {
        if (_settings.TryGetValue(key, out var val) && val.ValueKind == JsonValueKind.Number)
            return val.GetInt32();
        return defaultValue;
    }

    public void Set(string key, object value)
    {
        var json = JsonSerializer.SerializeToElement(value);
        _settings[key] = json;
        Save();
    }

    // --- DPAPI encrypted storage (for API keys, JWT) ---
    public void SetEncrypted(string key, string value)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            var base64 = Convert.ToBase64String(encrypted);
            Set(key, base64);
        }
        catch
        {
            // Fallback: store unencrypted
            Set(key, value);
        }
    }

    public string GetEncrypted(string key, string defaultValue = "")
    {
        var stored = GetString(key, "");
        if (string.IsNullOrEmpty(stored)) return defaultValue;
        try
        {
            var encrypted = Convert.FromBase64String(stored);
            var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            // Maybe stored unencrypted
            return stored;
        }
    }

    // --- Persistence ---
    private void Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                _settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                    ?? new Dictionary<string, JsonElement>();
            }
        }
        catch
        {
            _settings = new Dictionary<string, JsonElement>();
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
        catch { /* Disk error */ }
    }

    // --- Convenience properties ---
    public string GeminiApiKey
    {
        get => GetEncrypted(Keys.GeminiApiKey);
        set => SetEncrypted(Keys.GeminiApiKey, value);
    }

    public string SelectedMode
    {
        get => GetString(Keys.SelectedMode, "text");
        set => Set(Keys.SelectedMode, value);
    }

    public string SelectedLanguage
    {
        get => GetString(Keys.SelectedLanguage, "en");
        set => Set(Keys.SelectedLanguage, value);
    }

    public bool PlaySounds
    {
        get => GetBool(Keys.PlaySounds, true);
        set => Set(Keys.PlaySounds, value);
    }

    public bool UseLocalWhisper
    {
        get => GetBool(Keys.UseLocalWhisper, false);
        set => Set(Keys.UseLocalWhisper, value);
    }

    public bool HasCompletedSetup
    {
        get => GetBool(Keys.HasCompletedSetup, false);
        set => Set(Keys.HasCompletedSetup, value);
    }

    public bool WakeWordEnabled
    {
        get => GetBool(Keys.WakeWordEnabled, false);
        set => Set(Keys.WakeWordEnabled, value);
    }

    public string WakeWord
    {
        get => GetString(Keys.WakeWord, "Vox");
        set => Set(Keys.WakeWord, value);
    }

    // --- Clarify Text (default true, same as macOS) ---
    public bool ClarifyText
    {
        get => GetBool(Keys.ClarifyText, true);
        set => Set(Keys.ClarifyText, value);
    }

    // --- BYOK (Bring Your Own Key) - Easter egg ---
    public bool ByokEnabled
    {
        get => GetBool(Keys.ByokEnabled, false);
        set => Set(Keys.ByokEnabled, value);
    }

    public string ByokApiKey
    {
        get => GetEncrypted(Keys.ByokApiKey);
        set => SetEncrypted(Keys.ByokApiKey, value);
    }

    /// True if user has BYOK enabled AND has entered a key
    public bool HasByokKey => ByokEnabled && !string.IsNullOrEmpty(ByokApiKey);

    // --- Offline Mode (Pro users can force Whisper local) ---
    public bool OfflineMode
    {
        get => GetBool(Keys.OfflineMode, false);
        set => Set(Keys.OfflineMode, value);
    }
}
