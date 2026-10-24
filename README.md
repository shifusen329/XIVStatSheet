# XIV Stat Sheet

A [Dalamud](https://dalamud.dev) plugin that exports your character's current stats and equipped gear.

Each export writes two files (both can be toggled in settings):

- **JSON**: character info, the stats shown in the in-game Character window (grouped as there), and every gear slot with item level, HQ, item stats, materia, glamour, condition and spiritbond.
- **Markdown**: the same snapshot as readable tables, for sharing or notes.

Stats are the live values from the Character window, so food and level sync are included.

## Usage

| Command | Action |
|---|---|
| `/statsheet` | Open the window (preview, Export, Open Folder, Settings) |
| `/statsheet export` | Export right away and print the saved file paths in chat |

Files are named `{Character}_{JOB}_{yyyyMMdd-HHmmss}.json` / `.md`. By default they go to
`%AppData%\XIVLauncher\pluginConfigs\XIVStatSheet\exports`. You can choose another folder in settings.

## Building

Prerequisites: XIVLauncher, FINAL FANTASY XIV and Dalamud installed, with the game run under Dalamud at least once, plus the .NET 10 SDK. If Dalamud's dev directory isn't in the default location, set `DALAMUD_HOME`.

```
dotnet build -c Release
```

The plugin is built to `XIVStatSheet/bin/x64/Release/XIVStatSheet.dll`.

## Loading in-game

1. Open `/xlsettings` → **Experimental** and add the full path to `XIVStatSheet.dll` under **Dev Plugin Locations**.
2. Open `/xlplugins` → **Dev Tools > Installed Dev Plugins** and enable **XIV Stat Sheet**.
3. Run `/statsheet`.
