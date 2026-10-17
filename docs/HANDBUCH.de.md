# HV-EPROM Programmer GUI - Handbuch

**Version 1.0.0** - English version: [MANUAL.en.md](MANUAL.en.md)

Eine Windows-Oberfläche für den HVEPROM-EPROM-Programmer von Matthew Millman. Sie ersetzt das Kommandozeilen-Tool
[hvepromcmd](https://github.com/inaxeon/hvepromcmd) durch ein grafisches Programm mit Hex-Editor, Prüfsummen
und der Möglichkeit, Abschnitte aus großen Dateien in ein EPROM zu brennen.

> **Stand:** Das serielle Protokoll wurde aus dem Quellcode von hvepromcmd übernommen. Das Programm wurde mit
> **2708-EPROMs getestet: Lesen, Schreiben und Vergleichen funktionieren**. Die übrigen Bausteintypen folgen
> ebenfalls der Logik von hvepromcmd, wurden aber noch nicht an echter Hardware getestet. Probiere sie zuerst mit
> einem Baustein, den du entbehren kannst.

---

## 1. Inhalt

1. [Voraussetzungen und Installation](#2-voraussetzungen-und-installation)
2. [Programmer verbinden](#3-programmer-verbinden)
3. [Hauptfenster](#4-hauptfenster)
4. [Unterstützte Bausteine](#5-unterstützte-bausteine)
5. [Dateien und Puffer](#6-dateien-und-puffer)
6. [Brenn-Abschnitte](#7-brenn-abschnitte)
7. [Programmer-Funktionen](#8-programmer-funktionen)
8. [Hex-Editor](#9-hex-editor)
9. [Prüfsummen](#10-prüfsummen)
10. [Typische Abläufe](#11-typische-abläufe)
11. [Fehlersuche](#12-fehlersuche)
12. [Sicherheitshinweise](#13-sicherheitshinweise)
13. [Protokoll in Kürze (für Entwickler)](#14-protokoll-in-kürze-für-entwickler)
14. [Bauen, Version, Lizenz, Änderungen](#15-bauen-version-lizenz-änderungen)

Die Oberfläche des Programms ist englisch. Die englischen Bezeichnungen stehen deshalb in diesem Handbuch
**fett** oder in `Schreibmaschinenschrift`.

---

## 2. Voraussetzungen und Installation

| Punkt | Anforderung |
|-------|-------------|
| Betriebssystem | Windows 10 oder 11 |
| Laufzeit | .NET 8 Desktop Runtime (zum Bauen: .NET 8 SDK) |
| Hardware | HVEPROM-Programmer mit serieller (USB-)Verbindung und passendem Sockel-Shield |
| Versorgung | 12-V-Netzteil für den Programmer (er misst seine Versorgungsspannung selbst) |

Bauen und starten:

```
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false
```

Die Dokumentation aus dem Ordner `docs` wird neben die ausführbare Datei kopiert und lässt sich über das Menü
**Help** öffnen (F1 = englisches Handbuch, **Dokumentation (Deutsch)** = dieses Handbuch). Sie öffnet sich als
HTML-Seite im Webbrowser. Derselbe Text liegt auch als Markdown-Dateien (`.md`) bei; die HTML-Seiten werden daraus
erzeugt.

---

## 3. Programmer verbinden

1. Programmer an den PC anschließen und die 12-V-Versorgung einschalten.
2. In der oberen Leiste den **Port** wählen. Die Liste zeigt den Namen, den auch der Windows-Geräte-Manager
   verwendet, zum Beispiel `COM3 - Arduino Mega 2560` oder `COM5 - USB Serial Port`. Die Namen werden im Hintergrund
   abgefragt: Zuerst erscheinen die reinen `COMx`-Namen, kurz danach die Beschreibungen. Die Schaltfläche **↻**
   liest die Ports neu ein, zum Beispiel nach dem Anstecken des Kabels.
3. Die **Baud**-Rate wählen (9600, 38400 oder 115200). Voreinstellung ist **38400** wie beim Kommandozeilen-Tool;
   sie muss zur Firmware des Programmers passen.
4. Den **Device** (Baustein) wählen, siehe [Abschnitt 5](#5-unterstützte-bausteine).
5. **Connect** klicken. Das Programm öffnet den Port (8 Datenbits, keine Parität, 1 Stoppbit, kein Handshake)
   und misst die Versorgungsspannung; das Ergebnis steht im Log. Antwortet das Board nicht sofort (Boards wie der
   Arduino starten beim Öffnen des Ports neu), wartet das Programm zwei Sekunden und versucht es noch einmal.
6. **Disconnect** schließt den Port wieder. Port, Baudrate und die Verbinden-Schaltfläche sind gesperrt, solange
   eine Funktion läuft.

---

## 4. Hauptfenster

| Bereich | Zweck |
|---------|-------|
| Menüleiste | Befehle für File, Edit, View und Help |
| Obere Leiste | Port, Baudrate, Baustein, Connect |
| Mitte | Hex-Editor mit dem Puffer |
| Rechts | **Burn section** (Brenn-Abschnitt) und **Programmer** |
| Unten links | Fortschrittsbalken und Log |
| Unten rechts | **Checksums** (Prüfsummen) |
| Statusleiste | Puffergröße, Cursor-Offset und -Wert, Auswahl |

Die Titelleiste zeigt die Programmversion, den Dateinamen und ein `*`, wenn der Puffer ungespeicherte
Änderungen enthält.

---

## 5. Unterstützte Bausteine

Die Liste entspricht hvepromcmd. „Füllwert“ ist der Wert einer unprogrammierten Zelle. Das Programm verwendet ihn
für alle Teile des Baustein-Abbilds, die deine Daten nicht abdecken.

| Baustein | Größe | Schreibmodus | Standard | Füllwert |
|----------|-------|--------------|----------|----------|
| 1702A | 256 B | feste Durchläufe | 32 Durchläufe | 00 |
| 2704 | 512 B | feste Durchläufe | 100 Durchläufe | FF |
| 2708 | 1 KB | feste Durchläufe | 100 Durchläufe | FF |
| TMS2716 | 2 KB | feste Durchläufe | 100 Durchläufe | FF |
| MCM6876x | 8 KB | bis gesetzt | 5 Nachschreib-Zyklen | FF |
| 8741 | 1 KB | bis gesetzt | - | 00 |
| 8742 | 2 KB | bis gesetzt | - | 00 |
| 8748 | 1 KB | bis gesetzt | - | 00 |
| 8749 | 2 KB | bis gesetzt | - | 00 |
| 8041 | 1 KB | bis gesetzt | - | FF |
| 8042 | 2 KB | bis gesetzt | - | FF |
| 8048 | 1 KB | bis gesetzt | - | FF |
| 8049 | 2 KB | bis gesetzt | - | FF |
| 8050 | 4 KB | bis gesetzt | - | FF |
| 8755 | 2 KB | bis gesetzt | - | FF |

**Schreibmodi**

* **Feste Durchläufe** - Das ganze Abbild wird pro Durchlauf einmal gesendet. Die Anzahl stellst du bei
  **Passes** ein.
* **Bis gesetzt (hit until set)** - Ein Durchlauf, in dem der Programmer jedes Byte selbst so lange wiederholt,
  bis es richtig zurückgelesen wird. Nach dem Schreiben zeigt das Log, wie oft das am häufigsten wiederholte Byte
  geschrieben wurde und wie viele Schreibvorgänge es insgesamt waren. Die Checkbox
  **Fixed passes instead of "hit until set" (-m)** schaltet diese Bausteine auf feste Durchläufe um (entspricht
  `-m` im Kommandozeilen-Tool); dann gilt **Passes**. **Retry cycles** (Nachschreib-Zyklen) gibt es nur beim
  MCM6876x.

Beim Wechsel des Bausteins werden Durchläufe, Nachschreib-Zyklen und Abschnitt auf die Standardwerte des neuen
Bausteins gesetzt. Der Puffer bleibt erhalten.

**Spannungsprüfung.** Vor jeder Funktion misst der Programmer seine 12-V-Versorgung. Beim 2704 und 2708 muss sie
zwischen 11,5 V und 13,0 V liegen, bei allen anderen Bausteinen zwischen 10 V und 14 V. Sonst wird die
Funktion verweigert.

---

## 6. Dateien und Puffer

Der **Puffer** ist das Abbild, das im Hex-Editor steht. Er kann beliebig groß sein (1 Byte bis 16 MB) und ist
nicht an die Größe des Bausteins gebunden.

| Befehl | Beschreibung |
|--------|--------------|
| File > Open (Strg+O) | Lädt `.bin`/`.rom` als Rohdaten oder `.hex`/`.ihx` als Intel-HEX. Intel-HEX wird an die echten Adressen von 0 bis zur höchsten belegten Adresse gelegt, Lücken werden mit FF gefüllt. |
| File > New buffer (Strg+N) | Leerer Puffer in Bausteingröße (FF bzw. 00, je nach Baustein). |
| File > Resize buffer | Vergrößert den Puffer (mit FF aufgefüllt) oder verkleinert ihn (Daten am Ende gehen nach Rückfrage verloren). Die Größe wird hexadezimal eingegeben. |
| File > Save as BIN (Strg+S) | Speichert den ganzen Puffer als Rohdaten. |
| File > Save as Intel HEX | Speichert den ganzen Puffer; die Adressen entsprechen den Puffer-Offsets (oberhalb 64 KB mit Extended-Linear-Address-Records). |
| File > Save section as BIN | Speichert nur den aktuellen Brenn-Abschnitt. |

Ist der Puffer größer als der Baustein, weist das Log darauf hin, dass du einen Abschnitt oder eine Bank wählen
sollst.

---

## 7. Brenn-Abschnitte

Ein **Abschnitt** legt fest, welcher Teil des Puffers in welchen Teil des Bausteins geschrieben wird. Damit brennst
du große Dateien in kleine EPROMs.

| Feld | Bedeutung |
|------|-----------|
| **Source offset (hex)** | Erstes Byte des Abschnitts im Puffer |
| **Length (hex)** | Anzahl der Bytes |
| **Device offset (hex)** | Adresse im Baustein, an die das erste Byte kommt (normalerweise 0) |

Der Abschnitt ist im Hex-Editor gelb markiert und unter den Schaltflächen zusammengefasst, einschließlich der
Bank-Nummer (`bank 2/4` = der zweite bausteingroße Block des Puffers). Ungültige Eingaben (kein Hex, Abschnitt
hinter dem Ende des Puffers oder des Bausteins) werden an derselben Stelle gemeldet.

Schaltflächen:

* **From selection** (Strg+B) - übernimmt die markierten Bytes als Quelle. Ist die Auswahl größer als der
  Baustein, wird sie gekürzt.
* **◀ Bank / Bank ▶** - verschiebt den Quell-Offset um eine Bausteingröße und setzt die Länge auf einen ganzen
  Baustein (oder den Rest des Puffers). Der Baustein-Offset wird auf 0 gesetzt.

Standardmäßig (nach dem Laden einer Datei oder dem Wechsel des Bausteins) ist der Abschnitt der erste
bausteingroße Block des Puffers.

**Wie ein Abschnitt geschrieben wird.** Das Programm baut ein vollständiges Baustein-Abbild: Der Bereich außerhalb
des Abschnitts wird mit dem „unprogrammierten“ Wert des Bausteins gefüllt (FF, bei 8741/8742/8748/8749/1702A
00), dein Abschnitt wird am Baustein-Offset eingesetzt. Da Programmieren Zellen nur vom gelöschten in den
programmierten Zustand bringen kann, bleibt der Bereich außerhalb des Abschnitts bei einem leeren Baustein
unverändert. So kannst du **mehrere Abschnitte nacheinander in denselben Baustein brennen** - schalte dafür
**Blank check before writing** beim zweiten und jedem weiteren Abschnitt aus.

**Verify** vergleicht nur den Abschnitt (Bausteinbytes ab Baustein-Offset gegen Pufferbytes ab Quell-Offset).

**Read: insert into buffer at source offset.** Ist dieses Häkchen gesetzt, ersetzt **Read** den Puffer nicht.
Stattdessen wird der Bausteinbereich (Baustein-Offset, Länge) am Quell-Offset in den Puffer kopiert; der Puffer
wächst bei Bedarf (mit FF aufgefüllt). So setzt du aus mehreren EPROMs ein großes Abbild zusammen.

---

## 8. Programmer-Funktionen

Alle Funktionen brauchen eine Verbindung. Während eine läuft, ist **Cancel** aktiv, ein Fortschrittsbalken wird
angezeigt und der Editor bleibt bedienbar. Das Log zeigt jeden Schritt mit Uhrzeit. Nach jeder Funktion (auch nach
Fehlern und Abbruch) sendet das Programm ein Reset an den Programmer.

| Schaltfläche | Funktion |
|--------------|----------|
| **Measure 12 V** | Liest und prüft die Versorgungsspannung. |
| **Blank check** | Prüft, ob der ganze Baustein leer ist; wenn nicht, zeigt das Log den ersten nicht leeren Offset und dessen Wert. |
| **Read** | Liest den ganzen Baustein. Standardmäßig ersetzt der Bausteininhalt den Puffer, der Dateiname wird gelöscht und der Inhalt wird zur *Referenz* (siehe unten). Die CRC-32 der Daten steht im Log. Mit *insert into buffer* wird nur ein Bereich eingefügt. |
| **Verify section** | Liest den Baustein und vergleicht den Abschnitt. Die erste Abweichung wird im Editor markiert und im Log genannt; zusätzlich steht dort die Zahl der abweichenden Bytes. |
| **Write section** | Fragt nach Bestätigung, prüft die Versorgung, führt optional den Blank-Check aus, schreibt den Abschnitt und verifiziert ihn optional. |
| **Cancel** | Bricht nach dem aktuellen Block ab. Ein teilweise programmierter Baustein ist möglich - im Zweifel verifizieren. |

**Referenz (rote Bytes).** Nach Read oder Verify bleiben die gelesenen Bausteindaten für den betroffenen Bereich
als Referenz erhalten. Bytes im Editor, die von der Referenz abweichen, werden **rot** gezeichnet. Mit
*View > Clear device reference* entfernst du sie. Auch Laden einer Datei oder Wechsel des Bausteins löscht
die Referenz.

**Optionen**

* **Blank check before writing** - bricht das Schreiben ab, wenn der Baustein nicht leer ist.
* **Verify section after writing** - liest nach dem Schreiben zurück und vergleicht den Abschnitt.

**Programmierdauer.** Das Schreiben ist von Natur aus langsam, das Lesen nicht.

* EPROMs wie das 2708 werden in vielen Durchläufen programmiert (Standard 100). In jedem Durchlauf bekommt jede
  Adresse einen kurzen Programmierimpuls; beim 2708 sind 1-ms-Impulse das übliche Verfahren. Das summiert sich auf
  etwa 100 ms pro Byte, also in der Größenordnung von **zwei Minuten für ein 2708 mit 1 KB**, noch ohne den
  Aufwand für die Übertragung.
* Der PC sendet das Abbild in Blöcken zu 8 Bytes und wartet nach jedem Block auf die Bestätigung. Ein Durchlauf
  eines 1-KB-Bausteins braucht also 128 Hin-und-Rück-Übertragungen, und jeder Durchlauf sendet das vollständige
  Baustein-Abbild. Einen *kürzeren Abschnitt* zu schreiben macht das Schreiben daher **nicht** schneller.
* **Zeitanzeige.** Über dem Log, unter dem Fortschrittsbalken, zeigt eine Zeile die aktuelle Phase (*Blank check*,
  *Writing*, *Verifying*, *Read* ...), den Prozentwert, die **verstrichene Zeit** und die **geschätzte Restzeit**,
  zum Beispiel `Writing: 35 %   elapsed 0:42   remaining ~1:18   (total 0:50)`. Die Schätzung erscheint nach
  wenigen Sekunden und wird aus dem bisherigen Fortschritt hochgerechnet; sie wird mit dem Fortschreiben des
  Schreibens genauer. Jede Phase (Blank-Check, Schreiben, Verify) wird einzeln gemessen, die Gesamtzeit der ganzen
  Funktion steht in Klammern. Am Ende der Funktion zeigt die Zeile das Ergebnis, z. B.
  `Write section finished after 2:05`.
* Das Log zeigt nach jedem Schreiben die tatsächliche Dauer (`Write finished (x s)`) sowie die Gesamtzeit jeder
  Funktion, die länger als zwei Sekunden gedauert hat.
* So bleibt die Dauer möglichst kurz: Die höchste Baudrate verwenden, die die Firmware deines Programmers
  unterstützt (der Übertragungsanteil sinkt, die Impulszeit nicht). **Passes** auf dem Standardwert lassen - eine
  kleinere Zahl verkürzt das Schreiben im gleichen Verhältnis, kann aber schwach programmierte Zellen
  hinterlassen; wer es ausprobiert, sollte gründlich verifizieren.
* Lesen, Blank-Check oder Verify eines 1-KB-Bausteins dauert nur wenige Sekunden. Blank-Check und Verify
  eingeschaltet zu lassen kostet deshalb im Vergleich zum Schreiben kaum Zeit.
* Bausteine im Modus *bis gesetzt* (8741/8742/8748/8749/8041/8042/8048/8049/8050/8755/MCM6876x) programmieren jedes
  Byte, bis es richtig zurückgelesen wird. Ihre Dauer hängt vom Baustein ab und steht im Log.

---

## 9. Hex-Editor

Der Editor zeigt Offset-Spalte, Hex-Bytes und ASCII-Zeichen. Die **Anzahl der Bytes pro Zeile passt sich der
Fensterbreite in 16-Byte-Schritten an** (16, 32, 48, 64 ... bis 256). Beim Ändern der Fenstergröße bleibt dasselbe
Byte oben sichtbar. Große Puffer bekommen 6- oder 8-stellige Offsets.

**Maus:** Klick setzt den Cursor, Ziehen markiert, Shift+Klick erweitert, Mausrad scrollt. Ein Klick in den Hex-
oder ASCII-Bereich legt fest, in welchem Bereich du tippst.

**Tastatur**

| Taste | Aktion |
|-------|--------|
| `0`-`9`, `A`-`F` | Byte am Cursor ändern, eine Hex-Ziffer nach der anderen (Hex-Bereich) |
| druckbares Zeichen | Byte ersetzen (ASCII-Bereich) |
| Einfg (Insert) | Zwischen Hex- und ASCII-Bereich wechseln |
| Pfeiltasten | Bewegen; Auf/Ab bewegt um eine Zeile |
| Bild auf / Bild ab | Um eine Bildschirmseite bewegen |
| Pos1 / Ende | Zeilenanfang / -ende; mit Strg: Pufferanfang / -ende |
| Shift + Bewegung | Auswahl erweitern |
| Strg+A | Alles markieren |
| Strg+C / Strg+V | Kopieren / Einfügen (Hex-Text `DE AD BE EF` im Hex-Bereich, Zeichen im ASCII-Bereich) |
| Entf | Auswahl mit FF füllen |
| Strg+Z / Strg+Y | Rückgängig / Wiederholen (der Verlauf wird beim Laden eines neuen Abbilds oder beim Ändern der Puffergröße gelöscht) |
| Strg+F | Hex-Bytes suchen; Strg+Umschalt+F Text suchen; F3 weitersuchen (springt am Ende nach vorn) |
| Strg+G | Zu Adresse springen (hex) |
| Strg+B | Auswahl als Brenn-Abschnitt übernehmen |

Die Größe des Puffers lässt sich nicht durch Tippen ändern, dafür gibt es *Resize buffer*.

---

## 10. Prüfsummen

Das Feld **Checksums** (unten rechts) rechnet laufend neu. Bereich wählbar:

* **Whole buffer** - gesamter Puffer
* **Selection** - Auswahl
* **Burn section** - Brenn-Abschnitt

| Name | Beschreibung |
|------|--------------|
| Sum-8 (mod 256) | Summe aller Bytes modulo 256 |
| Sum-8 two's complement | Das Byte, das die 8-Bit-Summe zu null macht (Prüfbyte zum Ablegen im Abbild) |
| Sum-16 | Summe aller Bytes modulo 65536 |
| XOR-8 | XOR aller Bytes |
| CRC-8 | Polynom 0x07, Startwert 0, nicht gespiegelt |
| CRC-16/CCITT-FALSE | Polynom 0x1021, Startwert 0xFFFF |
| CRC-16/XMODEM | Polynom 0x1021, Startwert 0 |
| CRC-16/ARC (IBM) | Polynom 0x8005 gespiegelt (0xA001), Startwert 0 |
| CRC-32 (IEEE 802.3) | Das übliche CRC-32 von ZIP/Ethernet |

Prüfwerte für den ASCII-Text `123456789`: CRC-8 `F4`, CCITT-FALSE `29B1`, XMODEM `31C3`, ARC `BB3D`, CRC-32
`CBF43926`. Ein Doppelklick auf eine Zeile kopiert den Wert in die Zwischenablage.

---

## 11. Typische Abläufe

**A. Eine Datei in ein 2708 brennen**

1. Verbinden, *2708* wählen.
2. File > Open: `.bin`- oder `.hex`-Datei laden.
3. Abschnitt unverändert lassen (Offset 0, Länge 400 hex = 1 KB).
4. Leeren (UV-gelöschten) Baustein einsetzen, **Write section** klicken und bestätigen. Blank-Check und Verify
   sind standardmäßig eingeschaltet.

**B. Ein großes ROM-Abbild auf mehrere EPROMs aufteilen**

1. Große Datei laden (zum Beispiel 4 KB) und die Bausteingröße wählen (zum Beispiel 2708 = 1 KB).
2. Der Abschnitt zeigt Bank 1/4. Brennen, dann **Bank ▶** drücken, nächsten Baustein einsetzen und erneut
   brennen. Jedes EPROM enthält die Bytes seiner Bank ab Adresse 0.

**C. Mehrere Stücke einer Datei in einen Baustein brennen**

1. Das erste Stück im Editor markieren und **From selection** (Strg+B) drücken; Baustein-Offset setzen.
2. Für jedes weitere Stück **Blank check before writing** ausschalten und schreiben.

**D. Aus mehreren EPROMs ein Abbild zusammensetzen**

1. Häkchen *Read: insert into buffer at source offset* setzen.
2. Quell-Offset auf die Stelle setzen, an die der erste Baustein kommt (z. B. 0), Baustein einsetzen, **Read**.
3. Nächsten Quell-Offset setzen (z. B. 400), nächsten Baustein einsetzen, **Read**. Dann als BIN oder Intel-HEX
   speichern.

**E. Baustein mit einer Datei vergleichen**

Datei laden, Abschnitt setzen, **Verify section** klicken. Abweichungen sind im Editor rot.

---

## 12. Fehlersuche

| Symptom | Ursache / Abhilfe |
|---------|-------------------|
| Portliste leer | Kabel oder Treiber fehlt. Geräte-Manager prüfen; ↻ drücken. |
| Nur `COMx` ohne Beschreibung | Die Beschreibung wird von Windows (WMI) gelesen; auf eingeschränkten Systemen ist sie nicht verfügbar. Der Port funktioniert trotzdem. |
| „Timeout - no response from the programmer“ | Falsche Baudrate, falscher Port oder der Programmer hat keine Versorgung. Boards, die beim Öffnen des Ports neu starten, brauchen zwei Sekunden; einmal neu verbinden. |
| „Invalid response ...“ | Falsche Baudrate oder der Port gehört zu einem anderen Gerät. |
| „Supply voltage outside the allowed range“ | 12-V-Versorgung des Programmers prüfen; Bereich siehe [Abschnitt 5](#5-unterstützte-bausteine). |
| „Programmer reports: no hardware (shield) detected“ / „wrong hardware“ | Das Shield für den gewählten Bausteintyp fehlt oder ist falsch, oder die Schalterstellung stimmt nicht. |
| „Device is not blank“ | Der Baustein enthält Daten. Löschen (UV) oder den Blank-Check ausschalten, um darüber zu programmieren. |
| Verify schlägt nach dem Schreiben fehl | Schlechter Kontakt, gealterter Baustein, zu wenige Durchläufe (**Passes** erhöhen), falsche Spannung. Das erste abweichende Byte ist markiert. |
| Das Schreiben dauert Minuten | Normal bei EPROMs, die in 100 Durchläufen programmiert werden, siehe *Programmierdauer* in [Abschnitt 8](#8-programmer-funktionen). Eine höhere Baudrate verkürzt nur den Übertragungsanteil. |
| Baustein mit falschem Abschnitt beschrieben | Programmieren kann Bits nicht auf 1 zurücksetzen; der Baustein muss (bei EPROM-Typen) mit UV gelöscht werden. |

---

## 13. Sicherheitshinweise

* Der Programmer erzeugt hohe Programmierspannungen. Bausteine nur einsetzen und entnehmen, wenn der Programmer
  im Leerlauf ist und, falls die Hardware es verlangt, bei abgeschalteter Versorgung.
* Vor dem Schreiben Einbaulage des Bausteins sowie Shield- und Schaltereinstellungen prüfen.
* Ein Abbruch während des Schreibens kann einen teilweise programmierten Baustein hinterlassen.
* Bisher ist nur das 2708 an echter Hardware getestet; bei allen anderen Bausteintypen die Ergebnisse immer verifizieren.

---

## 14. Protokoll in Kürze (für Entwickler)

Serielle Verbindung 8N1, kein Handshake, Lese-Timeout 3 s. Jeder Befehl lautet `cmd, ~cmd, Argumente`. Der
Programmer antwortet mit `cmd, Fehler`. Fehlercodes: `00` ok, `01` weiter (Dual-Socket), `02` fertig, `09` nicht
leer; weitere sind Fehler (`03` ungültiger Befehl, `04` nicht unterstützt, `05` kein Shield, `06` falsches Shield,
`07` falsche Schalterstellung, `0A` Wiederholungen überschritten).

| Befehl | Code | Argumente | Daten |
|--------|------|-----------|-------|
| Start write | `10` | Baustein, „bis gesetzt“-Flag, Wiederholungen | - |
| Write chunk | `11` | 8 Datenbytes (nach dem Kopf) | Schlussantwort `02`, dann 5 Ergebnisbytes |
| Start read | `12` | Baustein | - |
| Read chunk | `13` | - | 8 Bytes nach der Antwort, Schlussantwort `02` |
| Start blank check | `14` | Baustein | - |
| Blank check | `15` | - | `02` = leer, sonst 3 Bytes (Offset hoch, tief, Wert) |
| Reset | `16` | - | - |
| Measure 12 V | `17` | - | 2 Bytes, Big Endian, hundertstel Volt |

Bausteincodes: 1702A `00`, 2704 `01`, 2708 `02`, MCM6876x `03`, 8748 `04`, 8749 `05`, 8741 `06`, 8742 `07`,
8048 `08`, 8049 `09`, 8050 `0A`, 8755 `0B`, 8041 `0C`, 8042 `0D`, TMS2716 `0E`.

---

## 15. Bauen, Version, Lizenz, Änderungen

**Quelldateien**

| Datei | Inhalt |
|-------|--------|
| `Program.cs` | Einstiegspunkt |
| `MainForm.cs` | Hauptfenster und alle Funktionen |
| `HexEditor.cs` | Hex-Editor-Steuerelement |
| `Programmer.cs` | Serielles Protokoll und Bausteintabelle |
| `PortInfo.cs` | COM-Ports mit Namen aus dem Geräte-Manager |
| `Checksums.cs`, `IntelHex.cs` | Prüfsummen und Intel-HEX |
| `AppInfo.cs` | Programmname und Version |

**Versionsnummer.** Die Version wird an einer einzigen Stelle gesetzt: `<Version>` in `HvEpromGui.csproj`. Sie
erscheint in der Titelleiste und unter *Help > About*. Für jede neue Ausgabe dort erhöhen.

**Lizenz.** Die Protokollimplementierung ist von hvepromcmd von Matthew Millman abgeleitet (GPL v2 oder neuer);
dieses Programm wird deshalb ebenfalls unter **GPL-2.0-or-later** weitergegeben.

**Änderungen**

| Version | Änderungen |
|---------|------------|
| 1.0.0 | Erste nummerierte Ausgabe: alle Bausteintypen aus hvepromcmd, Hex-Editor mit anpassbarer Zeilenlänge, Rückgängig/Wiederholen, Suche, Prüfsummen (Summen und CRCs), BIN- und Intel-HEX-Dateien bis 16 MB, Abschnittsbrennen mit Bank-Navigation, Lesen in den Puffer, COM-Port-Namen aus dem Geräte-Manager, englische und deutsche Dokumentation. Mit 2708-EPROMs getestet (Lesen, Schreiben, Vergleichen). Fortschrittsanzeige mit verstrichener Zeit, geschätzter Restzeit und Gesamtzeit pro Funktion und Phase. Die Dokumentation öffnet sich als HTML-Seite im Webbrowser. |
