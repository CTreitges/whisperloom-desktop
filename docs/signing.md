# Signieren durch die Firma / Code signing by your company

[Deutsch](#deutsch) · [English](#english)

## Deutsch

WhisperLoom wird **unsigniert** veröffentlicht (`.github/workflows/release.yml` hat bewusst keinen Signierschritt).
Firmen, die nur signierte Software zulassen, signieren selbst mit ihrem Zertifikat und `signtool.exe`
(Windows SDK). Dieses Dokument sagt, welche Dateien das sind und an welcher Stelle im Ablauf.

### Pakete im Release

| Datei | Zweck | Aktualisiert sich selbst |
|---|---|---|
| `WhisperLoom-win-Setup.exe` | Installation pro Benutzer nach `%LocalAppData%\WhisperLoom`, ohne Admin (Velopack) | ja, aus den GitHub-Releases (Prüfung beim Start und täglich, Installation erst nach Bestätigung) |
| `WhisperLoom-<version>-win-x64-portable.zip` | Ordner zum Entpacken, ohne Installation und ohne Updater | nein |
| `WhisperLoom.AppImage` | Linux | ja (Velopack ersetzt die Datei) |

### Weg 1 (empfohlen für Firmen): portable ZIP signieren und verteilen

Die ZIP enthält nur das `dotnet publish` der App, kein `Update.exe`. Signiert wird **nach dem Entpacken, vor der
Verteilung**:

1. ZIP entpacken, z. B. nach `C:\build\WhisperLoom`.
2. Alle Programmdateien ohne gültige Signatur signieren. Das sind `WhisperLoom.exe`, `WhisperLoom.dll`,
   `WhisperLoom.Core.dll` sowie die mitgelieferten nativen und .NET-Bibliotheken ohne Herstellersignatur
   (z. B. `portaudio.dll`, Avalonia- und Skia-DLLs). Bereits von Microsoft signierte .NET-Laufzeitdateien bleiben,
   wie sie sind:

   ```powershell
   $files = Get-ChildItem C:\build\WhisperLoom -Recurse -Include *.exe, *.dll |
       Where-Object { (Get-AuthenticodeSignature $_.FullName).Status -ne 'Valid' }
   signtool sign /fd SHA256 /td SHA256 /tr http://timestamp.digicert.com /sha1 <Zertifikat-Fingerabdruck> $files.FullName
   ```

3. Den Ordner (neu gezippt oder per Softwareverteilung) an die Rechner verteilen.
4. **Updates abschalten** per Provisioning ([provisioning.md](provisioning.md)): Abschnitt
   `"policy": { "updatesEnabled": false }`. Die portable Fassung hat ohnehin keinen Updater; die Richtlinie sorgt dafür,
   dass auch eine später doch per Setup installierte Fassung nie die unsignierte Version aus GitHub nachlädt.

### Weg 2: Setup selbst bauen und dabei signieren

Wer das Setup (Installation pro Benutzer, Startmenü-Eintrag) verteilen will, baut es selbst aus dem Quellcode.
Signiert wird **vor bzw. während `vpk pack`**, denn `vpk pack` packt die Dateien in das Paket (`.nupkg`) und das
Setup; danach lässt sich nichts mehr signieren, ohne die Prüfsummen im Feed zu brechen.

1. `dotnet publish src/WhisperLoom.App -c Release -r win-x64 --self-contained -p:PublishReadyToRun=true -p:Version=<x.y.z> -o publish`
   (wie `build/pack.py`; ReadyToRun ohne Composite, die Microsoft-Signaturen der .NET-Laufzeit bleiben erhalten)
2. Signieren, am einfachsten durch Velopack selbst: Es signiert dann die App-Dateien, `Update.exe`, den Start-Stub
   und `Setup.exe` in einem Durchgang mit `signtool`:

   ```
   dotnet tool restore
   dotnet vpk pack --packId WhisperLoom --packVersion <x.y.z> --packDir publish --mainExe WhisperLoom.exe ^
     --packTitle WhisperLoom --icon src\WhisperLoom.App\Assets\whisperloom.ico --noPortable ^
     --signParams "/fd SHA256 /td SHA256 /tr http://timestamp.digicert.com /sha1 <Zertifikat-Fingerabdruck>"
   ```

   Alternativ die Dateien in `publish` vorher selbst signieren (wie in Weg 1, Schritt 2); `Update.exe`, Stub und
   `Setup.exe` von Velopack bleiben dann aber unsigniert.
3. Updates abschalten (`updatesEnabled=false`, wie oben) **oder** einen eigenen Feed betreiben: Eine signierte
   Installation, die sich aus den GitHub-Releases aktualisiert, holt sich sonst die unsignierte Fassung.
   Zum Testen eines Feeds: `"%LocalAppData%\WhisperLoom\WhisperLoom.exe" update --feed <ordner|url>`.

`build/pack.py` macht Schritt 1 und 2 ohne Signatur; es eignet sich als Vorlage.

### Unsignierte Builds und SmartScreen

Beim ersten Start einer unsignierten `WhisperLoom-win-Setup.exe` bzw. `WhisperLoom.exe` zeigt Windows SmartScreen
„Der Computer wurde durch Windows geschützt“. Weiter geht es über **Weitere Informationen → Trotzdem ausführen**.
Das ist bei unsignierter Software normal und verschwindet erst mit einer Signatur (bzw. mit wachsender Reputation
eines Zertifikats). In verwalteten Umgebungen kann die IT die Datei per Hash freigeben oder selbst signieren (oben).

### Was nicht im Paket ist

Die lokale Erkennung lädt `whisper-server` und die Modelle erst bei Bedarf herunter (in den Datenordner, Prüfung per
SHA256 gegen fest eingetragene Werte). Diese Dateien sind nicht Teil der Pakete und nicht von der Firma signiert.
Wo AppLocker oder WDAC nur signierte Programme zulässt, `whisper-server` per Hash freigeben oder die lokale Erkennung
nicht nutzen.

### Linux

Das AppImage braucht keine Signatur. Updates lädt es als Paket (`WhisperLoom-<version>-linux-full.nupkg`); Velopack
prüft das Paket gegen die SHA256 in `releases.linux.json` des Releases.

## English

WhisperLoom is published **unsigned** (`.github/workflows/release.yml` deliberately has no signing step). Companies
that only allow signed software sign it themselves with their certificate and `signtool.exe` (Windows SDK). This
document lists the files and where in the process they are signed.

### Release packages

| File | Purpose | Updates itself |
|---|---|---|
| `WhisperLoom-win-Setup.exe` | Per-user install into `%LocalAppData%\WhisperLoom`, no admin rights (Velopack) | yes, from GitHub Releases (checked at start and daily, installed only after confirmation) |
| `WhisperLoom-<version>-win-x64-portable.zip` | Folder to unzip, no installation, no updater | no |
| `WhisperLoom.AppImage` | Linux | yes (Velopack replaces the file) |

### Option 1 (recommended for companies): sign and distribute the portable ZIP

The ZIP only contains the app's `dotnet publish` output, no `Update.exe`. Sign **after unzipping, before
distribution**:

1. Unzip, e.g. to `C:\build\WhisperLoom`.
2. Sign every program file without a valid signature: `WhisperLoom.exe`, `WhisperLoom.dll`, `WhisperLoom.Core.dll`
   and the bundled native and .NET libraries without a vendor signature (e.g. `portaudio.dll`, Avalonia and Skia
   DLLs). .NET runtime files already signed by Microsoft stay as they are:

   ```powershell
   $files = Get-ChildItem C:\build\WhisperLoom -Recurse -Include *.exe, *.dll |
       Where-Object { (Get-AuthenticodeSignature $_.FullName).Status -ne 'Valid' }
   signtool sign /fd SHA256 /td SHA256 /tr http://timestamp.digicert.com /sha1 <certificate-thumbprint> $files.FullName
   ```

3. Distribute the folder (zipped again or through your software distribution).
4. **Turn updates off** through provisioning ([provisioning.md](provisioning.md)): section
   `"policy": { "updatesEnabled": false }`. The portable build has no updater anyway; the policy makes sure that a
   build installed through Setup later never pulls the unsigned version from GitHub.

### Option 2: build Setup yourself and sign it

To distribute Setup (per-user install, Start menu entry), build it from source. Sign **before or during
`vpk pack`**: `vpk pack` puts the files into the package (`.nupkg`) and into Setup; signing afterwards would break the
checksums in the feed.

1. `dotnet publish src/WhisperLoom.App -c Release -r win-x64 --self-contained -p:PublishReadyToRun=true -p:Version=<x.y.z> -o publish`
   (same as `build/pack.py`; ReadyToRun without composite keeps Microsoft's signatures on the .NET runtime)
2. Sign, most simply through Velopack itself. It then signs the app files, `Update.exe`, the launcher stub and
   `Setup.exe` in one pass with `signtool`:

   ```
   dotnet tool restore
   dotnet vpk pack --packId WhisperLoom --packVersion <x.y.z> --packDir publish --mainExe WhisperLoom.exe ^
     --packTitle WhisperLoom --icon src\WhisperLoom.App\Assets\whisperloom.ico --noPortable ^
     --signParams "/fd SHA256 /td SHA256 /tr http://timestamp.digicert.com /sha1 <certificate-thumbprint>"
   ```

   Alternatively sign the files in `publish` yourself first (as in option 1, step 2); Velopack's `Update.exe`, stub
   and `Setup.exe` then stay unsigned.
3. Turn updates off (`updatesEnabled=false`, as above) **or** run your own feed: a signed installation that updates
   from GitHub Releases would otherwise fetch the unsigned build. To test a feed:
   `"%LocalAppData%\WhisperLoom\WhisperLoom.exe" update --feed <folder|url>`.

`build/pack.py` runs steps 1 and 2 without signing; use it as a template.

### Unsigned builds and SmartScreen

On first start of an unsigned `WhisperLoom-win-Setup.exe` or `WhisperLoom.exe`, Windows SmartScreen shows
"Windows protected your PC". Continue via **More info → Run anyway**. This is normal for unsigned software and only
goes away with a signature (or as a certificate's reputation grows). In managed environments IT can allow the file by
hash or sign it itself (above).

### Not part of the packages

Local recognition downloads `whisper-server` and the models on demand (into the data folder, verified by SHA256
against pinned values). These files are not part of the packages and not signed by your company. Where AppLocker or
WDAC only allows signed programs, allow `whisper-server` by hash or do not use local recognition.

### Linux

The AppImage needs no signature. It downloads updates as a package (`WhisperLoom-<version>-linux-full.nupkg`);
Velopack verifies the package against the SHA256 in the release's `releases.linux.json`.
