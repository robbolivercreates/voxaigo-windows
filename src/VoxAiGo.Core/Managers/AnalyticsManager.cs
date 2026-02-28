using System.Text.Json;

namespace VoxAiGo.Core.Managers;

public class AnalyticsManager
{
    private static readonly Lazy<AnalyticsManager> _instance = new(() => new AnalyticsManager());
    public static AnalyticsManager Shared => _instance.Value;

    private readonly string _analyticsPath;
    private AnalyticsData _data;

    public event Action? StatsChanged;

    // Public accessors
    public int TotalTranscriptions => _data.TotalTranscriptions;
    public int TotalCharacters => _data.TotalCharacters;
    public double TotalAudioSeconds => _data.TotalAudioSeconds;
    public int DailyStreak => _data.DailyStreak;
    public int LongestStreak => _data.LongestStreak;
    public int TodayTranscriptions => GetTodayCount();
    public Dictionary<string, int> ModeUsage => _data.ModeUsage;
    public Dictionary<string, int> LanguageUsage => _data.LanguageUsage;

    private AnalyticsManager()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoxAiGo");
        Directory.CreateDirectory(dir);
        _analyticsPath = Path.Combine(dir, "analytics.json");
        _data = Load();
    }

    public void RecordTranscription(string mode, string languageCode, int charCount, double audioSeconds)
    {
        _data.TotalTranscriptions++;
        _data.TotalCharacters += charCount;
        _data.TotalAudioSeconds += audioSeconds;

        // Mode usage
        if (!_data.ModeUsage.ContainsKey(mode))
            _data.ModeUsage[mode] = 0;
        _data.ModeUsage[mode]++;

        // Language usage
        if (!_data.LanguageUsage.ContainsKey(languageCode))
            _data.LanguageUsage[languageCode] = 0;
        _data.LanguageUsage[languageCode]++;

        // Daily tracking
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        if (!_data.DailyTranscriptions.ContainsKey(today))
            _data.DailyTranscriptions[today] = 0;
        _data.DailyTranscriptions[today]++;

        // Streak calculation
        UpdateStreak();

        Save();
        StatsChanged?.Invoke();
    }

    private int GetTodayCount()
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        return _data.DailyTranscriptions.TryGetValue(today, out var count) ? count : 0;
    }

    private void UpdateStreak()
    {
        var today = DateTime.UtcNow.Date;
        var streak = 0;

        for (var d = today; ; d = d.AddDays(-1))
        {
            var key = d.ToString("yyyy-MM-dd");
            if (_data.DailyTranscriptions.ContainsKey(key) && _data.DailyTranscriptions[key] > 0)
                streak++;
            else
                break;
        }

        _data.DailyStreak = streak;
        if (streak > _data.LongestStreak)
            _data.LongestStreak = streak;
    }

    public (string Mode, int Count)? TopMode()
    {
        if (_data.ModeUsage.Count == 0) return null;
        var top = _data.ModeUsage.OrderByDescending(kv => kv.Value).First();
        return (top.Key, top.Value);
    }

    public (string Language, int Count)? TopLanguage()
    {
        if (_data.LanguageUsage.Count == 0) return null;
        var top = _data.LanguageUsage.OrderByDescending(kv => kv.Value).First();
        return (top.Key, top.Value);
    }

    private AnalyticsData Load()
    {
        try
        {
            if (File.Exists(_analyticsPath))
            {
                var json = File.ReadAllText(_analyticsPath);
                return JsonSerializer.Deserialize<AnalyticsData>(json) ?? new AnalyticsData();
            }
        }
        catch { }
        return new AnalyticsData();
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_analyticsPath, json);
        }
        catch { }
    }
}

public class AnalyticsData
{
    public int TotalTranscriptions { get; set; }
    public int TotalCharacters { get; set; }
    public double TotalAudioSeconds { get; set; }
    public int DailyStreak { get; set; }
    public int LongestStreak { get; set; }
    public Dictionary<string, int> ModeUsage { get; set; } = new();
    public Dictionary<string, int> LanguageUsage { get; set; } = new();
    public Dictionary<string, int> DailyTranscriptions { get; set; } = new();
}
