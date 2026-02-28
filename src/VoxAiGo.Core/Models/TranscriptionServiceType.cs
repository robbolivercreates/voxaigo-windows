namespace VoxAiGo.Core.Models;

/// <summary>
/// Determines which transcription engine to use.
/// Port of macOS TranscriptionServiceType enum from SupabaseModels.swift.
/// </summary>
public enum TranscriptionServiceType
{
    /// Gemini cloud via Supabase edge function (Pro/Trial)
    Supabase,

    /// Local Whisper transcription (Free/Offline mode)
    Whisper,

    /// Direct Gemini API with user's own key (easter egg)
    Byok,

    /// Not authenticated — cannot transcribe
    None
}
