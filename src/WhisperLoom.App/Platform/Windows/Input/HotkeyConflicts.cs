namespace WhisperLoom.App.Platform.Windows.Input;

/// <summary>
/// Warnungen zu einer Tastenkombination für den Rekorder (Lokalisierungs-Schlüssel <c>Hotkey_Conflict_*</c>).
/// Warnungen blockieren nicht: der Nutzer darf die Kombi trotzdem behalten.
/// </summary>
public static class HotkeyConflicts
{
    private static readonly (Hotkey Combo, string Key)[] Known =
    [
        // VS Code, Visual Studio, IntelliJ: Autovervollständigung.
        (new Hotkey(HotkeyModifiers.Ctrl, Hotkey.VkSpace), "Hotkey_Conflict_Autocomplete"),
        // Windows: Eingabesprache wechseln.
        (new Hotkey(HotkeyModifiers.Win, Hotkey.VkSpace), "Hotkey_Conflict_InputLanguage"),
        (new Hotkey(HotkeyModifiers.Win | HotkeyModifiers.Shift, Hotkey.VkSpace), "Hotkey_Conflict_InputLanguage"),
        // Strg+Alt = AltGr auf deutschen Tastaturen.
        (new Hotkey(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 'Q'), "Hotkey_Conflict_AltGr"),
        (new Hotkey(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 'E'), "Hotkey_Conflict_AltGr"),
    ];

    /// <param name="others">Die anderen eigenen Kombis (Doppelbelegung).</param>
    public static IReadOnlyList<string> For(Hotkey combo, IEnumerable<Hotkey?> others)
    {
        var warnings = new List<string>();
        if (combo.Modifiers == HotkeyModifiers.None)
        {
            warnings.Add("Hotkey_Conflict_NoModifier");
        }
        if (combo.IsModifierOnly && combo.Modifiers is HotkeyModifiers.Ctrl or HotkeyModifiers.Alt or HotkeyModifiers.Shift or HotkeyModifiers.Win)
        {
            warnings.Add("Hotkey_Conflict_SingleModifier");
        }
        warnings.AddRange(Known.Where(k => k.Combo == combo).Select(k => k.Key));
        if (combo.Modifiers.HasFlag(HotkeyModifiers.Win) && !combo.IsModifierOnly)
        {
            warnings.Add("Hotkey_Conflict_Windows");
        }
        if (combo.Key is Hotkey.VkReturn or Hotkey.VkBack && combo.Modifiers is HotkeyModifiers.None or HotkeyModifiers.Ctrl)
        {
            warnings.Add("Hotkey_Conflict_Overlay");
        }
        if (others.Any(o => o == combo))
        {
            warnings.Add("Hotkey_Conflict_Duplicate");
        }
        return warnings.Distinct().ToList();
    }
}
