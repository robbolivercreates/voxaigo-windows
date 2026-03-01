using System.Text;
using VoxAiGo.Core.Services;

namespace VoxAiGo.Core.Managers;

public class WritingStyleManager
{
    private static readonly Lazy<WritingStyleManager> _instance = new(() => new WritingStyleManager());
    public static WritingStyleManager Shared => _instance.Value;

    private readonly SettingsManager _settings = SettingsManager.Shared;

    public event Action? StyleChanged;

    private const int MaxSamples = 3;
    private const int MaxCharsPerSample = 300;

    public string CustomInstructions
    {
        get => _settings.GetString("writing_custom_instructions", "");
        set { _settings.Set("writing_custom_instructions", value); StyleChanged?.Invoke(); }
    }

    public bool IsEnabled
    {
        get => _settings.GetBool(SettingsManager.Keys.WritingStyleEnabled, false);
        set { _settings.Set(SettingsManager.Keys.WritingStyleEnabled, value); StyleChanged?.Invoke(); }
    }

    private WritingStyleManager() { }

    /// <summary>
    /// Generates a style personalization string based on previous transcription samples.
    /// Mirrors macOS WritingStyleManager behavior.
    /// </summary>
    public string GetStylePrompt(IReadOnlyList<TranscriptionRecord>? recentRecords = null)
    {
        if (!IsEnabled) return "";

        var samples = recentRecords?
            .Where(r => !string.IsNullOrWhiteSpace(r.Text) && r.Text.Length >= 20)
            .Take(MaxSamples)
            .ToList() ?? [];

        if (samples.Count == 0 && string.IsNullOrWhiteSpace(CustomInstructions))
            return "";

        var sb = new StringBuilder();
        sb.Append("\n\nWRITING STYLE PERSONALIZATION:");

        if (samples.Count > 0)
        {
            sb.Append("\nMatch the user's writing style based on these previous examples:\n");
            for (int i = 0; i < samples.Count; i++)
            {
                var text = samples[i].Text;
                if (text.Length > MaxCharsPerSample)
                    text = text[..MaxCharsPerSample];
                sb.Append($"\nExample {i + 1}:\n{text}");
            }
            sb.Append("\n\nMimic their vocabulary, sentence structure, tone, and formatting preferences.");
        }

        if (!string.IsNullOrWhiteSpace(CustomInstructions))
        {
            sb.Append($"\n{CustomInstructions.Trim()}");
        }

        return sb.ToString();
    }
}
