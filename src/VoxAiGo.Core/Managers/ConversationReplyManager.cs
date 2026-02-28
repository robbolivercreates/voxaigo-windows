using CommunityToolkit.Mvvm.ComponentModel;

namespace VoxAiGo.Core.Managers;

/// <summary>
/// State machine for Conversation Reply feature.
/// Flow: idle → translating → ready → recording → processing → idle.
/// Select foreign text → Ctrl+Shift+R → read translation → hold Ctrl+Space → speak reply → paste in their language.
/// </summary>
public partial class ConversationReplyManager : ObservableObject
{
    public static readonly ConversationReplyManager Shared = new();

    public enum ReplyState
    {
        Idle,
        Translating,
        Ready,
        Recording,
        Processing
    }

    [ObservableProperty]
    private ReplyState _state = ReplyState.Idle;

    [ObservableProperty]
    private double _timeoutProgress = 1.0;

    // Context from the detected translation
    [ObservableProperty]
    private string _originalText = "";

    [ObservableProperty]
    private string _translation = "";

    [ObservableProperty]
    private string _fromLanguageName = "";

    [ObservableProperty]
    private string _fromLanguageCode = "";

    [ObservableProperty]
    private string _toLanguageName = "";

    public bool IsActive => State != ReplyState.Idle;

    private System.Timers.Timer? _countdownTimer;
    private DateTime _countdownStart;
    private const double TimeoutDuration = 25.0; // seconds

    private ConversationReplyManager() { }

    public void BeginTranslating()
    {
        State = ReplyState.Translating;
        TimeoutProgress = 1.0;
    }

    public void ShowReady(string originalText, string translation, string fromLanguageName, string fromLanguageCode, string toLanguageName)
    {
        OriginalText = originalText;
        Translation = translation;
        FromLanguageName = fromLanguageName;
        FromLanguageCode = fromLanguageCode;
        ToLanguageName = toLanguageName;
        State = ReplyState.Ready;
        StartCountdown();
    }

    public void BeginRecordingReply()
    {
        StopCountdown();
        State = ReplyState.Recording;
    }

    public void BeginProcessingReply()
    {
        State = ReplyState.Processing;
    }

    public void Dismiss()
    {
        StopCountdown();
        State = ReplyState.Idle;
        TimeoutProgress = 1.0;
        OriginalText = "";
        Translation = "";
        FromLanguageName = "";
        FromLanguageCode = "";
        ToLanguageName = "";
    }

    private void StartCountdown()
    {
        StopCountdown();
        TimeoutProgress = 1.0;
        _countdownStart = DateTime.UtcNow;

        _countdownTimer = new System.Timers.Timer(50); // 50ms tick
        _countdownTimer.Elapsed += (s, e) =>
        {
            var elapsed = (DateTime.UtcNow - _countdownStart).TotalSeconds;
            var progress = Math.Max(0.0, 1.0 - elapsed / TimeoutDuration);

            TimeoutProgress = progress;
            if (progress <= 0)
            {
                Dismiss();
                TimedOut?.Invoke();
            }
        };
        _countdownTimer.Start();
    }

    private void StopCountdown()
    {
        _countdownTimer?.Stop();
        _countdownTimer?.Dispose();
        _countdownTimer = null;
    }

    /// <summary>
    /// Fired when the countdown reaches zero or the user explicitly dismisses.
    /// </summary>
    public event Action? TimedOut;
}
