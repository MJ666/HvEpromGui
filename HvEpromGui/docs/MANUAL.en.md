# HV-EPROM Programmer GUI - User Manual

**Version 1.0.0** - Deutsche Fassung: [HANDBUCH.de.md](HANDBUCH.de.md)

A Windows front end for the HVEPROM EPROM programmer by Matthew Millman. It replaces the command line tool
[hvepromcmd](https://github.com/inaxeon/hvepromcmd) with a graphical program that has a hex editor, checksums
and the ability to burn sections of large files into an EPROM.

> **Status:** the serial protocol was ported from the hvepromcmd source code. The program has been tested with
> **2708 EPROMs: reading, writing and verifying work**. The other device types follow the hvepromcmd logic as
> well but have not been tested on real hardware yet; try them with a device you can afford to lose.

---

## 1. Contents

1. [Requirements and installation](#2-requirements-and-installation)
2. [Connecting the programmer](#3-connecting-the-programmer)
3. [Main window](#4-main-window)
4. [Supported devices](#5-supported-devices)
5. [Working with files and the buffer](#6-working-with-files-and-the-buffer)
6. [Burn sections](#7-burn-sections)
7. [Programmer operations](#8-programmer-operations)
8. [Hex editor](#9-hex-editor)
9. [Checksums](#10-checksums)
10. [Typical workflows](#11-typical-workflows)
11. [Troubleshooting](#12-troubleshooting)
12. [Safety notes](#13-safety-notes)
13. [Protocol summary (for developers)](#14-protocol-summary-for-developers)
14. [Building, versioning, license, changelog](#15-building-versioning-license-changelog)

---

## 2. Requirements and installation

| Item | Requirement |
|------|-------------|
| Operating system | Windows 10 or 11 |
| Runtime | .NET 8 Desktop Runtime (to build: .NET 8 SDK) |
| Hardware | HVEPROM programmer with its serial (USB) connection and the matching socket shield |
| Supply | 12 V supply for the programmer (it measures its own supply voltage) |

Build and run:

```
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false
```

The documentation in the `docs` folder is copied next to the executable and can be opened from the
**Help** menu (F1 = English manual). It opens as an HTML page in your web browser. The same text is also provided
as Markdown (`.md`) files; the HTML pages are generated from them.

---

## 3. Connecting the programmer

1. Connect the programmer to the PC and switch on its 12 V supply.
2. In the top bar choose the **Port**. The list shows the name that the Windows Device Manager uses, for example
   `COM3 - Arduino Mega 2560` or `COM5 - USB Serial Port`. The names are looked up in the background, so the
   plain `COMx` names appear first and the descriptions follow after a moment. The **↻** button rescans the ports,
   for example after plugging in the cable.
3. Choose the **Baud** rate (9600, 38400 or 115200). The default is **38400**, the same as the command line
   tool; it must match the firmware of the programmer.
4. Choose the **Device** (see [section 5](#5-supported-devices)).
5. Click **Connect**. The program opens the port (8 data bits, no parity, 1 stop bit, no handshake) and
   measures the supply voltage; the result appears in the log. If the board does not answer immediately (boards
   such as the Arduino reset when the port is opened) the program waits two seconds and tries once more.
6. **Disconnect** closes the port again. The port, baud rate and the **Connect** button are locked while an
   operation is running.

---

## 4. Main window

| Area | Purpose |
|------|---------|
| Menu bar | File, Edit, View and Help commands |
| Top bar | Port, baud rate, device, connect button |
| Center | Hex editor with the buffer |
| Right | **Burn section** and **Programmer** controls |
| Bottom left | Progress bar and log |
| Bottom right | **Checksums** |
| Status bar | Buffer size, cursor offset and value, selection |

The title bar shows the program version, the file name and a `*` when the buffer has unsaved changes.

---

## 5. Supported devices

The list follows hvepromcmd. "Fill" is the byte value of an unprogrammed cell that the program uses for the
parts of the device image that are not covered by your data.

| Device | Size | Write mode | Default | Fill |
|--------|------|------------|---------|------|
| 1702A | 256 B | fixed passes | 32 passes | 00 |
| 2704 | 512 B | fixed passes | 100 passes | FF |
| 2708 | 1 KB | fixed passes | 100 passes | FF |
| TMS2716 | 2 KB | fixed passes | 100 passes | FF |
| MCM6876x | 8 KB | hit until set | 5 retry cycles | FF |
| 8741 | 1 KB | hit until set | - | 00 |
| 8742 | 2 KB | hit until set | - | 00 |
| 8748 | 1 KB | hit until set | - | 00 |
| 8749 | 2 KB | hit until set | - | 00 |
| 8041 | 1 KB | hit until set | - | FF |
| 8042 | 2 KB | hit until set | - | FF |
| 8048 | 1 KB | hit until set | - | FF |
| 8049 | 2 KB | hit until set | - | FF |
| 8050 | 4 KB | hit until set | - | FF |
| 8755 | 2 KB | hit until set | - | FF |

**Write modes**

* **Fixed passes** - the whole image is sent once per pass; the number of passes is set in **Passes**.
* **Hit until set** - one pass in which the programmer itself repeats each byte until it reads back correctly.
  After writing, the log shows the largest number of writes needed for a single byte and the total number of
  writes. The checkbox **Fixed passes instead of "hit until set" (-m)** switches these devices to fixed passes
  (corresponds to `-m` of the command line tool); then **Passes** is used. **Retry cycles** applies to the
  MCM6876x only.

Changing the device resets passes, retry cycles and the section to the defaults of that device. The buffer
itself is kept.

**Supply voltage check.** Before every operation the programmer measures its 12 V supply. For the 2704 and 2708
the value must be between 11.5 V and 13.0 V, for all other devices between 10 V and 14 V; otherwise the
operation is refused.

---

## 6. Working with files and the buffer

The **buffer** is the image shown in the hex editor. It can be any size from 1 byte up to 16 MB; it is not tied
to the size of the device.

| Command | Description |
|---------|-------------|
| File > Open (Ctrl+O) | Loads `.bin`/`.rom` as raw bytes, or `.hex`/`.ihx` as Intel HEX. Intel HEX is placed at its real addresses from 0 to the highest used address, gaps are filled with FF. |
| File > New buffer (Ctrl+N) | Empty buffer (FF / 00 depending on the device) with the size of the device. |
| File > Resize buffer | Makes the buffer larger (padded with FF) or smaller (data at the end is lost, after confirmation). The size is entered in hex. |
| File > Save as BIN (Ctrl+S) | Saves the whole buffer as raw bytes. |
| File > Save as Intel HEX | Saves the whole buffer; addresses equal buffer offsets (extended linear address records above 64 KB). |
| File > Save section as BIN | Saves only the current burn section. |

When the buffer is larger than the device, the log reminds you to choose a section or bank.

---

## 7. Burn sections

A **section** says which part of the buffer is written to which part of the device. It is the key to
burning large files into small EPROMs.

| Field | Meaning |
|-------|---------|
| **Source offset (hex)** | First byte of the section in the buffer |
| **Length (hex)** | Number of bytes |
| **Device offset (hex)** | Address inside the device where the first byte goes (normally 0) |

The section is highlighted yellow in the hex editor and summarised below the buttons, including the bank number
(`bank 2/4` = the second device-sized block of the buffer). Invalid values (non-hex text, section beyond the end
of the buffer or of the device) are shown in the same place.

Buttons:

* **From selection** (Ctrl+B) - uses the selected bytes as source. If the selection is larger than the device
  it is shortened to fit.
* **◀ Bank / Bank ▶** - moves the source offset by one device size and sets the length to a full device (or the
  rest of the buffer). The device offset is reset to 0.

By default (after loading a file or changing the device) the section is the first device-sized block of the
buffer.

**How a section is written.** The program builds a complete device image: the area outside your section is
filled with the "unprogrammed" value of the device (FF, or 00 for 8741/8742/8748/8749/1702A) and your section is
copied in at the device offset. Because programming can only change cells from the erased to the programmed state,
the area outside the section stays untouched on a blank device. This also allows you to burn **several sections
one after the other into the same device** - turn **Blank check before writing** off for the second and later
sections.

**Verify** compares only the section (device bytes from the device offset against buffer bytes from the source
offset).

**Read: insert into buffer at source offset.** When this box is ticked, **Read** does not replace the buffer.
Instead the device range (device offset, length) is copied into the buffer at the source offset; the buffer grows
(FF padded) if needed. This is how you assemble one large image from several EPROMs.

---

## 8. Programmer operations

All operations need a connection. While one is running, the **Cancel** button is active, a progress bar is shown and
the editor stays usable. The log shows every step with a time stamp. After each operation (also after an error or
a cancel) the program sends a reset to the programmer.

| Button | What it does |
|--------|--------------|
| **Measure 12 V** | Reads and checks the supply voltage. |
| **Blank check** | Checks if the whole device is blank; if not, the log shows the first non-blank offset and its value. |
| **Read** | Reads the whole device. By default the buffer is replaced by the device contents, the file name is cleared and the contents become the *reference* (see below). The CRC-32 of the data is logged. With *insert into buffer* only a range is inserted. |
| **Verify section** | Reads the device and compares the section. The first difference is selected in the editor and logged; the number of differing bytes is reported. |
| **Write section** | Asks for confirmation, checks the supply, optionally runs the blank check, writes the section and optionally verifies it. |
| **Cancel** | Stops after the current block. A partially programmed device is possible - verify if in doubt. |

**Reference (red bytes).** After a read or verify the data read from the device is kept as a reference for the
affected range. Bytes in the editor that differ from the reference are drawn in **red**. Use *View > Clear device
reference* to remove it. Loading a file or changing the device also clears it.

**Options**

* **Blank check before writing** - aborts the write if the device is not blank.
* **Verify section after writing** - reads the device back after the write and compares the section.

**Programming time.** Writing is slow by nature, reading is not.

* EPROMs such as the 2708 are programmed in many passes (default 100). In every pass each address receives a
  short programming pulse; the usual 2708 method uses 1 ms pulses. That adds up to roughly 100 ms per byte, which
  is in the order of **two minutes for a 1 KB 2708** before any communication overhead is counted.
* The PC sends the image in blocks of 8 bytes and waits for an acknowledgement after every block, so one pass
  of a 1 KB device needs 128 round trips and every pass sends the complete device image. Writing a *shorter section*
  therefore does **not** make the write faster.
* **Time display.** Above the log, under the progress bar, a line shows the current phase (*Blank check*,
  *Writing*, *Verifying*, *Read* ...), the percentage, the **elapsed time** and the **estimated remaining time**,
  for example `Writing: 35 %   elapsed 0:42   remaining ~1:18   (total 0:50)`. The estimate appears after a
  couple of seconds and is extrapolated from the progress so far, so it becomes more accurate as the write goes on.
  Each phase (blank check, write, verify) is timed separately and the total of the whole operation is shown
  in brackets. When an operation ends, the line shows the final result, e.g. `Write section finished after 2:05`.
* The log prints the real duration after each write (`Write finished (x s)`) and the total time of every
  operation that took longer than two seconds.
* To keep the time as short as possible: use the highest baud rate that your programmer's firmware supports
  (the communication part shrinks, the pulse time does not); leave **Passes** at the default - a smaller number
  shortens the write in proportion but can leave cells weakly programmed, so verify carefully if you try it.
* A read, blank check or verify of a 1 KB device takes only a few seconds, so leaving blank check and verify
  switched on costs little compared with the write itself.
* Devices in *hit until set* mode (8741/8742/8748/8749/8041/8042/8048/8049/8050/8755/MCM6876x) program each byte
  until it reads back correctly; their time depends on the device and is reported in the log.

---

## 9. Hex editor

The editor shows the offset column, the hex bytes and the ASCII characters. The number of **bytes per row adapts to
the window width in steps of 16** (16, 32, 48, 64 ... up to 256). When you resize the window the same byte
stays at the top. Large buffers get 6- or 8-digit offsets.

**Mouse:** click to place the cursor, drag to select, Shift+click to extend, wheel to scroll. A click in the hex
pane or the ASCII pane selects the pane you type in.

**Keyboard**

| Key | Action |
|-----|--------|
| `0`-`9`, `A`-`F` | Edit the byte at the cursor, one hex digit at a time (hex pane) |
| any printable character | Replace the byte (ASCII pane) |
| Insert | Switch between hex pane and ASCII pane |
| Arrow keys | Move; Up/Down move by one row |
| Page Up / Page Down | Move by one screen |
| Home / End | Start / end of the row; with Ctrl: start / end of the buffer |
| Shift + movement | Extend the selection |
| Ctrl+A | Select all |
| Ctrl+C / Ctrl+V | Copy / paste (hex text `DE AD BE EF` in the hex pane, characters in the ASCII pane) |
| Delete | Fill the selection with FF |
| Ctrl+Z / Ctrl+Y | Undo / redo (the history is cleared when a new image is loaded or the buffer is resized) |
| Ctrl+F | Find hex bytes; Ctrl+Shift+F find text; F3 find next (wraps around) |
| Ctrl+G | Go to address (hex) |
| Ctrl+B | Use the selection as burn section |

The size of the buffer cannot change by typing; use *Resize buffer* instead.

---

## 10. Checksums

The **Checksums** box (bottom right) recalculates live. Choose the range:

* **Whole buffer**
* **Selection**
* **Burn section**

| Name | Description |
|------|-------------|
| Sum-8 (mod 256) | Sum of all bytes modulo 256 |
| Sum-8 two's complement | The byte that makes the 8-bit sum zero (a checksum byte to store in the image) |
| Sum-16 | Sum of all bytes modulo 65536 |
| XOR-8 | XOR of all bytes |
| CRC-8 | Polynomial 0x07, init 0, no reflection |
| CRC-16/CCITT-FALSE | Polynomial 0x1021, init 0xFFFF |
| CRC-16/XMODEM | Polynomial 0x1021, init 0 |
| CRC-16/ARC (IBM) | Polynomial 0x8005 reflected (0xA001), init 0 |
| CRC-32 (IEEE 802.3) | The usual CRC-32 of ZIP/Ethernet |

Check values for the ASCII text `123456789`: CRC-8 `F4`, CCITT-FALSE `29B1`, XMODEM `31C3`, ARC `BB3D`, CRC-32
`CBF43926`. Double-click a row to copy its value to the clipboard.

---

## 11. Typical workflows

**A. Burn a file into a 2708**

1. Connect, choose *2708*.
2. File > Open the `.bin` or `.hex` file.
3. Keep the section defaults (offset 0, length 400 hex = 1 KB).
4. Insert a blank (UV-erased) device, click **Write section** and confirm. Blank check and verify are on by default.

**B. Split a large ROM image over several EPROMs**

1. Load the large file (for example 4 KB) and choose the device size (for example 2708 = 1 KB).
2. The section shows bank 1/4. Burn it, then press **Bank ▶**, insert the next device and burn again.
   Each EPROM contains the bytes of its bank starting at address 0.

**C. Burn several pieces of a file into one device**

1. Select the first piece in the editor and press **From selection** (Ctrl+B); set the device offset.
2. Turn off **Blank check before writing** for every piece after the first and write.

**D. Build one image from several EPROMs**

1. Tick *Read: insert into buffer at source offset*.
2. Set Source offset to where the first device goes (e.g. 0), insert the device, **Read**.
3. Set the next source offset (e.g. 400), insert the next device, **Read**. Save as BIN or Intel HEX.

**E. Compare a device with a file**

Load the file, set the section, click **Verify section**. Differences are red in the editor.

---

## 12. Troubleshooting

| Symptom | Cause / remedy |
|---------|----------------|
| Port list is empty | Cable/driver missing. Check the Device Manager; press ↻. |
| Only `COMx` without a description | The description is read from Windows (WMI); on restricted systems it is not available. The port still works. |
| "Timeout - no response from the programmer" | Wrong baud rate, wrong port, or the programmer has no power. Boards that reset when the port is opened need two seconds; reconnect once. |
| "Invalid response ..." | Wrong baud rate or the port belongs to another device. |
| "Supply voltage outside the allowed range" | Check the 12 V supply of the programmer; allowed range see [section 5](#5-supported-devices). |
| "Programmer reports: no hardware (shield) detected" / "wrong hardware" | The shield for the chosen device type is missing or wrong, or the switch position is wrong. |
| "Device is not blank" | The device contains data. Erase it (UV) or turn off the blank check to program over it. |
| Verify fails after writing | Poor contact, aged device, too few passes (raise **Passes**), wrong voltage. The first differing byte is selected. |
| Writing takes minutes | Normal for EPROMs that are programmed in 100 passes, see *Programming time* in [section 8](#8-programmer-operations). A higher baud rate helps only with the communication part. |
| A device was written with the wrong section | Programming cannot set bits back to 1; the device must be UV-erased (EPROM types). |

---

## 13. Safety notes

* The programmer generates high programming voltages. Insert and remove devices only when the programmer is idle
  and, if the hardware requires it, with the supply switched off.
* Check orientation of the device in the socket and the shield/switch settings before you write.
* Cancelling during a write can leave a device partially programmed.
* Only the 2708 has been tested on real hardware so far; for all other device types verify the results.

---

## 14. Protocol summary (for developers)

Serial link 8N1, no handshake, read timeout 3 s. Every command is `cmd, ~cmd, arguments`. The programmer answers
with `cmd, error`. Errors: `00` ok, `01` proceed (dual socket), `02` complete, `09` not blank, others are
failures (`03` bad command, `04` not supported, `05` no shield, `06` wrong shield, `07` wrong switch position,
`0A` retries exceeded).

| Command | Code | Arguments | Data |
|---------|------|-----------|------|
| Start write | `10` | device, hit-until-set flag, retries | - |
| Write chunk | `11` | 8 data bytes (after the header) | final answer `02`, then 5 result bytes |
| Start read | `12` | device | - |
| Read chunk | `13` | - | 8 bytes after the answer, final answer `02` |
| Start blank check | `14` | device | - |
| Blank check | `15` | - | `02` = blank, otherwise 3 bytes (offset hi, lo, value) |
| Reset | `16` | - | - |
| Measure 12 V | `17` | - | 2 bytes, big endian, hundredths of a volt |

Device codes: 1702A `00`, 2704 `01`, 2708 `02`, MCM6876x `03`, 8748 `04`, 8749 `05`, 8741 `06`, 8742 `07`,
8048 `08`, 8049 `09`, 8050 `0A`, 8755 `0B`, 8041 `0C`, 8042 `0D`, TMS2716 `0E`.

---

## 15. Building, versioning, license, changelog

**Source files**

| File | Content |
|------|---------|
| `Program.cs` | Entry point |
| `MainForm.cs` | Main window and all operations |
| `HexEditor.cs` | Hex editor control |
| `Programmer.cs` | Serial protocol and device table |
| `PortInfo.cs` | COM ports with Device Manager names |
| `Checksums.cs`, `IntelHex.cs` | Checksums and Intel HEX |
| `AppInfo.cs` | Program name and version |

**Version number.** The version is set in one place: `<Version>` in `HvEpromGui.csproj`. It appears in the title
bar and in *Help > About*. Increase it there for every release.

**License.** The protocol implementation is derived from hvepromcmd by Matthew Millman (GPL v2 or later);
this program is therefore also distributed under **GPL-2.0-or-later**.

**Changelog**

| Version | Changes |
|---------|---------|
| 1.0.0 | First numbered release: all hvepromcmd device types, hex editor with adaptive row length, undo/redo, search, checksums (sums and CRCs), BIN and Intel HEX files up to 16 MB, section burning with bank navigation, read into buffer, COM port names from the Device Manager, English and German documentation. Tested with 2708 EPROMs (read, write, verify). Progress display with elapsed time, estimated remaining time and total time per operation and phase. Documentation opens as HTML pages in the web browser. |
