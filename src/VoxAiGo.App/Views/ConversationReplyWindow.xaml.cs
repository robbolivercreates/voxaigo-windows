using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using VoxAiGo.App.ViewModels;
using VoxAiGo.Core.Managers;

namespace VoxAiGo.App.Views;

public partial class ConversationReplyWindow : Window
{
    private readonly ConversationReplyManager _manager = ConversationReplyManager.Shared;
    private readonly MainViewModel _viewModel;
    private Storyboard? _sparkleStoryboard;
    private static readonly Duration FastDuration = new(TimeSpan.FromMilliseconds(200));
    private static readonly IEasingFunction SmoothEase = new CubicEase { EasingMode = EasingMode.EaseInOut };

    public ConversationReplyWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;

        Loaded += (s, e) => PositionOverlay();

        // Listen to manager state changes
        _manager.PropertyChanged += (s, e) =>
        {
            Dispatcher.Invoke(() =>
            {
                switch (e.PropertyName)
                {
                    case nameof(ConversationReplyManager.State):
                        UpdateVisualState();
                        break;
                    case nameof(ConversationReplyManager.TimeoutProgress):
                        UpdateCountdownBar();
                        break;
                }
            });
        };

        // Listen to audio level for recording waves
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.AudioLevel))
            {
                Dispatcher.Invoke(() =>
                {
                    RecordingWaves.AudioLevel = _viewModel.AudioLevel;
                });
            }
        };

        // Dismiss on timeout
        _manager.TimedOut += () =>
        {
            Dispatcher.Invoke(FadeOutAndHide);
        };

        Opacity = 0;
        CreateSparkleAnimation();
    }

    private void PositionOverlay()
    {
        var screen = SystemParameters.WorkArea;
        Left = (screen.Width - Width) / 2;
        Top = screen.Height - (screen.Height * 0.25) - (Height / 2);
    }

    public void ShowWithSlideIn()
    {
        PositionOverlay();
        Show();

        // Slide in from below + fade in
        var fadeAnim = new DoubleAnimation(0, 1, FastDuration) { EasingFunction = SmoothEase };
        BeginAnimation(OpacityProperty, fadeAnim);

        UpdateVisualState();
    }

    public void FadeOutAndHide()
    {
        var anim = new DoubleAnimation(1, 0, FastDuration) { EasingFunction = SmoothEase };
        anim.Completed += (s, e) =>
        {
            Hide();
        };
        BeginAnimation(OpacityProperty, anim);
    }

    private void UpdateVisualState()
    {
        TranslatingState.Visibility = Visibility.Collapsed;
        ReadyState.Visibility = Visibility.Collapsed;
        RecordingState.Visibility = Visibility.Collapsed;
        ProcessingState.Visibility = Visibility.Collapsed;

        switch (_manager.State)
        {
            case ConversationReplyManager.ReplyState.Translating:
                TranslatingState.Visibility = Visibility.Visible;
                StopSparkleAnimation();
                AnimateSize(460, 72);
                UpdateBorder(BorderStyle.Default);
                break;

            case ConversationReplyManager.ReplyState.Ready:
                ReadyState.Visibility = Visibility.Visible;
                FromCodeText.Text = _manager.FromLanguageCode;
                FromNameText.Text = _manager.FromLanguageName;
                ToNameText.Text = _manager.ToLanguageName;
                TranslationText.Text = _manager.Translation;
                ReplyCtaText.Text = $"to reply in {_manager.FromLanguageName}";
                StopSparkleAnimation();
                AnimateSize(460, 200);
                UpdateBorder(BorderStyle.Default);
                break;

            case ConversationReplyManager.ReplyState.Recording:
                RecordingState.Visibility = Visibility.Visible;
                RecordingLanguageText.Text = $"Replying in {_manager.FromLanguageName}...";
                StopSparkleAnimation();
                AnimateSize(460, 80);
                UpdateBorder(BorderStyle.Recording);
                break;

            case ConversationReplyManager.ReplyState.Processing:
                ProcessingState.Visibility = Visibility.Visible;
                StartSparkleAnimation();
                AnimateSize(460, 72);
                UpdateBorder(BorderStyle.Processing);
                break;

            case ConversationReplyManager.ReplyState.Idle:
                StopSparkleAnimation();
                break;
        }
    }

    private void UpdateCountdownBar()
    {
        if (_manager.State != ConversationReplyManager.ReplyState.Ready) return;

        var totalWidth = ReadyState.ActualWidth > 0 ? ReadyState.ActualWidth : 460;
        CountdownBar.Width = totalWidth * _manager.TimeoutProgress;
    }

    private enum BorderStyle { Default, Recording, Processing }

    private void UpdateBorder(BorderStyle style)
    {
        switch (style)
        {
            case BorderStyle.Recording:
                MainCard.BorderBrush = new LinearGradientBrush(
                    Color.FromArgb(89, 255, 255, 255),
                    Color.FromArgb(128, 242, 64, 64), 45);
                MainCard.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Color.FromRgb(242, 64, 64),
                    BlurRadius = 14,
                    ShadowDepth = 0,
                    Opacity = 0.3
                };
                break;

            case BorderStyle.Processing:
                MainCard.BorderBrush = new LinearGradientBrush(
                    Color.FromArgb(89, 255, 255, 255),
                    Color.FromArgb(128, 212, 175, 55), 45);
                MainCard.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Color.FromRgb(212, 175, 55),
                    BlurRadius = 14,
                    ShadowDepth = 0,
                    Opacity = 0.25
                };
                break;

            default:
                MainCard.BorderBrush = new LinearGradientBrush(
                    Color.FromArgb(89, 255, 255, 255),
                    Color.FromArgb(38, 255, 255, 255), 45);
                MainCard.Effect = null;
                break;
        }
    }

    private void AnimateSize(double targetWidth, double targetHeight)
    {
        var widthAnim = new DoubleAnimation(Width, targetWidth, FastDuration) { EasingFunction = SmoothEase };
        var heightAnim = new DoubleAnimation(Height, targetHeight, FastDuration) { EasingFunction = SmoothEase };
        heightAnim.Completed += (s, e) => PositionOverlay();
        BeginAnimation(WidthProperty, widthAnim);
        BeginAnimation(HeightProperty, heightAnim);
    }

    private void CreateSparkleAnimation()
    {
        var animation = new DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = TimeSpan.FromSeconds(2.5),
            RepeatBehavior = RepeatBehavior.Forever
        };

        _sparkleStoryboard = new Storyboard();
        _sparkleStoryboard.Children.Add(animation);
        Storyboard.SetTarget(animation, ProcessingSparkle);
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

    private void DismissButton_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _manager.Dismiss();
        FadeOutAndHide();
    }
}
