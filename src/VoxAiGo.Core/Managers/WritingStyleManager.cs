namespace VoxAiGo.Core.Managers;

public class WritingStyleManager
{
    private static readonly Lazy<WritingStyleManager> _instance = new(() => new WritingStyleManager());
    public static WritingStyleManager Shared => _instance.Value;

    private readonly SettingsManager _settings = SettingsManager.Shared;

    public event Action? StyleChanged;

    // 0 = casual, 50 = neutral, 100 = formal
    public int FormalityLevel
    {
        get => _settings.GetInt("writing_formality", 50);
        set { _settings.Set("writing_formality", value); StyleChanged?.Invoke(); }
    }

    // 0 = concise, 50 = balanced, 100 = detailed
    public int VerbosityLevel
    {
        get => _settings.GetInt("writing_verbosity", 50);
        set { _settings.Set("writing_verbosity", value); StyleChanged?.Invoke(); }
    }

    // 0 = simple, 50 = moderate, 100 = highly technical
    public int TechnicalLevel
    {
        get => _settings.GetInt("writing_technical", 50);
        set { _settings.Set("writing_technical", value); StyleChanged?.Invoke(); }
    }

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
    /// Generates a style instruction string to append to the system prompt.
    /// </summary>
    public string GetStylePrompt()
    {
        if (!IsEnabled) return "";

        var parts = new List<string>();

        if (FormalityLevel < 30)
            parts.Add("Use a casual, conversational tone.");
        else if (FormalityLevel > 70)
            parts.Add("Use a formal, professional tone.");

        if (VerbosityLevel < 30)
            parts.Add("Be very concise and brief.");
        else if (VerbosityLevel > 70)
            parts.Add("Be detailed and thorough.");

        if (TechnicalLevel < 30)
            parts.Add("Use simple, non-technical language.");
        else if (TechnicalLevel > 70)
            parts.Add("Use precise technical terminology.");

        if (!string.IsNullOrWhiteSpace(CustomInstructions))
            parts.Add(CustomInstructions.Trim());

        return parts.Count > 0
            ? "\n\nWRITING STYLE INSTRUCTIONS:\n" + string.Join("\n", parts)
            : "";
    }
}
