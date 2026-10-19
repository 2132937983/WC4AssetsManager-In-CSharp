# Changelog

All notable changes to WC4AssetsManager (the WC4 map / asset editor) are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); dates are ISO (YYYY-MM-DD).

## [Unreleased] — 2026-10-18 (branch `6.1`)

### Added

**Interface localization**
- Translation files `Resource/Lang/zh-CN.json` and `Resource/Lang/en-US.json` (1,577 keys each, identical key sets). Keys are named `Area.Item`; `{0}`-style placeholders are filled at runtime. Missing keys fall back to zh-CN, then to the key name, so a partial translation never breaks the UI.
- `Loc` loader in `WC4MapEditor.Core.Config` (`Loc.T(key)`, `Loc.F(key, args)`). Language preference is stored in `setting.txt` under `Language`.
- Language menu on the start screen (the "文A" button). Scenes are rebuilt on entry, so the new language applies immediately.
- `Resource/Lang/README.md` explaining how to add a language.

**Unified editor theme**
- `src/WC4MapEditor.GUI/Theme/EditorTheme.xaml` + `EditorTheme.cs`: one dark palette, implicit styles for all standard WPF controls (Button, TextBox, ComboBox, CheckBox, ListBox/ListView, ScrollBar, TabControl, Slider, ProgressBar, ToolTip, ContextMenu…), and factory helpers (`TopBar`, `Panel`, `SearchBox`, `FormRow`, `SectionHeader`, `StatusBar`, `Card`, `Chip`…). Applied per scene with `EditorTheme.Apply(this)`.
- Shared `NumericUpDown` control in `src/WC4MapEditor.GUI/Controls/` (previously a private copy inside `GeneralEditScene.cs`).

**Layout editor (rebuilt)**
- Property panel driven by `LayoutSchema` (Core), which lists only the tags and attributes the game's `layout.xml` actually uses, with proper editors per attribute type (enums, colors, images, fonts, stringtable keys, margins, template ids).
- Fonts read from `font_resource.xml` with a selectable device profile (PHONE/HD default, PAD/HD, PAD/SD); bitmap fonts honour `contentscale`.
- Stringtable language selector (cn/tw/en/ja/ko/de/es) for label text, with per-label `es`/`de` font overrides.
- Undo/redo (Ctrl+Z/Y), save back to the asset file with a `.bak` copy (Ctrl+S), export XML/PNG, add top-level forms (Form/TmpWindow/Template), add children from the real tag list, duplicate, reorder, arrow-key nudging, snap-to-grid, image picking from the sprite gallery (double-click assigns to the selected widget).
- Canvas size presets, margin guides, hidden-widget ghosting, missing-image markers.

**Developer switches**
- `WC4MapEditor.exe --scene <name>` opens an edit scene directly (same names as the AI tool `open_scene`: `layout`, `army`, `general`, `country`, `building`, `ingame_tech`, `skill`, …).
- `--open <file.btl|.bin>` loads a map into the matching render scene.
- `--screenshot <png>` renders the window to a PNG a few seconds after start-up and exits. Works without window focus.

**Core / rendering API**
- `StageParser.LastError` / `ConquestParser.LastError` expose why `SaveData` failed; the GUI shows the message.
- `ModifierBase.IsInitializedFor(MapData)`.
- `CapitalFlagRender.InvalidateProvinceLegionCache()`; `ClearGeneralHeadCache()` on `ArmyRender`, `ReinforceRender`, `ReinforceRenderNew` (wired into `SkiaRenderEngine`).
- Core parsers `FontResourceParser` and `LayoutColor` (`r,g,b,a` 0–255, `#RRGGBB[AA]`).

**Mode framework (Core)**
- A Mode is now a collection of Modules plus a key map. `ModeBase` + `ModeActionTable` declare, in one place, which key / mouse gesture / Esc runs which Module function; key bindings, dispatch and the localized help overlay are generated from that table, so help text can no longer drift from the real keys. Duplicate keys, and keys reserved by the scene (Space, Tab, Esc, F1-F3, B, N, Ctrl+Z/Y/S, digits), are rejected at start-up.
- Modes declare their render layers (`ModeRenderLayers`) and selection filter; `RenderSceneBase` no longer hard-codes either. New `ClippedTiles` layer draws red crosses outside a conquest map's data window.
- `EditorKinds` / `AssistEditorHost`: modes ask for a window by name (`context.OpenEditor(EditorKinds.X)`); window classes carry `[AssistEditor(kind)]`. `ListEditorWindow` is the shared base for list + detail editors (validation, undo, duplicate, delete confirm, Esc/Enter).
- Undo: `RecordCollectionChange` (any entity list) and `RecordEntitiesSnapshot` (all entity lists + provinces + belongs); army eraser strokes and CLI/Lua entity edits are now undoable.
- Localization: per-feature translation files in `Resource/Lang/<code>/*.json` are loaded on top of the main files.

**New edit modes** (the five pre-written Modules are now reachable in the stage and conquest scenes; digit keys 7, 9, 0 and Space cycle; weather and strategic construction via Space)
- Event editing (7): event window with trigger/effect pickers from `setting.txt`, legion pickers, sort, copy/paste, `[` `]` cycling, reference check.
- Air raids / air support (9): raid and support list windows, random support, generate raids at airports.
- AI plans / map cases (0): unit selection, Ctrl+Q sets the unit's *policy* (the pre-written module wrote the wrong field), case list window with the type list from `setting.txt`.
- Weather and strategic construction: list windows, random weather sequence, named construction codes.
- The `setting.txt` lists `[事件修改]`, `[空袭修改]`, `[方针修改]`, `[天气修改]` are now parsed (they were discarded).

**Existing modes, brought up to the VB feature set**
- Terrain: selection-aware batch tools with undo, cancellable recognition, MST road connection, safe resize/scale (refused on conquest layouts), brush window fixes.
- Province: rectangle / polygon selection, flood fill with a value, create province from selection; hex 0 cannot be a capital (value 0 means "no province").
- Building: left-click info and capital-province highlight, Ctrl+Q occupation-event link, move tool with undo and ghost preview, owner taken from the province capital, validated create/edit dialog.
- Units and traps: Q edits an existing unit (it used to overwrite it), v1 and v3 copy/paste/delete, plan/event, general auto-assign with options, trap level cycling / find / randomize / generate by province, v3 units are drawn.
- Unit dialogs: v3 extension fields, general info panel, inline validation, dialog keys; reserved bytes are preserved.
- Legion / belong: legion list window with drag/arrow reorder, capital toggle, set player legion, F6 conquer settings, multi-selection for belong keys, undo for legion/header edits.
- Reinforcements: Q editor window (v1 and v3), copy/delete/sync owner, layers per VB; the help text now matches the real keys.

**Scene UX**
- Status bar with mode messages, cell position and zoom (status messages never reached the user before). Ctrl+Z / Ctrl+Y always work with feedback; arrow-key pan and +/- zoom; Shift+O view layer; the help fade completes; toolbar buttons no longer steal Space/Enter; Back asks before leaving with unsaved edits; the hex-info window is mode-aware and reads v3 data; renderers rebind when a map is replaced.
- Esc goes to the mode first (cancel region move / clear selection) and only then asks to return to the main screen.

**Conquest maps**
- Maps are routed by header (conquest layout) instead of file name, saved in their own format, and drawn on their world map (`world{N}.bin`); scene tabs keep unsaved edits.

**Data check scene** (start screen "检查数据")
- BTL/BIN inspector: header, per-block record lists with decoded fields and file offsets, virtualized hex viewer with jump/search, segment address map, A/B binary compare, v1 <-> v3 conversion. CLI: `inspect`, `compare`, `convert`.

**JSON table editors**
- All 11 editors share one infrastructure: load-error reporting, unsaved-changes guard (also on scene switch and window close), undo/redo + history (Ctrl+H), copy/paste, multi-select delete, reorder, batch edit, search, F1 help; one shared string table; unknown JSON fields and file layout are preserved on save.

**Start screen**
- Settings window (live `setting.txt` editor with hot reload; comments and unknown lines are preserved; the API key is never displayed), music player, recommendation window, developer mode, opening animation and loading progress.

**CLI / Lua / AI**
- JSON exports now carry every field with its real name; Lua gained event, case, weather, strategic construction, air raid/support and reinforcement functions, move-faction and set-player-at; `check` validates event references; `--screenshot` also writes the open windows; `--mode` / `--editor` startup switches open a mode or window for screenshots.

### Changed
- Every user-visible string in `src/WC4MapEditor.GUI` (scenes, dialogs, assist windows, rendering-scene toolbars and console output, main window) now comes from translation keys.
- Start-screen navigation labels are taken from the translation files, not from `setting.txt` `[主场景编辑]`. `TitleBarText`, `WindowTitleText` and `VersionText` in `setting.txt` are still honoured.
- All edit scenes, overlay dialogs and assist windows were restyled on the shared theme. The rendering scenes (map/campaign/conquest) and the start screen keep their original look.
- Layout engine semantics aligned with the game data: unspecified `halign`/`valign` means left/top; unsized widgets fill their parent (only images/buttons/animations with a resolvable texture use the texture size); label text is centred by default; `color` is parsed as 0–255 RGBA; UI atlases are resolved through `image_resource.xml` including `assets/image/` (previously only the assets root was scanned, so most layout images showed as missing); font sizes come from `font_resource.xml` instead of name-based guesses.
- Search boxes use a real watermark instead of placeholder text that leaked into filters.
- Tactical map editor toolbar moved from the bottom to the top of the scene.
- `ProgressWindow` title bar follows `Window.Title`, so callers' titles are shown.
- Country-tech scene: the "preview NeedId" and "auto connect" checkboxes now actually toggle their settings (they were previously display-only).
- Flag editor: mode toggle buttons stay in sync with the keyboard shortcuts (R/E/P).

### Fixed
- **V3 maps in the GUI**: `ArmyV3Modifier` and `ReinforcementV3Modifier` were never registered in `EditModeManager`, so placing a unit or reinforcement on a `BtlVersion ≥ 3` map threw a null reference. CLI and Lua now reuse the registered instance instead of creating a new one per call.
- **Building coordinate index** could go stale: `FindBuildingIndex` trusted the cache without checking the coordinate, and `ReplaceBuilding` never removed the old cell after a move. The index now verifies hits, drops the old key, and is cleared when the collection is replaced.
- **`MapData.Provinces` / `.Terrains`** rebuilt a full copy of the map on every access; trap generation (Ctrl+I) and the `info` command used them in loops (O(n²)). The properties were removed and callers index cells directly.
- **Conquest "new map"** wrote the header width/height transposed relative to the stage parser and both readers; non-square new conquest maps reopened transposed.
- **Save failures** during "create new campaign/conquest" were swallowed; the error text is now shown.
- `ProvinceRender` leaked a native `SKPath` every frame.
- Coast decoration lookup was allocation-heavy and ran for the whole map on the UI thread after every edit (see Performance).

- **Map file layout**: the stage parser sized the terrain block by `SelectableTileCount` while real files use `MapLength x MapWidth` (491 of 1,350 stock maps), so buildings, units, traps, events, cases, weather and air raids were read at the wrong offset on those maps. One block-address table (`BTLLayout`, checked against all 1,358 stock files) now drives load, save and the inspector. v2 maps use 64/104-byte unit and reinforcement records, the trailing extension block is kept, and **every stock map now round-trips byte-identically** (it was none).
- **Conquest ownership**: the loader added 1 to every belong value (London showed as the next faction); belongs are 0-based legion indices. The belong lookup honours the conquest data window (wraps across the east-west seam). 188 padded conquest files used to be saved with a wrong tile count.
- **Key codes**: digit and `,` `.` key codes were off by one WPF `Key` value (pressing 1 selected mode 2; the terrain type keys never fired).
- Edits that bypassed undo, stuck selections and the (0,0) focus fallback, Esc firing both the mode action and the exit dialog, stale event subscriptions after leaving a scene, and `ConfigManager` getters that returned empty settings in the CLI until `GetText` had run.
- Unit records: reserved bytes are preserved and the editor-only `LegionId` is no longer written into the file (units take their owner from the hex belong value). v1 reinforcement records are 80 bytes again (owner and spawn turn were lost). Building reserved bytes and legion byte 0x2B are kept.

### Removed
- The private `NumericUpDown` copy in `GeneralEditScene.cs` (replaced by the shared control).
- `StageOffsets` / `ConquestOffsets` (replaced by one block-address table, see Fixed), the per-mode copies of global keys (B, N, F1, Ctrl+F1, Ctrl+Z, Ctrl+Y) in the terrain mode, and the dead legion/header/conquer callbacks on `EditModeManager` / `ModeContext`.
- Note: the pre-written VB-port classes (`AirForceModifier`, `EventModifier`, `WeatherModifier`, `MapCaseModifier`, `StrategicConstructionModifier`, `Models/Modules/*`, `BTLParser`, the BTL enums) were briefly deleted as "dead code" earlier in this release and have been restored; they are now wired into the new modes below.

### Performance
- Coastlines: the baked "final atlas" is now loaded from the disk cache (or baked once in the background) during normal editing. Previously it was only used by the screenshot path, so every coast cell went through three offscreen surfaces on each background rebuild and pan strip.
- Coast decoration type is a 64-entry table indexed by a 6-bit neighbour mask (no heap allocations); verified identical to the previous logic on all ocean cells of the test map.
- Capital flags: province → owner lookup table instead of scanning every map cell per capital per frame; invalidated together with the province/legion/belong caches.
- Brush mode no longer repaints the whole surface on mouse moves that stay inside the same hex.
- Per-frame churn removed across renderers: `SKFont`/`SKPaint` objects are fields instead of per-draw allocations (Reinforce, ReinforceNew, Strategic, AirForce, MapCase, BelongFlag, Overlay, GeoRuler); `MapCaseRender` no longer matches the "Microsoft YaHei" typeface every frame; hex paths are cached per size (Province); general lookups use a dictionary index (`GeneralSettingParser.GetById`) with a per-renderer head-image cache; legion → country maps are built once and invalidated on `Legions.CollectionChanged`; flag caches are keyed by country instead of pixel size (bounded memory across zoom levels); trap positions use `stackalloc`; building atlas keys are cached and the three per-frame `GroupBy` calls use pooled grouping; belong labels use a 256-entry string table; selection hexes reuse the cached path; overlay help text is split and measured only when it changes.

### Known issues / notes
- The stage-parser layout fix changes what the editor reads from 491 stock maps; data that was mis-parsed before (and any edits saved from it) was never valid. The standalone CLI `stage` command still parses conquest-layout files as campaigns.
- Strings intentionally left in Chinese: debug/trace logs, console command descriptions and usage text (they double as the CLI documentation), default texts written into game data (e.g. new buff/skill descriptions), brush shape constants compared in Core, and AI tool result strings. The AI chat window (`WC4MapEditor.AI`) is not localized.
- Layout engine rules were inferred from the shipped `layout_x.xml`, `font_resource.xml` and `image_resource.xml`, not from the game binary. Compare a familiar form in-game if something looks off.
- GPU rendering (`SKGLElement`) is not available while the main window uses `AllowsTransparency=true`. `SkiaSharp.Views.WPF` (3.116.1) and `SkiaSharp` (3.119.2) versions should be aligned before experimenting.

## Earlier commits

- `eebc9af` 2026-10-18 — `261018-1749`
- `1f21322` — `调整结构` (project restructuring)
- `f2e10d0` — `新增AI功能` (AI assistant features)
