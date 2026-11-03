using WhisperLoom.App.Localization;
using WhisperLoom.App.ViewModels;
using WhisperLoom.Core.Contracts;

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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
    public async Task Deleting_without_a_key_offers_nothing_to_undo()
    {
        var (key, _) = await Make(withKey: false);

        await key.DeleteCommand.ExecuteAsync(null);

        Assert.False(key.CanUndo);
    }
}
