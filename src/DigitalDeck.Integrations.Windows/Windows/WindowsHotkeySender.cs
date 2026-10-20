using System.Diagnostics;
using System.Runtime.InteropServices;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;

namespace DigitalDeck.Integrations.Windows;

/// <inheritdoc cref="IHotkeySender"/>
/// <remarks>
/// The fiddly parts, learned the hard way against Ableton Live and true of most audio software:
/// <list type="bullet">
///   <item>A host only acts on injected keys that carry a <b>scan code</b>. A plain virtual-key
///   <c>SendInput</c> raises the window and then does nothing — the dialog never opens. So every
///   key here goes out with <c>KEYEVENTF_SCANCODE</c>.</item>
///   <item><c>SetForegroundWindow</c> on its own moves the window but not always the keyboard
///   focus. Attaching to the target's input thread first is what makes the focus actually land.</item>
///   <item>If the target runs elevated and the deck does not, Windows silently drops the
///   keystrokes (UIPI). That case is detected and turned into a message rather than a mystery.</item>
/// </list>
/// </remarks>
public sealed class WindowsHotkeySender : IHotkeySender
{
    /// <summary>How long to keep trying to get the target to the foreground before giving up.</summary>
    private static readonly TimeSpan DefaultForegroundTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Polling step while waiting for the foreground to actually change.</summary>
    private static readonly TimeSpan ForegroundPoll = TimeSpan.FromMilliseconds(60);

    /// <summary>
    /// Extra pause once the target <em>is</em> the foreground window, before the keys go out.
    /// </summary>
    /// <remarks>
    /// Becoming the foreground window and being ready to act on a keystroke are not the same moment:
    /// the target's UI thread still has to finish processing the activation, and a chord sent in
    /// that gap is swallowed — the window is in front and nothing happens. This wait is deliberately
    /// generous; nobody notices a third of a second on a button press.
    /// </remarks>
    private static readonly TimeSpan SettleAfterFocus = TimeSpan.FromMilliseconds(350);

    private readonly Func<string, IntPtr?> _findWindow;
    private readonly Func<HotkeySpec, CancellationToken, Task> _pressKeys;
    private readonly TimeSpan _foregroundTimeout;

    public WindowsHotkeySender()
    {
        _findWindow = FindWindowOf;
        _pressKeys = SendChordAsync;
        _foregroundTimeout = DefaultForegroundTimeout;
    }

    /// <summary>Test seam: the two Win32 halves swapped for fakes.</summary>
    internal WindowsHotkeySender(
        Func<string, IntPtr?> findWindow,
        Func<HotkeySpec, CancellationToken, Task> pressKeys,
        TimeSpan? foregroundTimeout = null)
    {
        _findWindow = findWindow;
        _pressKeys = pressKeys;
        _foregroundTimeout = foregroundTimeout ?? DefaultForegroundTimeout;
    }

    public async Task SendAsync(HotkeySpec keys, string program, CancellationToken ct = default)
    {
        if (!keys.IsValid)
            throw new InvalidOperationException("Este botón no tiene una combinación de teclas válida.");

        if (string.IsNullOrWhiteSpace(program))
            throw new InvalidOperationException("Este botón no dice en qué programa apretar las teclas.");

        var name = program.Trim();

        var window = _findWindow(name)
            ?? throw new InvalidOperationException(
                $"No encontré la ventana de {name}. Abrilo y probá de nuevo.");

        ct.ThrowIfCancellationRequested();

        GuardAgainstElevationMismatch(window, name);

        // Ask for the foreground, then wait until Windows says the target actually has it — polling,
        // not a fixed sleep, so a slow machine gets as long as it needs and a fast one is not held
        // back. One re-assert halfway through, in case the first attempt lost a race with whatever
        // had the foreground.
        ForceForeground(window);

        var deadline = DateTime.UtcNow + _foregroundTimeout;
        var reasserted = false;

        while (GetForegroundWindow() != window)
        {
            if (DateTime.UtcNow >= deadline)
                throw new InvalidOperationException(
                    $"No pude traer {name} al frente. Hacé clic en esa ventana y volvé a probar.");

            if (!reasserted && DateTime.UtcNow > deadline - _foregroundTimeout / 2)
            {
                ForceForeground(window);
                reasserted = true;
            }

            await Task.Delay(ForegroundPoll, ct).ConfigureAwait(false);
        }

        // In front now, but its UI thread may still be catching up with the activation. Give it that
        // beat before the chord, or the window is showing and the keys still go nowhere.
        await Task.Delay(SettleAfterFocus, ct).ConfigureAwait(false);

        await _pressKeys(keys, ct).ConfigureAwait(false);
    }

    // ---------- Finding the window ----------

    /// <summary>
    /// The main window of the first running process whose name starts with <paramref name="prefix"/>.
    /// </summary>
    /// <remarks>
    /// A prefix rather than an exact name because most of these carry their version in the process
    /// name — "Ableton Live 12 Suite", "Photoshop 2024" — and a button configured once should
    /// survive the next update of the program it drives.
    /// </remarks>
    private static IntPtr? FindWindowOf(string prefix)
    {
        Process[] processes;

        try
        {
            processes = Process.GetProcesses();
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        try
        {
            var handle = processes
                .Where(p => p.ProcessName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(p =>
                {
                    try { return p.MainWindowHandle; }
                    catch (InvalidOperationException) { return IntPtr.Zero; }
                })
                .FirstOrDefault(h => h != IntPtr.Zero);

            return handle == IntPtr.Zero ? null : handle;
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    // ---------- Bringing it to the front ----------

    /// <summary>
    /// Raises the window and moves the keyboard focus to it, not just the window to the top.
    /// </summary>
    /// <remarks>
    /// The AttachThreadInput dance is the part that matters: while our input thread is attached to
    /// the target's, Windows lets us hand it the foreground outright, and the keyboard focus goes
    /// with it. Detached again immediately so a later crash in either process cannot wedge the
    /// other's queue.
    /// </remarks>
    private static void ForceForeground(IntPtr window)
    {
        AllowSetForegroundWindow(AsfwAny);

        if (IsIconic(window)) ShowWindow(window, SwRestore);

        var us = GetCurrentThreadId();
        var them = GetWindowThreadProcessId(window, out _);

        var attached = us != them && AttachThreadInput(us, them, true);

        try
        {
            BringWindowToTop(window);
            SetForegroundWindow(window);
            SetActiveWindow(window);
        }
        finally
        {
            if (attached) AttachThreadInput(us, them, false);
        }
    }

    // ---------- Elevation ----------

    private static void GuardAgainstElevationMismatch(IntPtr window, string program)
    {
        var pid = GetWindowThreadProcessId(window, out var processId) == 0 ? 0u : processId;

        if (pid == 0) return;

        bool elevated;

        try
        {
            elevated = IsProcessElevated(pid);
        }
        catch (Exception)
        {
            // Can't tell — most likely because the target *is* elevated and we can't open its
            // token. Fall through; the keystroke attempt will simply do nothing and the caller
            // sees that.
            return;
        }

        if (elevated && !IsProcessElevated((uint)Environment.ProcessId))
        {
            throw new InvalidOperationException(
                $"{program} está corriendo como administrador y DigitalDeck no, así que Windows no " +
                "deja mandarle las teclas. Cerrá DigitalDeck y abrilo como administrador (o abrí " +
                $"{program} sin administrador).");
        }
    }

    private static bool IsProcessElevated(uint pid)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, false, pid);

        if (process == IntPtr.Zero)
            throw new InvalidOperationException("No pude abrir el proceso.");

        try
        {
            if (!OpenProcessToken(process, TokenQuery, out var token))
                throw new InvalidOperationException("No pude abrir el token.");

            try
            {
                var elevation = default(TOKEN_ELEVATION);
                var size = (uint)Marshal.SizeOf<TOKEN_ELEVATION>();

                if (!GetTokenInformation(token, TokenElevation, ref elevation, size, out _))
                    throw new InvalidOperationException("No pude leer el token.");

                return elevation.TokenIsElevated != 0;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    // ---------- Pressing the keys ----------

    /// <summary>The modifiers of a combination, in the order they go down. Released in reverse.</summary>
    private static IEnumerable<ushort> ModifierKeys(HotkeyModifiers modifiers)
    {
        if (modifiers.HasFlag(HotkeyModifiers.Control)) yield return VkControl;
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) yield return VkMenu;
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) yield return VkShift;
        if (modifiers.HasFlag(HotkeyModifiers.Windows)) yield return VkLeftWindows;
    }

    private static async Task SendChordAsync(HotkeySpec keys, CancellationToken ct)
    {
        var modifiers = ModifierKeys(keys.Modifiers).ToArray();

        var down = modifiers.Select(KeyDown).ToList();
        down.Add(KeyDown((ushort)keys.VirtualKey));

        var up = new List<INPUT> { KeyUp((ushort)keys.VirtualKey) };
        up.AddRange(modifiers.Reverse().Select(KeyUp));

        Send(down);

        // A real keypress is not instantaneous, and some hosts miss a chord that goes down and up
        // in the same message batch.
        await Task.Delay(40, ct).ConfigureAwait(false);

        Send(up);
    }

    private static void Send(IReadOnlyList<INPUT> inputs)
    {
        var array = inputs.ToArray();
        var sent = SendInput((uint)array.Length, array, Marshal.SizeOf<INPUT>());

        if (sent != array.Length)
            throw new InvalidOperationException(
                "Windows no dejó mandar las teclas. Si el programa está como administrador, abrí " +
                "DigitalDeck como administrador también.");
    }

    // ---------- Win32 ----------

    private const int SwRestore = 9;
    private const uint AsfwAny = 0xFFFFFFFF;

    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12;
    private const ushort VkLeftWindows = 0x5B;

    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint KeyeventfScancode = 0x0008;
    private const uint MapvkVkToVsc = 0;

    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenElevation = 20;

    private static INPUT KeyDown(ushort key) => Key(key, 0);
    private static INPUT KeyUp(ushort key) => Key(key, KeyeventfKeyup);

    private static INPUT Key(ushort vk, uint flags)
    {
        var scan = (ushort)MapVirtualKey(vk, MapvkVkToVsc);

        return new INPUT
        {
            type = InputKeyboard,
            u = new InputUnion
            {
                // Scan code with the SCANCODE flag: audio hosts ignore injected keys that carry
                // only a virtual-key code. wVk stays populated so anything reading it still works.
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = scan,
                    dwFlags = flags | (scan != 0 ? KeyeventfScancode : 0)
                }
            }
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_ELEVATION
    {
        public uint TokenIsElevated;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetActiveWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        IntPtr TokenHandle, int TokenInformationClass,
        ref TOKEN_ELEVATION TokenInformation, uint TokenInformationLength, out uint ReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
