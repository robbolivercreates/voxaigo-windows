using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using VoxAiGo.Core.Models;
using VoxAiGo.Core.Managers;

namespace VoxAiGo.Core.Services;

public class GeminiTranscriptionService : ITranscriptionService
{
    private readonly string _apiKey;
    private readonly HttpClient _httpClient;
    private readonly HistoryService? _historyService;
    private const string Model = "gemini-2.5-flash";
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    public GeminiTranscriptionService(string apiKey, HistoryService? historyService = null)
    {
        _apiKey = apiKey;
        _historyService = historyService;
        _httpClient = new HttpClient();
    }

    public async Task<string> TranscribeAsync(byte[] audioData, TranscriptionMode mode, SpeechLanguage outputLanguage)
    {
        var stylePrompt = WritingStyleManager.Shared.GetStylePrompt(_historyService?.Records);
        var wakeWord = SettingsManager.Shared.WakeWord;
        var prompt = PromptBuilder.Build(mode, outputLanguage, clarifyText: SettingsManager.Shared.ClarifyText,
            wakeWord: wakeWord, styleSamples: stylePrompt);
        var audioBase64 = Convert.ToBase64String(audioData);

        var requestBody = new GeminiRequest
        {
            SystemInstruction = new ContentPart { Parts = [new Part { Text = prompt }] },
            Contents =
            [
                new Content
                {
                    Parts =
                    [
                        new Part { Text = "Transcreva e processe o áudio a seguir conforme suas instruções:" },
                        new Part { InlineData = new InlineData { MimeType = "audio/wav", Data = audioBase64 } }
                    ]
                }
            ],
            GenerationConfig = new GenerationConfig
            {
                Temperature = mode.GetTemperature(),
                MaxOutputTokens = mode.GetMaxOutputTokens(),
                ThinkingConfig = new ThinkingConfig { ThinkingBudget = 0 }
            }
        };

        var url = $"{BaseUrl}/{Model}:generateContent?key={_apiKey}";
        
        var response = await _httpClient.PostAsJsonAsync(url, requestBody, new JsonSerializerOptions 
        { 
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Gemini API Error: {response.StatusCode} - {errorContent}");
        }

        var jsonResponse = await response.Content.ReadFromJsonAsync<GeminiResponse>();
        var text = jsonResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;

        if (string.IsNullOrEmpty(text))
            throw new Exception("No text returned from Gemini.");

        return CleanMarkdown(text);
    }

    // MARK: - Conversation Reply: Text Translation

    /// <summary>
    /// Detects the source language and translates text into targetLanguage.
    /// Used by the Conversation Reply feature (no audio needed).
    /// </summary>
    public async Task<(string Translation, string FromLanguageName, string FromLanguageCode)> DetectAndTranslateAsync(
        string text, SpeechLanguage targetLanguage)
    {
        var prompt = $$"""
            Translate the following text to {{targetLanguage.FullName}}.
            Detect the source language.
            Respond with valid JSON only — no markdown, no explanation:
            {"translation":"<translated text>","fromLanguageName":"<source language in English>","fromLanguageCode":"<ISO 639-1 code, e.g. ja>"}

            Text:
            {{text}}
            """;

        var requestBody = new GeminiRequest
        {
            Contents =
            [
                new Content { Parts = [new Part { Text = prompt }] }
            ],
            GenerationConfig = new GenerationConfig
            {
                Temperature = 0.1f,
                MaxOutputTokens = 2048,
                ThinkingConfig = new ThinkingConfig { ThinkingBudget = 0 }
            }
        };

        var url = $"{BaseUrl}/{Model}:generateContent?key={_apiKey}";
        var response = await _httpClient.PostAsJsonAsync(url, requestBody, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Translation failed: {response.StatusCode} - {errorContent}");
        }

        var jsonResponse = await response.Content.ReadFromJsonAsync<GeminiResponse>();
        var responseText = jsonResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text?.Trim();

        if (string.IsNullOrEmpty(responseText))
            throw new Exception("No translation returned from Gemini.");

        // Strip markdown fences if added
        if (responseText.StartsWith("```"))
        {
            responseText = responseText.Replace("```json", "").Replace("```", "").Trim();
        }

        using var doc = JsonDocument.Parse(responseText);
        var root = doc.RootElement;
        var translation = root.GetProperty("translation").GetString() ?? "";
        var fromName = root.GetProperty("fromLanguageName").GetString() ?? "";
        var fromCode = root.GetProperty("fromLanguageCode").GetString()?.ToUpperInvariant() ?? "";

        return (translation, fromName, fromCode);
    }

    // MARK: - Conversation Reply: Speech → Target Language

    /// <summary>
    /// Transcribes audio and translates it directly to toLanguage.
    /// Used when the user records their reply in conversation mode.
    /// </summary>
    public async Task<string> TranslateSpeechReplyAsync(byte[] audioData, string toLanguage)
    {
        var systemPrompt = $"""
            You are a professional translator.
            Transcribe the audio and immediately translate it to {toLanguage}.
            Output ONLY the translated text in {toLanguage}. No greeting, no explanation, no original text.
            """;

        var audioBase64 = Convert.ToBase64String(audioData);

        var requestBody = new GeminiRequest
        {
            SystemInstruction = new ContentPart { Parts = [new Part { Text = systemPrompt }] },
            Contents =
            [
                new Content
                {
                    Parts =
                    [
                        new Part { Text = $"Translate this audio to {toLanguage}:" },
                        new Part { InlineData = new InlineData { MimeType = "audio/wav", Data = audioBase64 } }
                    ]
                }
            ],
            GenerationConfig = new GenerationConfig
            {
                Temperature = 0.2f,
                MaxOutputTokens = 2048,
                ThinkingConfig = new ThinkingConfig { ThinkingBudget = 0 }
            }
        };

        var url = $"{BaseUrl}/{Model}:generateContent?key={_apiKey}";
        var response = await _httpClient.PostAsJsonAsync(url, requestBody, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Speech translation failed: {response.StatusCode} - {errorContent}");
        }

        var jsonResponse = await response.Content.ReadFromJsonAsync<GeminiResponse>();
        var text = jsonResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;

        if (string.IsNullOrEmpty(text))
            throw new Exception("No translation returned from Gemini.");

        return text.Trim();
    }

    private string CleanMarkdown(string text)
    {
        var result = text;

        // Remove markdown code blocks
        result = Regex.Replace(result, @"```[a-zA-Z]*\n?", "");
        result = result.Replace("```", "");

        // Remove greetings
        string[] greetings =
        [
            "Olá!", "Olá,", "Oi!", "Claro!", "Claro,",
            "Aqui está:", "Aqui está o código:", "Aqui está o texto:",
            "Segue o código:", "Segue:", "Certo!",
            "Hello!", "Hi!", "Sure!", "Here is:", "Here's the code:"
        ];

        foreach (var greeting in greetings)
        {
            if (result.TrimStart().StartsWith(greeting, StringComparison.OrdinalIgnoreCase))
            {
                var index = result.IndexOf(greeting, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    result = result.Substring(index + greeting.Length);
                }
            }
        }

        return result.Trim();
    }

    // --- Private DTOs for JSON Serialization ---

    private class GeminiRequest
    {
        [JsonPropertyName("system_instruction")]
        public ContentPart? SystemInstruction { get; set; }
        public List<Content> Contents { get; set; } = [];
        public GenerationConfig? GenerationConfig { get; set; }
    }

    private class ContentPart
    {
        public List<Part> Parts { get; set; } = [];
    }

    private class Content
    {
        public List<Part> Parts { get; set; } = [];
    }

    private class Part
    {
        public string? Text { get; set; }
        [JsonPropertyName("inline_data")]
        public InlineData? InlineData { get; set; }
    }

    private class InlineData
    {
        [JsonPropertyName("mime_type")]
        public string MimeType { get; set; } = "";
        public string Data { get; set; } = "";
    }

    private class GenerationConfig
    {
        public float Temperature { get; set; }
        [JsonPropertyName("maxOutputTokens")]
        public int MaxOutputTokens { get; set; }
        [JsonPropertyName("thinkingConfig")]
        public ThinkingConfig? ThinkingConfig { get; set; }
    }

    private class ThinkingConfig
    {
        [JsonPropertyName("thinkingBudget")]
        public int ThinkingBudget { get; set; }
    }

    private class GeminiResponse
    {
        public List<Candidate>? Candidates { get; set; }
    }

    private class Candidate
    {
        public Content? Content { get; set; }
    }
}
