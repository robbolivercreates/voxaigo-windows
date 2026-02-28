using System.Diagnostics;
using System.Runtime.InteropServices;
using VoxAiGo.App.Native;

namespace VoxAiGo.App.Platform;

public class GlobalHotkeyManager : IDisposable
{
    private IntPtr _hookId = IntPtr.Zero;
    private NativeMethods.LowLevelKeyboardProc _proc;

    private bool _ctrlPressed;
    private bool _shiftPressed;
    private bool _spacePressed;
    private bool _holdActive;

    public event Action? HoldStarted;
    public event Action? HoldStopped;
    public event Action? ConversationReplyTriggered;

    // Key codes
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_SPACE = 0x20;
    private const int VK_R = 0x52;

    public GlobalHotkeyManager()
    {
        _proc = HookCallback;
    }

    public bool IsInstalled => _hookId != IntPtr.Zero;

    public void Install()
    {
        try
        {
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;

            var moduleName = curModule?.ModuleName;
            if (string.IsNullOrEmpty(moduleName))
            {
                Debug.WriteLine("[GlobalHotkeyManager] WARNING: MainModule.ModuleName is null — hotkeys not registered.");
                return;
            }

            _hookId = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_KEYBOARD_LL,
                _proc,
                NativeMethods.GetModuleHandle(moduleName),
                0);

            if (_hookId == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                Debug.WriteLine($"[GlobalHotkeyManager] ERROR: SetWindowsHookEx failed — Win32 error {error}. Hotkeys will not work.");
            }
            else
            {
                Debug.WriteLine("[GlobalHotkeyManager] Keyboard hook installed successfully.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GlobalHotkeyManager] EXCEPTION during Install: {ex.Message}");
        }
    }

    public void Uninstall()
    {
        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            bool isDown = (wParam == (IntPtr)NativeMethods.WM_KEYDOWN || wParam == (IntPtr)NativeMethods.WM_SYSKEYDOWN);
            bool isUp = (wParam == (IntPtr)NativeMethods.WM_KEYUP || wParam == (IntPtr)NativeMethods.WM_SYSKEYUP);

            // Track Ctrl
            if (vkCode == VK_LCONTROL || vkCode == VK_RCONTROL)
            {
                _ctrlPressed = isDown;
            }

            // Track Shift
            if (vkCode == VK_LSHIFT || vkCode == VK_RSHIFT)
            {
                _shiftPressed = isDown;
            }

            // Track Space
            if (vkCode == VK_SPACE)
            {
                if (isDown) _spacePressed = true;
                else if (isUp) _spacePressed = false;
            }

            // Conversation Reply: Ctrl+Shift+R (single press, not hold)
            if (vkCode == VK_R && isDown && _ctrlPressed && _shiftPressed)
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() => ConversationReplyTriggered?.Invoke());
                return (IntPtr)1; // Swallow
            }

            // Hold-to-talk: Ctrl+Space
            bool bothPressed = _ctrlPressed && _spacePressed;

            if (bothPressed && !_holdActive)
            {
                _holdActive = true;
                System.Windows.Application.Current?.Dispatcher.Invoke(() => HoldStarted?.Invoke());
                // Swallow the space key to prevent it from typing
                if (vkCode == VK_SPACE && isDown)
                    return (IntPtr)1;
            }
            else if (!bothPressed && _holdActive)
            {
                _holdActive = false;
                System.Windows.Application.Current?.Dispatcher.Invoke(() => HoldStopped?.Invoke());
                // Swallow the space key-up to prevent stray space character
                if (vkCode == VK_SPACE && isUp)
                    return (IntPtr)1;
            }

            // Swallow space while hold is active (prevent typing spaces)
            if (_holdActive && vkCode == VK_SPACE)
                return (IntPtr)1;
        }
        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Uninstall();
    }
}
