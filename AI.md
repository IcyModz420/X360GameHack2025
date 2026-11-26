# Using X360GameHack from a script or an AI agent

X360GameHack can be driven without its window. There are two ways in, and both offer the same commands.

## 1. One command at a time

```
X360GameHack.exe cli <command> [arguments]
```

- Standard output: one JSON object. `"ok": true` on success, or `"ok": false` with an `"error"` text.
- Exit code: `0` on success, `1` on error.
- `X360GameHack.exe cli help` lists every command with its parameters. It is the source of truth.
- `"beta": true` in `cli help` (and `[BETA - not proven end to end]` at the start of an MCP tool's description) marks a command that has not been proven to work end to end yet - for example edits whose files no console has used yet. Prefer proven commands; keep a copy of any file a beta command changes. TESTING.md says how each proven one was tested.
- Start with `cli info <file>` on a file you do not know. Its answer includes a `next` hint naming the commands that fit that kind of file.

## 2. As an MCP server

```
X360GameHack.exe mcp
```

A Model Context Protocol server on standard input / output. Every command is a tool with a description and a typed
input schema, so an AI application that speaks MCP sees them without reading this file. Tools that only read are
marked read-only.

Example entry for an MCP client's configuration:

```json
{
  "mcpServers": {
    "x360gamehack": { "command": "C:\\Path\\To\\X360GameHack.exe", "args": ["mcp"] }
  }
}
```

## Commands

| Command | What it does | Changes files |
|---|---|---|
| `info <file>` | What a file is and its main details, plus which commands fit it | no |
| `list <package>` | Files inside an STFS package (saves, profiles, title updates, DLC, arcade) | no |
| `verify <package>` | Checks every hash; reports the signature state | no |
| `extract <package> <folder>` | Extracts every file | writes the folder |
| `tu-check <update> <game>` | Does this title update fit this game (default.xex, .iso or package)? Title ID, Media ID and version are compared; says where the update belongs on the console | no |
| `tu-extract <package> <folder>` | Takes a title update apart (default.xexp + updated game files, sub-folders kept) | writes the folder; a differing file is kept as `.bak` |
| `rehash <package> [keyvault]` | Rebuilds every hash; signs a CON package when the owner's keyvault file is given | edits in place, keeps `.bak` |
| `create-save <output> <like> <files...>` | Builds a new save package from files, details copied from an existing save | writes a new file |
| `gamesave-list <save> [filter] [count]` | Named values inside a game save, when a free (open) save mod for that game is in the mods folder, plus the one-step actions. Paid save mods are not usable from the command line | reads |
| `gamesave-set <save> <name=value...>` | Changes named values; all or nothing; rehashes and verifies the save | edits in place, keeps .bak |
| `gamesave-action <save> <action>` | Runs a one-step action that gamesave-list names (for example "Max money") | edits in place, keeps .bak |
| `profile <profile>` | Gamertag, gamerscore, games | no |
| `achievements <profile> <titleId>` | Achievements of one game | no |
| `unlock <profile> [titleId or all] [natural or same]` | Unlocks every achievement of one game or of all | edits in place, keeps `.bak` |
| `xex <xex>` | Title ID, image base, entry point, encryption, sections, counts | no |
| `functions <xex> [from] [count]` | Function start addresses | no |
| `imports <xex>` | Imported kernel / XAM / XBDM functions by name | no |
| `disasm <xex> <address> [count]` | PowerPC disassembly | no |
| `decompile <xex> <address>` | The function at the address as C-style code | no |
| `save-image <xex> <output>` | The decrypted, decompressed base file | writes a new file |
| `ghidra <xex> <folder>` | A Ghidra-ready export | writes a new sub-folder |
| `iso-list <iso>` | Every file in a disc image | no |
| `iso-extract <iso> <folder>` | Extracts a disc image | writes the folder |
| `iso-create <folder> <output>` | Builds an XISO from a folder | writes a new file |
| `iso-swap <iso> <output> <path=file...>` | A new image with some files replaced (for testing mods) | writes a new file |
| `patch-list <patchFile>` | The patches in a Xenia `.patch.toml` | no |
| `patch-install <patchFile> [xenia] [on...]` | Installs a patch file into Xenia with the numbered patches switched on | writes into Xenia's patches folder |
| `xenia-read <address> <length>` | Memory of the game running in Xenia, at console addresses | no |
| `xenia-write <address> <bytes>` | Writes bytes into the game running in Xenia | changes the running game only |
| `drive-list <image> [partition] [folder]` | Partitions of an Xbox 360 / original Xbox drive image, or the contents of a folder on one | no |
| `drive-check <image> <partition>` | Consistency check of one partition (every file against the allocation table) | no |
| `drive-extract <image> <partition> <path> <folder>` | Copies a file or folder out of a drive image | writes new files |
| `drive-add <image> <partition> <folder> <items...>` | Copies files and folders from the PC into a folder on the image | **changes the image in place** |
| `drive-install <image> <partition> <packages...>` | Installs packages into the proper `Content` folders on the image | **changes the image in place** |
| `drive-delete <image> <partition> <path>` | Deletes a file or folder from the image | **changes the image in place** |
| `console-find` | IP addresses of consoles running XBDM on the local network | no |
| `console-info <ip>` | Name, kernel, running title, drives with free space, loaded modules | no |
| `console-list <ip> <folder>` | Files and folders in a folder on the console (`Hdd:\`, `Usb0:\Content` ...) | no |
| `console-get <ip> <remote> <output>` | Downloads one file from the console | writes a new file |
| `console-put <ip> <file> <remote>` | Uploads one file to the console, creating folders | writes to the console |
| `console-read <ip> <address> <length>` | Memory of the title running on the console | no |
| `console-write <ip> <address> <bytes>` | Writes bytes into the running title; returns the old bytes | changes the running title |
| `console-screenshot <ip> <output>` | What the console shows, as a PNG | writes a new file |
| `console-notify <ip> <text> [icon]` | Pop-up notification on the console. Needs the JRPC2 plugin there (plain XBDM cannot do this) | sends to the console |
| `console-notify-icons` | The 67 notification pictures and their numbers | reads nothing |
| `console-temperatures <ip>` | CPU, GPU, memory and motherboard temperature in degrees Celsius. Needs the JRPC2 plugin | reads |
| `mod-info <mod>` | What a .x360mod file is: id, name, game, kind, and open / protected / server-only. Never the contents of a paid mod | no |
| `mod-validate <payload>` | Checks a mod payload the way the program will read it. `//` notes are allowed. Problems come back with line numbers in plain words, plus notes (likely typos) and a summary | no |
| `mod-build <output> <id> <name> <protection> <game> <titleId> <description> <requires> [payload]` | For mod makers: builds a .x360mod (notes are removed first). Every field is required (buyers see them) and the Title ID must be the payload's. `protected` also writes a key card beside it; `server-only` writes the payload for the server beside it (a server-only mod may leave the payload out) | writes new files |

Paid mods are sealed and licensed to one machine. No command opens, unlocks, dumps or converts one, and an agent must not try to get around that (no reading the program's memory, no copying another person's token or key card). `mod-build` is for building your own mods from your own payload.

Addresses and title IDs are hexadecimal (`0x` optional).

## Rules the tool keeps for you

- **Arguments are data.** No command builds a command line, starts another program or uses a shell, so nothing in
  an argument can be run. Numbers must be numbers; paths are only ever used as paths.
- It never runs elevated. Started as administrator it exits.
- It never overwrites an output file on the PC.
- A command that edits a file keeps the original as `<file>.bak` the first time. The exception is the `drive-*`
  commands that change a drive image: images are too large to copy silently, so **copy the image yourself first**.
  They refuse before writing anything when a name is not allowed on an Xbox drive or the space is not enough.
- Only the `console-*` commands use the network, and only to the IP address you give (it must be a plain IPv4
  address). They use XBDM (port 730); `console-notify` and `console-temperatures` also need the JRPC2 plugin, whose
  commands go over that same XBDM connection. Nothing in this mode reboots, shuts down, freezes or debugs a console,
  and nothing goes online.
- **One connection, not one per command.** The console's debug service holds only a few connections at a time, and
  connecting over and over makes it time out and refuse new ones. The MCP server connects once and every console
  tool reuses that connection until the session ends. Run as single commands, each run is one connection (closed
  properly with `bye` at the end) - so do not call `console-*` commands in a fast loop from a script; use the MCP
  server for many console steps.
- Paths on a drive image or a console may not contain `..`; console paths may not contain quotes or line breaks.
- A keyvault given to `rehash` is read into memory for that one call and nothing about it is stored.

## Examples

```
X360GameHack.exe cli info "C:\saves\E0000A2A4B0D0C01"
X360GameHack.exe cli tu-extract "C:\tu\tu00000001_00000000" "C:\games\MyGame"
X360GameHack.exe cli decompile "C:\games\default.xex" 0x822B3598
X360GameHack.exe cli iso-swap "C:\game.iso" "C:\game-modded.iso" "media\movie.bik=C:\mods\movie.bik"
X360GameHack.exe cli drive-list "C:\images\hdd.bin"
X360GameHack.exe cli drive-install "C:\images\hdd.bin" 7 "C:\packages\tu00000001_00000000"
X360GameHack.exe cli console-list 192.168.1.50 "Hdd:\Content"
X360GameHack.exe cli console-read 192.168.1.50 0x82000000 64
```

Work on copies of files you care about.
