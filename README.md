# IssaPlugin

A [BepInEx](https://github.com/BepInEx/BepInEx) mod for **Super Battle Golf** that adds new items to the game, including a swingable bat with custom animations and combat logic.

## Features

- Adds custom items (e.g. the **Bat**) with unique swing mechanics
- Patches player inventory and animation systems to support new item types
- Uses the Golf Club animation rig as a base for new melee items

## Requirements

- [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases) (netstandard2.1)
- Super Battle Golf

## Installation

1. Install BepInEx into your Super Battle Golf game folder if you haven't already.
2. Download the latest release of IssaPlugin from the [Releases](#) page.
3. Drop `IssaPlugin.dll` into your `BepInEx/plugins/` folder.
4. Launch the game — BepInEx will load the plugin automatically.

## Building from Source

1. Clone the repository.
2. Update the `<HintPath>` entries in `IssaPlugin.csproj` to point to your local game and BepInEx DLLs.
3. Run:
   ```
   dotnet build
   ```
4. The compiled DLL will appear in `bin/Debug/netstandard2.1/`.

## Project Structure

```
IssaPlugin/
├── Plugin.cs               # BepInEx plugin entry point
├── BatItem.cs              # Bat item definition and swing coroutine
└── Patches/
    └── InventoryPatches.cs # Harmony patches for inventory & animation
```