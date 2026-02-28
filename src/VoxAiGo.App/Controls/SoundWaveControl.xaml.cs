using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace VoxAiGo.App.Controls;

public partial class SoundWaveControl : UserControl
{
    public static readonly DependencyProperty AudioLevelProperty =
        DependencyProperty.Register("AudioLevel", typeof(double), typeof(SoundWaveControl),
            new PropertyMetadata(0.0, OnAudioLevelChanged));

    public double AudioLevel
    {
        get { return (double)GetValue(AudioLevelProperty); }
        set { SetValue(AudioLevelProperty, value); }
    }

    private static void OnAudioLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (SoundWaveControl)d;
        var level = (double)e.NewValue; // Normalized 0-1
        
        control.AnimateBars(level);
    }

    public SoundWaveControl()
    {
        InitializeComponent();
    }

    private readonly Random _rng = new();

    private void AnimateBars(double level)
    {
        // Max height of the container is ~60. Let's say max bar height is 50.
        // Min bar height is 4.
        
        double[] targetHeights = new double[6];
        // Generate pseudo-random heights based on level
        // Center bars are taller.
        
        targetHeights[0] = Math.Max(4, level * 20 * (_rng.NextDouble() + 0.5));
        targetHeights[1] = Math.Max(6, level * 35 * (_rng.NextDouble() + 0.5));
        targetHeights[2] = Math.Max(8, level * 50 * (_rng.NextDouble() + 0.8)); // Peak
        targetHeights[3] = Math.Max(6, level * 40 * (_rng.NextDouble() + 0.6));
        targetHeights[4] = Math.Max(4, level * 25 * (_rng.NextDouble() + 0.5));
        targetHeights[5] = Math.Max(4, level * 15 * (_rng.NextDouble() + 0.5));

        AnimateBar(Bar1, targetHeights[0]);
        AnimateBar(Bar2, targetHeights[1]);
        AnimateBar(Bar3, targetHeights[2]);
        AnimateBar(Bar4, targetHeights[3]);
        AnimateBar(Bar5, targetHeights[4]);
        AnimateBar(Bar6, targetHeights[5]);
    }

    private void AnimateBar(System.Windows.Shapes.Rectangle bar, double toHeight)
    {
        var anim = new DoubleAnimation
        {
            To = toHeight,
            Duration = TimeSpan.FromMilliseconds(50), // Fast response
            EasingFunction = new PowerEase { EasingMode = EasingMode.EaseOut, Power = 2 }
        };
        bar.BeginAnimation(HeightProperty, anim);
    }
}
