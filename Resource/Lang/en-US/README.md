# en-US split translation files

`Resource/Lang/en-US.json` holds the text of the asset editors (list / property panels / asset browser /
tactical map). This folder holds the map editor's text, split by feature and layered on top of that main file.

## Loading

`Loc.LoadLanguage()` (`src/WC4MapEditor.Core/Config/Localization.cs`) works like this:

1. read `../en-US.json` first;
2. then read every `*.json` in this folder in case-insensitive filename order and `TryAdd` each entry.

Which gives three rules:

- **The main file wins.** When a key exists in both places the value from `en-US.json` is used; split files only
  add new keys. Today the main file has 1577 entries and this folder has 2540, with no key in common.
- **No duplicates between files.** Inside this folder the first file read wins (currently none of the 21 files
  duplicate a key).
- **Fallback chain:** current language → `zh-CN.json` → the key itself (`Loc.T` / `Loc.F`). A missing English
  string therefore shows the Chinese one instead of a blank, and a brand-new key only needs to be added to
  `zh-CN` first.

Adding a new file here needs no code change: every `*.json` in this folder is loaded, so keep helper files on a
different extension.

## Format

- **Strict JSON**: no `//` comments, no trailing commas. The loader tolerates both, but comments break IDEs,
  validators and translation tooling, so this folder is plain JSON (all comments were removed in 2026-10).
- One flat object, keys shaped `Area.Entry` (`Mode.Army.Name`, `Module.Building.DeleteFailed`). Nested objects
  are flattened on dots and are equivalent, but the flat form is used here so keys sort and diff by prefix.
- `{0}`, `{1}` are `string.Format` placeholders and must be kept, in the same order the code passes them.
  A broken placeholder only makes `Loc.F` fall back to the unformatted text — no crash, but the raw `{0}` shows up.
- UTF-8 without BOM, LF line endings, one trailing newline.
- Keys must match `../zh-CN/` one to one. A key that only exists in Chinese makes the English UI show Chinese:
  that is the fallback working, not an error.

## Files

| File | Covers | Main key prefixes | Entries |
| --- | --- | --- | --- |
| `mode-framework.json` | Edit-mode framework: mode base class, mode manager, key names. Per-mode text does not live here | `Mode.` `Key.` | 25 |
| `scene.json` | Render scene interaction: bottom status area, undo / redo, key help, close / back confirmations, Tab cell info window | `Scene.` `Assist.` | 159 |
| `startapp.json` | Start screen and app-level features: settings, recommendation, background music, developer mode, loading screen | `Main.` `Begin.` `Assist.` `Console.` | 107 |
| `conquest.json` | Scene file operations (open / save / new / switch) and the conquest world base map | `SceneFile.` `Conquest.` | 15 |
| `terrain.json` | Terrain painting mode, terrain module (TerrainModifier / TerrainRecognizer), brush window | `Mode.` `Module.` `Assist.` | 154 |
| `province.json` | Province editing: mode, module messages, province console commands | `Mode.` `Module.` `Render.` | 101 |
| `belong.json` | Ownership edit mode, ownership module, ownership list window (Q) | `Mode.` `Module.` `Assist.` | 71 |
| `army.json` | Unit deployment mode (units v1 / v3 + traps): mode text, status messages, dialogs, undo descriptions | `Mode.` `Module.` | 177 |
| `army-dialogs.json` | Unit setting dialogs (v1 / v3), general info panel, Core ArmyRules warnings | `Assist.` | 80 |
| `reinforcement.json` | Reinforcement mode, reinforcement module (incl. V3), reinforcement editor window | `Mode.` `Module.` `Assist.` | 133 |
| `building.json` | Building deployment mode, building module (BuildingModifier / BuildingNameResolver), building editor window, building rendering | `Mode.` `Module.` `Assist.` `Render.` | 177 |
| `airforce.json` | Air raid edit mode, air raid / air support module, the two list windows | `Mode.` `Module.` `Assist.` | 120 |
| `legion.json` | Faction edit mode, faction / capital modules, faction settings / list / header data windows | `Mode.` `Module.` `Assist.` | 137 |
| `event.json` | Event edit mode, event settings window, event module and reference check | `Mode.` `Module.` `Assist.` | 123 |
| `strategic.json` | Strategic construction: mode, module, list window, top-right map panel, `strategic` commands | `Mode.` `Module.` `Assist.` `Render.` `Cli.` | 66 |
| `weather.json` | Weather: mode, module, list window, top-right map panel, `weather` commands | `Mode.` `Module.` `Assist.` `Render.` `Cli.` | 67 |
| `mapcase.json` | AI plan (map case) editing: mode, plan list window, module, render layer, console output | `Mode.` `Module.` `Assist.` `Render.` `Cli.` | 91 |
| `unitplacement.json` | Output of the `unitplace` commands (read-only data, nothing editable) | `Cli.` | 4 |
| `datacheck.json` | Data check scene, `inspect` / `compare` / `convert` commands, BtlInspector / BinaryCompare / BtlVersionConverter | `DataCheck.` | 572 |
| `json-editors.json` | Shared shell of the JSON table editors (history / save / batch / help) and data-integrity messages | `JsonEditor.` | 145 |
| `list-editor.json` | Shared text of list + detail windows (`ListEditorWindow` base class) | `Assist.` | 16 |

2540 entries in total, matching the 21 files in `../zh-CN/` one to one (same file names, same keys).

## Where to add text

- Text of one mode / module → its feature file (building text goes to `building.json`, etc.).
- Things several modes share (mode-switch prompts, key names) → `mode-framework.json`.
- Scene-level feedback such as the status bar, undo prompts or "return to the previous screen?" → `scene.json`.
- Common buttons and hints of list + detail windows → `list-editor.json`.
- Renaming a key means updating the `Loc.T("...")` / `Loc.F("...")` calls in code too; a key with no code
  reference is dead and can be deleted.
- A mode name is always `Mode.<Mode>.Name` and is defined once: mode names live in their feature files,
  `mode-framework.json` only holds framework text.
