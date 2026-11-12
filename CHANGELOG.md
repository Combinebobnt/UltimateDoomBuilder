# Changelog

Changes this fork makes on top of upstream [UltimateDoomBuilder/UltimateDoomBuilder](https://github.com/UltimateDoomBuilder/UltimateDoomBuilder) `master`, which it tracks. The license is unchanged (GPL-3, see `LICENSE.txt`).

## Differences from upstream

- **Linux build and launch:** `make linux` builds the editor and its native library, and `builder.sh` runs it from the checkout or from `Build/`.
- **Linux node builder:** the bundled zdbsp is rebuilt from the ZDBSP 1.19 source release (Release build, zlib linked statically), so it runs on glibc 2.34 instead of 2.38.
- **Crash handling:** crash dialogs that work on Mono, a map backup before the dialog, worker and timer exceptions reported instead of killing the editor, and the previous log kept as `UDBuilder.log.1`.
- **Responsiveness on Mono:** no more UI deadlocks from blocking invokes, Map Analysis no longer freezes on large result counts, and models load on the UI thread.
- **Mono UI fixes:** blank button text (font remap), Map Analysis and help panels, Mode menu rows, the About dialog, side panel (docker) pages, the unsaved-map title marker, Preferences slider steps, and resource-warning resizing in map/configuration dialogs.
- **Native file dialogs on Linux:** file and folder pickers use the GTK chooser through `zenity` when it is installed, falling back to Mono's dialogs.
- **Map save safety:** maps are saved to a sibling file and swapped in, backups are written atomically, read-only targets are reported, and UDMF text round-trips as Latin-1.
- **Script compilation:** ACS recompiles on Save Map after a script editor save, a failed compile warns and keeps the map marked unsaved, and BCS compiles with the system zt-bcc.
- **Parsers:** DECORATE `Goto` and state-end fixes, UTF-8 BOMs, correct line numbers on LF files, TEXTURES numeric `Blend` under Zandronum configs, and SNDINFO/TERRAIN conditional blocks.
- **Game configurations:** `forceheight`/`forcewidth` thing properties (documented in Help), Zandronum configs targeting Zandronum 3.3, engine archive checks (`requiredarchives`), GZDoom/UZDoom `filter/` folders, and DnD (Death and Decay) configurations built on the Zandronum 3.3 configuration.
- **Editing:** arg0str survives bulk edits, the Undo panel previews on single click, a discarded redo branch stays restorable, draw tools withdraw a failed draw cleanly, and pasting from another program's clipboard no longer hangs.
- **Key bindings:** each action accepts a primary and an alternate shortcut in Preferences > Controls, with conflicts checked across both and both shown in the command palette and shortcut reference.
- **Tag ranges:** row/column ordering, start corners, row strides, linedef orientation filtering, and linedef/thing argument targets, with a preview summary and one undo step.
- **Draw Grid:** the post-draw sector dialog edits every split cell, including triangulated grids, without editing an enclosing pre-existing room.
- **Dialog loading:** cached font names and batched list fills make repeated Preferences and Game Configurations opens faster.
- **Visual mode placeholders:** the missing, unknown and still-loading texture placeholders are 64x64 grids labelled MISSING, UNKNOWN and LOADING.
- **Resource loading:** faster PK3 file lookups, a PK3 image that fails to read is marked failed instead of breaking the background loader, and required-archive checks can read embedded WADs before a map is open.
- **Diagnostics:** set `UDB_GEOMETRY_LOG=1` to log texture browser window geometry and resource-warning resize bounds, `UDB_UI_TIMING=1` for dialog-open phases, or `UDB_DOCKER_TRACE=1` to trace side panel state.

## Experimental

Still being tested: the crash handler rework, the Mono UI action queue, side panel page fixes, the zenity file chooser, the undo branch, clipboard paste, the DnD configurations, and `forceheight`/`forcewidth`.

## Linux requirements beyond upstream's

- BCS scripts need zt-bcc installed as `/usr/bin/bcc`.
- The bundled Linux zdbsp node builder needs glibc 2.34 or newer.
