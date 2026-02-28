namespace VoxAiGo.Core.Models;

public record SpeechLanguage(string Code, string DisplayName, string FullName, string Flag, string[] VoiceAliases, bool IsFreeTier = false)
{
    public static SpeechLanguage Portuguese => new("pt", "Português", "Português (Brasil)", "🇧🇷", ["português", "portugues", "portuguese", "brasil"], true);
    public static SpeechLanguage English => new("en", "English", "English (US)", "🇺🇸", ["inglês", "ingles", "english", "americano"], true);
    
    // Additional languages
    public static SpeechLanguage Spanish => new("es", "Español", "Español", "🇪🇸", ["espanhol", "spanish", "castelhano"]);
    public static SpeechLanguage French => new("fr", "Français", "Français", "🇫🇷", ["francês", "frances", "french"]);
    public static SpeechLanguage German => new("de", "Deutsch", "Deutsch", "🇩🇪", ["alemão", "alemao", "german"]);
    public static SpeechLanguage Italian => new("it", "Italiano", "Italiano", "🇮🇹", ["italiano", "italian"]);
    public static SpeechLanguage Japanese => new("ja", "日本語", "Japanese", "🇯🇵", ["japonês", "japones", "japanese", "nihongo"]);
    public static SpeechLanguage Chinese => new("zh", "中文", "Chinese (Mandarin)", "🇨🇳", ["chinês", "chines", "chinese", "mandarin"]);
    public static SpeechLanguage Russian => new("ru", "Русский", "Russian", "🇷🇺", ["russo", "russian"]);
    
    public static List<SpeechLanguage> All =>
    [
        Portuguese, English, Spanish, French, German, Italian, Japanese, Chinese, Russian,
        // Add remaining 21 languages as needed per spec
        new("nl", "Nederlands", "Dutch", "🇳🇱", ["holandês", "dutch"]),
        new("ko", "한국어", "Korean", "🇰🇷", ["coreano", "korean"]),
        new("ar", "العربية", "Arabic", "🇸🇦", ["árabe", "arabe", "arabic"]),
        new("hi", "हिन्दी", "Hindi", "🇮🇳", ["hindi", "indiano"]),
        new("tr", "Türkçe", "Turkish", "🇹🇷", ["turco", "turkish"]),
        new("pl", "Polski", "Polish", "🇵🇱", ["polonês", "polish"]),
        new("sv", "Svenska", "Swedish", "🇸🇪", ["sueco", "swedish"]),
        new("no", "Norsk", "Norwegian", "🇳🇴", ["norueguês", "norwegian"]),
        new("da", "Dansk", "Danish", "🇩🇰", ["dinamarquês", "danish"]),
        new("fi", "Suomi", "Finnish", "🇫🇮", ["finlandês", "finnish"]),
        new("cs", "Čeština", "Czech", "🇨🇿", ["checo", "czech"]),
        new("el", "Ελληνικά", "Greek", "🇬🇷", ["grego", "greek"]),
        new("he", "עברית", "Hebrew", "🇮🇱", ["hebraico", "hebrew"]),
        new("th", "ไทย", "Thai", "🇹🇭", ["tailandês", "thai"]),
        new("vi", "Tiếng Việt", "Vietnamese", "🇻🇳", ["vietnamita", "vietnamese"]),
        new("id", "Bahasa Indonesia", "Indonesian", "🇮🇩", ["indonésio", "indonesian"]),
        new("ms", "Bahasa Melayu", "Malay", "🇲🇾", ["malaio", "malay"]),
        new("uk", "Українська", "Ukrainian", "🇺🇦", ["ucraniano", "ukrainian"]),
        new("ro", "Română", "Romanian", "🇷🇴", ["romeno", "romanian"]),
        new("hu", "Magyar", "Hungarian", "🇭🇺", ["húngaro", "hungarian"]),
        new("ca", "Català", "Catalan", "🇪🇸", ["catalão", "catalan"])
    ];
}
