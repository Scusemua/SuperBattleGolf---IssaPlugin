# IssaPlugin

A [BepInEx](https://github.com/BepInEx/BepInEx) mod for **Super Battle Golf** that adds new items to the game, including a swingable bat, a controllable predator missile, and a stealth bomber.

## Features

- Adds custom items (e.g. the **Baseball Bat**, **Predator Missile**, and **Stealth Bomber**) with unique mechanics
- Patches player inventory and animation systems to support new item types

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
├── Configuration.cs        # Defines configuration parameters for custom items
├── BaseballBatItem.cs      # Baseball Bat item definition and swing coroutine
├── StealthBomberItem.cs    # Stealth Bomber item definition and bombing run coroutine
└── Patches/
    └── InventoryPatches.cs # Harmony patches for inventory & animation
```

# Credits

"Snowball - Low resources" (https://skfb.ly/oyCyZ) by JanesBT is licensed under Creative Commons Attribution (http://creativecommons.org/licenses/by/4.0/).
