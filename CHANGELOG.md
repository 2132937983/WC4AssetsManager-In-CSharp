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

### Removed
- About 2,200 lines of dead code: `AirForceModifier`, `EventModifier`, `WeatherModifier`, `MapCaseModifier`, `StrategicConstructionModifier` (never referenced), `BTLParser` (encoded an outdated file layout), `Models/Modules/*`, nine unused `BTL*` enums (only `BTLDirection` remains), unused `MapData` members (`AddBuilding`, `AddArmy`, `AddArmyV3`, `ClearSelection`, `GetArmiesByLegion`, `GetBuildingsByLegion`, `RemoveReinforcementV3At`, `SetProvinceAt`, `SetTerrainAt`, `RemoveBuildingAt(col,row)`), and `MapDataRecoveryStrategy`.
- The private `NumericUpDown` copy in `GeneralEditScene.cs` (replaced by the shared control).

### Performance
- Coastlines: the baked "final atlas" is now loaded from the disk cache (or baked once in the background) during normal editing. Previously it was only used by the screenshot path, so every coast cell went through three offscreen surfaces on each background rebuild and pan strip.
- Coast decoration type is a 64-entry table indexed by a 6-bit neighbour mask (no heap allocations); verified identical to the previous logic on all ocean cells of the test map.
- Capital flags: province → owner lookup table instead of scanning every map cell per capital per frame; invalidated together with the province/legion/belong caches.
- Brush mode no longer repaints the whole surface on mouse moves that stay inside the same hex.
- Per-frame churn removed across renderers: `SKFont`/`SKPaint` objects are fields instead of per-draw allocations (Reinforce, ReinforceNew, Strategic, AirForce, MapCase, BelongFlag, Overlay, GeoRuler); `MapCaseRender` no longer matches the "Microsoft YaHei" typeface every frame; hex paths are cached per size (Province); general lookups use a dictionary index (`GeneralSettingParser.GetById`) with a per-renderer head-image cache; legion → country maps are built once and invalidated on `Legions.CollectionChanged`; flag caches are keyed by country instead of pixel size (bounded memory across zoom levels); trap positions use `stackalloc`; building atlas keys are cached and the three per-frame `GroupBy` calls use pooled grouping; belong labels use a 256-entry string table; selection hexes reuse the cached path; overlay help text is split and measured only when it changes.

### Known issues / notes
- Real game `.btl` files do not round-trip byte-identically through `SaveData` (header counts are normalised on save). This predates these changes; the editor's own test map round-trips identically.
- Strings intentionally left in Chinese: debug/trace logs, console command descriptions and usage text (they double as the CLI documentation), default texts written into game data (e.g. new buff/skill descriptions), brush shape constants compared in Core, and AI tool result strings. The AI chat window (`WC4MapEditor.AI`) is not localized.
- Layout engine rules were inferred from the shipped `layout_x.xml`, `font_resource.xml` and `image_resource.xml`, not from the game binary. Compare a familiar form in-game if something looks off.
- GPU rendering (`SKGLElement`) is not available while the main window uses `AllowsTransparency=true`. `SkiaSharp.Views.WPF` (3.116.1) and `SkiaSharp` (3.119.2) versions should be aligned before experimenting.

## Earlier commits

- `eebc9af` 2026-10-18 — `261018-1749`
- `1f21322` — `调整结构` (project restructuring)
- `f2e10d0` — `新增AI功能` (AI assistant features)
