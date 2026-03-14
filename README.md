# IssaPlugin

A **BepInEx mod for Super Battle Golf** that adds chaotic, weaponized gameplay through custom items (airstrikes, missiles, weapons, and physics modifiers), world modifiers, and cinematic strike abilities.

------------------------------------------------------------------------

# Features

## Custom Items

The plugin adds multiple fully functional items with custom behaviors:

  -----------------------------------------------------------------------
  Item                                Description
  ----------------------------------- -----------------------------------
  **Baseball Bat**                    Swingable melee bat that can launch
                                      objects and players.

  **Predator Missile**                Call in and remotely control a
                                      high-speed missile strike.

  **Stealth Bomber**                  Mark a location to trigger a
                                      bombing run from a stealth bomber.

  **AC-130 Gunship**                  Enter a gunship camera and rain
                                      down explosive fire.

  **Sniper Rifle**                    Long-range scoped weapon with
                                      custom overlay.

  **Freeze World**                    Temporarily freeze physics and
                                      player movement.

  **Low Gravity**                     Applies a low-gravity effect to the
                                      world.
  -----------------------------------------------------------------------

------------------------------------------------------------------------

## Gameplay Systems

The mod integrates deeply into the game through:

-   Custom item registry
-   Network synchronization
-   Custom hittable objects
-   Dynamic item spawning
-   Explosion scaling
-   Effect duration tracking

------------------------------------------------------------------------

## Visual Overlays

Several custom overlays enhance the gameplay experience:

-   AC-130 targeting HUD
-   Bomber strike indicator
-   Sniper scope overlay
-   Low gravity effect indicator
-   Freeze effect UI
-   Player highlight overlays

------------------------------------------------------------------------

## Multiplayer Support

The mod includes networking bridges so custom items behave correctly in multiplayer sessions.

Examples include:

-   Missile control synchronization
-   AC-130 targeting state
-   Freeze world events
-   Low gravity activation
-   Bombing run markers

------------------------------------------------------------------------

# Requirements

-   **BepInEx 5.x**
-   **Super Battle Golf**
-   .NET **netstandard2.1**

------------------------------------------------------------------------

# Installation

1.  Install **BepInEx** into your Super Battle Golf directory.
2.  Download the latest release of **IssaPlugin**.
3.  Place the compiled plugin DLL inside:
    `BepInEx/plugins/`

4.  Launch the game.

BepInEx will automatically load the plugin.

------------------------------------------------------------------------

# Building From Source

### 1. Clone the repository

    git clone https://github.com/yourname/IssaPlugin.git

### 2. Configure references

Update the paths in:

    `IssaPlugin.csproj`

to point to your local copies of:

- `GameAssembly.dll`
- `Mirror.dll`
- `SharedAssembly.dll`
- `Unity.InputSystem.dll`
- `Unity.InputSystem.ForUI.dll`
- `Unity.Localization.dll`
- `UnityEngine.InputForUIModule.dll`
- `UnityEngine.InputLegacyModule.dll`
- `UnityEngine.InputModule.dll`
- `UnityEngine.LocalizationModule.dll`

These can be found in the game's local files, specifically in `steamapps/common/Super Battle Golf/Super Battle Golf_Data/Managed/`.

### 3. Build

    `dotnet build`

The compiled plugin will appear in:

    `bin/Debug/netstandard2.1/`

------------------------------------------------------------------------

# Project Structure
```
    IssaPlugin/
    │
    ├── Plugin.cs
    ├── PluginInfo.cs
    ├── Configuration.cs
    │
    ├── Items/
    │   ├── AC130/
    │   ├── PredatorMissile/
    │   ├── StealthBomber/
    │   ├── SniperRifle/
    │   ├── FreezeWorld/
    │   ├── LowGravity/
    │   └── BaseballBatItem.cs
    │
    ├── Overlays/
    │   ├── AC130Overlay
    │   ├── BomberOverlay
    │   ├── SniperScopeOverlay
    │   └── Effect UI components
    │
    ├── Patches/
    │   ├── ItemRegistryPatches
    │   ├── NetworkManagerPatches
    │   ├── ExplosionPatches
    │   ├── RocketPatches
    │   └── Gameplay patches
    │
    └── Bundle/
        └── AssetBundle files
```
------------------------------------------------------------------------

# Configuration

Some behavior can be configured through the generated BepInEx config
file:

    `BepInEx/config/IssaPlugin.cfg`

Options may include:

-   item tuning
-   effect durations
-   debug settings

------------------------------------------------------------------------

# Credits

Assets used in the project:

-   "Snowball - Low resources"\
    https://skfb.ly/oyCyZ\
    Licensed under Creative Commons Attribution 4.0

-   "REMOTE"\
    https://skfb.ly/pAwPL\
    Licensed under Creative Commons Attribution 4.0

-   "M200 Intervention (Low-poly)"\
    https://skfb.ly/prrWy\
    Licensed under Creative Commons Attribution 4.0
