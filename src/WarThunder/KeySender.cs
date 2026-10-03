using System.Runtime.InteropServices;

namespace WarThunder;

/// <summary>
/// A key combination like "F13", "Ctrl+Shift+G" or "Numpad5", sent as hardware scancodes
/// (games like War Thunder read scancodes and often ignore virtual-key-only input).
/// </summary>
public sealed record KeyCombo(ushort[] Modifiers, ushort Key, bool Extended, string Text)
{
    private const ushort Ctrl = 0x1D, Shift = 0x2A, Alt = 0x38;

    /// <summary>Set-1 scancodes. Extended keys (E0 prefix) are listed in <see cref="ExtendedKeys"/>.</summary>
    private static readonly Dictionary<string, ushort> Codes = BuildCodes();
    private static readonly HashSet<string> ExtendedKeys =
        ["INSERT", "DELETE", "HOME", "END", "PAGEUP", "PAGEDOWN", "UP", "DOWN", "LEFT", "RIGHT", "NUMPADDIVIDE", "NUMPADENTER", "RCTRL", "RALT"];

    public static KeyCombo? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var modifiers = new List<ushort>();
        foreach (var mod in parts[..^1])
        {
            modifiers.Add(mod.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => Ctrl,
                "SHIFT" => Shift,
                "ALT" => Alt,
                _ => throw new FormatException($"Unknown modifier '{mod}'"),
            });
        }

        var key = parts[^1].ToUpperInvariant();
        if (!Codes.TryGetValue(key, out var code))
            throw new FormatException($"Unknown key '{parts[^1]}'");
        return new KeyCombo([.. modifiers], code, ExtendedKeys.Contains(key), text.Trim());
    }

    /// <summary>Like <see cref="Parse"/> but logs and returns null for bad input.</summary>
    public static KeyCombo? TryParse(string? text)
    {
        try
        {
            return Parse(text);
        }
        catch (FormatException ex)
        {
            Console.Error.WriteLine($"Ignoring key setting: {ex.Message}");
            return null;
        }
    }

    private static Dictionary<string, ushort> BuildCodes()
    {
        var c = new Dictionary<string, ushort>();
        const string row1 = "QWERTYUIOP", row2 = "ASDFGHJKL", row3 = "ZXCVBNM";
        for (var i = 0; i < row1.Length; i++) c[row1[i].ToString()] = (ushort)(0x10 + i);
        for (var i = 0; i < row2.Length; i++) c[row2[i].ToString()] = (ushort)(0x1E + i);
        for (var i = 0; i < row3.Length; i++) c[row3[i].ToString()] = (ushort)(0x2C + i);
        for (var i = 1; i <= 9; i++) c[i.ToString()] = (ushort)(0x01 + i);
        c["0"] = 0x0B;
        for (var i = 1; i <= 10; i++) c[$"F{i}"] = (ushort)(0x3A + i);
        c["F11"] = 0x57; c["F12"] = 0x58;
        // F13-F24: not on normal keyboards, so they never clash with existing binds.
        ushort[] high = [0x64, 0x65, 0x66, 0x67, 0x68, 0x69, 0x6A, 0x6B, 0x6C, 0x6D, 0x6E, 0x76];
        for (var i = 0; i < high.Length; i++) c[$"F{13 + i}"] = high[i];
        ushort[] numpad = [0x52, 0x4F, 0x50, 0x51, 0x4B, 0x4C, 0x4D, 0x47, 0x48, 0x49];
        for (var i = 0; i < 10; i++) c[$"NUMPAD{i}"] = numpad[i];
        c["NUMPADADD"] = 0x4E; c["NUMPADSUBTRACT"] = 0x4A; c["NUMPADMULTIPLY"] = 0x37;
        c["NUMPADDIVIDE"] = 0x35; c["NUMPADDECIMAL"] = 0x53; c["NUMPADENTER"] = 0x1C;
        c["SPACE"] = 0x39; c["ENTER"] = 0x1C; c["TAB"] = 0x0F; c["ESCAPE"] = 0x01; c["ESC"] = 0x01; c["BACKSPACE"] = 0x0E;
        c["INSERT"] = 0x52; c["DELETE"] = 0x53; c["HOME"] = 0x47; c["END"] = 0x4F; c["PAGEUP"] = 0x49; c["PAGEDOWN"] = 0x51;
        c["UP"] = 0x48; c["DOWN"] = 0x50; c["LEFT"] = 0x4B; c["RIGHT"] = 0x4D;
        c["MINUS"] = 0x0C; c["EQUALS"] = 0x0D; c["LBRACKET"] = 0x1A; c["RBRACKET"] = 0x1B; c["SEMICOLON"] = 0x27;
        c["QUOTE"] = 0x28; c["GRAVE"] = 0x29; c["BACKSLASH"] = 0x2B; c["COMMA"] = 0x33; c["PERIOD"] = 0x34; c["SLASH"] = 0x35;
        c["CTRL"] = Ctrl; c["SHIFT"] = Shift; c["ALT"] = Alt; c["RCTRL"] = 0x1D; c["RALT"] = 0x38;
        return c;
    }
}

/// <summary>Sends <see cref="KeyCombo"/>s to the focused window. Windows only (SendInput); no-op elsewhere.</summary>
public static class KeySender
{
    /// <summary>Hold time for a "click" so games polling input at 60 Hz still see it.</summary>
    private const int ClickMs = 40;

    public static void Down(KeyCombo combo)
    {
        if (!OperatingSystem.IsWindows())
            return;
        var inputs = new List<Input>();
        foreach (var m in combo.Modifiers)
            inputs.Add(Key(m, false, up: false));
        inputs.Add(Key(combo.Key, combo.Extended, up: false));
        Send(inputs);
    }

    public static void Up(KeyCombo combo)
    {
        if (!OperatingSystem.IsWindows())
            return;
        var inputs = new List<Input> { Key(combo.Key, combo.Extended, up: true) };
        foreach (var m in combo.Modifiers.Reverse())
            inputs.Add(Key(m, false, up: true));
        Send(inputs);
    }

    public static async Task ClickAsync(KeyCombo combo, int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            Down(combo);
            await Task.Delay(ClickMs);
            Up(combo);
            if (i + 1 < times)
                await Task.Delay(ClickMs);
        }
    }

    private static Input Key(ushort scan, bool extended, bool up) => new()
    {
        Type = 1, // INPUT_KEYBOARD
        Ki = new KeybdInput
        {
            Scan = scan,
            Flags = 0x0008 /* SCANCODE */ | (extended ? 0x0001u : 0) | (up ? 0x0002u : 0),
        },
    };

    private static void Send(List<Input> inputs)
    {
        var array = inputs.ToArray();
        if (SendInput((uint)array.Length, array, Marshal.SizeOf<Input>()) != array.Length)
            Console.Error.WriteLine($"SendInput failed ({Marshal.GetLastWin32Error()})");
    }

    // INPUT is a union of mouse/keyboard/hardware input; size it for the largest (MOUSEINPUT).
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public KeybdInput Ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
}
