# Changelog

Changes this fork makes on top of upstream [UltimateDoomBuilder/UltimateDoomBuilder](https://github.com/UltimateDoomBuilder/UltimateDoomBuilder) `master`, which it tracks. The license is unchanged (GPL-3, see `LICENSE.txt`).

## Differences from upstream

- **Linux build and launch:** `make linux` builds the editor and its native library, and `builder.sh` runs it from the checkout or from `Build/`.
- **Fork builds:** pushes check Windows x64/x86 builds and x64 tests; the manual Fork Build workflow publishes portable Windows zips and a Mono-bundling Linux AppImage as `R<upstream>-fork.<commits>`. Release builds show that label and disable upstream updates.
- **Linux node builder:** the bundled zdbsp is rebuilt from the ZDBSP 1.19 source release (Release build, zlib linked statically), so it runs on glibc 2.34 instead of 2.38.
- **Crash handling:** crash dialogs that work on Mono, a map backup before the dialog, worker and timer exceptions reported instead of killing the editor, and the previous log kept as `UDBuilder.log.1`.
- **Responsiveness on Mono:** no more UI deadlocks from blocking invokes, Map Analysis no longer freezes on large result counts, models load on the UI thread, and Visual Mode stops redrawing behind edit dialogs after a texture browser was opened from one, while edit dialogs still update the 3D view on each change.
- **Mono UI fixes:** blank button text (font remap), Map Analysis and help panels, Mode menu rows, the About dialog, side panel (docker) pages, the unsaved-map title marker, Preferences slider steps, and resource-warning resizing in map/configuration dialogs.
- **Native file dialogs on Linux:** file and folder pickers use the GTK chooser through `zenity` when it is installed, falling back to Mono's dialogs.
- **Map save safety:** maps are saved to a sibling file and swapped in, backups are written atomically, read-only targets are reported, and UDMF text round-trips as Latin-1.
- **Script compilation:** ACS recompiles on Save Map after a script editor save, a failed compile warns and keeps the map marked unsaved, and BCS compiles with the system zt-bcc.
- **Parsers:** DECORATE `Goto` and state-end fixes, UTF-8 BOMs, correct line numbers on LF files, TEXTURES numeric `Blend` under Zandronum configs, and SNDINFO/TERRAIN conditional blocks.
- **Game configurations:** `forceheight`/`forcewidth` thing properties (documented in Help), Zandronum configs targeting Zandronum 3.3 with supported sector-portal choices, engine archive checks (`requiredarchives`), GZDoom/UZDoom `filter/` folders, and DnD (Death and Decay) configurations built on the Zandronum 3.3 configuration.
- **Editing:** arg0str survives bulk edits, the Undo panel previews on single click, a discarded redo branch stays restorable, draw tools withdraw a failed draw cleanly, and pasting from another program's clipboard no longer hangs.
- **Clipboard history:** a bounded, in-memory side panel reuses copied or cut geometry across maps without replacing the system clipboard, with duplicate detection, Paste Special and per-entry actions.
- **Key bindings:** each action accepts a primary and an alternate shortcut in Preferences > Controls, with conflicts checked across both and both shown in the command palette and shortcut reference.
- **Tag ranges:** row/column ordering, start corners, row strides, linedef orientation filtering, and linedef/thing argument targets, with a preview summary and one undo step.
- **Draw Grid:** the post-draw sector dialog edits every split cell, including triangulated grids, without editing an enclosing pre-existing room.
- **Dialog loading:** cached font names and batched list fills make repeated Preferences and Game Configurations opens faster.
- **Visual mode placeholders:** the missing, unknown and still-loading texture placeholders are 64x64 grids labelled MISSING, UNKNOWN and LOADING.
- **Visual mode animation:** ANIMDEFS and ANIMATED texture/flat frames cycle on geometry, including stock default animations, with a persistent View/toolbar toggle. ANIMDEFS warp/warp2 and SMMU swirls use built-in shaders ahead of hardware shaders; Zandronum warp2 uses the UZDoom formula.
- **Visual mode materials:** GLDEFS brightmaps and compatible hardware shaders render on walls, flats and sprites, with dialect-aware shader support, a persistent toggle hidden when no map is open, and plain-texture fallback for failed programs.
- **Visual mode fog:** coloured fog follows UZDoom's sector/outside/level density precedence and light-level fallback; MAPINFO lightmode 8/16 use the unset mode, while invalid values warn.
- **Visual mode models:** OBJ triangle winding matches UZDoom, so translucent models show the correct faces.
- **Resource loading:** faster PK3 file lookups, a PK3 image that fails to read is marked failed instead of breaking the background loader, and required-archive checks can read embedded WADs before a map is open.
- **Resource reload:** unchanged WAD and PK3 readers retain their raw indexes while configuration, parser and image state rebuilds; thing sprite frames resolve through a prefix index and texture sets match exact and prefix filters without a regex, so loads and reloads with many actors or large texture sets are faster; stage timings expose remaining reload costs.
- **Diagnostics:** set `UDB_GEOMETRY_LOG=1` to log texture browser window geometry and resource-warning resize bounds, `UDB_UI_TIMING=1` for dialog-open phases, `UDB_DOCKER_TRACE=1` to trace side panel state, or `UDB_INTERACTION_LOG=1` for interaction timings and marks.

## Experimental

Still being tested: the crash handler rework, the Mono UI action queue, side panel page fixes, the zenity file chooser, the undo branch, clipboard paste/history, incremental reload, visual texture animation/materials, the DnD configurations, and `forceheight`/`forcewidth`.

## Linux requirements beyond upstream's

- BCS scripts need zt-bcc installed as `/usr/bin/bcc`.
- The bundled Linux zdbsp node builder needs glibc 2.34 or newer.
