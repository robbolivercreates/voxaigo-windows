using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using VoxAiGo.App.ViewModels;

namespace VoxAiGo.App.Views;

public partial class OverlayWindow : Window
{
    private readonly MainViewModel _viewModel;
    private Storyboard? _sparkleStoryboard;
    private System.Timers.Timer? _successTimer;
    private bool _isShowingNotification; // Prevents FadeOut from hiding an incoming notification
    private static readonly Duration FastDuration = new(TimeSpan.FromMilliseconds(200));
    private static readonly Duration MediumDuration = new(TimeSpan.FromMilliseconds(300));
    private static readonly IEasingFunction SmoothEase = new CubicEase { EasingMode = EasingMode.EaseInOut };

    public OverlayWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        Loaded += (s, e) => PositionOverlay();

        // Listen to state changes
        _viewModel.PropertyChanged += (s, e) =>
        {
            Dispatcher.Invoke(() =>
            {
                switch (e.PropertyName)
                {
                    case nameof(MainViewModel.IsOverlayVisible):
                        if (_viewModel.IsOverlayVisible)
                        {
                            // Cancel any pending auto-hide timer and fade-out animation
                            // from a previous success/notification/error state
                            _successTimer?.Stop();
                            _isShowingNotification = false;
                            BeginAnimation(OpacityProperty, null); // Cancel fade-out
                            Opacity = 1;

                            if (Visibility != Visibility.Visible)
                            {
                                PositionOverlay();
                                Show();
                                FadeIn();
                            }
                            UpdateHudState();
                        }
                        else
                        {
                            // Show success briefly before hiding
                            if (!_viewModel.IsRecording && !_viewModel.IsProcessing &&
                                _viewModel.StatusText.Contains("Transcribed"))
                            {
                                ShowSuccessState();
                            }
                            else if (_viewModel.StatusText.StartsWith("Error"))
                            {
                                ShowErrorState(_viewModel.StatusText);
                            }
                            else
                            {
                                FadeOutAndHide();
                            }
                        }
                        break;

                    case nameof(MainViewModel.StatusText):
                        if (!_viewModel.IsOverlayVisible && !_viewModel.IsRecording && !_viewModel.IsProcessing)
                        {
                            if (_viewModel.StatusText.StartsWith("Language switched to:") || 
                                _viewModel.StatusText.StartsWith("Language:"))
                            {
                                var text = _viewModel.StatusText.Replace("Language switched to: ", "").Replace("Language: ", "");
                                ShowNotificationState("\uE774", text); // Globe icon
                            }
                            else if (_viewModel.StatusText.StartsWith("Mode switched to:"))
                            {
                                var text = _viewModel.StatusText.Replace("Mode switched to: ", "");
                                ShowNotificationState("\uE713", text); // Settings icon as generic mode
                            }
                            else if (_viewModel.StatusText.StartsWith("Error"))
                            {
                                ShowErrorState(_viewModel.StatusText);
                            }
                        }
                        break;

                    case nameof(MainViewModel.IsRecording):
                    case nameof(MainViewModel.IsProcessing):
                        if (_viewModel.IsOverlayVisible) UpdateHudState();
                        break;

                    case nameof(MainViewModel.AudioLevel):
                        UpdateMicVisual();
                        break;
                }
            });
        };

        Opacity = 0;
        Visibility = Visibility.Collapsed;
        CreateSparkleAnimation();
    }

    private void PositionOverlay()
    {
        var screen = SystemParameters.WorkArea;
        Left = (screen.Width - Width) / 2;
        // 25% from bottom of screen (per UI guide)
        Top = screen.Height - (screen.Height * 0.25) - (Height / 2);
    }

    private void FadeIn()
    {
        _isShowingNotification = false;
        var opacityAnim = new DoubleAnimation(0, 1, FastDuration) { EasingFunction = SmoothEase };

        // Animate slide-up on the HudCapsule (Window doesn't support RenderTransform)
        var transform = new TranslateTransform(0, 20);
        HudCapsule.RenderTransform = transform;
        var translateAnim = new DoubleAnimation(20, 0, FastDuration) { EasingFunction = SmoothEase };

        BeginAnimation(OpacityProperty, opacityAnim);
        transform.BeginAnimation(TranslateTransform.YProperty, translateAnim);
    }

    private void FadeOutAndHide()
    {
        var opacityAnim = new DoubleAnimation(1, 0, FastDuration) { EasingFunction = SmoothEase };

        // Animate slide-down on the HudCapsule (Window doesn't support RenderTransform)
        var transform = HudCapsule.RenderTransform as TranslateTransform ?? new TranslateTransform(0, 0);
        HudCapsule.RenderTransform = transform;
        var translateAnim = new DoubleAnimation(0, 20, FastDuration) { EasingFunction = SmoothEase };

        opacityAnim.Completed += (s, e) =>
        {
            // Don't hide if a notification was shown while we were fading out
            if (_isShowingNotification)
                return;
            Hide();
            ShowIdleState();
        };

        BeginAnimation(OpacityProperty, opacityAnim);
        transform.BeginAnimation(TranslateTransform.YProperty, translateAnim);
    }

    private void AnimateWidth(double targetWidth)
    {
        if (double.IsNaN(Width)) Width = ActualWidth;
        if (double.IsNaN(Left)) PositionOverlay();

        var widthAnim = new DoubleAnimation(Width, targetWidth, MediumDuration) { EasingFunction = SmoothEase };
        
        var screen = SystemParameters.WorkArea;
        double targetLeft = (screen.Width - targetWidth) / 2;
        var leftAnim = new DoubleAnimation(Left, targetLeft, MediumDuration) { EasingFunction = SmoothEase };
        
        widthAnim.Completed += (s, e) => PositionOverlay();
        
        BeginAnimation(WidthProperty, widthAnim);
        BeginAnimation(LeftProperty, leftAnim);
    }

    private void UpdateHudState()
    {
        if (_viewModel.IsProcessing)
        {
            ShowProcessingState();
        }
        else if (_viewModel.IsRecording)
        {
            ShowListeningState();
        }
        else
        {
            ShowIdleState();
        }
    }

    private void ShowIdleState()
    {
        IdleState.Visibility = Visibility.Visible;
        ListeningState.Visibility = Visibility.Collapsed;
        ProcessingState.Visibility = Visibility.Collapsed;
        SuccessState.Visibility = Visibility.Collapsed;
        NotificationState.Visibility = Visibility.Collapsed;
        ErrorState.Visibility = Visibility.Collapsed;
        StopSparkleAnimation();
        UpdateGoldBorder(false);
        IdleHalo.Visibility = _viewModel.IsVoxActive ? Visibility.Visible : Visibility.Collapsed;
        AnimateWidth(340);
    }

    private void ShowListeningState()
    {
        IdleState.Visibility = Visibility.Collapsed;
        ListeningState.Visibility = Visibility.Visible;
        ProcessingState.Visibility = Visibility.Collapsed;
        SuccessState.Visibility = Visibility.Collapsed;
        NotificationState.Visibility = Visibility.Collapsed;
        ErrorState.Visibility = Visibility.Collapsed;
        StopSparkleAnimation();
        
        bool isVoxActive = _viewModel.IsVoxActive;
        ListeningText.Text = isVoxActive ? "Vox ouvindo..." : "Ouvindo...";
        ListeningText.Foreground = isVoxActive ? new SolidColorBrush(Color.FromRgb(212, 175, 55)) : Brushes.White;
        UpdateGoldBorder(isVoxActive);
        
        AnimateWidth(isVoxActive ? 520 : 460);
    }

    private void ShowProcessingState()
    {
        IdleState.Visibility = Visibility.Collapsed;
        ListeningState.Visibility = Visibility.Collapsed;
        ProcessingState.Visibility = Visibility.Visible;
        SuccessState.Visibility = Visibility.Collapsed;
        NotificationState.Visibility = Visibility.Collapsed;
        ErrorState.Visibility = Visibility.Collapsed;
        StartSparkleAnimation();
        
        bool isVoxActive = _viewModel.IsVoxActive;
        ProcessingText.Text = isVoxActive ? "Vox processando..." : "Processando...";
        UpdateGoldBorder(isVoxActive);
        
        AnimateWidth(isVoxActive ? 300 : 260);
    }

    private void ShowSuccessState()
    {
        IdleState.Visibility = Visibility.Collapsed;
        ListeningState.Visibility = Visibility.Collapsed;
        ProcessingState.Visibility = Visibility.Collapsed;
        SuccessState.Visibility = Visibility.Visible;
        NotificationState.Visibility = Visibility.Collapsed;
        ErrorState.Visibility = Visibility.Collapsed;
        StopSparkleAnimation();
        UpdateGoldBorder(false);
        AnimateWidth(240);

        // Auto-hide with fade after 2 seconds
        AutoHideTimer(2000);
    }

    private void ShowNotificationState(string icon, string text)
    {
        // Cancel any pending fade-out that might hide this notification
        _isShowingNotification = true;
        BeginAnimation(OpacityProperty, null); // Stop fade-out animation
        Opacity = 1;

        if (Visibility != Visibility.Visible)
        {
            PositionOverlay();
            Show();
        }

        IdleState.Visibility = Visibility.Collapsed;
        ListeningState.Visibility = Visibility.Collapsed;
        ProcessingState.Visibility = Visibility.Collapsed;
        SuccessState.Visibility = Visibility.Collapsed;
        NotificationState.Visibility = Visibility.Visible;
        ErrorState.Visibility = Visibility.Collapsed;
        
        NotificationIcon.Text = icon;
        NotificationText.Text = text;
        
        StopSparkleAnimation();
        UpdateGoldBorder(true);
        AnimateWidth(Math.Max(300, 100 + (text.Length * 8)));

        AutoHideTimer(2500);
    }

    private void ShowErrorState(string message)
    {
        // Cancel any pending fade-out
        _isShowingNotification = true;
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;

        if (Visibility != Visibility.Visible)
        {
            PositionOverlay();
            Show();
        }

        IdleState.Visibility = Visibility.Collapsed;
        ListeningState.Visibility = Visibility.Collapsed;
        ProcessingState.Visibility = Visibility.Collapsed;
        SuccessState.Visibility = Visibility.Collapsed;
        NotificationState.Visibility = Visibility.Collapsed;
        ErrorState.Visibility = Visibility.Visible;
        ErrorText.Text = message.Replace("Error: ", "");
        StopSparkleAnimation();
        UpdateGoldBorder(false);
        AnimateWidth(440);

        AutoHideTimer(3000);
    }

    private void AutoHideTimer(double milliseconds)
    {
        _successTimer?.Stop();
        _successTimer = new System.Timers.Timer(milliseconds);
        _successTimer.Elapsed += (s, e) =>
        {
            _successTimer?.Stop();
            Dispatcher.Invoke(() =>
            {
                _isShowingNotification = false;
                FadeOutAndHide();
            });
        };
        _successTimer.Start();
    }

    private void UpdateMicVisual()
    {
        if (!_viewModel.IsRecording) return;

        // Red pulse when speech detected (audioLevel > 0.05)
        if (_viewModel.AudioLevel > 0.05)
        {
            MicCircle.Background = new SolidColorBrush(Color.FromRgb(242, 64, 64)); // speechActive red
        }
        else
        {
            MicCircle.Background = Brushes.White;
        }
    }

    private void UpdateGoldBorder(bool isGold)
    {
        if (isGold)
        {
            var brush = new LinearGradientBrush(
                Color.FromArgb(255, 212, 175, 55),  // solid gold
                Color.FromArgb(255, 232, 212, 139), // light gold
                45);
            HudCapsule.BorderBrush = brush;
            HudCapsule.Background = new SolidColorBrush(Color.FromRgb(31, 31, 31)); // slightly lighter surface
            HudCapsule.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(212, 175, 55),
                BlurRadius = 24,
                ShadowDepth = 0,
                Opacity = 0.3
            };
        }
        else
        {
            HudCapsule.BorderBrush = new SolidColorBrush(Color.FromRgb(42, 42, 42)); // subtle border
            HudCapsule.Background = new SolidColorBrush(Color.FromRgb(26, 26, 26)); // default surface
            HudCapsule.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 20,
                ShadowDepth = 4,
                Opacity = 0.6
            };
        }
    }

    private void CreateSparkleAnimation()
    {
        var animation = new DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = TimeSpan.FromSeconds(2),
            RepeatBehavior = RepeatBehavior.Forever
        };

        _sparkleStoryboard = new Storyboard();
        _sparkleStoryboard.Children.Add(animation);
        Storyboard.SetTarget(animation, SparkleIcon);
        Storyboard.SetTargetProperty(animation, new PropertyPath("(UIElement.RenderTransform).(RotateTransform.Angle)"));
    }

    private void StartSparkleAnimation()
    {
        _sparkleStoryboard?.Begin(this, true);
    }

    private void StopSparkleAnimation()
    {
        _sparkleStoryboard?.Stop(this);
    }
}
