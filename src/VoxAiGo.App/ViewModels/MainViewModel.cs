using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NAudio.Wave;
using System.IO;
using System.Windows;
using VoxAiGo.App.Native;
using VoxAiGo.App.Platform;
using VoxAiGo.Core.Models;
using VoxAiGo.Core.Services;
using VoxAiGo.Core.Managers;
using System.Runtime.InteropServices;

namespace VoxAiGo.App.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly GlobalHotkeyManager _hotkeyManager;
    private readonly AudioRecorder _audioRecorder;
    private readonly WhisperService _localService;
    private readonly HistoryService _historyService;
    private readonly SettingsManager _settings = SettingsManager.Shared;
    private readonly SubscriptionManager _subscription = SubscriptionManager.Shared;
    private readonly AnalyticsManager _analytics = AnalyticsManager.Shared;
    private readonly SnippetsManager _snippets = SnippetsManager.Shared;
    private readonly SoundManager _sound = SoundManager.Shared;
    private readonly WritingStyleManager _writingStyle = WritingStyleManager.Shared;
    private System.Diagnostics.Stopwatch? _recordingStopwatch;

    // Transcription services
    private GeminiTranscriptionService? _byokService;
    private SupabaseTranscriptionService? _supabaseService;
    private AuthService? _authService;

    [ObservableProperty]
    private TranscriptionMode _currentMode = TranscriptionMode.Text;

    [ObservableProperty]
    private SpeechLanguage _currentLanguage = SpeechLanguage.English;

    [ObservableProperty]
    private double _audioLevel;

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private bool _isOverlayVisible;

    [ObservableProperty]
    private bool _useLocalModel = false;

    [ObservableProperty]
    private string _activeEngine = "None";

    [ObservableProperty]
    private string _statusText = "";

    private IntPtr _savedWindowHandle;

    // Conversation Reply mode: set when Ctrl+Space is pressed during conversation HUD
    public bool IsConversationReplyMode { get; set; }
    public string ConversationReplyTargetLanguage { get; set; } = "";

    // Event for wake word pro gating — free users trying to use wake word commands
    public event Action? WakeWordProLocked;

    // Event for conversation reply activation (Ctrl+Shift+R)
    public event Action? ConversationReplyActivated;

    public MainViewModel(HistoryService historyService)
    {
        _hotkeyManager = new GlobalHotkeyManager();
        _audioRecorder = new AudioRecorder();
        _localService = new WhisperService();
        _historyService = historyService;

        // Initialize BYOK if user has their own key (easter egg)
        if (_settings.HasByokKey)
            _byokService = new GeminiTranscriptionService(_settings.ByokApiKey);

        _hotkeyManager.HoldStarted += OnHoldStarted;
        _hotkeyManager.HoldStopped += OnHoldStopped;
        _hotkeyManager.ConversationReplyTriggered += () => ConversationReplyActivated?.Invoke();

        _audioRecorder.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(AudioRecorder.AudioLevel))
                AudioLevel = _audioRecorder.AudioLevel;
        };

        // Auto-load local model if available
        Task.Run(async () =>
        {
            try { await _localService.LoadModelAsync(); } catch {}
        });

        // Load saved mode/language
        LoadSettings();
    }

    private void LoadSettings()
    {
        var modeStr = _settings.GetString(SettingsManager.Keys.SelectedMode, "Text");
        if (Enum.TryParse<TranscriptionMode>(modeStr, out var mode))
            CurrentMode = mode;

        var langCode = _settings.GetString(SettingsManager.Keys.SelectedLanguage, "en");
        var lang = SpeechLanguage.All.FirstOrDefault(l => l.Code == langCode);
        if (lang != null)
            CurrentLanguage = lang;
    }

    public void Initialize()
    {
        _hotkeyManager.Install();
    }

    public void SetAuthService(AuthService authService)
    {
        _authService = authService;
        _supabaseService = new SupabaseTranscriptionService(authService);

        // Fetch profile on init if logged in
        if (authService.IsLoggedIn)
        {
            Task.Run(async () =>
            {
                await _subscription.FetchProfileAsync(authService);
                await _subscription.SyncWhisperUsageToServer(authService);
                await _subscription.FetchCloudStats(authService);
                await TrialManager.Shared.AutoStartTrialIfEligible(authService);
            });
            _subscription.StartPeriodicRefresh(authService);
        }

        // Listen for auth changes
        authService.UserChanged += () =>
        {
            if (authService.IsLoggedIn)
            {
                Task.Run(async () =>
                {
                    await _subscription.FetchProfileAsync(authService);
                    await _subscription.SyncWhisperUsageToServer(authService);
                    await _subscription.FetchCloudStats(authService);
                    await TrialManager.Shared.AutoStartTrialIfEligible(authService);
                });
                _subscription.StartPeriodicRefresh(authService);
            }
            else
            {
                _subscription.ResetToFree();
            }
        };
    }

    public void UpdateLanguage(string code)
    {
        var lang = SpeechLanguage.All.FirstOrDefault(l => l.Code == code);
        if (lang != null)
            CurrentLanguage = lang;
    }

    public void UpdateMode(string modeName)
    {
        if (Enum.TryParse<TranscriptionMode>(modeName, out var mode))
            CurrentMode = mode;
    }

    public void UpdateByokKey(string apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey) && _settings.ByokEnabled)
            _byokService = new GeminiTranscriptionService(apiKey);
        else
            _byokService = null;
    }

    /// <summary>
    /// Engine routing — identical to macOS SaaS (VoxAiGoViewModel.getTranscriptionService):
    ///
    /// Priority 1: BYOK (easter egg — always wins)
    /// Priority 2: Must be authenticated
    /// Priority 3: Manual offline toggle (Pro can force Whisper)
    /// Priority 4: Pro subscriber → Supabase cloud (Gemini proxy)
    /// Priority 5: Trial active → Supabase cloud
    /// Priority 6: Free → Whisper local
    /// </summary>
    private TranscriptionServiceType GetTranscriptionService()
    {
        // Priority 1: BYOK (easter egg — always wins)
        if (_settings.HasByokKey)
            return TranscriptionServiceType.Byok;

        // Must be authenticated
        if (_authService == null || !_authService.IsLoggedIn)
            return TranscriptionServiceType.None;

        // Priority 2: Manual offline toggle (Pro users can force Whisper local)
        if (_settings.OfflineMode)
            return TranscriptionServiceType.Whisper;

        // Priority 3: Pro subscriber → Gemini cloud
        if (_subscription.IsPro)
            return TranscriptionServiceType.Supabase;

        // Priority 4: Trial active → Gemini cloud
        if (TrialManager.Shared.IsTrialActive())
            return TranscriptionServiceType.Supabase;

        // Priority 5: Free → Whisper local
        return TranscriptionServiceType.Whisper;
    }

    private void OnHoldStarted()
    {
        if (IsProcessing) return;

        // Conversation Reply path: if HUD is showing "ready", intercept to record reply
        if (ConversationReplyManager.Shared.State == ConversationReplyManager.ReplyState.Ready)
        {
            var targetLanguage = ConversationReplyManager.Shared.FromLanguageName;
            ConversationReplyManager.Shared.BeginRecordingReply();

            IsConversationReplyMode = true;
            ConversationReplyTargetLanguage = targetLanguage;

            _savedWindowHandle = NativeMethods.GetForegroundWindow();
            _sound.PlayStart();
            _recordingStopwatch = System.Diagnostics.Stopwatch.StartNew();
            IsRecording = true;
            _audioRecorder.Start();
            return;
        }

        // Feature gating checks
        if (!_subscription.CanUseMode(CurrentMode))
        {
            StatusText = $"Mode '{CurrentMode}' requires Pro. Upgrade to unlock all modes.";
            return;
        }

        if (!_subscription.CanUseLanguage(CurrentLanguage))
        {
            StatusText = $"Language '{CurrentLanguage.DisplayName}' requires Pro. Free: PT/EN only.";
            return;
        }

        if (_subscription.NeedsOnlineValidation && _subscription.IsPro)
        {
            StatusText = "Please connect to the internet to validate your subscription.";
            return;
        }

        if (TrialManager.Shared.HasReachedTrialLimit && TrialManager.Shared.IsTrialActive())
        {
            StatusText = "Trial limit reached (50 transcriptions). Upgrade to Pro!";
            return;
        }

        if (_subscription.HasReachedWhisperLimit)
        {
            StatusText = $"Free limit reached ({SubscriptionManager.WhisperMonthlyLimit}/month). Upgrade to Pro!";
            return;
        }

        // Debug: show all routing variables
        var debugInfo = $"[DEBUG] LoggedIn={_authService?.IsLoggedIn}, Plan={_subscription.Plan}, Status={_subscription.SubscriptionStatus}, IsPro={_subscription.IsPro}, Offline={_settings.OfflineMode}, Trial={TrialManager.Shared.IsTrialActive()}, BYOK={_settings.HasByokKey}";
        System.Diagnostics.Debug.WriteLine(debugInfo);

        var service = GetTranscriptionService();
        ActiveEngine = service.ToString();
        if (service == TranscriptionServiceType.None)
        {
            StatusText = $"Please sign in. {debugInfo}";
            return;
        }

        StatusText = $"Recording... (engine: {service}) | {debugInfo}";
        IsOverlayVisible = true;
        IsRecording = true;
        _savedWindowHandle = NativeMethods.GetForegroundWindow();
        _sound.PlayStart();
        _recordingStopwatch = System.Diagnostics.Stopwatch.StartNew();
        _audioRecorder.Start();
    }

    private async void OnHoldStopped()
    {
        if (!IsRecording) return;

        IsRecording = false;
        _audioRecorder.Stop();
        _recordingStopwatch?.Stop();
        _sound.PlayStop();
        IsProcessing = true;
        var audioSeconds = _recordingStopwatch?.Elapsed.TotalSeconds ?? 0;

        // Small delay to ensure WAV file is fully written
        await Task.Delay(150);

        try
        {
            var audioBytes = _audioRecorder.GetRecordedBytes();
            StatusText = $"Audio: {(audioBytes?.Length ?? 0) / 1024}KB, duration: {audioSeconds:F1}s";
            if (audioBytes == null || audioBytes.Length == 0)
            {
                StatusText = "Error: No audio recorded. Check microphone permissions.";
                _sound.PlayError();
                return;
            }

            // MARK: Conversation Reply path (translate speech → detected language)
            if (IsConversationReplyMode)
            {
                IsConversationReplyMode = false;
                var targetLanguage = ConversationReplyTargetLanguage;
                ConversationReplyTargetLanguage = "";
                StatusText = "Translating reply...";
                ConversationReplyManager.Shared.BeginProcessingReply();

                var service = GetTranscriptionService();
                string translatedReply;

                switch (service)
                {
                    case TranscriptionServiceType.Byok:
                        translatedReply = await _byokService!.TranslateSpeechReplyAsync(audioBytes, targetLanguage);
                        break;
                    case TranscriptionServiceType.Supabase:
                        translatedReply = await _supabaseService!.TranslateSpeechReplyAsync(audioBytes, targetLanguage);
                        if (TrialManager.Shared.IsTrialActive() && !_subscription.IsPro)
                            TrialManager.Shared.RecordTrialTranscription();
                        break;
                    default:
                        throw new Exception("Conversation Reply requires cloud transcription (Pro or Trial).");
                }

                if (!string.IsNullOrWhiteSpace(translatedReply))
                {
                    _sound.PlaySuccess();
                    await PasteTextAsync(translatedReply);
                    StatusText = $"Reply pasted in {targetLanguage} ({translatedReply.Length} chars)";
                }

                ConversationReplyManager.Shared.Dismiss();
                IsProcessing = false;
                IsOverlayVisible = false;
                return;
            }

            {
                var service = GetTranscriptionService();
                string text;

                switch (service)
                {
                    case TranscriptionServiceType.Byok:
                        ActiveEngine = "Gemini (BYOK)";
                        text = await _byokService!.TranscribeAsync(audioBytes, CurrentMode, CurrentLanguage);
                        break;

                    case TranscriptionServiceType.Supabase:
                        ActiveEngine = "Cloud (Gemini)";
                        text = await _supabaseService!.TranscribeAsync(audioBytes, CurrentMode, CurrentLanguage);
                        // Record trial usage if in trial
                        if (TrialManager.Shared.IsTrialActive() && !_subscription.IsPro)
                            TrialManager.Shared.RecordTrialTranscription();
                        break;

                    case TranscriptionServiceType.Whisper:
                        ActiveEngine = "Whisper (Local)";
                        if (!_localService.IsModelLoaded)
                        {
                            StatusText = "Local model not downloaded. Go to Settings > Advanced.";
                            return;
                        }
                        text = await _localService.TranscribeAsync(audioBytes, CurrentMode, CurrentLanguage);
                        // Record whisper usage for free users
                        if (!_subscription.IsPro && !TrialManager.Shared.IsTrialActive())
                            _subscription.RecordWhisperTranscription();
                        break;

                    default:
                        StatusText = "Please sign in to use VoxAiGo.";
                        return;
                }

                if (!string.IsNullOrWhiteSpace(text))
                {
                    // Check for wake word commands (e.g., "Hey Vox, email")
                    if (_settings.WakeWordEnabled)
                    {
                        var wakeResult = WakeWordDetector.Detect(text, _settings.WakeWord);
                        if (wakeResult != null)
                        {
                            HandleWakeWordCommand(wakeResult);
                            return;
                        }
                    }

                    // Apply snippet expansion
                    text = _snippets.ExpandSnippets(text);

                    await _historyService.AddRecordAsync(text, CurrentMode, CurrentLanguage);

                    // Record analytics
                    _analytics.RecordTranscription(
                        CurrentMode.GetApiName(),
                        CurrentLanguage.Code,
                        text.Length,
                        audioSeconds);

                    _sound.PlaySuccess();
                    await PasteTextAsync(text);
                    WizardBus.FireTranscription(text);
                    StatusText = $"Transcribed ({text.Length} chars) — pasted to clipboard";
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Transcription Error: {ex.Message}");
            StatusText = $"Error: {ex.Message}";
            _sound.PlayError();
        }
        finally
        {
            IsProcessing = false;
            IsOverlayVisible = false;
        }
    }

    private async Task PasteTextAsync(string text)
    {
        // 1. Set Clipboard (Must be STA) — retry for COMException (clipboard locked)
        Application.Current.Dispatcher.Invoke(() =>
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try { Clipboard.SetText(text); return; }
                catch (System.Runtime.InteropServices.COMException) { System.Threading.Thread.Sleep(50); }
            }
            Clipboard.SetText(text);
        });

        // 2. Restore Focus to the previously active window
        if (_savedWindowHandle != IntPtr.Zero)
        {
            uint currentThread = NativeMethods.GetCurrentThreadId();
            uint targetThread = NativeMethods.GetWindowThreadProcessId(_savedWindowHandle, out _);

            NativeMethods.AttachThreadInput(currentThread, targetThread, true);
            NativeMethods.SetForegroundWindow(_savedWindowHandle);
            NativeMethods.AttachThreadInput(currentThread, targetThread, false);
        }

        // 3. Poll for focus restoration (up to 500ms) instead of fixed delay
        for (int i = 0; i < 50; i++)
        {
            if (_savedWindowHandle == IntPtr.Zero || NativeMethods.GetForegroundWindow() == _savedWindowHandle)
                break;
            await Task.Delay(10);
        }
        await Task.Delay(50); // Small extra buffer for IDE input readiness

        // 4. Simulate Ctrl+V with scan codes (required for modern apps like VS Code)
        var inputs = new NativeMethods.INPUT[4];

        // Ctrl Down (with scan code + extended key flag)
        inputs[0].type = NativeMethods.INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = NativeMethods.VK_CONTROL;
        inputs[0].u.ki.wScan = NativeMethods.SCAN_LCTRL;
        inputs[0].u.ki.dwFlags = 0;

        // V Down (with scan code)
        inputs[1].type = NativeMethods.INPUT_KEYBOARD;
        inputs[1].u.ki.wVk = NativeMethods.VK_V;
        inputs[1].u.ki.wScan = NativeMethods.SCAN_V;
        inputs[1].u.ki.dwFlags = 0;

        // V Up
        inputs[2].type = NativeMethods.INPUT_KEYBOARD;
        inputs[2].u.ki.wVk = NativeMethods.VK_V;
        inputs[2].u.ki.wScan = NativeMethods.SCAN_V;
        inputs[2].u.ki.dwFlags = NativeMethods.KEYEVENTF_KEYUP;

        // Ctrl Up
        inputs[3].type = NativeMethods.INPUT_KEYBOARD;
        inputs[3].u.ki.wVk = NativeMethods.VK_CONTROL;
        inputs[3].u.ki.wScan = NativeMethods.SCAN_LCTRL;
        inputs[3].u.ki.dwFlags = NativeMethods.KEYEVENTF_KEYUP;

        uint sent = NativeMethods.SendInput(4, inputs, Marshal.SizeOf(typeof(NativeMethods.INPUT)));
        System.Diagnostics.Debug.WriteLine($"[Paste] SendInput sent {sent}/4 events, target={_savedWindowHandle}, focused={NativeMethods.GetForegroundWindow()}");
    }

    private void HandleWakeWordCommand(WakeWordResult result)
    {
        // Pro gating: free/expired users can't use wake word commands
        if (!_subscription.IsPro && !TrialManager.Shared.IsTrialActive())
        {
            _sound.PlayError();
            StatusText = "Wake word commands require Pro. Upgrade to unlock.";
            WakeWordProLocked?.Invoke();
            return;
        }

        switch (result.Type)
        {
            case WakeWordResultType.Mode when result.Mode.HasValue:
                if (!_subscription.CanUseMode(result.Mode.Value))
                {
                    _sound.PlayError();
                    StatusText = $"Mode '{result.Mode.Value}' requires Pro.";
                    WakeWordProLocked?.Invoke();
                    return;
                }
                CurrentMode = result.Mode.Value;
                _settings.Set(SettingsManager.Keys.SelectedMode, result.Mode.Value.ToString());
                _sound.PlaySuccess();
                WizardBus.FireWakeWord();
                StatusText = $"Mode switched to: {result.Mode.Value}";
                break;

            case WakeWordResultType.Language when result.Language != null:
                if (!_subscription.CanUseLanguage(result.Language))
                {
                    _sound.PlayError();
                    StatusText = $"Language '{result.Language.DisplayName}' requires Pro.";
                    WakeWordProLocked?.Invoke();
                    return;
                }
                CurrentLanguage = result.Language;
                _settings.Set(SettingsManager.Keys.SelectedLanguage, result.Language.Code);
                _sound.PlaySuccess();
                WizardBus.FireWakeWord();
                StatusText = $"Language switched to: {result.Language.DisplayName} {result.Language.Flag}";
                break;

            case WakeWordResultType.NextLanguage:
                CycleLanguage(forward: true);
                break;

            case WakeWordResultType.PreviousLanguage:
                CycleLanguage(forward: false);
                break;

            default:
                StatusText = "Wake word detected but command not recognized.";
                break;
        }
    }

    private void CycleLanguage(bool forward)
    {
        var languages = SpeechLanguage.All;
        var currentIndex = languages.FindIndex(l => l.Code == CurrentLanguage.Code);
        if (currentIndex < 0) currentIndex = 0;

        var newIndex = forward
            ? (currentIndex + 1) % languages.Count
            : (currentIndex - 1 + languages.Count) % languages.Count;

        CurrentLanguage = languages[newIndex];
        _settings.Set(SettingsManager.Keys.SelectedLanguage, CurrentLanguage.Code);
        _sound.PlaySuccess();
        StatusText = $"Language: {CurrentLanguage.DisplayName} {CurrentLanguage.Flag}";
    }

    public void Dispose()
    {
        _hotkeyManager.Dispose();
        _audioRecorder.Dispose();
        _localService.Dispose();
    }
}
