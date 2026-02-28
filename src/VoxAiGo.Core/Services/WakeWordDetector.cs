using VoxAiGo.Core.Models;

namespace VoxAiGo.Core.Services;

public enum WakeWordResultType { None, Mode, Language, NextLanguage, PreviousLanguage }

public record WakeWordResult(WakeWordResultType Type, TranscriptionMode? Mode = null, SpeechLanguage? Language = null);

/// <summary>
/// Detects wake word commands in transcribed text.
/// Port of macOS detectWakeWordCommand() from VibeFlowViewModel.swift.
/// </summary>
public static class WakeWordDetector
{
    // Common filler words to strip before matching
    private static readonly string[] Fillers = [
        "please", "por favor", "agora", "now", "mudar para", "switch to",
        "trocar para", "change to", "usar", "use", "modo", "mode",
        "idioma", "language", "em", "in", "para", "to", "o", "a"
    ];

    /// <summary>
    /// Checks if the transcribed text starts with the wake word and contains a command.
    /// Returns null if no wake word command detected.
    /// </summary>
    public static WakeWordResult? Detect(string text, string wakeWord = "Hey Vox")
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var lower = text.Trim().ToLowerInvariant();
        var wakeWordLower = wakeWord.ToLowerInvariant();

        // Check all wake word variants
        var variants = new List<string> { wakeWordLower };
        if (wakeWordLower == "hey vox")
            variants.AddRange(["ei vox", "hey fox", "hey box", "a vox", "hey vocs"]);

        string? matchedVariant = null;
        foreach (var variant in variants)
        {
            if (lower.StartsWith(variant))
            {
                matchedVariant = variant;
                break;
            }
        }

        if (matchedVariant == null) return null;

        // Extract command after wake word
        var command = lower[matchedVariant.Length..].Trim().TrimStart(',', '.', '!', ' ');
        if (string.IsNullOrWhiteSpace(command)) return null;

        // Clean fillers from command
        command = CleanFillers(command);
        if (string.IsNullOrWhiteSpace(command)) return null;

        // Check special navigation commands first
        if (IsNextLanguageCommand(command))
            return new WakeWordResult(WakeWordResultType.NextLanguage);

        if (IsPreviousLanguageCommand(command))
            return new WakeWordResult(WakeWordResultType.PreviousLanguage);

        // Check mode matches (priority: mode > language)
        foreach (var mode in Enum.GetValues<TranscriptionMode>())
        {
            var aliases = mode.GetVoiceAliases();
            if (aliases.Any(alias => command.Contains(alias.ToLowerInvariant())))
                return new WakeWordResult(WakeWordResultType.Mode, Mode: mode);
        }

        // Check language matches
        foreach (var lang in SpeechLanguage.All)
        {
            if (lang.VoiceAliases.Any(alias => command.Contains(alias.ToLowerInvariant())))
                return new WakeWordResult(WakeWordResultType.Language, Language: lang);
        }

        return null;
    }

    private static string CleanFillers(string command)
    {
        var words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        // Remove filler words from the beginning
        while (words.Count > 0 && Fillers.Contains(words[0]))
            words.RemoveAt(0);

        return string.Join(' ', words);
    }

    private static bool IsNextLanguageCommand(string command) =>
        command.Contains("próximo idioma") || command.Contains("proximo idioma") ||
        command.Contains("next language") || command.Contains("próxima língua") ||
        command.Contains("proxima lingua");

    private static bool IsPreviousLanguageCommand(string command) =>
        command.Contains("idioma anterior") || command.Contains("previous language") ||
        command.Contains("língua anterior") || command.Contains("lingua anterior");
}
