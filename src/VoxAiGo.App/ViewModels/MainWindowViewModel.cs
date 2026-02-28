using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using VoxAiGo.Core.Managers;
using VoxAiGo.Core.Models;
using VoxAiGo.Core.Services;

namespace VoxAiGo.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly AuthService _authService;
    private readonly HistoryService _historyService;
    private readonly AnalyticsManager _analytics = AnalyticsManager.Shared;
    private readonly SnippetsManager _snippets = SnippetsManager.Shared;
    private readonly WritingStyleManager _writingStyle = WritingStyleManager.Shared;
    private readonly SoundManager _sound = SoundManager.Shared;
    private readonly SettingsManager _settings = SettingsManager.Shared;

    // ===== Navigation =====
    [ObservableProperty] private int _selectedSectionIndex = 0;

    public bool IsHomeVisible => SelectedSectionIndex == 0;
    public bool IsLanguagesVisible => SelectedSectionIndex == 1;
    public bool IsModesVisible => SelectedSectionIndex == 2;
    public bool IsSnippetsVisible => SelectedSectionIndex == 3;
    public bool IsStyleVisible => SelectedSectionIndex == 4;
    public bool IsSettingsVisible => SelectedSectionIndex == 5;

    partial void OnSelectedSectionIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsHomeVisible));
        OnPropertyChanged(nameof(IsLanguagesVisible));
        OnPropertyChanged(nameof(IsModesVisible));
        OnPropertyChanged(nameof(IsSnippetsVisible));
        OnPropertyChanged(nameof(IsStyleVisible));
        OnPropertyChanged(nameof(IsSettingsVisible));
    }

    // ===== Auth =====
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _firstName = "";
    [ObservableProperty] private string _lastName = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string _statusMessageColor = "#FF6666";
    [ObservableProperty] private bool _isLoggedIn = false;
    [ObservableProperty] private bool _isAuthLoading = false;
    [ObservableProperty] private bool _isPro = false;
    [ObservableProperty] private string _subscriptionLabel = "Free";
    [ObservableProperty] private string _usageLabel = "";
    [ObservableProperty] private int _authViewIndex = 0; // 0=SignIn, 1=SignUp, 2=Reset
    [ObservableProperty] private int _signInMethodIndex = 0; // 0=Password, 1=MagicLink

    public bool IsSignInView => AuthViewIndex == 0;
    public bool IsSignUpView => AuthViewIndex == 1;
    public bool IsResetView => AuthViewIndex == 2;
    public bool IsPasswordMethod => SignInMethodIndex == 0;
    public bool IsMagicLinkMethod => SignInMethodIndex == 1;

    partial void OnAuthViewIndexChanged(int value)
    {
        StatusMessage = "";
        OnPropertyChanged(nameof(IsSignInView));
        OnPropertyChanged(nameof(IsSignUpView));
        OnPropertyChanged(nameof(IsResetView));
    }

    partial void OnSignInMethodIndexChanged(int value)
    {
        StatusMessage = "";
        OnPropertyChanged(nameof(IsPasswordMethod));
        OnPropertyChanged(nameof(IsMagicLinkMethod));
    }

    // ===== Home Section =====
    [ObservableProperty] private string _currentModeName = "Text";
    [ObservableProperty] private string _currentModeIcon = "\uE8C4";
    [ObservableProperty] private string _currentLanguageName = "English";
    [ObservableProperty] private string _currentLanguageFlag = "\U0001F1FA\U0001F1F8";
    [ObservableProperty] private int _todayTranscriptions = 0;
    [ObservableProperty] private int _totalTranscriptions = 0;
    [ObservableProperty] private int _dailyStreak = 0;
    [ObservableProperty] private string _shortcutHint = "Ctrl + Space (hold)";
    [ObservableProperty] private string _recordingStatus = "";
    [ObservableProperty] private string _recordingStatusColor = "#888";
    [ObservableProperty] private string _activeEngine = "";

    // ===== Mic Test =====
    [ObservableProperty] private bool _isMicTesting;
    [ObservableProperty] private double _micTestLevel;
    [ObservableProperty] private string _micTestStatus = "Click to test your microphone";
    [ObservableProperty] private string _micTestStatusColor = "#888";
    [ObservableProperty] private bool _micTestPassed;
    private WasapiCapture? _testCapture;
    private System.Timers.Timer? _micTestTimer;

    // ===== Languages Section =====
    [ObservableProperty] private ObservableCollection<LanguageItem> _languages = [];
    [ObservableProperty] private string _languageSearchText = "";

    public ObservableCollection<LanguageItem> FilteredLanguages
    {
        get
        {
            if (string.IsNullOrWhiteSpace(LanguageSearchText))
                return Languages;
            var lower = LanguageSearchText.ToLowerInvariant();
            return new ObservableCollection<LanguageItem>(
                Languages.Where(l => l.DisplayName.ToLowerInvariant().Contains(lower)
                    || l.FullName.ToLowerInvariant().Contains(lower)
                    || l.Code.ToLowerInvariant().Contains(lower)));
        }
    }

    partial void OnLanguageSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredLanguages));
    }

    // ===== Modes Section =====
    [ObservableProperty] private ObservableCollection<ModeItem> _modes = [];

    // ===== Snippets Section =====
    [ObservableProperty] private ObservableCollection<Snippet> _snippetsList = [];
    [ObservableProperty] private string _newSnippetTrigger = "";
    [ObservableProperty] private string _newSnippetReplacement = "";

    // ===== Style Section =====
    [ObservableProperty] private bool _styleEnabled;
    [ObservableProperty] private int _formalityLevel;
    [ObservableProperty] private int _verbosityLevel;
    [ObservableProperty] private int _technicalLevel;
    [ObservableProperty] private string _customInstructions = "";

    partial void OnStyleEnabledChanged(bool value) => _writingStyle.IsEnabled = value;
    partial void OnFormalityLevelChanged(int value) => _writingStyle.FormalityLevel = value;
    partial void OnVerbosityLevelChanged(int value) => _writingStyle.VerbosityLevel = value;
    partial void OnTechnicalLevelChanged(int value) => _writingStyle.TechnicalLevel = value;
    partial void OnCustomInstructionsChanged(string value) => _writingStyle.CustomInstructions = value;

    public string FormalityLabel => FormalityLevel < 30 ? "Casual" : FormalityLevel > 70 ? "Formal" : "Neutral";
    public string VerbosityLabel => VerbosityLevel < 30 ? "Concise" : VerbosityLevel > 70 ? "Detailed" : "Balanced";
    public string TechnicalLabel => TechnicalLevel < 30 ? "Simple" : TechnicalLevel > 70 ? "Technical" : "Moderate";

    // ===== Toast Notification =====
    [ObservableProperty] private string _toastMessage = "";
    [ObservableProperty] private string _toastColor = "#D4A017";
    [ObservableProperty] private bool _isToastVisible;
    private System.Timers.Timer? _toastTimer;

    public void ShowToast(string message, string color = "#D4A017", int durationMs = 3000)
    {
        _toastTimer?.Stop();
        _toastTimer?.Dispose();

        ToastMessage = message;
        ToastColor = color;
        IsToastVisible = true;

        _toastTimer = new System.Timers.Timer(durationMs);
        _toastTimer.Elapsed += (s, e) =>
        {
            _toastTimer?.Stop();
            _toastTimer?.Dispose();
            _toastTimer = null;
            System.Windows.Application.Current?.Dispatcher.Invoke(() => IsToastVisible = false);
        };
        _toastTimer.AutoReset = false;
        _toastTimer.Start();
    }

    // ===== DevTools (Easter Egg — session only) =====
    [ObservableProperty] private bool _isDevToolsVisible;
    [ObservableProperty] private bool _isDevToolsUnlocked;
    private int _versionTapCount;
    private DateTime _lastVersionTap = DateTime.MinValue;

    public void HandleVersionTap()
    {
        var now = DateTime.Now;
        if ((now - _lastVersionTap).TotalSeconds > 2) _versionTapCount = 0;
        _lastVersionTap = now;
        _versionTapCount++;

        if (_versionTapCount >= 5 && !IsDevToolsUnlocked)
        {
            // Will be handled in code-behind to show password dialog
            DevToolsPasswordRequested?.Invoke();
        }
    }

    public event Action? DevToolsPasswordRequested;

    public void UnlockDevTools(string password)
    {
        if (password == "voxdev")
        {
            IsDevToolsUnlocked = true;
            IsDevToolsVisible = true;
            ShowToast("Dev Tools unlocked!", "#00CC88");
        }
        else
        {
            ShowToast("Invalid password", "#FF6666");
        }
    }

    [RelayCommand]
    private async Task DevForceFreePlan()
    {
        TrialManager.Shared.ForceExpireTrial();
        SubscriptionManager.Shared.Plan = "free";
        SubscriptionManager.Shared.SubscriptionStatus = null;
        SubscriptionManager.Shared.WhisperTranscriptionsUsed = 0;

        if (_authService.IsLoggedIn && _authService.CurrentUser?.Id != null)
        {
            await _authService.PatchAsync(
                $"/rest/v1/profiles?id=eq.{_authService.CurrentUser.Id}",
                new { plan = "free", subscription_status = (string?)null });
        }

        SubscriptionManager.Shared.EnforceFreeTierDefaultsPublic();
        ShowToast("Forced FREE plan (server + local)", "#FF6666");
    }

    [RelayCommand]
    private async Task DevStartTrial()
    {
        await TrialManager.Shared.StartTrial(_authService);
        SubscriptionManager.Shared.Plan = "free";
        SubscriptionManager.Shared.SubscriptionStatus = null;

        if (_authService.IsLoggedIn && _authService.CurrentUser?.Id != null)
        {
            await _authService.PatchAsync(
                $"/rest/v1/profiles?id=eq.{_authService.CurrentUser.Id}",
                new { plan = "free", subscription_status = (string?)null });
        }

        ShowToast("Trial started! 7 days + 50 transcriptions", "#D4A017");
    }

    [RelayCommand]
    private async Task DevForcePro()
    {
        SubscriptionManager.Shared.Plan = "pro";
        SubscriptionManager.Shared.SubscriptionStatus = "active";

        if (_authService.IsLoggedIn && _authService.CurrentUser?.Id != null)
        {
            await _authService.PatchAsync(
                $"/rest/v1/profiles?id=eq.{_authService.CurrentUser.Id}",
                new { plan = "pro", subscription_status = "active" });
        }

        ShowToast("Forced PRO plan (server + local)", "#00CC88");
    }

    // ===== Settings Section =====
    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private bool _byokEnabled = false;
    [ObservableProperty] private bool _playSounds = true;
    [ObservableProperty] private bool _wakeWordEnabled = false;
    [ObservableProperty] private string _wakeWord = "Hey Vox";
    [ObservableProperty] private bool _useLocalModel = false;
    [ObservableProperty] private bool _isDownloading = false;
    [ObservableProperty] private double _downloadProgress = 0;
    [ObservableProperty] private string _downloadStatus = "";
    [ObservableProperty] private List<TranscriptionRecord> _historyRecords = [];

    // Events for App.xaml.cs wiring
    public event Action<bool>? UseLocalModelChanged;
    public event Action<string>? ApiKeyChanged;
    public event Action<string>? LanguageChanged;
    public event Action<string>? ModeChanged;

    partial void OnApiKeyChanged(string value)
    {
        _settings.ByokApiKey = value;
        ApiKeyChanged?.Invoke(value);
    }

    partial void OnByokEnabledChanged(bool value)
    {
        _settings.ByokEnabled = value;
        ApiKeyChanged?.Invoke(ApiKey);
    }

    partial void OnPlaySoundsChanged(bool value) => _sound.IsEnabled = value;
    partial void OnWakeWordEnabledChanged(bool value) => _settings.WakeWordEnabled = value;
    partial void OnWakeWordChanged(string value) => _settings.WakeWord = value;
    partial void OnUseLocalModelChanged(bool value) => UseLocalModelChanged?.Invoke(value);

    // ===== Constructor =====
    public MainWindowViewModel(AuthService authService, HistoryService historyService)
    {
        _authService = authService;
        _historyService = historyService;

        // Auth
        _authService.UserChanged += UpdateAuthStatus;
        UpdateAuthStatus();

        // History
        _historyService.HistoryChanged += () =>
            HistoryRecords = _historyService.Records.ToList();
        HistoryRecords = _historyService.Records.ToList();

        // Subscription
        SubscriptionManager.Shared.SubscriptionChanged += UpdateSubscriptionStatus;
        UpdateSubscriptionStatus();

        // Analytics
        _analytics.StatsChanged += UpdateHomeStats;
        UpdateHomeStats();

        // Snippets
        _snippets.SnippetsChanged += LoadSnippets;
        LoadSnippets();

        // Load settings
        ByokEnabled = _settings.ByokEnabled;
        ApiKey = _settings.ByokApiKey;
        PlaySounds = _settings.PlaySounds;
        WakeWordEnabled = _settings.WakeWordEnabled;
        WakeWord = _settings.WakeWord;

        // Load style settings
        StyleEnabled = _writingStyle.IsEnabled;
        FormalityLevel = _writingStyle.FormalityLevel;
        VerbosityLevel = _writingStyle.VerbosityLevel;
        TechnicalLevel = _writingStyle.TechnicalLevel;
        CustomInstructions = _writingStyle.CustomInstructions;

        // Initialize languages and modes
        InitLanguages();
        InitModes();
        UpdateCurrentModeLanguage();
    }

    private void InitLanguages()
    {
        var selectedCode = _settings.SelectedLanguage;
        var sub = SubscriptionManager.Shared;
        Languages.Clear();
        foreach (var lang in SpeechLanguage.All)
        {
            Languages.Add(new LanguageItem
            {
                Code = lang.Code,
                DisplayName = lang.DisplayName,
                FullName = lang.FullName,
                Flag = lang.Flag,
                IsSelected = lang.Code == selectedCode,
                IsFreeTier = lang.IsFreeTier,
                IsLocked = !lang.IsFreeTier && !sub.IsPro && !TrialManager.Shared.IsTrialActive()
            });
        }
    }

    private void InitModes()
    {
        var selectedMode = _settings.SelectedMode;
        var sub = SubscriptionManager.Shared;
        Modes.Clear();

        foreach (var mode in Enum.GetValues<TranscriptionMode>())
        {
            var name = mode.ToString();
            var isFree = SubscriptionManager.FreeModes.Contains(mode);
            Modes.Add(new ModeItem
            {
                Mode = mode,
                Name = name,
                Icon = mode.GetIconGlyph(),
                IsSelected = name == selectedMode || mode.GetApiName() == selectedMode,
                IsFreeTier = isFree,
                IsLocked = !isFree && !sub.IsPro && !TrialManager.Shared.IsTrialActive()
            });
        }
    }

    private void UpdateCurrentModeLanguage()
    {
        var lang = SpeechLanguage.All.FirstOrDefault(l => l.Code == _settings.SelectedLanguage) ?? SpeechLanguage.English;
        CurrentLanguageName = lang.DisplayName;
        CurrentLanguageFlag = lang.Flag;

        if (Enum.TryParse<TranscriptionMode>(_settings.SelectedMode, out var mode))
        {
            CurrentModeName = mode.ToString();
            CurrentModeIcon = mode.GetIconGlyph();
        }
    }

    private void UpdateHomeStats()
    {
        TodayTranscriptions = _analytics.TodayTranscriptions;
        TotalTranscriptions = _analytics.TotalTranscriptions;
        DailyStreak = _analytics.DailyStreak;
    }

    private void UpdateAuthStatus()
    {
        IsLoggedIn = _authService.IsLoggedIn;
        Email = _authService.CurrentUser?.Email ?? "";
        UpdateSubscriptionStatus();
    }

    private void UpdateSubscriptionStatus()
    {
        var sub = SubscriptionManager.Shared;
        IsPro = sub.IsPro;

        if (sub.IsPro)
        {
            SubscriptionLabel = "Pro";
            UsageLabel = "Unlimited transcriptions";
        }
        else if (TrialManager.Shared.IsTrialActive())
        {
            SubscriptionLabel = $"Trial ({TrialManager.Shared.TrialDaysRemaining}d left)";
            UsageLabel = $"{TrialManager.Shared.TrialTranscriptionsUsed}/{TrialManager.TrialTranscriptionLimit} transcriptions";
        }
        else
        {
            SubscriptionLabel = "Free";
            UsageLabel = $"{sub.WhisperTranscriptionsUsed}/{SubscriptionManager.WhisperMonthlyLimit} local transcriptions this month";
        }

        // Refresh locked state
        InitLanguages();
        InitModes();
    }

    private void LoadSnippets()
    {
        SnippetsList = new ObservableCollection<Snippet>(_snippets.Snippets);
    }

    // ===== Pipeline Sync (wake word changes mode/language in MainViewModel → reflect here) =====
    public void SyncModeFromPipeline(TranscriptionMode mode)
    {
        var modeName = mode.ToString();
        _settings.SelectedMode = modeName;
        foreach (var m in Modes) m.IsSelected = m.Name == modeName;
        OnPropertyChanged(nameof(Modes));
        CurrentModeName = modeName;
        CurrentModeIcon = mode.GetIconGlyph();
    }

    public void SyncLanguageFromPipeline(SpeechLanguage language)
    {
        _settings.SelectedLanguage = language.Code;
        foreach (var l in Languages) l.IsSelected = l.Code == language.Code;
        OnPropertyChanged(nameof(Languages));
        OnPropertyChanged(nameof(FilteredLanguages));
        CurrentLanguageName = language.DisplayName;
        CurrentLanguageFlag = language.Flag;
    }

    // ===== Mic Test Commands =====
    [RelayCommand]
    private void ToggleMicTest()
    {
        if (IsMicTesting)
            StopMicTest();
        else
            StartMicTest();
    }

    private void StartMicTest()
    {
        try
        {
            var enumerator = new MMDeviceEnumerator();
            MMDevice device;
            try
            {
                device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            }
            catch
            {
                MicTestStatus = "No microphone found! Check your audio settings.";
                MicTestStatusColor = "#FF6666";
                return;
            }

            MicTestStatus = "Listening... speak now!";
            MicTestStatusColor = "#D4A017";
            MicTestPassed = false;
            MicTestLevel = 0;
            IsMicTesting = true;

            _testCapture = new WasapiCapture(device);
            bool speechDetected = false;

            _testCapture.DataAvailable += (s, e) =>
            {
                float sum = 0;
                int samples = 0;
                if (_testCapture.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
                {
                    for (int i = 0; i < e.BytesRecorded; i += 4)
                    {
                        float sample = BitConverter.ToSingle(e.Buffer, i);
                        sum += sample * sample;
                        samples++;
                    }
                }
                else
                {
                    for (int i = 0; i < e.BytesRecorded; i += 2)
                    {
                        short sample = BitConverter.ToInt16(e.Buffer, i);
                        float normalized = sample / 32768f;
                        sum += normalized * normalized;
                        samples++;
                    }
                }

                if (samples > 0)
                {
                    float rms = MathF.Sqrt(sum / samples);
                    double level = Math.Min(rms * 5, 1.0); // Scale up for visibility
                    var newLevel = MicTestLevel * 0.5 + level * 0.5;
                    var detected = rms > 0.02f && !speechDetected;
                    if (detected) speechDetected = true;

                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        MicTestLevel = newLevel;
                        if (detected)
                        {
                            MicTestStatus = "Microphone working! Speech detected.";
                            MicTestStatusColor = "#00CC88";
                            MicTestPassed = true;
                        }
                    });
                }
            };

            _testCapture.RecordingStopped += (s, e) =>
            {
                _testCapture?.Dispose();
                _testCapture = null;
            };

            _testCapture.StartRecording();

            // Auto-stop after 5 seconds
            _micTestTimer = new System.Timers.Timer(5000);
            _micTestTimer.Elapsed += (s, e) =>
            {
                _micTestTimer?.Stop();
                _micTestTimer?.Dispose();
                _micTestTimer = null;
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (IsMicTesting && !MicTestPassed)
                    {
                        MicTestStatus = "No speech detected. Check mic permissions and input device.";
                        MicTestStatusColor = "#FF6666";
                    }
                    StopMicTest();
                });
            };
            _micTestTimer.Start();
        }
        catch (Exception ex)
        {
            MicTestStatus = $"Mic error: {ex.Message}";
            MicTestStatusColor = "#FF6666";
            IsMicTesting = false;
        }
    }

    private void StopMicTest()
    {
        _testCapture?.StopRecording();
        _micTestTimer?.Stop();
        _micTestTimer?.Dispose();
        _micTestTimer = null;
        IsMicTesting = false;
        MicTestLevel = 0;
    }

    // ===== Navigation Commands =====
    [RelayCommand] private void NavigateHome() => SelectedSectionIndex = 0;
    [RelayCommand] private void NavigateLanguages() => SelectedSectionIndex = 1;
    [RelayCommand] private void NavigateModes() => SelectedSectionIndex = 2;
    [RelayCommand] private void NavigateSnippets() => SelectedSectionIndex = 3;
    [RelayCommand] private void NavigateStyle() => SelectedSectionIndex = 4;
    [RelayCommand] private void NavigateSettings() => SelectedSectionIndex = 5;

    // ===== Auth Commands =====
    [RelayCommand]
    private void SignInWithGoogle()
    {
        _authService.SignInWithGoogle();
        StatusMessage = "Opening browser for Google sign-in...";
        StatusMessageColor = "#D4A017";
    }

    [RelayCommand]
    private async Task SignIn()
    {
        IsAuthLoading = true;
        StatusMessage = "Signing in...";
        StatusMessageColor = "#D4A017";
        if (await _authService.SignInAsync(Email, Password))
        {
            StatusMessage = "";
            Password = "";
        }
        else
        {
            StatusMessage = "Invalid email or password.";
            StatusMessageColor = "#FF6666";
        }
        IsAuthLoading = false;
    }

    [RelayCommand]
    private async Task SendMagicLink()
    {
        IsAuthLoading = true;
        StatusMessage = "Sending...";
        StatusMessageColor = "#D4A017";
        if (await _authService.SendMagicLinkAsync(Email))
        {
            StatusMessage = "Magic link sent! Check your email.";
            StatusMessageColor = "#00CC88";
        }
        else
        {
            StatusMessage = "Failed to send magic link.";
            StatusMessageColor = "#FF6666";
        }
        IsAuthLoading = false;
    }

    [RelayCommand]
    private async Task SignUp()
    {
        IsAuthLoading = true;
        StatusMessage = "Creating account...";
        StatusMessageColor = "#D4A017";
        if (await _authService.SignUpAsync(Email, Password))
        {
            StatusMessage = "Check your email to confirm your account.";
            StatusMessageColor = "#00CC88";
        }
        else
        {
            StatusMessage = "Sign up failed.";
            StatusMessageColor = "#FF6666";
        }
        IsAuthLoading = false;
    }

    [RelayCommand]
    private async Task ResetPassword()
    {
        IsAuthLoading = true;
        StatusMessage = "Sending...";
        StatusMessageColor = "#D4A017";
        if (await _authService.ResetPasswordAsync(Email))
        {
            StatusMessage = "Check your inbox to reset your password.";
            StatusMessageColor = "#00CC88";
        }
        else
        {
            StatusMessage = "Failed to send reset link.";
            StatusMessageColor = "#FF6666";
        }
        IsAuthLoading = false;
    }

    [RelayCommand]
    private async Task SignOut()
    {
        await _authService.SignOutAsync();
        SubscriptionManager.Shared.ResetToFree();
    }

    [RelayCommand] private void SwitchToSignIn() => AuthViewIndex = 0;
    [RelayCommand] private void SwitchToSignUp() => AuthViewIndex = 1;
    [RelayCommand] private void SwitchToReset() => AuthViewIndex = 2;
    [RelayCommand] private void SwitchToPasswordMethod() => SignInMethodIndex = 0;
    [RelayCommand] private void SwitchToMagicLinkMethod() => SignInMethodIndex = 1;

    // ===== Language Commands =====
    [RelayCommand]
    private void SelectLanguage(string code)
    {
        _settings.SelectedLanguage = code;
        foreach (var l in Languages) l.IsSelected = l.Code == code;
        OnPropertyChanged(nameof(Languages));
        OnPropertyChanged(nameof(FilteredLanguages));
        UpdateCurrentModeLanguage();
        LanguageChanged?.Invoke(code);
        WizardBus.FireLanguageChanged(code);
    }

    // ===== Mode Commands =====
    [RelayCommand]
    private void SelectMode(string modeName)
    {
        if (Enum.TryParse<TranscriptionMode>(modeName, out var mode))
        {
            _settings.SelectedMode = modeName;
            foreach (var m in Modes) m.IsSelected = m.Name == modeName;
            OnPropertyChanged(nameof(Modes));
            UpdateCurrentModeLanguage();
            ModeChanged?.Invoke(modeName);
            WizardBus.FireModeChanged(modeName);
        }
    }

    // ===== Snippet Commands =====
    [RelayCommand]
    private void AddSnippet()
    {
        if (string.IsNullOrWhiteSpace(NewSnippetTrigger) || string.IsNullOrWhiteSpace(NewSnippetReplacement))
            return;
        _snippets.Add(NewSnippetTrigger.Trim(), NewSnippetReplacement.Trim());
        NewSnippetTrigger = "";
        NewSnippetReplacement = "";
    }

    [RelayCommand]
    private void RemoveSnippet(string id)
    {
        _snippets.Remove(id);
    }

    [RelayCommand]
    private void ToggleSnippet(string id)
    {
        _snippets.ToggleEnabled(id);
    }

    // ===== Settings Commands =====
    [RelayCommand]
    private void OpenSubscriptionPage()
    {
        var url = SubscriptionManager.ProMonthlyCheckoutURL;
        var email = _authService.CurrentUser?.Email ?? "";
        if (!string.IsNullOrEmpty(email))
            url += $"?email={Uri.EscapeDataString(email)}&skip=1";
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    [RelayCommand]
    private async Task ClearHistory()
    {
        await _historyService.ClearHistoryAsync();
    }

    private readonly HttpClient _httpClient = new();

    [RelayCommand]
    private async Task DownloadModel()
    {
        var modelName = "ggml-base.bin";
        var url = $"https://huggingface.co/sandrocaea/whisper.net/resolve/main/{modelName}";
        var destinationPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", modelName);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        if (File.Exists(destinationPath))
        {
            DownloadStatus = "Model already installed.";
            return;
        }

        IsDownloading = true;
        DownloadStatus = "Starting download...";
        DownloadProgress = 0;

        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? 142000000;
            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                totalRead += bytesRead;
                DownloadProgress = (double)totalRead / totalBytes * 100;
                DownloadStatus = $"Downloading... {(int)DownloadProgress}%";
            }
            DownloadStatus = "Download complete!";
        }
        catch (Exception ex)
        {
            DownloadStatus = $"Error: {ex.Message}";
            if (File.Exists(destinationPath)) File.Delete(destinationPath);
        }
        finally { IsDownloading = false; }
    }
}

// ===== View Models for items =====

public partial class LanguageItem : ObservableObject
{
    [ObservableProperty] private string _code = "";
    [ObservableProperty] private string _displayName = "";
    [ObservableProperty] private string _fullName = "";
    [ObservableProperty] private string _flag = "";
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isFreeTier;
    [ObservableProperty] private bool _isLocked;
}

public partial class ModeItem : ObservableObject
{
    [ObservableProperty] private TranscriptionMode _mode;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _icon = "";
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isFreeTier;
    [ObservableProperty] private bool _isLocked;
}
