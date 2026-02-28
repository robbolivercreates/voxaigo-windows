using System.Text.Json;
using VoxAiGo.Core.Managers;
using VoxAiGo.Core.Models;

namespace VoxAiGo.Core.Services;

public class HistoryService
{
    private readonly string HistoryFile = Path.Combine(
        SettingsManager.Shared.AppDataDir, "history.json");
    private List<TranscriptionRecord> _records = [];

    public IReadOnlyList<TranscriptionRecord> Records => _records;

    public event Action? HistoryChanged;

    public async Task InitializeAsync()
    {
        if (File.Exists(HistoryFile))
        {
            try
            {
                var json = await File.ReadAllTextAsync(HistoryFile);
                _records = JsonSerializer.Deserialize<List<TranscriptionRecord>>(json) ?? [];
            }
            catch 
            {
                _records = [];
            }
        }
    }

    public async Task AddRecordAsync(string text, TranscriptionMode mode, SpeechLanguage language)
    {
        var record = new TranscriptionRecord(
            Guid.NewGuid(),
            DateTime.Now,
            text,
            mode,
            language.Code
        );

        _records.Insert(0, record); // Add to top
        if (_records.Count > 100) _records.RemoveAt(_records.Count - 1); // Keep last 100

        await SaveAsync();
        HistoryChanged?.Invoke();
    }

    private async Task SaveAsync()
    {
        var json = JsonSerializer.Serialize(_records);
        await File.WriteAllTextAsync(HistoryFile, json);
    }

    public async Task ClearHistoryAsync()
    {
        _records.Clear();
        await SaveAsync();
        HistoryChanged?.Invoke();
    }
}

public record TranscriptionRecord(Guid Id, DateTime Timestamp, string Text, TranscriptionMode Mode, string LanguageCode);
