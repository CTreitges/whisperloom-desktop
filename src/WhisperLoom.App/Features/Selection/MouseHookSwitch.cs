using WhisperLoom.App.Platform.Windows.Input;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.Features.Selection;

/// <summary>
/// Installiert den globalen Maus-Hook nur, solange ein Maus-Starter an ist (<c>selection.floatingButton</c> oder
/// <c>selection.ctrlRightClick</c>), und entfernt ihn wieder, sobald beide aus sind. Setzt die Firma beide Fest auf aus, wird
/// der Hook nie installiert (die Richtlinie schreibt den Fest-Wert vor jedem Anwenden in die Einstellungen,
/// <c>PolicyStore.EnforceFixed</c>).
/// </summary>
public sealed class MouseHookSwitch(Func<IMouseHook> create) : IDisposable
{
    private IMouseHook? _hook;

    public bool Installed => _hook is not null;

    /// <summary>Wie oft ein Hook angelegt wurde (Test, Log).</summary>
    public int Created { get; private set; }

    public static bool Needed(SelectionSettings selection) => selection.FloatingButton || selection.CtrlRightClick;

    /// <returns>false, wenn ein Hook nötig wäre, Windows ihn aber verweigert.</returns>
    public bool Apply(SelectionSettings selection)
    {
        if (!Needed(selection))
        {
            _hook?.Dispose();
            _hook = null;
            return true;
        }
        if (_hook is null)
        {
            Created++;
            var hook = create();
            if (!hook.Start())
            {
                hook.Dispose();
                return false;
            }
            _hook = hook;
        }
        _hook.Update(m =>
        {
            m.FloatingButton = selection.FloatingButton;
            m.CtrlRightClick = selection.CtrlRightClick;
        });
        return true;
    }

    /// <summary>Sichtbare Fenster (Knopf, Wähler) an den Hook geben; ohne Hook nichts.</summary>
    public void Update(Action<MouseGestures> change) => _hook?.Update(change);

    public void Dispose()
    {
        _hook?.Dispose();
        _hook = null;
    }
}
