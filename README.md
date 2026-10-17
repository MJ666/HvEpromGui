# HV-EPROM Programmer GUI

A Windows front end for the [HVEPROM](https://github.com/inaxeon/hvepromcmd) EPROM programmer by Matthew Millman.
It replaces the command line tool with a graphical program: read, write, blank-check and verify EPROMs,
edit the data in a hex editor, and burn just a section of a large file into a device.

![HV-EPROM Programmer GUI](docs/HVEPROMGUI.png)

## Features

- **Hex editor** with hex and ASCII panes, undo/redo, search, copy/paste, and a row length that adapts to the window width
- **Burn sections:** load BIN or Intel HEX files up to 16 MB and write any part of them to the device (bank navigation, device offset); read several EPROMs into one image
- **Checksums** shown live: sum-8/16, XOR, CRC-8, CRC-16 (CCITT, XMODEM, ARC) and CRC-32, for the whole buffer, a selection or the burn section
- **Programmer functions:** measure 12 V, blank check, read, write and verify, with progress, elapsed and remaining time
- **COM ports** listed with their Device Manager names (e.g. `COM15 - Arduino Mega 2560`)
- All device types of hvepromcmd: 1702A, 2704, 2708, TMS2716, MCM6876x, 8741/42/48/49, 8041/42, 8048/49/50, 8755
- English and German documentation

Tested with 2708 EPROMs (read, write, verify). The other device types follow the hvepromcmd protocol but
have not been tested on real hardware yet.

## Build

.NET 8 SDK, Windows 10/11: `dotnet build -c Release`

## License

GPL-2.0-or-later. The serial protocol is derived from hvepromcmd by Matthew Millman.
