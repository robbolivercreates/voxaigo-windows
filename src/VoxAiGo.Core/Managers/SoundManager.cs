using System.Runtime.InteropServices;

namespace VoxAiGo.Core.Managers;

public class SoundManager
{
    private static readonly Lazy<SoundManager> _instance = new(() => new SoundManager());
    public static SoundManager Shared => _instance.Value;

    public bool IsEnabled
    {
        get => SettingsManager.Shared.PlaySounds;
        set => SettingsManager.Shared.Set(SettingsManager.Keys.PlaySounds, value);
    }

    private SoundManager() { }

    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint uType);

    // MB_OK = 0x0, MB_ICONASTERISK = 0x40, MB_ICONEXCLAMATION = 0x30, MB_ICONHAND = 0x10
    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONASTERISK = 0x00000040;
    private const uint MB_ICONEXCLAMATION = 0x00000030;
    private const uint MB_ICONHAND = 0x00000010;

    public void PlayStart()
    {
        if (!IsEnabled) return;
        try { MessageBeep(MB_ICONEXCLAMATION); } catch { }
    }

    public void PlayStop()
    {
        if (!IsEnabled) return;
        try { MessageBeep(MB_ICONASTERISK); } catch { }
    }

    public void PlaySuccess()
    {
        if (!IsEnabled) return;
        try { MessageBeep(MB_OK); } catch { }
    }

    public void PlayError()
    {
        if (!IsEnabled) return;
        try { MessageBeep(MB_ICONHAND); } catch { }
    }
}
