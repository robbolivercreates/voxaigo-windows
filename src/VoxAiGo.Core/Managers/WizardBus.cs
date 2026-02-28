namespace VoxAiGo.Core.Managers;

/// <summary>
/// Static event bus that decouples the transcription pipeline from the Setup Wizard window.
/// MainViewModel fires events here; SetupWizardWindow subscribes to them.
/// </summary>
public static class WizardBus
{
    /// <summary>Fired after a successful transcription is pasted.</summary>
    public static event Action<string>? TranscriptionCompleted;

    /// <summary>Fired when the user selects a different mode (UI or wake word).</summary>
    public static event Action<string>? ModeChanged;

    /// <summary>Fired when the user selects a different language (UI or wake word).</summary>
    public static event Action<string>? LanguageChanged;

    /// <summary>Fired when a wake word command is successfully detected and applied.</summary>
    public static event Action? WakeWordFired;

    public static void FireTranscription(string text) => TranscriptionCompleted?.Invoke(text);
    public static void FireModeChanged(string modeName) => ModeChanged?.Invoke(modeName);
    public static void FireLanguageChanged(string languageCode) => LanguageChanged?.Invoke(languageCode);
    public static void FireWakeWord() => WakeWordFired?.Invoke();
}
