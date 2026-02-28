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
                            PositionOverlay();
                            Show();
                            FadeIn();
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

                    case nameof(MainViewModel.IsRecording):
                    case nameof(MainViewModel.IsProcessing):
                        UpdateHudState();
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
        var anim = new DoubleAnimation(0, 1, FastDuration) { EasingFunction = SmoothEase };
        BeginAnimation(OpacityProperty, anim);
    }

    private void FadeOutAndHide()
    {
        var anim = new DoubleAnimation(1, 0, FastDuration) { EasingFunction = SmoothEase };
        anim.Completed += (s, e) =>
        {
            Hide();
            ShowIdleState();
        };
        BeginAnimation(OpacityProperty, anim);
    }

    private void AnimateWidth(double targetWidth)
    {
        var anim = new DoubleAnimation(Width, targetWidth, MediumDuration) { EasingFunction = SmoothEase };
        anim.Completed += (s, e) => PositionOverlay();
        BeginAnimation(WidthProperty, anim);
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
        ErrorState.Visibility = Visibility.Collapsed;
        StopSparkleAnimation();
        AnimateWidth(300);
    }

    private void ShowListeningState()
    {
        IdleState.Visibility = Visibility.Collapsed;
        ListeningState.Visibility = Visibility.Visible;
        ProcessingState.Visibility = Visibility.Collapsed;
        SuccessState.Visibility = Visibility.Collapsed;
        ErrorState.Visibility = Visibility.Collapsed;
        StopSparkleAnimation();
        UpdateGoldBorder(true);
        AnimateWidth(460);
    }

    private void ShowProcessingState()
    {
        IdleState.Visibility = Visibility.Collapsed;
        ListeningState.Visibility = Visibility.Collapsed;
        ProcessingState.Visibility = Visibility.Visible;
        SuccessState.Visibility = Visibility.Collapsed;
        ErrorState.Visibility = Visibility.Collapsed;
        StartSparkleAnimation();
        AnimateWidth(240);
    }

    private void ShowSuccessState()
    {
        IdleState.Visibility = Visibility.Collapsed;
        ListeningState.Visibility = Visibility.Collapsed;
        ProcessingState.Visibility = Visibility.Collapsed;
        SuccessState.Visibility = Visibility.Visible;
        ErrorState.Visibility = Visibility.Collapsed;
        StopSparkleAnimation();
        UpdateGoldBorder(false);
        AnimateWidth(200);

        // Auto-hide with fade after 2 seconds
        _successTimer?.Stop();
        _successTimer = new System.Timers.Timer(2000);
        _successTimer.Elapsed += (s, e) =>
        {
            _successTimer?.Stop();
            Dispatcher.Invoke(FadeOutAndHide);
        };
        _successTimer.Start();
    }

    private void ShowErrorState(string message)
    {
        IdleState.Visibility = Visibility.Collapsed;
        ListeningState.Visibility = Visibility.Collapsed;
        ProcessingState.Visibility = Visibility.Collapsed;
        SuccessState.Visibility = Visibility.Collapsed;
        ErrorState.Visibility = Visibility.Visible;
        ErrorText.Text = message.Replace("Error: ", "");
        StopSparkleAnimation();
        UpdateGoldBorder(false);
        AnimateWidth(400);

        // Auto-hide with fade after 3 seconds
        _successTimer?.Stop();
        _successTimer = new System.Timers.Timer(3000);
        _successTimer.Elapsed += (s, e) =>
        {
            _successTimer?.Stop();
            Dispatcher.Invoke(FadeOutAndHide);
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
                Color.FromArgb(102, 212, 175, 55),  // gold @ 0.4
                Color.FromArgb(26, 212, 175, 55),   // gold @ 0.1
                45);
            HudCapsule.BorderBrush = brush;
            HudCapsule.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(212, 175, 55),
                BlurRadius = 12,
                ShadowDepth = 0,
                Opacity = 0.2
            };
        }
        else
        {
            HudCapsule.BorderBrush = new LinearGradientBrush(
                Color.FromArgb(38, 255, 255, 255),
                Color.FromArgb(5, 255, 255, 255),
                45);
            HudCapsule.Effect = null;
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
