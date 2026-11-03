using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Management;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.Views;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Tests;

/// <summary>
/// Key-Feld (UX-2, zielstruktur.md Key-Feld): Zustand als Satz ohne den Key, „Löschen“ mit „Rückgängig“ (Meldung bleibt bis zur
/// nächsten Aktion, kein Stapel), Hauptaktion je nach Zustand.
/// </summary>
public sealed class KeyFieldUndoTest
{
    private const string Secret = "test-key-not-real-71";

    private static async Task<(KeyFieldViewModel Key, MemorySecretStore Store)> Make(bool withKey = true)
    {
        Loc.Instance.SetLanguage("de");
        var store = new MemorySecretStore();
        if (withKey)
        {
            store.Values[SecretNames.Stt] = Secret;
        }
        var key = new KeyFieldViewModel(SecretNames.Stt, store, new FakeAccessChecker(), () => ("openai", ""));
        await key.LoadAsync();
        return (key, store);
    }

    [AvaloniaFact]
    public async Task State_is_a_sentence_and_never_the_key()
    {
        var (key, _) = await Make();
        Assert.Equal("Key gespeichert", key.StateText);
        Assert.True(key.ShowCheck);
        Assert.False(key.ShowEnter);

        var (empty, _) = await Make(withKey: false);
        Assert.Equal("Kein Key gespeichert", empty.StateText);
        Assert.False(empty.ShowCheck);
        Assert.True(empty.ShowEnter);
        Assert.DoesNotContain(Secret[^4..], key.StateText, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Delete_offers_undo_which_puts_the_same_key_back()
    {
        var (key, store) = await Make();

        await key.DeleteCommand.ExecuteAsync(null);

        Assert.False(store.Values.ContainsKey(SecretNames.Stt));
        Assert.False(key.IsSet);
        Assert.Equal("Key gelöscht.", key.Status);
        Assert.True(key.CanUndo);

        await key.UndoCommand.ExecuteAsync(null);

        Assert.Equal(Secret, store.Values[SecretNames.Stt]);
        Assert.True(key.IsSet);
        Assert.False(key.CanUndo);
        Assert.Equal("Key wiederhergestellt.", key.Status);
    }

    [AvaloniaFact]
    public async Task The_next_action_ends_the_undo_offer()
    {
        var (key, store) = await Make();
        await key.DeleteCommand.ExecuteAsync(null);

        key.ReplaceCommand.Execute(null); // nächste Aktion: Rückgängig verfällt, der gelöschte Key wird vergessen

        Assert.False(key.CanUndo);
        await key.UndoCommand.ExecuteAsync(null);
        Assert.False(store.Values.ContainsKey(SecretNames.Stt));
        Assert.False(key.IsSet);
    }

    [AvaloniaFact]
    public async Task Saving_a_new_key_after_delete_is_not_undone_by_a_late_undo()
    {
        var (key, store) = await Make();
        await key.DeleteCommand.ExecuteAsync(null);
        key.ReplaceCommand.Execute(null);
        key.NewValue = "test-key-not-real-72";
        await key.SaveKeyCommand.ExecuteAsync(null);

        await key.UndoCommand.ExecuteAsync(null);

        Assert.Equal("test-key-not-real-72", store.Values[SecretNames.Stt]);
        Assert.Equal("Key gespeichert.", key.Status);
    }

    [AvaloniaFact]
    public async Task Deleting_without_a_key_offers_nothing_to_undo()
    {
        var (key, _) = await Make(withKey: false);

        await key.DeleteCommand.ExecuteAsync(null);

        Assert.False(key.CanUndo);
    }
    /// <summary>
    /// Prüfer-Befund UX-2/1: Im Modus „Keys verteilen“ liefert die Firmen-Ablage beim Lesen den Firmen-Key, Löschen trifft aber nur
    /// die eigenen Keys. Rückgängig darf den Firmen-Key nie als eigenen Key abschreiben (sonst bliebe er nach Austritt auf dem Gerät).
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Undo_never_turns_a_distributed_company_key_into_an_own_key(bool ownKeyBefore)
    {
        Loc.Instance.SetLanguage("de");
        const string name = "custom:acme";
        var dir = Directory.CreateTempSubdirectory("wl-keyundo-").FullName;
        try
        {
            var settings = new WhisperLoomSettings();
            settings.Management.ServerUrl = "https://wl.example.com";
            settings.Management.State = "approved";
            settings.Management.KeyMode = "distribute";
            settings.Providers.Custom.Add(new CustomProvider { Id = "acme", Name = "Acme", BaseUrl = "http://127.0.0.1:9" });
            var user = new MemorySecretStore();
            var company = new MemorySecretStore();
            company.Values[name] = "test-key-not-real-81";
            if (ownKeyBefore)
            {
                user.Values[name] = "test-key-not-real-82";
            }
            var targets = new Dictionary<string, KeyTarget> { [name] = new(name, "http://127.0.0.1:9") };
            var store = new CompanySecretStore(user, company, () => settings, () => PolicySettings.None, () => targets, new OwnKeyTargets(dir));
            var key = new KeyFieldViewModel(name, store, new FakeAccessChecker(), () => (name, "http://127.0.0.1:9"));
            await key.LoadAsync();

            await key.DeleteCommand.ExecuteAsync(null);
            await key.UndoCommand.ExecuteAsync(null);

            Assert.Equal(ownKeyBefore ? "test-key-not-real-82" : null, user.Values.GetValueOrDefault(name));
            Assert.Equal("test-key-not-real-81", company.Values[name]); // Firmen-Ablage unberührt

            if (!ownKeyBefore)
            {
                // Nachprüfung B1: ohne eigenen Key löscht der Klick nichts – das Feld sagt das, statt „Key gelöscht“ zu zeigen.
                await key.DeleteCommand.ExecuteAsync(null);
                Assert.True(key.IsSet);
                Assert.False(key.CanUndo);
                Assert.Equal(Loc.Instance["Key_FromCompany"], key.Status);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
    /// <summary>Prüfer-Befund UX-2/3: Fokus landet nach Löschen auf „Rückgängig“, danach auf „Verbindung testen“ – nicht im Nichts.</summary>
    [AvaloniaFact]
    public async Task Focus_follows_delete_and_undo_in_the_view()
    {
        var (key, _) = await Make();
        var view = new KeyFieldView { DataContext = key };
        var window = new Window { Content = view, Width = 600, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        IInputElement? Focused() => window.FocusManager?.GetFocusedElement();

        view.FindControl<Button>("DeleteButton")!.Focus(NavigationMethod.Tab);
        await key.DeleteCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(view.FindControl<Button>("UndoKeyButton"), Focused());

        await key.UndoCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(view.FindControl<Button>("CheckButton"), Focused());
        window.Close();
    }

    /// <summary>Prüfer-Befund UX-4: Die Overlay-Tastenkappen hatten den Schlüssel <c>Key_Enter</c> überschrieben; der Hauptknopf hieß „Enter“.</summary>
    [AvaloniaFact]
    public async Task Enter_button_says_enter_key_not_the_keycap()
    {
        var (key, _) = await Make(withKey: false);
        var view = new KeyFieldView { DataContext = key };
        Assert.Equal("Key eingeben …", view.FindControl<Button>("EnterButton")!.Content);
        Loc.Instance.SetLanguage("en");
        Assert.Equal("Enter key …", view.FindControl<Button>("EnterButton")!.Content);
        Loc.Instance.SetLanguage("de");
    }

    /// <summary>Prüfer-Befund UX-2/2: Verschwindet die Ansicht (Fenster zu), verfällt „Rückgängig“; der gelöschte Key ist vergessen.</summary>
    [AvaloniaFact]
    public async Task Closing_the_view_forgets_the_deleted_key()
    {
        var (key, store) = await Make();
        var window = new Window { Content = new KeyFieldView { DataContext = key } };
        window.Show();
        await key.DeleteCommand.ExecuteAsync(null);
        Assert.True(key.CanUndo);

        window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.False(key.CanUndo);
        await key.UndoCommand.ExecuteAsync(null);
        Assert.False(store.Values.ContainsKey(SecretNames.Stt));
    }
}
