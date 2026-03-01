using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using Microsoft.Win32;
using VoxAiGo.App.ViewModels;
using VoxAiGo.App.Views;
using VoxAiGo.Core.Managers;
using VoxAiGo.Core.Models;
using VoxAiGo.Core.Services;
using H.NotifyIcon;

namespace VoxAiGo.App;

public partial class App : Application
{
    private MainViewModel? _viewModel;
    private OverlayWindow? _overlayWindow;
    private ConversationReplyWindow? _conversationReplyWindow;
    private TaskbarIcon? _taskbarIcon;
    private MainWindow? _mainWindow;
    private SetupWizardWindow? _wizardWindow;
    private AuthService? _authService;
    private HistoryService? _historyService;
    private DateTime _lastConversationReplyTime = DateTime.MinValue;

    private const string ProtocolScheme = "voxaigo";
    private const int OAuthPort = 43824;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Force software rendering for remote/VM environments (JumpStart Desktop, etc.)
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        // Tray app: never quit when last window closes — only quit via menu
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Global exception handlers — expose any hidden crash with a visible message
        DispatcherUnhandledException += (s, ex) =>
        {
            var msg = $"Unexpected error:\n\n{ex.Exception.Message}\n\n{ex.Exception.StackTrace}";
            System.Diagnostics.Debug.WriteLine($"[VoxAiGo CRASH] {msg}");
            LogError(ex.Exception);
            MessageBox.Show(msg, "VoxAiGo — Error", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
        {
            if (ex.ExceptionObject is Exception err)
            {
                LogError(err);
                System.Diagnostics.Debug.WriteLine($"[VoxAiGo FATAL] {err.Message}\n{err.StackTrace}");
            }
        };

        TaskScheduler.UnobservedTaskException += (s, ex) =>
        {
            LogError(ex.Exception);
            System.Diagnostics.Debug.WriteLine($"[VoxAiGo TASK] {ex.Exception.Message}");
            ex.SetObserved();
        };

        // Check if launched as OAuth callback handler (second instance)
        if (e.Args.Length > 0 && e.Args[0].StartsWith($"{ProtocolScheme}://", StringComparison.OrdinalIgnoreCase))
        {
            await ForwardOAuthCallback(e.Args[0]);
            Shutdown();
            return;
        }

        // Register voxaigo:// protocol handler (same as macOS Info.plist URL scheme)
        RegisterProtocolHandler();

        // 0. Initialize Services
        _authService = new AuthService();
        try { await _authService.InitializeAsync(); }
        catch { /* Auth will work without saved session */ }

        _historyService = new HistoryService();
        try { await _historyService.InitializeAsync(); }
        catch { /* History starts empty */ }

        // 1. Create Core ViewModel (recording pipeline)
        _viewModel = new MainViewModel(_historyService);
        _viewModel.SetAuthService(_authService);
        _viewModel.Initialize();

        // 2. Create Overlay Window (hidden by default)
        _overlayWindow = new OverlayWindow(_viewModel);
        _overlayWindow.Hide();

        // 3. Create Main Window (replaces old SettingsWindow)
        var mainWindowVm = new MainWindowViewModel(_authService, _historyService);
        mainWindowVm.UseLocalModelChanged += (val) => _viewModel.UseLocalModel = val;
        mainWindowVm.ApiKeyChanged += (key) => _viewModel.UpdateByokKey(key);
        mainWindowVm.LanguageChanged += (code) => _viewModel.UpdateLanguage(code);
        mainWindowVm.ModeChanged += (mode) => _viewModel.UpdateMode(mode);

        // Wire recording status from MainViewModel → MainWindowViewModel for visible feedback
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.StatusText))
            {
                Dispatcher.Invoke(() =>
                {
                    mainWindowVm.RecordingStatus = _viewModel.StatusText;
                    mainWindowVm.RecordingStatusColor = string.IsNullOrEmpty(_viewModel.StatusText) ? "#888"
                        : _viewModel.StatusText.StartsWith("Error") ? "#FF6666" : "#D4A017";
                });
            }
            if (e.PropertyName == nameof(MainViewModel.ActiveEngine))
            {
                Dispatcher.Invoke(() => mainWindowVm.ActiveEngine = _viewModel.ActiveEngine);
            }
            if (e.PropertyName == nameof(MainViewModel.IsRecording))
            {
                Dispatcher.Invoke(() =>
                {
                    mainWindowVm.RecordingStatus = _viewModel.IsRecording ? "Recording..." : mainWindowVm.RecordingStatus;
                    mainWindowVm.RecordingStatusColor = _viewModel.IsRecording ? "#00CC88" : mainWindowVm.RecordingStatusColor;
                });
            }
            if (e.PropertyName == nameof(MainViewModel.IsProcessing))
            {
                Dispatcher.Invoke(() =>
                {
                    if (_viewModel.IsProcessing)
                    {
                        mainWindowVm.RecordingStatus = "Transcribing...";
                        mainWindowVm.RecordingStatusColor = "#9966FF";
                    }
                });
            }
            // Sync wake word mode/language changes back to MainWindowViewModel + toast feedback
            if (e.PropertyName == nameof(MainViewModel.CurrentMode))
            {
                Dispatcher.Invoke(() =>
                {
                    mainWindowVm.SyncModeFromPipeline(_viewModel.CurrentMode);
                    mainWindowVm.ShowToast($"Mode: {_viewModel.CurrentMode}", "#D4A017");
                });
            }
            if (e.PropertyName == nameof(MainViewModel.CurrentLanguage))
            {
                Dispatcher.Invoke(() =>
                {
                    mainWindowVm.SyncLanguageFromPipeline(_viewModel.CurrentLanguage);
                    mainWindowVm.ShowToast($"Language: {_viewModel.CurrentLanguage.DisplayName} {_viewModel.CurrentLanguage.Flag}", "#D4A017");
                });
            }
        };

        // Wake word pro gating — show upgrade prompt for free users
        _viewModel.WakeWordProLocked += () =>
        {
            Dispatcher.Invoke(() =>
            {
                var result = MessageBox.Show(
                    "Comandos de voz (Agente Vox) são um recurso Pro.\n\nFaça upgrade para Pro e tenha transcrições ilimitadas, todos os 15 modos, 30 idiomas e comandos de voz.\n\nAbrir página de upgrade?",
                    "VoxAiGo Pro Necessário",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);
                if (result == MessageBoxResult.Yes)
                    OpenCheckoutWithEmail();
            });
        };

        // Conversation Reply: Ctrl+Shift+R activation
        _viewModel.ConversationReplyActivated += () =>
        {
            Dispatcher.Invoke(() => ActivateConversationReply());
        };

        // Soft upgrade reminder every 15 Whisper transcriptions
        SubscriptionManager.Shared.ShowUpgradeReminder += () =>
        {
            Dispatcher.Invoke(() =>
            {
                var sub = SubscriptionManager.Shared;
                var remaining = sub.WhisperTranscriptionsRemaining;
                var result = MessageBox.Show(
                    $"You've used {sub.WhisperTranscriptionsUsed} of {SubscriptionManager.WhisperMonthlyLimit} free transcriptions this month.\n\n{remaining} remaining.\n\nUpgrade to Pro for unlimited transcriptions, all 15 AI modes, and 30 languages.\n\nOpen upgrade page?",
                    "VoxAiGo — Upgrade to Pro",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);
                if (result == MessageBoxResult.Yes)
                    OpenCheckoutWithEmail();
            });
        };

        _mainWindow = new MainWindow(mainWindowVm);

        // 4. Create System Tray Icon
        CreateTrayIcon();

        // 5. Gate behind login: show wizard first if not completed, then main window
        if (!SettingsManager.Shared.HasCompletedSetup)
        {
            // Show Setup Wizard (includes login step 0) — main window stays hidden
            _wizardWindow = new SetupWizardWindow(_authService);
            _wizardWindow.Closed += (s, args) =>
            {
                // Wizard finished or closed — now show main window
                _mainWindow?.Show();
                _mainWindow?.Activate();
            };
            _wizardWindow.Show();
            _wizardWindow.Activate();
        }
        else if (!_authService.IsLoggedIn)
        {
            // Setup done but not logged in — show wizard at login step, main window hidden
            _wizardWindow = new SetupWizardWindow(_authService);
            _wizardWindow.Closed += (s, args) =>
            {
                _mainWindow?.Show();
                _mainWindow?.Activate();
            };
            _wizardWindow.Show();
            _wizardWindow.Activate();
        }
        else
        {
            // Already logged in and setup complete — show main window directly
            _mainWindow?.Show();
            _mainWindow?.Activate();
        }
    }

    private static void RegisterProtocolHandler()
    {
        try
        {
            var exePath = Environment.ProcessPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VoxAiGo.App.exe");

            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProtocolScheme}");
            key.SetValue("", $"URL:{ProtocolScheme} Protocol");
            key.SetValue("URL Protocol", "");

            using var iconKey = key.CreateSubKey("DefaultIcon");
            iconKey.SetValue("", $"\"{exePath}\",0");

            using var commandKey = key.CreateSubKey(@"shell\open\command");
            commandKey.SetValue("", $"\"{exePath}\" \"%1\"");
        }
        catch { }
    }

    private static async Task ForwardOAuthCallback(string url)
    {
        try
        {
            var fragmentIndex = url.IndexOf('#');
            if (fragmentIndex < 0) return;
            var fragment = url[(fragmentIndex + 1)..];
            using var http = new HttpClient();
            var content = new StringContent(fragment, Encoding.UTF8, "text/plain");
            await http.PostAsync($"http://localhost:{OAuthPort}/auth/token", content);
        }
        catch { }
    }

    private System.Windows.Controls.MenuItem? _trayStatusItem;
    private System.Windows.Controls.MenuItem? _trayModeItem;

    private void CreateTrayIcon()
    {
        var icon = CreateDefaultIcon();

        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "VoxAiGo — Hold Ctrl+Space to talk",
            Icon = icon,
        };

        var contextMenu = new System.Windows.Controls.ContextMenu();
        contextMenu.Style = CreateDarkMenuStyle();

        var headerItem = new System.Windows.Controls.MenuItem
        {
            Header = "VoxAiGo v3.0.0",
            IsEnabled = false,
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(212, 160, 23))
        };

        _trayStatusItem = new System.Windows.Controls.MenuItem
        {
            Header = GetTrayStatusLabel(),
            IsEnabled = false,
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(136, 136, 136))
        };

        _trayModeItem = new System.Windows.Controls.MenuItem
        {
            Header = GetTrayModeLabel(),
            IsEnabled = false,
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(136, 136, 136))
        };

        var settingsItem = new System.Windows.Controls.MenuItem { Header = "Settings" };
        settingsItem.Click += (s, args) =>
        {
            _mainWindow?.Show();
            _mainWindow?.Activate();
        };

        var quitItem = new System.Windows.Controls.MenuItem { Header = "Quit VoxAiGo" };
        quitItem.Click += (s, args) => Shutdown();

        contextMenu.Items.Add(headerItem);
        contextMenu.Items.Add(_trayStatusItem);
        contextMenu.Items.Add(_trayModeItem);
        contextMenu.Items.Add(new System.Windows.Controls.Separator());
        contextMenu.Items.Add(settingsItem);
        contextMenu.Items.Add(new System.Windows.Controls.Separator());
        contextMenu.Items.Add(quitItem);

        _taskbarIcon.ContextMenu = contextMenu;
        _taskbarIcon.TrayMouseDoubleClick += (s, args) =>
        {
            _mainWindow?.Show();
            _mainWindow?.Activate();
        };

        // Update tray on subscription changes
        SubscriptionManager.Shared.SubscriptionChanged += UpdateTrayMenu;
    }

    private void OpenCheckoutWithEmail()
    {
        var email = _authService?.CurrentUser?.Email ?? "";
        var url = SubscriptionManager.ProMonthlyCheckoutURL;
        if (!string.IsNullOrEmpty(email))
            url += $"?email={Uri.EscapeDataString(email)}&skip=1";
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private void UpdateTrayMenu()
    {
        Dispatcher.Invoke(() =>
        {
            if (_trayStatusItem != null) _trayStatusItem.Header = GetTrayStatusLabel();
            if (_trayModeItem != null) _trayModeItem.Header = GetTrayModeLabel();
        });
    }

    private static string GetTrayStatusLabel()
    {
        var sub = SubscriptionManager.Shared;
        if (sub.IsPro)
            return "Pro — Unlimited";
        if (TrialManager.Shared.IsTrialActive())
            return $"Trial — {TrialManager.Shared.TrialTranscriptionsUsed}/{TrialManager.TrialTranscriptionLimit}";
        return $"Free — {sub.WhisperTranscriptionsUsed}/{SubscriptionManager.WhisperMonthlyLimit}";
    }

    private static string GetTrayModeLabel()
    {
        var mode = SettingsManager.Shared.GetString(SettingsManager.Keys.SelectedMode, "Text");
        var langCode = SettingsManager.Shared.GetString(SettingsManager.Keys.SelectedLanguage, "en");
        var lang = SpeechLanguage.All.FirstOrDefault(l => l.Code == langCode);
        return $"Mode: {mode} | {lang?.DisplayName ?? langCode}";
    }

    private static System.Drawing.Icon CreateDefaultIcon()
    {
        try
        {
            var icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "voxaigo.ico");
            if (File.Exists(icoPath))
                return new System.Drawing.Icon(icoPath);

            // Fallback: try embedded resource
            var uri = new Uri("pack://application:,,,/Resources/voxaigo.ico", UriKind.Absolute);
            var stream = System.Windows.Application.GetResourceStream(uri)?.Stream;
            if (stream != null)
                return new System.Drawing.Icon(stream);
        }
        catch { }

        // Final fallback: generated "V" icon
        var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.Clear(System.Drawing.Color.FromArgb(26, 26, 46));
        using var brush = new SolidBrush(System.Drawing.Color.FromArgb(212, 175, 55));
        using var font = new Font("Segoe UI", 18, System.Drawing.FontStyle.Bold);
        g.DrawString("V", font, brush, 4, 2);
        var handle = bmp.GetHicon();
        return System.Drawing.Icon.FromHandle(handle);
    }

    private static System.Windows.Style CreateDarkMenuStyle()
    {
        var style = new System.Windows.Style(typeof(System.Windows.Controls.ContextMenu));
        style.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty,
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 37, 37))));
        style.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty,
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(224, 224, 224))));
        style.Setters.Add(new Setter(System.Windows.Controls.Control.BorderBrushProperty,
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 60, 60))));
        return style;
    }

    // MARK: - Conversation Reply

    private async void ActivateConversationReply()
    {
        var manager = ConversationReplyManager.Shared;

        // Debounce (500ms)
        var now = DateTime.UtcNow;
        if ((now - _lastConversationReplyTime).TotalMilliseconds < 500)
            return;
        _lastConversationReplyTime = now;

        // If already active: dismiss
        if (manager.IsActive)
        {
            manager.Dismiss();
            _conversationReplyWindow?.FadeOutAndHide();
            return;
        }

        // Pro/Trial gating
        var sub = SubscriptionManager.Shared;
        if (!sub.IsPro && !TrialManager.Shared.IsTrialActive())
        {
            MessageBox.Show(
                "Conversation Reply requires Pro or Trial.\nUpgrade to translate messages and reply in any language.",
                "VoxAiGo Pro Required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // Read selected text via clipboard (Ctrl+C)
        var selectedText = GetSelectedText();
        if (string.IsNullOrWhiteSpace(selectedText) || selectedText.Length < 2)
        {
            _viewModel!.StatusText = "Select a message first, then press Ctrl+Shift+R";
            return;
        }
        if (selectedText.Length > 5000)
        {
            _viewModel!.StatusText = "Selected text is too long (max 5000 chars)";
            return;
        }

        // Start translating
        SoundManager.Shared.PlayStart();
        manager.BeginTranslating();
        ShowConversationReplyHUD();

        var langCode = SettingsManager.Shared.SelectedLanguage;
        var targetLanguage = SpeechLanguage.All.FirstOrDefault(l => l.Code == langCode) ?? SpeechLanguage.English;

        try
        {
            string translation, fromName, fromCode;
            var settings = SettingsManager.Shared;

            if (settings.HasByokKey)
            {
                // BYOK: direct Gemini API call
                var byokService = new GeminiTranscriptionService(settings.ByokApiKey);
                (translation, fromName, fromCode) = await byokService.DetectAndTranslateAsync(selectedText, targetLanguage);
            }
            else if (_authService != null && _authService.IsLoggedIn)
            {
                // Pro/Trial: translate via Supabase edge function proxy
                var result = await TranslateViaSupabase(selectedText, targetLanguage);
                translation = result.Translation;
                fromName = result.FromName;
                fromCode = result.FromCode;
            }
            else
            {
                throw new Exception("Please sign in to use Conversation Reply.");
            }

            Dispatcher.Invoke(() =>
            {
                manager.ShowReady(selectedText, translation, fromName, fromCode, targetLanguage.FullName);
            });
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                System.Diagnostics.Debug.WriteLine($"Conversation Reply error: {ex.Message}");
                manager.Dismiss();
                _conversationReplyWindow?.FadeOutAndHide();
                _viewModel!.StatusText = $"Translation error: {ex.Message}";
            });
        }
    }

    private async Task<(string Translation, string FromName, string FromCode)> TranslateViaSupabase(
        string text, SpeechLanguage targetLanguage)
    {
        if (_authService == null || !_authService.IsLoggedIn)
            throw new Exception("Not authenticated.");

        var prompt = $$"""
            Translate the following text to {{targetLanguage.FullName}}.
            Detect the source language.
            Respond with valid JSON only — no markdown, no explanation:
            {"translation":"<translated text>","fromLanguageName":"<source language in English>","fromLanguageCode":"<ISO 639-1 code, e.g. ja>"}

            Text:
            {{text}}
            """;

        var payload = new
        {
            audio = "",
            mode = "translation",
            language = targetLanguage.Code,
            prompt,
            temperature = 0.1f,
            maxTokens = 2048,
            textOnly = true,
            textInput = text
        };

        using var httpClient = new HttpClient();
        var jsonBody = System.Text.Json.JsonSerializer.Serialize(payload);
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"{SettingsManager.SupabaseUrl}/functions/v1/transcribe");
        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        request.Headers.Add("apikey", SettingsManager.SupabaseAnonKey);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", _authService.AccessToken);

        var response = await httpClient.SendAsync(request);
        var responseText = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Translation failed: {response.StatusCode}");

        // Parse response — the edge function returns {text: "..."}
        using var doc = System.Text.Json.JsonDocument.Parse(responseText);
        var textResult = doc.RootElement.TryGetProperty("text", out var textEl)
            ? textEl.GetString() ?? ""
            : responseText;

        // Try to parse the JSON result from Gemini
        var cleanText = textResult.Trim();
        if (cleanText.StartsWith("```"))
            cleanText = cleanText.Replace("```json", "").Replace("```", "").Trim();

        using var parsed = System.Text.Json.JsonDocument.Parse(cleanText);
        var root = parsed.RootElement;
        return (
            root.GetProperty("translation").GetString() ?? "",
            root.GetProperty("fromLanguageName").GetString() ?? "",
            (root.GetProperty("fromLanguageCode").GetString() ?? "").ToUpperInvariant()
        );
    }

    private static string GetSelectedText()
    {
        // Save current clipboard content
        string? savedClipboard = null;
        try
        {
            if (Clipboard.ContainsText())
                savedClipboard = Clipboard.GetText();
        }
        catch { }

        // Clear clipboard
        try { Clipboard.Clear(); } catch { }

        // Simulate Ctrl+C to copy selected text
        Thread.Sleep(50);
        var inputs = new VoxAiGo.App.Native.NativeMethods.INPUT[4];

        // Ctrl Down
        inputs[0].type = VoxAiGo.App.Native.NativeMethods.INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = VoxAiGo.App.Native.NativeMethods.VK_CONTROL;
        inputs[0].u.ki.wScan = VoxAiGo.App.Native.NativeMethods.SCAN_LCTRL;
        inputs[0].u.ki.dwFlags = 0;

        // C Down
        inputs[1].type = VoxAiGo.App.Native.NativeMethods.INPUT_KEYBOARD;
        inputs[1].u.ki.wVk = 0x43; // VK_C
        inputs[1].u.ki.dwFlags = 0;

        // C Up
        inputs[2].type = VoxAiGo.App.Native.NativeMethods.INPUT_KEYBOARD;
        inputs[2].u.ki.wVk = 0x43;
        inputs[2].u.ki.dwFlags = VoxAiGo.App.Native.NativeMethods.KEYEVENTF_KEYUP;

        // Ctrl Up
        inputs[3].type = VoxAiGo.App.Native.NativeMethods.INPUT_KEYBOARD;
        inputs[3].u.ki.wVk = VoxAiGo.App.Native.NativeMethods.VK_CONTROL;
        inputs[3].u.ki.wScan = VoxAiGo.App.Native.NativeMethods.SCAN_LCTRL;
        inputs[3].u.ki.dwFlags = VoxAiGo.App.Native.NativeMethods.KEYEVENTF_KEYUP;

        VoxAiGo.App.Native.NativeMethods.SendInput(4, inputs,
            System.Runtime.InteropServices.Marshal.SizeOf(typeof(VoxAiGo.App.Native.NativeMethods.INPUT)));

        // Wait for clipboard to update
        Thread.Sleep(200);

        // Read copied text
        string selectedText = "";
        try
        {
            if (Clipboard.ContainsText())
                selectedText = Clipboard.GetText();
        }
        catch { }

        // Restore original clipboard content
        if (savedClipboard != null)
        {
            try { Clipboard.SetText(savedClipboard); } catch { }
        }

        return selectedText;
    }

    private void ShowConversationReplyHUD()
    {
        if (_viewModel == null) return;

        if (_conversationReplyWindow == null)
        {
            _conversationReplyWindow = new ConversationReplyWindow(_viewModel);
        }
        _conversationReplyWindow.ShowWithSlideIn();

        // Dismiss on timeout
        ConversationReplyManager.Shared.TimedOut += () =>
        {
            Dispatcher.Invoke(() =>
            {
                _conversationReplyWindow?.FadeOutAndHide();
            });
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.Dispose();
        _taskbarIcon?.Dispose();
        base.OnExit(e);
    }

    private static void LogError(Exception ex)
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VoxAiGo");
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, "error.log");
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n\n";
            File.AppendAllText(logPath, entry);
        }
        catch { /* never crash in error handler */ }
    }
}
