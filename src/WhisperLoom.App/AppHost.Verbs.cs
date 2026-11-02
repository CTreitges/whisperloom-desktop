using WhisperLoom.App.Instance;
using WhisperLoom.App.Management;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App;

/// <summary>CLI-Verben aus der IPC bzw. vom eigenen Start (aus AppHost.cs ausgelagert, Repo-Regel ≤ 400 Zeilen; W3-PERF N9).</summary>
public sealed partial class AppHost
{
    /// <summary>Führt ein Verb aus (aus der IPC oder vom eigenen Start). Läuft auf dem UI-Thread.</summary>
    public async Task<IpcResponse> ExecuteAsync(string verb, IReadOnlyList<string> args)
    {
        string message;
        var ok = true;
        switch (verb)
        {
            case "toggle": message = await Controller.ToggleAsync(); break;
            case "start": message = Controller.Start(); break;
            case "stop": message = await Controller.StopAsync(); break;
            case "cancel": message = Controller.Cancel(); break;
            case "pause": message = Controller.Pause(); break;
            case "resume": message = Controller.Resume(); break;
            case "status": message = "status"; break;
            case "reload": message = await ReloadAsync(); break;
            case "stage" when Controller.StageLocked:
                ok = false;
                message = "stage-locked";
                break;
            case "stage":
                try
                {
                    message = "stage=" + Controller.SetStage(args.Count > 0 ? args[0] : "next").Key();
                }
                catch (ArgumentException)
                {
                    ok = false;
                    message = "stage-unavailable";
                }
                break;
            case "reprocess":
                try
                {
                    message = await Controller.ReprocessAsync(args.Count > 0 ? args[0] : Controller.Stage.Key(), args.Count > 1 ? args[1] : null);
                }
                catch (ArgumentException)
                {
                    ok = false;
                    message = "stage-unavailable";
                }
                break;
            case "reinsert":
                message = Reinsert is { } reinsert ? await reinsert() : await CopyAsync(await Controller.LatestTextAsync());
                break;
            case "recent-insert":
                message = await RecentInsertAsync(args.Count > 0 ? args[0] : "0");
                ok = message != "no-entry";
                break;
            case "retry":
                message = await Controller.RetryAsync();
                ok = message == "done";
                break;
            case "process-selection" when ProcessSelection is null:
                ok = false;
                message = "unavailable";
                break;
            case "process-selection":
                message = await ProcessSelection(args.Count > 0 ? args[0] : null, false);
                ok = message is "picker" or "done" or "instruction" or "busy";
                break;
            case "focus-probe":
                message = ProbeFocus is { } probe ? await probe() : "Unknown 0";
                break;
            case "history-copy":
                message = await HistoryCopyAsync(args.Count > 0 ? args[0] : "0");
                ok = message == "copied";
                break;
            case "show": ShowWindow?.Invoke("stage"); message = "shown"; break;
            case "settings" or "help": ShowWindow?.Invoke(verb); message = "shown"; break;
            case "setup":
                PendingJoin = args.Count == 2 ? new JoinLink(args[0], args[1]) : null;
                ShowWindow?.Invoke("setup");
                message = "shown";
                break;
            case "sync" when Manager is null:
                ok = false;
                message = "not-managed";
                break;
            case "sync":
                message = "sync=" + (await Manager.SyncAsync(CancellationToken.None)).ToString().ToLowerInvariant();
                break;
            case "quit": ShowWindow?.Invoke("quit"); message = "quitting"; break;
            default: ok = false; message = "unknown-verb"; break;
        }
        if (message is "no-entry")
        {
            ok = false;
        }
        if (message is "audio-unavailable" or "error" or "busy")
        {
            ok = message == "busy";
        }
        return new IpcResponse(ok, message, await StatusAsync());
    }
}
