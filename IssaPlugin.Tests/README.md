# IssaPlugin Unit Tests

A standalone NUnit 3 test project covering the pure-logic layers of IssaPlugin.

## Test files

| File | What it tests |
|---|---|
| `GlobalSessionLockTests.cs` | `GlobalSessionLock<T>` — acquire/release/independence |
| `AC130HelpersTests.cs` | `AC130Helpers.OrbitPosition` and `OrbitTangent` math |
| `VoteTallyTests.cs` | `VoteTally.Compute` — the extracted vote-tallying logic |
| `ItemRegistryTests.cs` | `ItemRegistry` structural invariants (IDs, lookups, names) |

## Project layout

```
SuperBattleGolf/
├── IssaPlugin.csproj          ← existing mod project
├── ...
└── IssaPlugin.Tests/
    ├── IssaPlugin.Tests.csproj
    ├── GlobalSessionLockTests.cs
    ├── AC130HelpersTests.cs
    ├── VoteTally.cs             ← copy this into the mod project too
    ├── VoteTallyTests.cs
    └── ItemRegistryTests.cs
```

## Running tests

```bash
dotnet test IssaPlugin.Tests/IssaPlugin.Tests.csproj
```

## What cannot be tested here

The following require a live Unity + BepInEx runtime and are intentionally excluded from this project:

- All `MonoBehaviour` subclasses (`VoteManager`, `SpawnWeightsSyncer`, etc.)
- All Harmony patch classes
- Any code that calls `NetworkServer.active`, `NetworkClient.Send`, or Mirror
- Asset-loading properties (`Icon`, `HeldModelPrefab`) on item definitions
- BepInEx `ConfigEntry<T>` values (`MaxUses`, `SpawnWeight`, `GiveKey`)
