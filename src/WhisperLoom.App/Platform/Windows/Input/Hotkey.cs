namespace WhisperLoom.App.Platform.Windows.Input;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// Eine Tastenkombination: Modifier plus höchstens eine weitere Taste (virtueller Tastencode). Ohne Taste
/// (<see cref="Key"/> = 0) besteht sie nur aus Modifiern, z. B. Strg+Win zum Halten. Text: "Ctrl+Win", "Ctrl+Alt+Space".
/// </summary>
// Doku: learn.microsoft.com/windows/win32/inputdev/virtual-key-codes (abgerufen 2026-10-31).
public sealed record Hotkey(HotkeyModifiers Modifiers, int Key)
{
    public const int VkBack = 0x08;
    public const int VkReturn = 0x0D;
    public const int VkShift = 0x10;
    public const int VkControl = 0x11;
    public const int VkMenu = 0x12;
    public const int VkEscape = 0x1B;
    public const int VkSpace = 0x20;
    public const int VkLWin = 0x5B;
    public const int VkRWin = 0x5C;
    public const int VkLShift = 0xA0;
    public const int VkRShift = 0xA1;
    public const int VkLControl = 0xA2;
    public const int VkRControl = 0xA3;
    public const int VkLMenu = 0xA4;
    public const int VkRMenu = 0xA5;

    /// <summary>Unbelegte Taste, die beim Loslassen von Win das Startmenü unterdrückt (wie AutoHotkey „MenuMaskKey“).</summary>
    public const int VkMaskKey = 0xE8;

    public static readonly Hotkey DefaultHold = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Win, 0);
    public static readonly Hotkey DefaultToggle = new(HotkeyModifiers.Ctrl, VkSpace);
    public static readonly Hotkey DefaultStage = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, VkSpace);

    private static readonly (HotkeyModifiers Flag, string Name)[] ModifierNames =
        [(HotkeyModifiers.Ctrl, "Ctrl"), (HotkeyModifiers.Alt, "Alt"), (HotkeyModifiers.Shift, "Shift"), (HotkeyModifiers.Win, "Win")];

    private static readonly Dictionary<string, int> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = VkSpace, ["Enter"] = VkReturn, ["Backspace"] = VkBack, ["Esc"] = VkEscape, ["Tab"] = 0x09,
        ["Insert"] = 0x2D, ["Delete"] = 0x2E, ["Home"] = 0x24, ["End"] = 0x23, ["PageUp"] = 0x21, ["PageDown"] = 0x22,
        ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28, ["Pause"] = 0x13, ["CapsLock"] = 0x14,
        ["ScrollLock"] = 0x91, ["PrintScreen"] = 0x2C, ["Apps"] = 0x5D,
    };

    public bool IsModifierOnly => Key == 0;

    /// <summary>Modifier einer Taste (links/rechts gleich), sonst <see cref="HotkeyModifiers.None"/>.</summary>
    public static HotkeyModifiers ModifierOf(int vk) => vk switch
    {
        VkControl or VkLControl or VkRControl => HotkeyModifiers.Ctrl,
        VkMenu or VkLMenu or VkRMenu => HotkeyModifiers.Alt,
        VkShift or VkLShift or VkRShift => HotkeyModifiers.Shift,
        VkLWin or VkRWin => HotkeyModifiers.Win,
        _ => HotkeyModifiers.None,
    };

    /// <summary>Liest "Ctrl+Alt+Space"; null bei leerem oder ungültigem Text.</summary>
    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        var modifiers = HotkeyModifiers.None;
        var key = 0;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var flag = ModifierNames.FirstOrDefault(m => m.Name.Equals(raw, StringComparison.OrdinalIgnoreCase)).Flag;
            if (flag == HotkeyModifiers.None && raw.Equals("Strg", StringComparison.OrdinalIgnoreCase))
            {
                flag = HotkeyModifiers.Ctrl;
            }
            if (flag != HotkeyModifiers.None)
            {
                modifiers |= flag;
                continue;
            }
            var vk = KeyCode(raw);
            if (vk == 0 || key != 0)
            {
                return null;
            }
            key = vk;
        }
        return modifiers == HotkeyModifiers.None && key == 0 ? null : new Hotkey(modifiers, key);
    }

    /// <summary>Parse mit Rückfall auf den Standard (leere oder kaputte Einstellung).</summary>
    public static Hotkey ParseOr(string? text, Hotkey fallback) => Parse(text) ?? fallback;

    public override string ToString()
    {
        var parts = ModifierNames.Where(m => Modifiers.HasFlag(m.Flag)).Select(m => m.Name).ToList();
        if (Key != 0)
        {
            parts.Add(KeyName(Key));
        }
        return string.Join("+", parts);
    }

    private static int KeyCode(string name)
    {
        if (NamedKeys.TryGetValue(name, out var vk))
        {
            return vk;
        }
        if (name.Length == 1 && char.IsAsciiLetterOrDigit(name[0]))
        {
            return char.ToUpperInvariant(name[0]);
        }
        if (name.Length is 2 or 3 && (name[0] is 'F' or 'f') && int.TryParse(name[1..], out var f) && f is >= 1 and <= 24)
        {
            return 0x70 + f - 1;
        }
        if (name.StartsWith("VK", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name[2..], System.Globalization.NumberStyles.HexNumber, null, out var hex) && hex is > 0 and < 0xFF)
        {
            return hex;
        }
        return 0;
    }

    private static string KeyName(int vk)
    {
        foreach (var (name, code) in NamedKeys)
        {
            if (code == vk)
            {
                return name;
            }
        }
        if (vk is >= 'A' and <= 'Z' or >= '0' and <= '9')
        {
            return ((char)vk).ToString();
        }
        if (vk is >= 0x70 and <= 0x87)
        {
            return "F" + (vk - 0x70 + 1);
        }
        return $"VK{vk:X2}";
    }
}
