using WhisperLoom.Core.Output;
using static WhisperLoom.Core.Output.FocusKind;
using static WhisperLoom.Core.Output.InsertAttempt;
using static WhisperLoom.Core.Output.OutputAction;
using static WhisperLoom.Core.Output.OutputHint;

namespace WhisperLoom.Core.Tests.Output;

/// <summary>Jede Zeile der Ausgabe-Tabelle (docs/parity-ux.md, W2-OUT) einzeln von Hand.</summary>
public class OutputDecisionTest
{
    public static TheoryData<FocusKind, InsertAttempt, bool, OutputAction, bool, OutputHint, bool> Rows() => new()
    {
        // Fokus, Einfügen, alwaysCopy → Aktion, Kopie, Hinweis, Verlauf
        { Editable, NotTried, false, ClipboardWithHint, true, Copied, true },
        { Editable, NotTried, true, ClipboardWithHint, true, Copied, true },
        { Editable, Inserted, false, Insert, false, None, true },
        { Editable, Inserted, true, Insert, true, None, true },
        { Editable, Failed, false, ClipboardWithHint, true, Copied, true },
        { Editable, Failed, true, ClipboardWithHint, true, Copied, true },
        { Password, NotTried, false, NothingWithHint, false, PasswordField, false },
        { Password, NotTried, true, NothingWithHint, false, PasswordField, false },
        { Password, Inserted, false, NothingWithHint, false, PasswordField, false },
        { Password, Inserted, true, NothingWithHint, false, PasswordField, false },
        { Password, Failed, false, NothingWithHint, false, PasswordField, false },
        { Password, Failed, true, NothingWithHint, false, PasswordField, false },
        { NotEditable, NotTried, false, ClipboardWithHint, true, Copied, true },
        { NotEditable, NotTried, true, ClipboardWithHint, true, Copied, true },
        { NotEditable, Inserted, false, Insert, false, None, true },
        { NotEditable, Inserted, true, Insert, true, None, true },
        { NotEditable, Failed, false, ClipboardWithHint, true, Copied, true },
        { NotEditable, Failed, true, ClipboardWithHint, true, Copied, true },
        { Unknown, NotTried, false, ClipboardWithHint, true, Copied, true },
        { Unknown, NotTried, true, ClipboardWithHint, true, Copied, true },
        { Unknown, Inserted, false, Insert, true, None, true },
        { Unknown, Inserted, true, Insert, true, None, true },
        { Unknown, Failed, false, ClipboardWithHint, true, Copied, true },
        { Unknown, Failed, true, ClipboardWithHint, true, Copied, true },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void JedeZeileDerTabelle(FocusKind focus, InsertAttempt attempt, bool alwaysCopy, OutputAction action, bool copy, OutputHint hint, bool history)
    {
        Assert.Equal(new OutputPlan(action, copy, hint, history), OutputDecision.Decide(focus, attempt, alwaysCopy));
    }

    [Fact]
    public void TabelleHatAlleKombinationen()
    {
        Assert.Equal(24, OutputDecision.Table().Count());
        Assert.Equal(Rows().Count, OutputDecision.Table().Count());
    }

    [Theory]
    [InlineData(Editable, true, true)]
    [InlineData(Unknown, true, true)]
    [InlineData(Password, true, false)]
    [InlineData(NotEditable, true, false)]
    [InlineData(Editable, false, false)]
    [InlineData(Unknown, false, false)]
    public void EinfuegenNurInFeldOderUnbekannt(FocusKind focus, bool canInsert, bool expected)
    {
        Assert.Equal(expected, OutputDecision.ShouldInsert(focus, canInsert));
    }

    [Fact]
    public void LinuxOhneEinfuegenLandetInDerZwischenablageMitHinweis()
    {
        var plan = OutputDecision.Decide(Unknown, OutputDecision.ShouldInsert(Unknown, canInsert: false) ? Inserted : NotTried, alwaysCopy: false);
        Assert.Equal(new OutputPlan(ClipboardWithHint, true, Copied, true), plan);
    }
}
