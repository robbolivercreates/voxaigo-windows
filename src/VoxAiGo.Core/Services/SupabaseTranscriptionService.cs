using System.Net.Http;
using System.Text;
using System.Text.Json;
using VoxAiGo.Core.Managers;
using VoxAiGo.Core.Models;

namespace VoxAiGo.Core.Services;

/// <summary>
/// Cloud transcription via Supabase Edge Function proxy.
/// Used for Pro subscribers who don't have their own API key.
/// The edge function handles Gemini API calls server-side.
/// </summary>
public class SupabaseTranscriptionService : ITranscriptionService
{
    private readonly AuthService _authService;
    private readonly HttpClient _httpClient;

    public SupabaseTranscriptionService(AuthService authService)
    {
        _authService = authService;
        _httpClient = new HttpClient();
    }

    public async Task<string> TranscribeAsync(byte[] audioData, TranscriptionMode mode, SpeechLanguage outputLanguage)
    {
        if (!_authService.IsLoggedIn || string.IsNullOrEmpty(_authService.AccessToken))
            throw new Exception("Not authenticated. Please sign in.");

        var audioBase64 = Convert.ToBase64String(audioData);
        var stylePrompt = WritingStyleManager.Shared.GetStylePrompt();
        var wakeWord = SettingsManager.Shared.WakeWord;
        var prompt = PromptBuilder.Build(mode, outputLanguage, clarifyText: SettingsManager.Shared.ClarifyText,
            wakeWord: wakeWord, styleSamples: stylePrompt);

        var payload = new
        {
            audio = audioBase64,
            mode = mode.GetApiName(),
            language = outputLanguage.Code,
            systemPrompt = prompt,
            temperature = mode.GetTemperature(),
            maxOutputTokens = mode.GetMaxOutputTokens()
        };

        var jsonBody = JsonSerializer.Serialize(payload);
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"{SettingsManager.SupabaseUrl}/functions/v1/transcribe");
        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        request.Headers.Add("apikey", SettingsManager.SupabaseAnonKey);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", _authService.AccessToken);

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Cloud transcription failed: {response.StatusCode} - {errorContent}");
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(responseJson);

        if (doc.RootElement.TryGetProperty("text", out var textEl))
            return textEl.GetString() ?? "";
        if (doc.RootElement.TryGetProperty("error", out var errorEl))
            throw new Exception($"Cloud error: {errorEl.GetString()}");

        return responseJson;
    }

    // MARK: - Conversation Reply: Speech → Target Language

    /// <summary>
    /// Transcribes audio and translates it directly to toLanguage via Supabase proxy.
    /// Used when the user records their reply in conversation mode (Pro users).
    /// </summary>
    public async Task<string> TranslateSpeechReplyAsync(byte[] audioData, string toLanguage)
    {
        if (!_authService.IsLoggedIn || string.IsNullOrEmpty(_authService.AccessToken))
            throw new Exception("Not authenticated. Please sign in.");

        var audioBase64 = Convert.ToBase64String(audioData);
        var systemPrompt = $"""
            You are a professional translator.
            Transcribe the audio and immediately translate it to {toLanguage}.
            Output ONLY the translated text in {toLanguage}. No greeting, no explanation, no original text.
            """;

        var payload = new
        {
            audio = audioBase64,
            mode = "translation",
            language = "en",
            prompt = systemPrompt,
            temperature = 0.2f,
            maxTokens = 2048
        };

        var jsonBody = JsonSerializer.Serialize(payload);
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"{SettingsManager.SupabaseUrl}/functions/v1/transcribe");
        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        request.Headers.Add("apikey", SettingsManager.SupabaseAnonKey);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", _authService.AccessToken);

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Cloud translation failed: {response.StatusCode} - {errorContent}");
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(responseJson);

        if (doc.RootElement.TryGetProperty("text", out var textEl))
            return textEl.GetString()?.Trim() ?? "";
        if (doc.RootElement.TryGetProperty("error", out var errorEl))
            throw new Exception($"Cloud error: {errorEl.GetString()}");

        return responseJson.Trim();
    }
}
