using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VoxAiGo.Core.Managers;

namespace VoxAiGo.Core.Services;

public class AuthService
{
    private readonly HttpClient _http;
    private readonly string _supabaseUrl;
    private readonly string _anonKey;
    private System.Timers.Timer? _refreshTimer;
    private HttpListener? _oauthListener;

    public string? AccessToken { get; private set; }
    public string? RefreshToken { get; private set; }
    public SupabaseUser? CurrentUser { get; private set; }
    public bool IsLoggedIn => CurrentUser != null && !string.IsNullOrEmpty(AccessToken);

    public event Action? UserChanged;

    private const int OAuthPort = 43824;
    // Use the same redirect URL as macOS (already whitelisted in Supabase)
    private const string OAuthRedirectUrl = "voxaigo://auth/callback";

    public AuthService()
    {
        _supabaseUrl = SettingsManager.SupabaseUrl;
        _anonKey = SettingsManager.SupabaseAnonKey;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("apikey", _anonKey);
    }

    public async Task InitializeAsync()
    {
        // Try to restore session from stored tokens
        var jwt = SettingsManager.Shared.GetEncrypted(SettingsManager.Keys.JwtToken);
        var refresh = SettingsManager.Shared.GetEncrypted(SettingsManager.Keys.RefreshToken);

        if (!string.IsNullOrEmpty(jwt) && !string.IsNullOrEmpty(refresh))
        {
            AccessToken = jwt;
            RefreshToken = refresh;

            // Try to refresh the session
            var refreshed = await RefreshSessionAsync();
            if (!refreshed)
            {
                ClearSession();
            }
        }
    }

    public async Task<bool> SignInAsync(string email, string password)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { email, password });
            var content = new StringContent(body, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync(
                $"{_supabaseUrl}/auth/v1/token?grant_type=password", content);

            if (!response.IsSuccessStatusCode) return false;

            var json = await response.Content.ReadAsStringAsync();
            return ParseAuthResponse(json);
        }
        catch { return false; }
    }

    public async Task<bool> SignUpAsync(string email, string password)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { email, password });
            var content = new StringContent(body, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync(
                $"{_supabaseUrl}/auth/v1/signup", content);

            if (!response.IsSuccessStatusCode) return false;

            var json = await response.Content.ReadAsStringAsync();
            return ParseAuthResponse(json);
        }
        catch { return false; }
    }

    public async Task<bool> SendMagicLinkAsync(string email)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { email });
            var content = new StringContent(body, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync(
                $"{_supabaseUrl}/auth/v1/magiclink?redirect_to={Uri.EscapeDataString(OAuthRedirectUrl)}", content);

            if (response.IsSuccessStatusCode)
            {
                StartOAuthListener();
                return true;
            }
            return false;
        }
        catch { return false; }
    }

    public void SignInWithGoogle()
    {
        // Start local HTTP listener to receive forwarded tokens from protocol handler
        StartOAuthListener();

        // Open browser for Google OAuth — uses voxaigo:// scheme (already whitelisted in Supabase)
        var authUrl = $"{_supabaseUrl}/auth/v1/authorize?provider=google&redirect_to={Uri.EscapeDataString(OAuthRedirectUrl)}";
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = authUrl,
            UseShellExecute = true
        });
    }

    public async Task<bool> ResetPasswordAsync(string email)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { email });
            var content = new StringContent(body, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{_supabaseUrl}/auth/v1/recover", content);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // --- OAuth Local Listener ---

    private void StartOAuthListener()
    {
        StopOAuthListener();

        try
        {
            _oauthListener = new HttpListener();
            _oauthListener.Prefixes.Add($"http://localhost:{OAuthPort}/");
            _oauthListener.Start();

            // Listen asynchronously
            Task.Run(async () =>
            {
                try
                {
                    while (_oauthListener?.IsListening == true)
                    {
                        var context = await _oauthListener.GetContextAsync();
                        await HandleOAuthRequest(context);
                    }
                }
                catch (ObjectDisposedException) { /* Listener stopped */ }
                catch (HttpListenerException) { /* Listener stopped */ }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AuthService] Failed to start OAuth listener: {ex.Message}");
        }
    }

    private void StopOAuthListener()
    {
        try
        {
            _oauthListener?.Stop();
            _oauthListener?.Close();
        }
        catch { }
        _oauthListener = null;
    }

    private async Task HandleOAuthRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        if (request.Url?.AbsolutePath == "/auth/callback")
        {
            // Supabase OAuth redirects with tokens in the URL fragment (#access_token=...)
            // But HTTP servers can't see fragments — they stay client-side.
            // So we serve a small HTML page that reads the fragment and posts it back.
            var html = @"<!DOCTYPE html>
<html>
<head>
    <title>VoxAiGo - Signing In...</title>
    <style>
        body { background: #1A1A2E; color: #E0E0E0; font-family: 'Segoe UI', sans-serif;
               display: flex; justify-content: center; align-items: center; height: 100vh; margin: 0; }
        .card { background: #252540; border-radius: 16px; padding: 40px; text-align: center;
                border: 1px solid #333366; max-width: 400px; }
        h1 { color: #D4A017; font-size: 24px; margin-bottom: 10px; }
        p { color: #888; font-size: 14px; }
        .success { color: #00CC88; }
        .error { color: #FF6666; }
    </style>
</head>
<body>
    <div class='card'>
        <h1>VoxAiGo</h1>
        <p id='status'>Completing sign-in...</p>
    </div>
    <script>
        (async function() {
            const hash = window.location.hash.substring(1);
            if (hash) {
                try {
                    const res = await fetch('/auth/token', {
                        method: 'POST',
                        headers: { 'Content-Type': 'text/plain' },
                        body: hash
                    });
                    if (res.ok) {
                        document.getElementById('status').className = 'success';
                        document.getElementById('status').textContent = 'Signed in! You can close this tab.';
                        setTimeout(() => window.close(), 2000);
                    } else {
                        document.getElementById('status').className = 'error';
                        document.getElementById('status').textContent = 'Sign-in failed. Please try again.';
                    }
                } catch (e) {
                    document.getElementById('status').className = 'error';
                    document.getElementById('status').textContent = 'Connection error. Please try again.';
                }
            } else {
                // Check query params (some flows use query params instead of fragment)
                const params = new URLSearchParams(window.location.search);
                if (params.has('access_token')) {
                    const tokenData = params.toString();
                    try {
                        const res = await fetch('/auth/token', {
                            method: 'POST',
                            headers: { 'Content-Type': 'text/plain' },
                            body: tokenData
                        });
                        if (res.ok) {
                            document.getElementById('status').className = 'success';
                            document.getElementById('status').textContent = 'Signed in! You can close this tab.';
                            setTimeout(() => window.close(), 2000);
                        }
                    } catch (e) {}
                } else {
                    document.getElementById('status').className = 'error';
                    document.getElementById('status').textContent = 'No authentication data received.';
                }
            }
        })();
    </script>
</body>
</html>";

            var buffer = Encoding.UTF8.GetBytes(html);
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = buffer.Length;
            response.StatusCode = 200;
            await response.OutputStream.WriteAsync(buffer);
            response.Close();
        }
        else if (request.Url?.AbsolutePath == "/auth/token" && request.HttpMethod == "POST")
        {
            // Receive the token fragment from our JavaScript
            using var reader = new System.IO.StreamReader(request.InputStream, request.ContentEncoding);
            var fragment = await reader.ReadToEndAsync();

            HandleOAuthCallback(fragment);

            var ok = Encoding.UTF8.GetBytes("OK");
            response.ContentType = "text/plain";
            response.ContentLength64 = ok.Length;
            response.StatusCode = 200;
            await response.OutputStream.WriteAsync(ok);
            response.Close();

            // Stop listener after successful auth
            _ = Task.Delay(2000).ContinueWith(_ => StopOAuthListener());
        }
        else
        {
            response.StatusCode = 404;
            response.Close();
        }
    }

    // --- Auth Token Handling ---

    public void HandleOAuthCallback(string fragment)
    {
        // Parse access_token=...&refresh_token=...&...
        var parts = fragment.TrimStart('#').Split('&');
        string? accessToken = null, refreshToken = null;

        foreach (var part in parts)
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;
            if (kv[0] == "access_token") accessToken = Uri.UnescapeDataString(kv[1]);
            if (kv[0] == "refresh_token") refreshToken = Uri.UnescapeDataString(kv[1]);
        }

        if (!string.IsNullOrEmpty(accessToken))
        {
            AccessToken = accessToken;
            RefreshToken = refreshToken;
            SaveSession();
            _ = FetchUserAsync();
        }
    }

    public async Task SignOutAsync()
    {
        try
        {
            if (!string.IsNullOrEmpty(AccessToken))
            {
                var request = new HttpRequestMessage(HttpMethod.Post, $"{_supabaseUrl}/auth/v1/logout");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
                await _http.SendAsync(request);
            }
        }
        catch { /* Best effort */ }

        ClearSession();
        UserChanged?.Invoke();
    }

    public async Task<bool> RefreshSessionAsync()
    {
        if (string.IsNullOrEmpty(RefreshToken)) return false;

        try
        {
            var body = JsonSerializer.Serialize(new { refresh_token = RefreshToken });
            var content = new StringContent(body, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync(
                $"{_supabaseUrl}/auth/v1/token?grant_type=refresh_token", content);

            if (!response.IsSuccessStatusCode) return false;

            var json = await response.Content.ReadAsStringAsync();
            return ParseAuthResponse(json);
        }
        catch { return false; }
    }

    private bool ParseAuthResponse(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("access_token", out var at))
                AccessToken = at.GetString();
            if (root.TryGetProperty("refresh_token", out var rt))
                RefreshToken = rt.GetString();
            if (root.TryGetProperty("user", out var userEl))
            {
                CurrentUser = JsonSerializer.Deserialize<SupabaseUser>(userEl.GetRawText());
            }

            if (!string.IsNullOrEmpty(AccessToken))
            {
                SaveSession();
                StartRefreshTimer();
                UserChanged?.Invoke();
                return true;
            }
        }
        catch { }
        return false;
    }

    private async Task FetchUserAsync()
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_supabaseUrl}/auth/v1/user");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
            var response = await _http.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                CurrentUser = JsonSerializer.Deserialize<SupabaseUser>(json);
                StartRefreshTimer();
                UserChanged?.Invoke();
            }
        }
        catch { }
    }

    private void SaveSession()
    {
        if (!string.IsNullOrEmpty(AccessToken))
            SettingsManager.Shared.SetEncrypted(SettingsManager.Keys.JwtToken, AccessToken);
        if (!string.IsNullOrEmpty(RefreshToken))
            SettingsManager.Shared.SetEncrypted(SettingsManager.Keys.RefreshToken, RefreshToken);
    }

    private void ClearSession()
    {
        AccessToken = null;
        RefreshToken = null;
        CurrentUser = null;
        SettingsManager.Shared.Set(SettingsManager.Keys.JwtToken, "");
        SettingsManager.Shared.Set(SettingsManager.Keys.RefreshToken, "");
        _refreshTimer?.Stop();
    }

    private void StartRefreshTimer()
    {
        _refreshTimer?.Stop();
        _refreshTimer = new System.Timers.Timer(55 * 60 * 1000); // 55 minutes
        _refreshTimer.Elapsed += async (s, e) => await RefreshSessionAsync();
        _refreshTimer.AutoReset = true;
        _refreshTimer.Start();
    }

    // --- Supabase REST helpers (authenticated) ---
    public async Task<string?> GetAsync(string path)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_supabaseUrl}{path}");
            if (!string.IsNullOrEmpty(AccessToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
            var response = await _http.SendAsync(request);
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadAsStringAsync();
        }
        catch { }
        return null;
    }

    public async Task<(string? body, HttpResponseMessage? response)> GetWithResponseAsync(string path)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_supabaseUrl}{path}");
            if (!string.IsNullOrEmpty(AccessToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
            var response = await _http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            return (response.IsSuccessStatusCode ? body : null, response);
        }
        catch { return (null, null); }
    }

    public async Task<bool> PatchAsync(string path, object body)
    {
        try
        {
            var jsonBody = JsonSerializer.Serialize(body);
            var request = new HttpRequestMessage(HttpMethod.Patch, $"{_supabaseUrl}{path}");
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            request.Headers.Add("Prefer", "return=minimal");
            if (!string.IsNullOrEmpty(AccessToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
            var response = await _http.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<string?> PostFunctionAsync(string functionName, object body)
    {
        try
        {
            var jsonBody = JsonSerializer.Serialize(body);
            var request = new HttpRequestMessage(HttpMethod.Post,
                $"{_supabaseUrl}/functions/v1/{functionName}");
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            if (!string.IsNullOrEmpty(AccessToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
            var response = await _http.SendAsync(request);
            return await response.Content.ReadAsStringAsync();
        }
        catch { return null; }
    }
}

public class SupabaseUser
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("user_metadata")]
    public Dictionary<string, JsonElement>? UserMetadata { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }
}
