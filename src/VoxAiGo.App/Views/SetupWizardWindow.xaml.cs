using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VoxAiGo.Core.Managers;
using VoxAiGo.Core.Models;
using VoxAiGo.Core.Services;

namespace VoxAiGo.App.Views;

public partial class SetupWizardWindow : Window
{
    private readonly AuthService _auth;
    private int _currentStep;
    private const int TotalSteps = 7;

    // Mic test
    private WasapiCapture? _testCapture;
    private System.Timers.Timer? _micTestTimer;
    private bool _micTestPassed;
    private bool _isMicTesting;

    // Step state flags
    private bool _transcriptionReceived;
    private bool _modeSelected;
    private bool _wakeWordFired;
    private bool _languageSelected;

    // Step panels + left indicators (indexed 0-6)
    private Grid[] _panels = null!;
    private Border[] _stepDots = null!;
    private TextBlock[] _stepNums = null!;
    private TextBlock[] _stepLabels = null!;

    public SetupWizardWindow(AuthService auth)
    {
        _auth = auth;
        InitializeComponent();

        // Cache step panels
        _panels = [Panel0, Panel1, Panel2, Panel3, Panel4, Panel5, Panel6];
        _stepDots = [StepDot0, StepDot1, StepDot2, StepDot3, StepDot4, StepDot5, StepDot6];
        _stepNums = [StepNum0, StepNum1, StepNum2, StepNum3, StepNum4, StepNum5, StepNum6];
        _stepLabels = [StepLabel0, StepLabel1, StepLabel2, StepLabel3, StepLabel4, StepLabel5, StepLabel6];

        // Subscribe to auth changes (auto-advance on login)
        _auth.UserChanged += () => Dispatcher.Invoke(() =>
        {
            if (_auth.IsLoggedIn && _currentStep == 0)
            {
                AuthStatusText.Text = "✓ Signed in!";
                AuthStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0, 204, 136));
                _ = Task.Delay(800).ContinueWith(_ => Dispatcher.Invoke(() => AdvanceTo(1)));
            }
        });

        // Subscribe to WizardBus events
        WizardBus.TranscriptionCompleted += OnTranscriptionCompleted;
        WizardBus.ModeChanged += OnModeChanged;
        WizardBus.LanguageChanged += OnLanguageChanged;
        WizardBus.WakeWordFired += OnWakeWordFired;

        // Populate modes and languages grids
        PopulateModesGrid();
        PopulateLanguagesGrid();

        // Determine start step
        _currentStep = _auth.IsLoggedIn ? 1 : 0;
        ShowStep(_currentStep);
    }

    // ===== Navigation =====

    private void ShowStep(int step)
    {
        for (int i = 0; i < TotalSteps; i++)
            _panels[i].Visibility = i == step ? Visibility.Visible : Visibility.Collapsed;

        UpdateStepIndicators(step);
        UpdateFooterButtons(step);
    }

    private void UpdateStepIndicators(int currentStep)
    {
        for (int i = 0; i < TotalSteps; i++)
        {
            if (i < currentStep)
            {
                // Completed
                _stepDots[i].Background = new SolidColorBrush(Color.FromRgb(0, 170, 110));
                _stepNums[i].Text = "✓";
                _stepNums[i].Foreground = new SolidColorBrush(Colors.White);
                _stepLabels[i].Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80));
            }
            else if (i == currentStep)
            {
                // Active
                _stepDots[i].Background = new SolidColorBrush(Color.FromRgb(212, 175, 55));
                _stepNums[i].Text = (i + 1).ToString();
                _stepNums[i].Foreground = new SolidColorBrush(Color.FromRgb(10, 10, 10));
                _stepLabels[i].Foreground = new SolidColorBrush(Colors.White);
            }
            else
            {
                // Pending
                _stepDots[i].Background = new SolidColorBrush(Color.FromRgb(51, 51, 51));
                _stepNums[i].Text = (i + 1).ToString();
                _stepNums[i].Foreground = new SolidColorBrush(Color.FromRgb(136, 136, 136));
                _stepLabels[i].Foreground = new SolidColorBrush(Color.FromRgb(85, 85, 85));
            }
        }
    }

    private void UpdateFooterButtons(int step)
    {
        BackBtn.Visibility = step > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Wake word and Recording steps have a Skip button
        SkipBtn.Visibility = (step == 2 || step == 4) ? Visibility.Visible : Visibility.Collapsed;

        if (step == TotalSteps - 1)
        {
            NextBtn.Content = "Finish ✓";
            NextBtn.Width = 120;
        }
        else
        {
            NextBtn.Content = "Next →";
            NextBtn.Width = 110;
        }
    }

    private void AdvanceTo(int step)
    {
        StopMicTest();
        if (step >= TotalSteps)
        {
            FinishWizard();
            return;
        }
        _currentStep = step;
        ShowStep(step);
    }

    private void FinishWizard()
    {
        SettingsManager.Shared.HasCompletedSetup = true;
        WizardBus.TranscriptionCompleted -= OnTranscriptionCompleted;
        WizardBus.ModeChanged -= OnModeChanged;
        WizardBus.LanguageChanged -= OnLanguageChanged;
        WizardBus.WakeWordFired -= OnWakeWordFired;
        StopMicTest();
        Close();
    }

    // ===== Footer Button Handlers =====

    private void NextBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep == TotalSteps - 1)
        {
            FinishWizard();
            return;
        }

        // Step-specific validation before advancing
        if (_currentStep == 0 && !_auth.IsLoggedIn)
        {
            // User chose to skip auth — that's OK (SkipAuthBtn handles it; NextBtn on step 0 tries sign-in)
            AuthStatusText.Text = "Sign in or click 'Continue without account' to proceed.";
            AuthStatusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 102, 102));
            return;
        }

        AdvanceTo(_currentStep + 1);
    }

    private void BackBtn_Click(object sender, RoutedEventArgs e)
    {
        AdvanceTo(_currentStep - 1);
    }

    private void SkipBtn_Click(object sender, RoutedEventArgs e)
    {
        AdvanceTo(_currentStep + 1);
    }

    // ===== Step 0: Auth =====

    private async void GoogleBtn_Click(object sender, RoutedEventArgs e)
    {
        GoogleBtn.IsEnabled = false;
        AuthStatusText.Text = "Opening browser for Google sign-in...";
        AuthStatusText.Foreground = new SolidColorBrush(Color.FromRgb(212, 175, 55));
        _auth.SignInWithGoogle();
        // Auth result comes via UserChanged event
        await Task.Delay(2000);
        GoogleBtn.IsEnabled = true;
    }

    private async void SignInBtn_Click(object sender, RoutedEventArgs e)
    {
        var email = EmailBox.Text.Trim();
        var password = PwdBox.Password;
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password)) return;

        SignInBtn.IsEnabled = false;
        AuthStatusText.Text = "Signing in...";
        AuthStatusText.Foreground = new SolidColorBrush(Color.FromRgb(212, 175, 55));

        var ok = await _auth.SignInAsync(email, password);

        if (ok)
        {
            // UserChanged will fire and auto-advance
        }
        else
        {
            AuthStatusText.Text = "Invalid email or password.";
            AuthStatusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 102, 102));
        }
        SignInBtn.IsEnabled = true;
    }

    private void SkipAuthBtn_Click(object sender, RoutedEventArgs e)
    {
        AdvanceTo(1);
    }

    // ===== Step 1: Mic Test =====

    private void MicTestBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isMicTesting)
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
            try { device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console); }
            catch
            {
                MicStatusText.Text = "No microphone found! Check your audio settings.";
                MicStatusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 102, 102));
                return;
            }

            MicStatusText.Text = "Listening... speak now!";
            MicStatusText.Foreground = new SolidColorBrush(Color.FromRgb(212, 175, 55));
            MicLevelBar.Value = 0;
            MicTestBtn.Content = "Stop Test";
            _isMicTesting = true;
            _micTestPassed = false;
            MicPassedText.Visibility = Visibility.Collapsed;

            _testCapture = new WasapiCapture(device);
            bool speechDetected = false;

            _testCapture.DataAvailable += (s, ev) =>
            {
                float sum = 0;
                int samples = 0;
                if (_testCapture.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
                {
                    for (int i = 0; i < ev.BytesRecorded; i += 4)
                    {
                        float sample = BitConverter.ToSingle(ev.Buffer, i);
                        sum += sample * sample;
                        samples++;
                    }
                }
                else
                {
                    for (int i = 0; i < ev.BytesRecorded; i += 2)
                    {
                        short sample = BitConverter.ToInt16(ev.Buffer, i);
                        float normalized = sample / 32768f;
                        sum += normalized * normalized;
                        samples++;
                    }
                }
                if (samples > 0)
                {
                    float rms = MathF.Sqrt(sum / samples);
                    double level = Math.Min(rms * 5, 1.0);
                    var detected = rms > 0.02f && !speechDetected;
                    if (detected) speechDetected = true;

                    Dispatcher.Invoke(() =>
                    {
                        MicLevelBar.Value = MicLevelBar.Value * 0.5 + level * 0.5;
                        if (detected)
                        {
                            MicStatusText.Text = "Microphone working! Speech detected.";
                            MicStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0, 204, 136));
                            MicPassedText.Visibility = Visibility.Visible;
                            _micTestPassed = true;
                        }
                    });
                }
            };

            _testCapture.RecordingStopped += (s, ev) =>
            {
                _testCapture?.Dispose();
                _testCapture = null;
            };

            _testCapture.StartRecording();

            // Auto-stop after 5 seconds
            _micTestTimer = new System.Timers.Timer(5000);
            _micTestTimer.Elapsed += (s, ev) =>
            {
                _micTestTimer?.Stop();
                _micTestTimer?.Dispose();
                _micTestTimer = null;
                Dispatcher.Invoke(() =>
                {
                    if (_isMicTesting && !_micTestPassed)
                    {
                        MicStatusText.Text = "No speech detected. Check mic permissions and input device.";
                        MicStatusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 102, 102));
                    }
                    StopMicTest();
                });
            };
            _micTestTimer.AutoReset = false;
            _micTestTimer.Start();
        }
        catch (Exception ex)
        {
            MicStatusText.Text = $"Mic error: {ex.Message}";
            MicStatusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 102, 102));
            _isMicTesting = false;
        }
    }

    private void StopMicTest()
    {
        _testCapture?.StopRecording();
        _micTestTimer?.Stop();
        _micTestTimer?.Dispose();
        _micTestTimer = null;
        _isMicTesting = false;
        Dispatcher.Invoke(() =>
        {
            MicLevelBar.Value = 0;
            MicTestBtn.Content = "Test Microphone";
        });
    }

    // ===== Step 2: Recording — WizardBus listener =====

    private void OnTranscriptionCompleted(string text)
    {
        if (_currentStep != 2 || _transcriptionReceived) return;
        _transcriptionReceived = true;

        Dispatcher.Invoke(() =>
        {
            RecordingWaitText.Foreground = new SolidColorBrush(Color.FromRgb(0, 204, 136));
            RecordingWaitText.Text = "✓ Transcription received!";
            RecordingResultText.Text = text.Length > 120 ? text[..120] + "…" : text;
            RecordingSuccessBorder.Visibility = Visibility.Visible;

            // Auto-advance after 1.5s
            _ = Task.Delay(1500).ContinueWith(_ => Dispatcher.Invoke(() =>
            {
                if (_currentStep == 2) AdvanceTo(3);
            }));
        });
    }

    // ===== Step 3: Modes — grid populated + WizardBus =====

    private void PopulateModesGrid()
    {
        var modes = Enum.GetValues<TranscriptionMode>();
        foreach (var mode in modes)
        {
            var btn = new Button
            {
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = mode.GetIconGlyph(),
                            FontFamily = new FontFamily("Segoe MDL2 Assets"),
                            FontSize = 18,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Margin = new Thickness(0, 0, 0, 4)
                        },
                        new TextBlock
                        {
                            Text = mode.ToString(),
                            FontSize = 11,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            TextWrapping = TextWrapping.Wrap,
                            TextAlignment = TextAlignment.Center
                        }
                    }
                },
                Margin = new Thickness(4),
                Height = 72,
                Background = new SolidColorBrush(Color.FromRgb(20, 20, 20)),
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Tag = mode
            };

            btn.Click += (s, e) =>
            {
                if (s is Button b && b.Tag is TranscriptionMode m)
                    SelectMode(m);
            };

            btn.MouseEnter += (s, e) =>
            {
                if (s is Button b) b.Background = new SolidColorBrush(Color.FromRgb(35, 35, 20));
            };
            btn.MouseLeave += (s, e) =>
            {
                if (s is Button b) b.Background = new SolidColorBrush(Color.FromRgb(20, 20, 20));
            };

            ModesGrid.Children.Add(btn);
        }
    }

    private void SelectMode(TranscriptionMode mode)
    {
        if (_currentStep != 3 || _modeSelected) return;
        _modeSelected = true;

        SettingsManager.Shared.Set(SettingsManager.Keys.SelectedMode, mode.ToString());
        WizardBus.FireModeChanged(mode.ToString());

        // Highlight selected button
        foreach (Button btn in ModesGrid.Children)
        {
            if (btn.Tag is TranscriptionMode m && m == mode)
                btn.Background = new SolidColorBrush(Color.FromRgb(50, 40, 10));
        }

        _ = Task.Delay(800).ContinueWith(_ => Dispatcher.Invoke(() =>
        {
            if (_currentStep == 3) AdvanceTo(4);
        }));
    }

    private void OnModeChanged(string modeName)
    {
        // Fired from MainWindow sidebar — accept it as mode selection in step 3
        if (_currentStep != 3 || _modeSelected) return;
        if (Enum.TryParse<TranscriptionMode>(modeName, out var mode))
            Dispatcher.Invoke(() => SelectMode(mode));
    }

    // ===== Step 4: Wake Word =====

    private void OnWakeWordFired()
    {
        if (_currentStep != 4 || _wakeWordFired) return;
        _wakeWordFired = true;

        Dispatcher.Invoke(() =>
        {
            WakeWordSuccessText.Visibility = Visibility.Visible;
            WakeWordStatusText.Text = "Wake word command detected!";
            WakeWordStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0, 204, 136));
            _ = Task.Delay(1200).ContinueWith(_ => Dispatcher.Invoke(() =>
            {
                if (_currentStep == 4) AdvanceTo(5);
            }));
        });
    }

    // ===== Step 5: Language =====

    private static readonly (string Code, string Flag, string Name)[] QuickLanguages =
    [
        ("pt", "🇧🇷", "Português"),
        ("en", "🇺🇸", "English"),
        ("es", "🇪🇸", "Español"),
        ("fr", "🇫🇷", "Français"),
        ("de", "🇩🇪", "Deutsch"),
        ("it", "🇮🇹", "Italiano"),
        ("ja", "🇯🇵", "日本語"),
        ("zh", "🇨🇳", "中文"),
    ];

    private void PopulateLanguagesGrid()
    {
        foreach (var (code, flag, name) in QuickLanguages)
        {
            var btn = new Button
            {
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = flag,
                            FontSize = 24,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Margin = new Thickness(0, 0, 0, 4)
                        },
                        new TextBlock
                        {
                            Text = name,
                            FontSize = 11,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            TextWrapping = TextWrapping.Wrap,
                            TextAlignment = TextAlignment.Center
                        }
                    }
                },
                Margin = new Thickness(4),
                Height = 72,
                Background = new SolidColorBrush(Color.FromRgb(20, 20, 20)),
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Tag = code
            };

            btn.Click += (s, e) =>
            {
                if (s is Button b && b.Tag is string lc)
                    SelectLanguage(lc);
            };

            btn.MouseEnter += (s, e) =>
            {
                if (s is Button b) b.Background = new SolidColorBrush(Color.FromRgb(35, 35, 20));
            };
            btn.MouseLeave += (s, e) =>
            {
                if (s is Button b) b.Background = new SolidColorBrush(Color.FromRgb(20, 20, 20));
            };

            LanguagesGrid.Children.Add(btn);
        }
    }

    private void SelectLanguage(string code)
    {
        if (_currentStep != 5 || _languageSelected) return;
        _languageSelected = true;

        SettingsManager.Shared.Set(SettingsManager.Keys.SelectedLanguage, code);
        WizardBus.FireLanguageChanged(code);

        // Highlight selected button
        foreach (Button btn in LanguagesGrid.Children)
        {
            if (btn.Tag is string c && c == code)
                btn.Background = new SolidColorBrush(Color.FromRgb(50, 40, 10));
        }

        _ = Task.Delay(800).ContinueWith(_ => Dispatcher.Invoke(() =>
        {
            if (_currentStep == 5) AdvanceTo(6);
        }));
    }

    private void OnLanguageChanged(string code)
    {
        // Fired from MainWindow — accept as language selection in step 5
        if (_currentStep != 5 || _languageSelected) return;
        Dispatcher.Invoke(() => SelectLanguage(code));
    }

    // ===== Cleanup =====

    protected override void OnClosed(System.EventArgs e)
    {
        WizardBus.TranscriptionCompleted -= OnTranscriptionCompleted;
        WizardBus.ModeChanged -= OnModeChanged;
        WizardBus.LanguageChanged -= OnLanguageChanged;
        WizardBus.WakeWordFired -= OnWakeWordFired;
        StopMicTest();
        base.OnClosed(e);
    }
}
