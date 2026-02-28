using System.Text;
using System.Text.Json;
using VoxAiGo.Core.Services;

namespace VoxAiGo.Core.Managers;

/// <summary>
/// Manages the 7-day Pro trial with device-based anti-abuse.
/// Port of macOS TrialManager.swift — shares the same Supabase backend.
/// </summary>
public class TrialManager
{
    private static readonly Lazy<TrialManager> _instance = new(() => new TrialManager());
    public static TrialManager Shared => _instance.Value;

    public enum TrialState
    {
        Unknown,
        Active,
        Expired
    }

    public TrialState CurrentState { get; private set; } = TrialState.Unknown;
    public int TrialDaysRemaining { get; private set; }
    public int TrialTranscriptionsUsed { get; private set; }

    public static readonly TimeSpan TrialDuration = TimeSpan.FromDays(7);
    public const int TrialTranscriptionLimit = 50;

    public event Action? TrialStateChanged;

    // Anti-tamper hash
    private const string TrialHashSalt = "v0x41g0_tr14l";

    private static class Keys
    {
        public const string TrialStartedAt = "trial_started_at";
        public const string TrialEndsAt = "trial_ends_at";
        public const string TrialDeviceRegistered = "trial_device_registered";
        public const string TrialTranscriptionsUsed = "trial_transcriptions_used";
        public const string TrialIntegrityHash = "ttu_h";
    }

    private TrialManager()
    {
        var stored = SettingsManager.Shared.GetInt(Keys.TrialTranscriptionsUsed, 0);
        var storedHash = SettingsManager.Shared.GetString(Keys.TrialIntegrityHash);
        var expected = TrialIntegrityHash(stored);

        if (!string.IsNullOrEmpty(storedHash) && storedHash != expected)
        {
            // Integrity check failed on load
        }

        TrialTranscriptionsUsed = stored;

        // Ensure hash exists (migration)
        if (string.IsNullOrEmpty(storedHash))
        {
            SaveTrialCount(stored);
        }

        UpdateTrialState();
    }

    // MARK: - Public API

    public bool IsTrialActive()
    {
        UpdateTrialState();
        return CurrentState == TrialState.Active;
    }

    public bool HasReachedTrialLimit =>
        TrialTranscriptionsUsed >= TrialTranscriptionLimit;

    public void ForceExpireTrial()
    {
        var now = DateTimeOffset.UtcNow;
        SettingsManager.Shared.Set(Keys.TrialStartedAt,
            (now - TrialDuration).ToUnixTimeSeconds());
        SettingsManager.Shared.Set(Keys.TrialEndsAt,
            (now - TimeSpan.FromSeconds(1)).ToUnixTimeSeconds());
        SettingsManager.Shared.Set(Keys.TrialDeviceRegistered, true);
        CurrentState = TrialState.Expired;
        TrialDaysRemaining = 0;
        TrialStateChanged?.Invoke();
    }

    public async Task<bool> CheckTrialEligibility(AuthService authService)
    {
        var deviceId = GetDeviceId();

        // Check server first
        var serverResult = await CheckDeviceOnServer(deviceId, authService);
        if (serverResult.HasValue)
            return serverResult.Value;

        // Fallback to local
        return !SettingsManager.Shared.GetBool(Keys.TrialDeviceRegistered, false);
    }

    public async Task StartTrial(AuthService authService)
    {
        var deviceId = GetDeviceId();

        // Register on server
        await RegisterDeviceOnServer(deviceId, authService);

        // Store locally
        var now = DateTimeOffset.UtcNow;
        var endsAt = now + TrialDuration;
        SettingsManager.Shared.Set(Keys.TrialStartedAt, now.ToUnixTimeSeconds());
        SettingsManager.Shared.Set(Keys.TrialEndsAt, endsAt.ToUnixTimeSeconds());
        SettingsManager.Shared.Set(Keys.TrialDeviceRegistered, true);
        SaveTrialCount(0);

        TrialTranscriptionsUsed = 0;
        UpdateTrialState();
    }

    public async Task AutoStartTrialIfEligible(AuthService authService)
    {
        if (CurrentState != TrialState.Unknown) return;

        var eligible = await CheckTrialEligibility(authService);
        if (!eligible)
        {
            CurrentState = TrialState.Expired;
            TrialStateChanged?.Invoke();
            return;
        }

        await StartTrial(authService);
    }

    public void RecordTrialTranscription()
    {
        VerifyTrialIntegrity();
        TrialTranscriptionsUsed++;
        SaveTrialCount(TrialTranscriptionsUsed);
        UpdateTrialState();
        TrialStateChanged?.Invoke();
    }

    // MARK: - State Management

    private void UpdateTrialState()
    {
        var endsAtTimestamp = SettingsManager.Shared.GetInt(Keys.TrialEndsAt, 0);

        if (endsAtTimestamp <= 0)
        {
            var registered = SettingsManager.Shared.GetBool(Keys.TrialDeviceRegistered, false);
            var newState = registered ? TrialState.Expired : TrialState.Unknown;
            if (CurrentState != newState)
            {
                CurrentState = newState;
                TrialDaysRemaining = 0;
            }
            return;
        }

        var endsAt = DateTimeOffset.FromUnixTimeSeconds(endsAtTimestamp);
        var now = DateTimeOffset.UtcNow;

        if (now < endsAt && !HasReachedTrialLimit)
        {
            var remaining = (endsAt - now).Days;
            CurrentState = TrialState.Active;
            TrialDaysRemaining = Math.Max(1, remaining);
        }
        else
        {
            CurrentState = TrialState.Expired;
            TrialDaysRemaining = 0;
        }
    }

    // MARK: - Anti-Tamper

    private string TrialIntegrityHash(int count)
    {
        var input = $"{TrialHashSalt}_{count}";
        ulong hash = 0;
        foreach (var c in Encoding.UTF8.GetBytes(input))
        {
            hash = unchecked(hash * 31 + c);
        }
        return ToBase36(hash);
    }

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

    private void VerifyTrialIntegrity()
    {
        var stored = SettingsManager.Shared.GetInt(Keys.TrialTranscriptionsUsed, 0);
        var storedHash = SettingsManager.Shared.GetString(Keys.TrialIntegrityHash);
        var expected = TrialIntegrityHash(stored);

        if (storedHash != expected && stored < TrialTranscriptionsUsed)
        {
            SettingsManager.Shared.Set(Keys.TrialTranscriptionsUsed, TrialTranscriptionsUsed);
            SettingsManager.Shared.Set(Keys.TrialIntegrityHash, TrialIntegrityHash(TrialTranscriptionsUsed));
        }
        else
        {
            TrialTranscriptionsUsed = stored;
        }
    }

    private void SaveTrialCount(int count)
    {
        SettingsManager.Shared.Set(Keys.TrialTranscriptionsUsed, count);
        SettingsManager.Shared.Set(Keys.TrialIntegrityHash, TrialIntegrityHash(count));
    }

    // MARK: - Device ID

    private static string GetDeviceId()
    {
        // Windows: use machine name + SID as stable device identifier
        var machineName = Environment.MachineName;
        var userName = Environment.UserName;
        var input = $"{machineName}_{userName}_voxaigo";
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    // MARK: - Server Communication

    private async Task<bool?> CheckDeviceOnServer(string deviceId, AuthService authService)
    {
        if (!authService.IsLoggedIn) return null;

        try
        {
            var json = await authService.GetAsync(
                $"/rest/v1/device_trials?device_id=eq.{deviceId}&select=id");

            if (string.IsNullOrEmpty(json)) return null;

            var results = JsonSerializer.Deserialize<JsonElement[]>(json);
            return results == null || results.Length == 0; // Empty = eligible
        }
        catch
        {
            return null; // Fallback to local
        }
    }

    private async Task RegisterDeviceOnServer(string deviceId, AuthService authService)
    {
        if (!authService.IsLoggedIn || authService.CurrentUser?.Id == null) return;

        try
        {
            await authService.PostFunctionAsync("register-trial-device", new
            {
                device_id = deviceId,
                user_id = authService.CurrentUser.Id
            });
        }
        catch { /* Best effort */ }
    }
}
