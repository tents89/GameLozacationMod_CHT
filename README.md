# FontReplacerMod

A [MelonLoader](https://github.com/LavaGang/MelonLoader) mod for **Schedule I** that replaces all in-game fonts with a custom font of your choice.

## Features

- Replaces all TextMeshPro UI and world text with your custom font
- Dynamic glyph atlas — supports CJK and other large character sets without pre-baking
- Automatically adds original fonts as fallbacks so missing glyphs still render
- Applies to every scene on load

## Requirements

- [Schedule I](https://store.steampowered.com/app/3164500/Schedule_I/) (Steam)
- [MelonLoader](https://github.com/LavaGang/MelonLoader) v0.6+
- .NET 6.0 SDK (for building from source)

## Installation

### From release

1. Download `FontReplacerMod.dll` from [Releases](../../releases).
2. Place it in `<Schedule I>/Mods/`.
3. Place your `.ttf` or `.otf` font file in `<Schedule I>/UserData/FontReplacer/`.
4. Launch the game.

### Building from source

Clone or copy this project into your Schedule I game directory:

```
<Schedule I>/FontReplacerMod/
```

Then build:

```bash
dotnet build -c Release
```

The build system auto-detects the game path via registry, Steam default paths, and common drive letters. If detection fails, specify manually:

```bash
dotnet build -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\Schedule I\"
```

The compiled DLL is output directly to `<Schedule I>/Mods/`.

## Usage

1. Launch the game once with MelonLoader installed (to generate proxy assemblies).
2. Place your `.ttf` or `.otf` font file in `<Schedule I>/UserData/FontReplacer/`.
3. Start the game — the mod will automatically replace all text fonts.

Check the MelonLoader console for status messages.

## License

MIT
