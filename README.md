# IssaPlugin

<img src="icon.png" width="256" height="256" />

Adds **15 new items** to Super Battle Golf -- from an orbiting gunship you pilot from above, to a pack of angry attack bears. All items drop from standard item boxes and are fully configurable.

To download and/or install the mod, please check out the GitHub releases or the mod's [ThunderStore page](https://thunderstore.io/c/super-battle-golf/).

## Items

### ⚾ Baseball Bat
A melee weapon that replaces your golf club swing. Wind up and send anyone in range flying. Straightforward, personal, deeply satisfying.

### 🛩️ AC-130 Gunship
You become the gunship. The plane circles the map at altitude while you stare down a targeting camera and rain rockets on your friends. Fire is free, ammo is infinite, and the only limit is how long you can stay on station (and also the configured time-limit). Hit the mayday button and the gunship nosedives -- your opponents can lock onto it with a rocket launcher and shoot it down before it reaches them. If they can't, the crash does the work for you.

### 💣 Stealth Bomber
Paint a target on the course from ground level, then confirm the strike. Moments later a B-2 roars overhead and carpets the area with bombs. Great for punishing anyone who stops moving. The bomber itself is a valid rocket launcher target -- lock on before the run completes to intercept it.

### 🎯 Predator Missile
You pilot the missile. It drops from altitude and you steer it directly with your camera from a sort of first-person view until it hits something or you let go. 

### ⚛️ Nuke 
Drop a nuclear bomb that blows all of the other players sky-high!

### 🍩 Giant Flying Donut
A massive donut spawns and you pilot it around the course from a third-person perspective. It follows the terrain at low altitude, fires downward lasers as it passes over players, and can be shot down with standard weapons. Equal parts absurd and devastating.

### 🐻 Attack Bears
Summons AI-controlled battle bears onto the course. Bears chase down the nearest player, wind up, and launch them like a ball. They take damage from all weapons -- guns, clubs, the bat, rockets, explosions -- and become more aggressive when they get hit. Bears are persistent until killed, which means your bears are everyone's problem. And your own.

### 🧊 Freeze World
Turn the world into ice and watch as everybody helplessly slips around.

### 🌌 Low Gravity
Reduces gravity across the entire course for a configurable duration. Shots fly further, players float on hits, and any ball already airborne gets a free extension. Can backfire badly on anyone with a long putt already in motion.

### 🎯 M200 Intervention
A scoped sniper rifle. Right-click to zoom and show the scope overlay. Fire to hit instantly at any range without the backwards-knockback dive of the Elephant Gun. Tight spread when scoped, normal spread when hipfired. One shot, one hole in someone's plan.

### ⚡ Javelin 
Locks onto a ground target point and flies a lofted arc: up, turn at apex, dive straight down.

### 💥 Sticky Grenade
Thrown with an arc preview so you know exactly where it's going. Sticks to whatever it lands on -- terrain, structures, or a player who wasn't paying attention. Detonates on a fuse. If it sticks to a player, they have a few seconds to contemplate their choices.

### 🌀 Black Hole Grenade
Thrown with an arc preview. Lands and opens a black hole that pulls every nearby player and object toward its center for several seconds, then violently ejects everything outward in random directions. Good luck putting after that.

### 🧱 Placeable Wall
Plants a destructible brick wall wherever you're standing. Useful for blocking shots, redirecting players, or just making the course more chaotic. The wall takes damage from rockets, explosives, golf clubs, and the baseball bat -- it will eventually come down, but it might save you long enough for it to matter.

### 🔫 AK-47
A rapid-fire sub-machine gun. Hold the fire button and spray bullets downrange at high speed. Each bullet hits independently, so sustained fire on a target adds up fast. Accuracy degrades with range, so this one rewards getting close.

---

## Installation

Install via [Thunderstore Mod Manager](https://www.overwolf.com/app/Thunderstore-Thunderstore_Mod_Manager) or r2modman. Requires **BepInEx**.

Manual install: drop `IssaPlugin.dll` and `IssaModBundle` into `BepinEx/plugins/`.

---

## Configuration

A config file is generated at `BepInEx/config/com.scusemua.IssaPlugin.cfg` on first launch. Every item has knobs for:

- **Uses** -- how many uses come with each pickup
- **Spawn weight** -- how often it appears in item boxes (set to `0` to disable entirely)
- **Give key** -- keyboard shortcut to give yourself the item instantly (for testing/hosting)
- **Damage, knockback, duration, speed, range** -- item-specific tuning values

All settings take effect immediately on the next session without restarting the game.

---

## Compatibility

- **Multiplayer:** All items are fully networked. The host runs as the server; all item behavior is server-authoritative. Clients do not need the mod installed to join a modded lobby, but they will not see custom item models or UI overlays unless they also have it installed.
- **Existing items:** No base game items are removed or rebalanced. This mod only adds.
- **Other mods:** Should be compatible with anything that doesn't patch the same inventory or item-spawning hooks. In particular, this mod is compatible with AtomicStudio's [ModConfig](https://thunderstore.io/c/super-battle-golf/p/AtomicStudio/ModConfig/) mod, which makes it very easy to tweak the numerous available settings, and with AtomicStudio's [ItemSpawner](https://thunderstore.io/c/super-battle-golf/p/AtomicStudio/ItemSpawner/) mod. Note that the host can change settings during the game, and they will take effect the next time the associated item is used. Spawn pool weights are synchronized on a five second interval as well.
- 
---

## Console Commands

- **`vote <timeout>`** — Host only. Starts a vote among all players to enable or disable individual custom items. The timeout (in seconds) controls how long the vote stays open before closing automatically.

---

## Known Issues / Notes

- The Donut, AC-130, and Stealth Bomber all appear as valid lock-on targets for the rocket launcher. This is intentional.
- The AK-47 sometimes triggers the game's anti-cheat, as it fires rather rapidly. I am working on a fix. (It may already be fixed.)

---

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

## Building From Source

### 1. Clone the repository

    git clone https://github.com/yourname/IssaPlugin.git

### 2. Configure references

Update the paths in:

    IssaPlugin.csproj

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

    dotnet build

The compiled plugin will appear in:

    bin/Debug/netstandard2.1/

------------------------------------------------------------------------

## Project Structure (Possibly Oudated)
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

-   "Militar explosive detonator"\
    (https://skfb.ly/o9YWC) by Oscar Royo 
    Licensed under Creative Commons Attribution 4.0

-   "Jinx Bomb - Arcane"\
    (https://skfb.ly/oytAV) by KangaroOz 3D 
    Licensed under CC Attribution-NonCommercial-ShareAlike

---

*IssaPlugin is a fan-made mod and is not affiliated with Brimstone or Oro Interactive.*