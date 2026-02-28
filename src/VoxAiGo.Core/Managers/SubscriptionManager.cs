using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VoxAiGo.Core.Models;
using VoxAiGo.Core.Services;

namespace VoxAiGo.Core.Managers;

/// <summary>
/// Manages subscription tiers (Free/Trial/Pro), feature gating, and usage tracking.
/// Port of macOS SubscriptionManager.swift — shares the same Supabase backend.
/// </summary>
public class SubscriptionManager
{
    private static readonly Lazy<SubscriptionManager> _instance = new(() => new SubscriptionManager());
    public static SubscriptionManager Shared => _instance.Value;

    // Eduzz checkout URLs
    public const string ProMonthlyCheckoutURL = "https://chk.eduzz.com/39ZQ2OYE9E";
    public const string ProAnnualCheckoutURL = "https://chk.eduzz.com/Z0B57NQ3WA";

    // Free tier limits (Whisper local — 75 transcriptions/month)
    public const int WhisperMonthlyLimit = 75;
    public static readonly TranscriptionMode[] FreeModes = [TranscriptionMode.Text];
    public static readonly SpeechLanguage[] FreeLanguages = [SpeechLanguage.Portuguese, SpeechLanguage.English];

    // Legacy server-side limit (kept for Supabase gating)
    public const int FreeMonthlyLimit = 100;

    // State
    public string Plan { get; set; } = "free";
    public string? SubscriptionStatus { get; set; }
    public int FreeTranscriptionsUsed { get; set; }
    public int WhisperTranscriptionsUsed { get; set; }
    public DateTime? LastOnlineValidation { get; set; }
    public bool NeedsOnlineValidation { get; set; }

    // Cloud stats (from usage_log — same source as dashboard)
    public int CloudTotalTranscriptions { get; set; }
    public double CloudTotalRecordingSeconds { get; set; }

    // Periodic refresh timer reference
    private System.Timers.Timer? _refreshTimer;
    private AuthService? _cachedAuthService;

    // Grace period: 48 hours without server check
    public static readonly TimeSpan OnlineValidationGracePeriod = TimeSpan.FromHours(48);

    // Anti-tamper hash
    private const string HashSalt = "v0x41g0_wh15p3r";

    // Upgrade reminder interval
    public const int UpgradeReminderInterval = 15;

    public event Action? SubscriptionChanged;
    public event Action? ShowUpgradeReminder;

    private static string ToBase36(ulong value)
    {
        const string chars = "0123456789abcdefghijklmnopqrstuvwxyz";
        if (value == 0) return "0";
        var result = new char[13];
        var i = result.Length;
        while (value > 0)
        {
            result[--i] = chars[(int)(value % 36)];
            value /= 36;
        }
        return new string(result, i, result.Length - i);
    }

    private SubscriptionManager()
    {
        LoadWhisperUsage();
        LoadOnlineValidation();
    }

    // MARK: - Feature Gating

    public bool IsPro =>
        Plan == "pro" && SubscriptionStatus == "active";

    public bool CanUseMode(TranscriptionMode mode) =>
        IsPro || TrialManager.Shared.IsTrialActive() || FreeModes.Contains(mode);

    public bool CanUseLanguage(SpeechLanguage language) =>
        IsPro || TrialManager.Shared.IsTrialActive() || FreeLanguages.Contains(language);

    public bool HasReachedFreeLimit =>
        !IsPro && FreeTranscriptionsUsed >= FreeMonthlyLimit;

    public bool HasReachedWhisperLimit
    {
        get
        {
            if (IsPro) return false;
            if (TrialManager.Shared.IsTrialActive()) return false;
            CheckWhisperMonthlyReset();
            return WhisperTranscriptionsUsed >= WhisperMonthlyLimit;
        }
    }

    public int WhisperTranscriptionsRemaining =>
        Math.Max(0, WhisperMonthlyLimit - WhisperTranscriptionsUsed);

    // MARK: - Whisper Usage Tracking

    public void RecordWhisperTranscription()
    {
        VerifyWhisperIntegrity();
        WhisperTranscriptionsUsed++;
        SaveWhisperCount(WhisperTranscriptionsUsed);
        SubscriptionChanged?.Invoke();

        // Background sync to server (best-effort, non-blocking)
        if (_cachedAuthService != null)
        {
            _ = Task.Run(async () => await SyncWhisperUsageToServer(_cachedAuthService));
        }

        // Show soft upgrade reminder every N transcriptions for free users
        if (!IsPro &&
            !TrialManager.Shared.IsTrialActive() &&
            WhisperTranscriptionsUsed > 0 &&
            WhisperTranscriptionsUsed % UpgradeReminderInterval == 0 &&
            WhisperTranscriptionsUsed < WhisperMonthlyLimit)
        {
            ShowUpgradeReminder?.Invoke();
        }
    }

    // MARK: - Online Validation

    public void MarkOnlineValidation()
    {
        LastOnlineValidation = DateTime.UtcNow;
        SettingsManager.Shared.Set("last_online_validation", LastOnlineValidation.Value.ToString("o"));
        NeedsOnlineValidation = false;
    }

    private void CheckOnlineValidationStatus()
    {
        if (LastOnlineValidation == null)
        {
            NeedsOnlineValidation = false; // First launch — don't block
            return;
        }

        var elapsed = DateTime.UtcNow - LastOnlineValidation.Value;

        // VUL-03: if device clock went backward (elapsed < 0), treat as expired
        if (elapsed < TimeSpan.Zero)
        {
            NeedsOnlineValidation = true;
            System.Diagnostics.Debug.WriteLine("[SubscriptionManager] Clock jumped backward — treating online validation as expired");
            return;
        }

        NeedsOnlineValidation = elapsed > OnlineValidationGracePeriod;
    }

    private void LoadOnlineValidation()
    {
        var stored = SettingsManager.Shared.GetString("last_online_validation");
        if (!string.IsNullOrEmpty(stored) && DateTime.TryParse(stored, out var date))
        {
            LastOnlineValidation = date;
        }
        CheckOnlineValidationStatus();
    }

    // MARK: - Whisper Counter Persistence

    private string WhisperIntegrityHash(int count)
    {
        var input = $"{HashSalt}_{count}";
        ulong hash = 0;
        foreach (var c in Encoding.UTF8.GetBytes(input))
        {
            hash = unchecked(hash * 31 + c);
        }
        return ToBase36(hash);
    }

    private void VerifyWhisperIntegrity()
    {
        var stored = SettingsManager.Shared.GetInt("whisper_transcriptions_used", 0);
        var storedHash = SettingsManager.Shared.GetString("wtu_h");
        var expected = WhisperIntegrityHash(stored);

        if (storedHash != expected && stored < WhisperTranscriptionsUsed)
        {
            // Counter was tampered (reduced) — restore known value
            SettingsManager.Shared.Set("whisper_transcriptions_used", WhisperTranscriptionsUsed);
            SettingsManager.Shared.Set("wtu_h", WhisperIntegrityHash(WhisperTranscriptionsUsed));
        }
        else
        {
            WhisperTranscriptionsUsed = stored;
        }
    }

    private void SaveWhisperCount(int count)
    {
        SettingsManager.Shared.Set("whisper_transcriptions_used", count);
        SettingsManager.Shared.Set("wtu_h", WhisperIntegrityHash(count));
    }

    private void LoadWhisperUsage()
    {
        var stored = SettingsManager.Shared.GetInt("whisper_transcriptions_used", 0);
        var storedHash = SettingsManager.Shared.GetString("wtu_h");
        var expected = WhisperIntegrityHash(stored);

        if (!string.IsNullOrEmpty(storedHash) && storedHash != expected)
        {
            // Integrity check failed on load
        }

        WhisperTranscriptionsUsed = stored;

        // Ensure hash exists (migration for new installs)
        if (string.IsNullOrEmpty(storedHash))
        {
            SaveWhisperCount(stored);
        }
    }

    private void CheckWhisperMonthlyReset()
    {
        var now = DateTime.Now;

        // VUL-02: if device clock is behind last server validation, refuse to reset
        if (LastOnlineValidation.HasValue && now < LastOnlineValidation.Value.ToLocalTime())
        {
            System.Diagnostics.Debug.WriteLine("[SubscriptionManager] Clock behind server validation — ignoring monthly reset");
            return;
        }

        var resetDateStr = SettingsManager.Shared.GetString("whisper_transcriptions_reset_date");

        if (string.IsNullOrEmpty(resetDateStr))
        {
            // First time — set reset to next month
            var nextReset = new DateTime(now.Year, now.Month, 1).AddMonths(1);
            SettingsManager.Shared.Set("whisper_transcriptions_reset_date", nextReset.ToString("o"));
            return;
        }

        if (DateTime.TryParse(resetDateStr, out var resetDate) && now >= resetDate)
        {
            // Reset counter
            WhisperTranscriptionsUsed = 0;
            SaveWhisperCount(0);
            var nextReset = new DateTime(now.Year, now.Month, 1).AddMonths(1);
            SettingsManager.Shared.Set("whisper_transcriptions_reset_date", nextReset.ToString("o"));
        }
    }

    // MARK: - Profile Sync (from Supabase)

    public async Task FetchProfileAsync(AuthService authService)
    {
        if (!authService.IsLoggedIn) return;

        try
        {
            var json = await authService.GetAsync("/rest/v1/profiles?select=*&id=eq." +
                authService.CurrentUser?.Id);

            System.Diagnostics.Debug.WriteLine($"[SubscriptionManager] FetchProfile response: {json ?? "NULL"}");

            if (string.IsNullOrEmpty(json))
            {
                System.Diagnostics.Debug.WriteLine("[SubscriptionManager] Empty response from profiles query");
                return;
            }

            var profiles = JsonSerializer.Deserialize<JsonElement[]>(json);
            System.Diagnostics.Debug.WriteLine($"[SubscriptionManager] Parsed {profiles?.Length ?? 0} profiles");

            if (profiles != null && profiles.Length > 0)
            {
                var profile = profiles[0];
                if (profile.TryGetProperty("plan", out var planEl))
                    Plan = planEl.GetString() ?? "free";
                if (profile.TryGetProperty("subscription_status", out var statusEl))
                    SubscriptionStatus = statusEl.GetString();
                if (profile.TryGetProperty("free_transcriptions_used", out var usedEl))
                    FreeTranscriptionsUsed = usedEl.GetInt32();

                System.Diagnostics.Debug.WriteLine($"[SubscriptionManager] Plan={Plan}, Status={SubscriptionStatus}, IsPro={IsPro}");
            }

            MarkOnlineValidation();
            SubscriptionChanged?.Invoke();

            // If user lost Pro status, enforce free tier defaults
            if (!IsPro && !TrialManager.Shared.IsTrialActive())
            {
                EnforceFreeTierDefaults();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SubscriptionManager] FetchProfile ERROR: {ex.Message}");
        }
    }

    public void EnforceFreeTierDefaultsPublic() => EnforceFreeTierDefaults();

    private void EnforceFreeTierDefaults()
    {
        // Auto-switch to Text mode if current mode isn't free
        var currentMode = SettingsManager.Shared.GetString(SettingsManager.Keys.SelectedMode, "Text");
        if (!Enum.TryParse<TranscriptionMode>(currentMode, out var mode) || !FreeModes.Contains(mode))
        {
            SettingsManager.Shared.Set(SettingsManager.Keys.SelectedMode, "Text");
        }

        // Auto-switch to English if current language isn't free
        var currentLang = SettingsManager.Shared.GetString(SettingsManager.Keys.SelectedLanguage, "en");
        var langIsFree = FreeLanguages.Any(l => l.Code == currentLang);
        if (!langIsFree)
        {
            SettingsManager.Shared.Set(SettingsManager.Keys.SelectedLanguage, "en");
        }

        // Disable wake word for free users (matches Mac behavior)
        if (SettingsManager.Shared.WakeWordEnabled)
        {
            SettingsManager.Shared.WakeWordEnabled = false;
            System.Diagnostics.Debug.WriteLine("[SubscriptionManager] Auto-downgrade: wake word disabled");
        }
    }

    // MARK: - Bidirectional Whisper Sync (matches Mac syncWhisperUsageToServer)

    public async Task SyncWhisperUsageToServer(AuthService authService)
    {
        if (!authService.IsLoggedIn || authService.CurrentUser?.Id == null) return;

        var localCount = WhisperTranscriptionsUsed;
        var serverCount = FreeTranscriptionsUsed;

        // Use the higher of local vs server count (prevents reinstall reset exploit)
        if (serverCount > localCount)
        {
            WhisperTranscriptionsUsed = serverCount;
            SaveWhisperCount(serverCount);
            System.Diagnostics.Debug.WriteLine($"[SubscriptionManager] Whisper sync: server had higher count ({serverCount} > {localCount}), updated local");
        }

        if (localCount > serverCount)
        {
            var path = $"/rest/v1/profiles?id=eq.{authService.CurrentUser.Id}";
            var success = await authService.PatchAsync(path, new { free_transcriptions_used = localCount });
            if (success)
                System.Diagnostics.Debug.WriteLine($"[SubscriptionManager] Whisper sync: pushed local count ({localCount}) to server");
        }

        if (localCount == serverCount)
            System.Diagnostics.Debug.WriteLine($"[SubscriptionManager] Whisper sync: counts match ({localCount})");
    }

    // MARK: - Verify Purchase (edge function)

    public async Task<bool> VerifyPurchaseAsync(AuthService authService)
    {
        if (!authService.IsLoggedIn) return false;

        try
        {
            var result = await authService.PostFunctionAsync("verify-purchase", new { });
            System.Diagnostics.Debug.WriteLine($"[SubscriptionManager] VerifyPurchase response: {result ?? "NULL"}");

            // Refresh profile after verification
            await FetchProfileAsync(authService);
            return !string.IsNullOrEmpty(result);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SubscriptionManager] VerifyPurchase ERROR: {ex.Message}");
            return false;
        }
    }

    // MARK: - Cloud Stats (from usage_log — same source as dashboard)

    public async Task FetchCloudStats(AuthService authService)
    {
        if (!authService.IsLoggedIn || authService.CurrentUser?.Id == null) return;

        try
        {
            var path = $"/rest/v1/usage_log?user_id=eq.{authService.CurrentUser.Id}&select=audio_duration_seconds";
            var (body, response) = await authService.GetWithResponseAsync(path);

            if (body == null || response == null) return;

            // Parse count from Content-Range header: "0-N/total" or "*/total"
            if (response.Headers.TryGetValues("Content-Range", out var rangeValues))
            {
                var contentRange = rangeValues.FirstOrDefault() ?? "";
                var slashIndex = contentRange.LastIndexOf('/');
                if (slashIndex >= 0 && int.TryParse(contentRange[(slashIndex + 1)..], out var total))
                {
                    CloudTotalTranscriptions = total;
                }
            }

            // Sum audio durations
            var rows = JsonSerializer.Deserialize<JsonElement[]>(body);
            double totalSeconds = 0;
            if (rows != null)
            {
                foreach (var row in rows)
                {
                    if (row.TryGetProperty("audio_duration_seconds", out var durEl) && durEl.ValueKind == JsonValueKind.Number)
                        totalSeconds += durEl.GetDouble();
                }
            }
            CloudTotalRecordingSeconds = totalSeconds;
            System.Diagnostics.Debug.WriteLine($"[SubscriptionManager] Cloud stats: {CloudTotalTranscriptions} transcriptions, {(int)totalSeconds}s recording");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SubscriptionManager] FetchCloudStats ERROR: {ex.Message}");
        }
    }

    // MARK: - Periodic Refresh (5 minutes)

    public void StartPeriodicRefresh(AuthService authService)
    {
        _cachedAuthService = authService;
        StopPeriodicRefresh();

        _refreshTimer = new System.Timers.Timer(300_000); // 5 minutes
        _refreshTimer.Elapsed += async (s, e) =>
        {
            await FetchProfileAsync(authService);
            await SyncWhisperUsageToServer(authService);
            await FetchCloudStats(authService);
        };
        _refreshTimer.AutoReset = true;
        _refreshTimer.Start();
        System.Diagnostics.Debug.WriteLine("[SubscriptionManager] Periodic refresh started (5 min interval)");
    }

    public void StopPeriodicRefresh()
    {
        _refreshTimer?.Stop();
        _refreshTimer?.Dispose();
        _refreshTimer = null;
    }

    public void ResetToFree()
    {
        Plan = "free";
        SubscriptionStatus = null;
        FreeTranscriptionsUsed = 0;
        CloudTotalTranscriptions = 0;
        CloudTotalRecordingSeconds = 0;
        StopPeriodicRefresh();
        SubscriptionChanged?.Invoke();
    }
}
