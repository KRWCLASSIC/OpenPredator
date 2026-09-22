using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenPredator.TestingSuite;

public unsafe class TerminalEngine : IDisposable
{
    private const int STD_INPUT_HANDLE = -10;
    private const int STD_OUTPUT_HANDLE = -11;

    private const uint ENABLE_PROCESSED_INPUT = 0x0001;
    private const uint ENABLE_LINE_INPUT = 0x0002;
    private const uint ENABLE_ECHO_INPUT = 0x0004;
    private const uint ENABLE_WINDOW_INPUT = 0x0008;
    private const uint ENABLE_MOUSE_INPUT = 0x0010;
    private const uint ENABLE_INSERT_MODE = 0x0020;
    private const uint ENABLE_QUICK_EDIT_MODE = 0x0040;
    private const uint ENABLE_EXTENDED_FLAGS = 0x0080;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadConsoleInput(IntPtr hConsoleInput, [Out] INPUT_RECORD[] lpBuffer, uint nLength, out uint lpNumberOfEventsRead);

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT_RECORD
    {
        [FieldOffset(0)] public ushort EventType;
        [FieldOffset(4)] public KEY_EVENT_RECORD KeyEvent;
        [FieldOffset(4)] public MOUSE_EVENT_RECORD MouseEvent;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEY_EVENT_RECORD
    {
        public int bKeyDown;
        public ushort wRepeatCount;
        public ushort wVirtualKeyCode;
        public ushort wVirtualScanCode;
        public char UnicodeChar;
        public uint dwControlKeyState;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSE_EVENT_RECORD
    {
        public COORD dwMousePosition;
        public uint dwButtonState;
        public uint dwControlKeyState;
        public uint dwEventFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct COORD
    {
        public short X;
        public short Y;
    }

    private readonly IntPtr _hIn;
    private readonly IntPtr _hOut;
    private readonly uint _originalInMode;
    private readonly uint _originalOutMode;

    public bool IsMouseCaptureEnabled { get; private set; } = false;

    public TerminalEngine()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.CursorVisible = false;

        if (OperatingSystem.IsWindows())
        {
            _hIn = GetStdHandle(STD_INPUT_HANDLE);
            _hOut = GetStdHandle(STD_OUTPUT_HANDLE);

            GetConsoleMode(_hIn, out _originalInMode);
            GetConsoleMode(_hOut, out _originalOutMode);

            SetConsoleMode(_hOut, _originalOutMode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
            
            // Default to QuickEdit enabled (Text selectable with mouse)
            SetMouseMode(false);
        }
    }

    public void SetMouseMode(bool enableCapture)
    {
        IsMouseCaptureEnabled = enableCapture;
        if (OperatingSystem.IsWindows())
        {
            if (enableCapture)
            {
                // Disable quick edit, enable mouse events capture
                SetConsoleMode(_hIn, ENABLE_MOUSE_INPUT | ENABLE_EXTENDED_FLAGS | ENABLE_WINDOW_INPUT);
            }
            else
            {
                // Enable quick edit so user can select & copy text natively
                SetConsoleMode(_hIn, ENABLE_QUICK_EDIT_MODE | ENABLE_EXTENDED_FLAGS | ENABLE_PROCESSED_INPUT);
            }
        }
    }

    public void ToggleMouseMode()
    {
        SetMouseMode(!IsMouseCaptureEnabled);
    }

    public struct InputEvent
    {
        public bool IsMouse;
        public int MouseX;
        public int MouseY;
        public bool MouseClicked;
        public ConsoleKey Key;
        public char Char;
    }

    public bool PollInput(out InputEvent evt)
    {
        evt = default;

        if (OperatingSystem.IsWindows())
        {
            if (IsMouseCaptureEnabled)
            {
                var records = new INPUT_RECORD[1];
                if (ReadConsoleInput(_hIn, records, 1, out uint read) && read > 0)
                {
                    var rec = records[0];
                    if (rec.EventType == 0x0002) // MOUSE_EVENT
                    {
                        evt.IsMouse = true;
                        evt.MouseX = rec.MouseEvent.dwMousePosition.X;
                        evt.MouseY = rec.MouseEvent.dwMousePosition.Y;
                        evt.MouseClicked = (rec.MouseEvent.dwButtonState & 0x0001) != 0;
                        return true;
                    }
                    else if (rec.EventType == 0x0001 && rec.KeyEvent.bKeyDown != 0) // KEY_EVENT
                    {
                        evt.IsMouse = false;
                        evt.Key = (ConsoleKey)rec.KeyEvent.wVirtualKeyCode;
                        evt.Char = rec.KeyEvent.UnicodeChar;
                        return true;
                    }
                }
            }
            else
            {
                if (Console.KeyAvailable)
                {
                    var keyInfo = Console.ReadKey(true);
                    evt.IsMouse = false;
                    evt.Key = keyInfo.Key;
                    evt.Char = keyInfo.KeyChar;
                    return true;
                }
            }
        }
        else
        {
            if (Console.KeyAvailable)
            {
                var keyInfo = Console.ReadKey(true);
                evt.IsMouse = false;
                evt.Key = keyInfo.Key;
                evt.Char = keyInfo.KeyChar;
                return true;
            }
        }

        return false;
    }

    public void ClearScreen()
    {
        Console.Write("\x1b[2J\x1b[H");
    }

    public void MoveCursor(int x, int y)
    {
        Console.Write($"\x1b[{y + 1};{x + 1}H");
    }

    public void Dispose()
    {
        Console.CursorVisible = true;
        if (OperatingSystem.IsWindows())
        {
            SetConsoleMode(_hIn, _originalInMode);
            SetConsoleMode(_hOut, _originalOutMode);
        }
    }
}
