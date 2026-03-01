using System.Text.Json.Serialization;

namespace VoxAiGo.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TranscriptionMode
{
    Text,
    Chat,
    Code,
    VibeCoder,
    Email,
    Formal,
    Social,
    XTweet,
    Summary,
    Topics,
    Meeting,
    UxDesign,
    Translation,
    Creative,
    Custom
}

public static class TranscriptionModeExtensions
{
    public static string GetApiName(this TranscriptionMode mode)
    {
        return mode switch
        {
            TranscriptionMode.Text => "text",
            TranscriptionMode.Chat => "chat",
            TranscriptionMode.Code => "code",
            TranscriptionMode.VibeCoder => "vibe_coder",
            TranscriptionMode.Email => "email",
            TranscriptionMode.Formal => "formal",
            TranscriptionMode.Social => "social",
            TranscriptionMode.XTweet => "x",
            TranscriptionMode.Summary => "summary",
            TranscriptionMode.Topics => "topics",
            TranscriptionMode.Meeting => "meeting",
            TranscriptionMode.UxDesign => "ux_design",
            TranscriptionMode.Translation => "translation",
            TranscriptionMode.Creative => "creative",
            TranscriptionMode.Custom => "custom",
            _ => "text"
        };
    }

    public static float GetTemperature(this TranscriptionMode mode)
    {
        return mode switch
        {
            TranscriptionMode.Text => 0.3f,
            TranscriptionMode.Chat => 0.4f,
            TranscriptionMode.Code => 0.1f,
            TranscriptionMode.VibeCoder => 0.3f,
            TranscriptionMode.Email => 0.2f,
            TranscriptionMode.Formal => 0.2f,
            TranscriptionMode.Social => 0.5f,
            TranscriptionMode.XTweet => 0.5f,
            TranscriptionMode.Summary => 0.3f,
            TranscriptionMode.Topics => 0.2f,
            TranscriptionMode.Meeting => 0.3f,
            TranscriptionMode.UxDesign => 0.5f,
            TranscriptionMode.Translation => 0.2f,
            TranscriptionMode.Creative => 0.7f,
            TranscriptionMode.Custom => 0.4f,
            _ => 0.3f
        };
    }

    public static int GetMaxOutputTokens(this TranscriptionMode mode)
    {
        return mode switch
        {
            TranscriptionMode.Code => 4096,
            TranscriptionMode.Translation => 4096,
            TranscriptionMode.XTweet => 512,
            TranscriptionMode.VibeCoder => 1024,
            TranscriptionMode.Custom => 2048,
            _ => 2048
        };
    }

    public static string[] GetVoiceAliases(this TranscriptionMode mode)
    {
        return mode switch
        {
            TranscriptionMode.Text => ["texto", "text", "transcrição", "transcricao", "transcription", "normal"],
            TranscriptionMode.Chat => ["chat", "conversa", "conversation", "reply"],
            TranscriptionMode.Code => ["código", "codigo", "code", "programação", "programacao", "programming"],
            TranscriptionMode.VibeCoder => ["vibe coder", "vibe", "vibe coding", "vibecoder"],
            TranscriptionMode.Email => ["email", "e-mail", "emails", "mensagem", "message"],
            TranscriptionMode.Formal => ["formal", "profissional", "professional", "corporativo", "corporate"],
            TranscriptionMode.Social => ["social", "post", "instagram", "redes sociais"],
            TranscriptionMode.XTweet => ["tweet", "x", "twitter"],
            TranscriptionMode.Summary => ["resumo", "summary", "resumir", "summarize", "sintetizar"],
            TranscriptionMode.Topics => ["tópicos", "topicos", "topics", "bullet points", "bullets", "lista", "list"],
            TranscriptionMode.Meeting => ["reunião", "reuniao", "meeting", "ata", "minutes"],
            TranscriptionMode.UxDesign => ["ux", "ux design", "design", "ui", "interface"],
            TranscriptionMode.Translation => [],
            TranscriptionMode.Creative => ["criativo", "creative", "criatividade", "creativity", "storytelling"],
            TranscriptionMode.Custom => ["meu modo", "custom", "personalizado", "personal"],
            _ => []
        };
    }
    
    // Icon mapping (to Segoe Fluent Icons or similar)
    public static string GetIconGlyph(this TranscriptionMode mode)
    {
        // Using approximate Segoe MDL2 Assets / Fluent Icons unicode
        return mode switch
        {
            TranscriptionMode.Text => "\uE8C4", // AlignLeft
            TranscriptionMode.Chat => "\uE8F2", // ChatBubbles
            TranscriptionMode.Code => "\uE943", // Code
            TranscriptionMode.VibeCoder => "\uE735", // Magic/Star (simulated)
            TranscriptionMode.Email => "\uE715", // Mail
            TranscriptionMode.Formal => "\uE821", // Work/Building
            TranscriptionMode.Social => "\uE789", // Megaphone/Share
            TranscriptionMode.XTweet => "\uE91C", // Hash/Tag
            TranscriptionMode.Summary => "\uF000", // TextDocument
            TranscriptionMode.Topics => "\uE8FD", // Bullets
            TranscriptionMode.Meeting => "\uE716", // People
            TranscriptionMode.UxDesign => "\uE790", // ColorPalette/Brush
            TranscriptionMode.Translation => "\uE8F2", // Chat (simulated)
            TranscriptionMode.Creative => "\uE790", // Palette
            TranscriptionMode.Custom => "\uE713", // Settings
            _ => "\uE8C4"
        };
    }
}
