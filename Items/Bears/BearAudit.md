# Bear Item — Implementation Audit
## Methodology
Every source file was read in full. Issues are grouped by severity and tagged
with the exact file and location. An action plan at the end converts findings
into ordered, atomic tasks.

---

## 1. Bugs & Correctness Errors

---

### 1-A · `BearNetworkBridge.WatchBear` — list removal after null check is always a no-op

**File:** `BearNetworkBridge.cs` — `WatchBear()`

```csharp
while (bearGo != null)
    yield return null;

_activeBears.Remove(bearGo);   // ← bearGo is now a Unity fake-null
```

`Unity.Object` fake-null means `bearGo != null` becomes `false` the frame the
object is destroyed, but the C# reference still points to the destroyed wrapper.
`List<GameObject>.Remove` uses `Object.Equals`, which *does* work for Unity
fake-nulls — so the remove itself is not broken. However, the subsequent check:

```csharp
if (_serverSessionActive && _activeBears.Count == 0)
```

…fires correctly only if the remove succeeded. The real problem is that
`EndServerSession()` already calls `NetworkServer.Destroy` on every bear in
`_activeBears`, which triggers `WatchBear`'s loop to exit for *each* bear in
quick succession. Each exiting coroutine removes from the list and checks
`Count == 0`. The *first* bear to exit will see `Count > 0` (others still in
list), remove itself, then the *last* one will see `Count == 0` and call
`EndServerSession()` again — which re-enters despite the `if (!_serverSessionActive)` guard
(already set to `false` by the first call). This is not a crash, but
`BearOverlayEndMessage` will be sent a second time, and the log will double-print.

**Fix:** Clear `_activeBears` *before* entering the destroy loop in
`EndServerSession`, so `WatchBear` coroutines that exit from the forced destroy
see an empty list and do nothing:

```csharp
private void EndServerSession()
{
    if (!_serverSessionActive) return;
    _serverSessionActive = false;            // set FIRST to block re-entry

    StopAndClearTimeout();

    var toDestroy = new List<GameObject>(_activeBears);
    _activeBears.Clear();                    // clear BEFORE destroying

    foreach (var bear in toDestroy)
        if (bear != null) NetworkServer.Destroy(bear);

    NetworkServer.SendToAll(new BearSessionEndMessage());
    connectionToClient?.Send(new BearOverlayEndMessage());
}
```

---

### 1-B · `BearHitReceiver.HandleHit` — double-invocation of `OnHitsExceeded`

**File:** `BearHitReceiver.cs` — `HandleHit()`

```csharp
HitCount++;

OnBearHitByPlayer?.Invoke(attacker);     // ← raises event

if (HitCount >= HitsRequired)
{
    OnHitsExceeded?.Invoke();            // ← kills the bear
}
else
{
    Behaviour?.OnHitByExplosion(attacker); // ← stuns
}
```

`OnBearHitByPlayer` is subscribed to by `BearBehaviour.OnHitByPlayer`, which
calls `_selector.NotifyHitBy(attacker)` — fine, no problem there.

But `OnHitsExceeded` is set in `Awake()` to call `Behaviour?.OnKilled()`, which
calls `TransitionTo(BearAIState.Dying)`, which calls `BroadcastState()` —
`NetworkServer.SendToAll`. Then `HandleHit` continues and the outer
`CustomHittable` base class may *also* invoke `OnHitsExceeded` through its own
path if it has any built-in bookkeeping. Inspect `CustomHittable` in the
original codebase — if `OnHit` is invoked by the base class *after* the
subclass's `HandleHit`, `OnKilled` may fire twice.

Additionally: the guard `if (HitCount >= HitsRequired) return;` at the top of
`HandleHit` checks before incrementing. After the kill hit increments `HitCount`
to equal `HitsRequired`, the guard will pass on the *next* call — so a second
explosion immediately after death will call `OnBearHitByPlayer` and
`OnHitsExceeded` again. `OnKilled` guards against this with its own state check,
but `OnBearHitByPlayer` will still fire unnecessarily.

**Fix:** After the kill branch, `return` immediately:
```csharp
if (HitCount >= HitsRequired)
{
    IssaPluginPlugin.Log.LogInfo("[Bear] Bear killed by explosion.");
    OnHitsExceeded?.Invoke();
    return;   // ← prevent fall-through to stun branch and future invocations
}
Behaviour?.OnHitByExplosion(attacker);
```

---

### 1-C · `BearBehaviour.Dead` state calls `NetworkServer.Destroy` every `FixedUpdate`

**File:** `BearBehaviour.cs` — `UpdateStateMachine()`

```csharp
case BearAIState.Dead:
    NetworkServer.Destroy(gameObject);
    break;
```

`NetworkServer.Destroy` is called every physics tick (50 Hz) until Unity
actually processes the destroy. On the frame it is first called, Unity queues
the destroy — but `FixedUpdate` may fire 1–2 more times before the object is
gone. `NetworkServer.Destroy` on an already-pending-destroy object is undefined
and may log errors or cause double-deregistration in Mirror's spawned list.

**Fix:** Set a `_destroying` flag on first entry:
```csharp
case BearAIState.Dead:
    if (!_destroying)
    {
        _destroying = true;
        NetworkServer.Destroy(gameObject);
    }
    break;
```

---

### 1-D · `BearBehaviour.GetActivePlayers` allocates a new `List<PlayerInfo>` every `FixedUpdate`

**File:** `BearBehaviour.cs` — `GetActivePlayers()`

```csharp
private static List<PlayerInfo> GetActivePlayers()
{
    var players = new List<PlayerInfo>();
    // ...
    return players;
}
```

This is called from `UpdateIdle`, `UpdatePursuing`, `UpdateEnraged` — up to
once per `FixedUpdate` per bear. With 4 bears that is 200 allocations/second.
The list is passed to `BearTargetSelector.SelectTarget` which only iterates it
— it never stores it.

**Fix:** Cache a reusable `static List<PlayerInfo> _playerScratchpad` and
`Clear()` + repopulate it each call. Because `BearBehaviour.FixedUpdate` runs
on the Unity main thread (single-threaded), a static scratch list is safe:

```csharp
private static readonly List<PlayerInfo> _playerScratchpad = new List<PlayerInfo>();

private static List<PlayerInfo> GetActivePlayers()
{
    _playerScratchpad.Clear();
    if (GameManager.LocalPlayerInfo != null)
        _playerScratchpad.Add(GameManager.LocalPlayerInfo);
    var remotes = GameManager.RemotePlayers;
    if (remotes != null)
        _playerScratchpad.AddRange(remotes);
    return _playerScratchpad;
}
```

---

### 1-E · `BearBehaviour._interest` and `_danger` arrays are `static` — unsafe with multiple bears

**File:** `BearBehaviour.cs`

```csharp
private static readonly float[] _interest = new float[ContextRayCount];
private static readonly float[] _danger   = new float[ContextRayCount];
```

These are shared across all `BearBehaviour` instances. `FixedUpdate` for
different bear instances runs sequentially on the main thread, so there is no
threading issue — but if two bears are within the same physics batch, the second
bear's context steering will operate on arrays partially written by the first
bear before it finishes. In practice Unity's `FixedUpdate` runs one at a time
(no parallelism within a frame), so this is safe but fragile and misleading.

**Fix:** Make them instance fields:
```csharp
private readonly float[] _interest = new float[ContextRayCount];
private readonly float[] _danger   = new float[ContextRayCount];
```

---

### 1-F · `BearBehaviour.ServerExplodeMethod` is declared but never used

**File:** `BearBehaviour.cs`

```csharp
private static readonly MethodInfo ServerExplodeMethod = typeof(Rocket).GetMethod(
    "ServerExplode",
    BindingFlags.NonPublic | BindingFlags.Instance
);
```

This reflection lookup is cached on class load but the field is never
referenced anywhere in `BearBehaviour`. Dead code that also performs a
reflection lookup on every game start.

**Fix:** Delete this field entirely. If a death explosion is ever needed, it can
be added then. The same method is already cached in `StickyGrenadeBehaviour` and
`JavelinRocketBehaviour` for that purpose.

---

### 1-G · `BearAnimatorDriver.NetId` property is computed but never called

**File:** `BearMarkerAndAnimatorDriver.cs`

```csharp
public uint NetId => _ni != null ? _ni.netId : 0u;
```

No external code reads `BearAnimatorDriver.NetId`. The routing in
`BearNetworkBridge.HandleBearState` goes through `NetworkClient.spawned` by
`msg.BearNetId` and then calls `ni.GetComponent<BearAnimatorDriver>()` — it
never asks the driver for its own netId.

**Fix:** Delete this property.

---

### 1-H · `BearMarkerAndAnimatorDriver.cs` uses `using Mirror` but `BearMarker` never uses Mirror types

**File:** `BearMarkerAndAnimatorDriver.cs`

```csharp
using Mirror;
using UnityEngine;
```

`BearMarker` is a plain `MonoBehaviour`. `BearAnimatorDriver` uses
`NetworkIdentity` (Mirror type), so the `using Mirror` is needed for the driver.
But `_ni` (the `NetworkIdentity`) is fetched in `Awake` and only used by the
dead `NetId` property (see 1-G). If `NetId` is removed, `_ni` and `using Mirror`
can both be removed from this file.

**Fix:** Remove `_ni`, `NetId`, and `using Mirror` once 1-G is fixed.

---

## 2. Logic & Design Issues

---

### 2-A · `AttackCooldown` transitions to `Pursuing` even when the target was null during attack

**File:** `BearBehaviour.cs` — `TransitionTo(BearAIState.AttackCooldown)` and `UpdateStateMachine`

After `AttackCooldown` timer expires:
```csharp
case BearAIState.AttackCooldown:
    if (_stateTimer <= 0f)
        TransitionTo(BearAIState.Pursuing);
    break;
```

`Pursuing` immediately calls `_selector.SelectTarget(...)`. If there are no
players, it transitions to `Idle`. This is correct and harmless — but
`_currentTarget` is never cleared between `Attacking` → `AttackCooldown`, so
`UpdateAttacking` re-checks `_currentTarget != null` unnecessarily (the attack
already fired). Minor, but `_currentTarget` should be nulled in `TransitionTo`
for `AttackCooldown` to avoid stale state.

---

### 2-B · `BearLockOnDetectionPatch` sends `BearPrepareHomingMessage` every frame while locked on

**File:** `BearLockOnPatches.cs` — `BearLockOnDetectionPatch.Postfix`

```csharp
if (nowTargetingBear)
    NetworkClient.Send(new BearPrepareHomingMessage());
```

This is the same B-1 issue identified in the original codebase audit for the
AC130 and Donut — the message is sent every frame (~60/sec) to set a boolean
flag that only needs to be set once per fire intent. The server handler just
does `PendingBearHoming = true`.

**Fix:** Only send on the rising edge (first frame of lock):
```csharp
if (nowTargetingBear && !_wasTargetingBear)
    NetworkClient.Send(new BearPrepareHomingMessage());
```

---

### 2-C · `BearTargetSelector` constants are hardcoded — should pull from `Configuration`

**File:** `BearTargetSelector.cs`

```csharp
private const float LockDuration          = 8f;
private const float StealDistanceThreshold = 12f;
private const float AbandonDistance        = 65f;
private const float AggroStealThreshold    = 4f;
private const float AggroDuration          = 15f;
```

Every other tunable value in the codebase is in `Configuration`. These five are
the most impactful gameplay parameters for how the bears feel — whether they
ping-pong, how sticky their target lock is, how far a player must flee to escape.
Having them as C# `const` means they cannot be tuned without recompiling.

**Fix:** Add them to `Configuration` and `BearConfiguration_Additions.cs`:
```csharp
public static ConfigEntry<float> BearTargetLockDuration       { get; private set; }
public static ConfigEntry<float> BearTargetStealThreshold     { get; private set; }
public static ConfigEntry<float> BearTargetAbandonDistance    { get; private set; }
public static ConfigEntry<float> BearAggroStealThreshold      { get; private set; }
public static ConfigEntry<float> BearAggroDuration            { get; private set; }
```

Then in `BearTargetSelector.SelectTarget` read from `Configuration.*` instead
of `const` fields.

---

### 2-D · `BearBehaviour.IsTargetReachable` reachability check is inverted and unreliable

**File:** `BearBehaviour.cs` — `IsTargetReachable()`

```csharp
Vector3 slopeCheck = Vector3.Lerp(toTarget, Vector3.up, 0.5f).normalized;

if (!Physics.Raycast(transform.position + Vector3.up, slopeCheck, 8f,
                     ItemHelper.GroundLayerMask))
    return false;
```

The logic reads: "if a ray fired halfway between toward-target and straight-up
hits *nothing*, then it's unreachable." This is inverted — a ray fired in that
diagonal direction hitting open air means there *is* a slope or ramp going up,
which would actually suggest the target *is* reachable. A ray hitting nothing
means the diagonal path is clear of terrain, which on a cliff face would still
be true since the vertical wall face often has no collider pointing that way.

The check as written is also only one-directional — it does not handle targets
*below* the bear (negative height diff), deep pits, or targets behind a wall at
the same elevation.

**Fix:** Replace with a simpler, more reliable check — cast a ray from the bear
toward the target and check if terrain blocks it within the horizontal XZ
distance. If blocked and height diff exceeds threshold, call it unreachable:

```csharp
private bool IsTargetReachable(Vector3 targetPos)
{
    float heightDiff = targetPos.y - transform.position.y;
    if (Mathf.Abs(heightDiff) <= Configuration.BearMaxClimbHeight.Value)
        return true;

    // Ray from bear toward target: if terrain blocks it before arrival,
    // the height gap is likely a cliff rather than a walkable slope.
    Vector3 toTarget = (targetPos - transform.position);
    float   dist     = toTarget.magnitude;
    if (Physics.Raycast(transform.position + Vector3.up * 0.5f,
                        toTarget.normalized, dist * 0.8f,
                        ItemHelper.GroundLayerMask))
        return false;

    return true;
}
```

---

### 2-E · `BearBehaviour.MoveInDirection` applies `GroundStickForce` only when grounded — but `_rb.linearVelocity.y` is always preserved regardless

**File:** `BearBehaviour.cs` — `MoveInDirection()`

```csharp
_rb.AddForce(Vector3.down * GroundStickForce, ForceMode.Acceleration);
// ...
_rb.linearVelocity = new Vector3(moveVelocity.x, _rb.linearVelocity.y, moveVelocity.z);
```

When the ground raycast hits, `GroundStickForce` is added and then immediately
on the same line, `_rb.linearVelocity` is set with the *current* `linearVelocity.y`
(which does not yet include the force just added, since `AddForce` applies next
physics step). This means the stick force accumulates across frames, and over
multiple frames on flat ground the downward velocity will grow until something
caps it. The bear will press increasingly hard into flat terrain.

Also: when the raycast misses (bear is airborne), `moveVelocity` is the flat
horizontal vector, and the full set still overwrites `linearVelocity` — but the
Y is preserved from the rigidbody. This is correct for airborne, but the
`GroundStickForce` branch never fires then, so it's fine.

**Fix:** Instead of accumulating `AddForce`, set Y velocity explicitly when
grounded, proportional to the slope difference:

```csharp
if (groundHit)
{
    Vector3 surfaceDir = Vector3.ProjectOnPlane(worldDir, hit.normal).normalized;
    moveVelocity = surfaceDir * speed;
    // Snap Y to terrain rather than accumulating stick force
    float targetY  = hit.point.y + 0.1f;
    float yVel     = Mathf.Clamp((targetY - transform.position.y) / Time.fixedDeltaTime,
                                  -10f, 10f);
    _rb.linearVelocity = new Vector3(moveVelocity.x, yVel, moveVelocity.z);
}
else
{
    // Airborne: preserve gravity-driven Y
    _rb.linearVelocity = new Vector3(moveVelocity.x, _rb.linearVelocity.y, moveVelocity.z);
}
```

---

### 2-F · `BearBehaviour.OnHitByPlayer` and `OnHitByExplosion` are redundant — one is the event handler for the other

**File:** `BearBehaviour.cs`

```csharp
// Event subscription in Start():
HitReceiver.OnBearHitByPlayer += OnHitByPlayer;

// Handler:
private void OnHitByPlayer(PlayerInfo attacker)
{
    _selector.NotifyHitBy(attacker);
}

// Also called from BearHitReceiver.HandleHit:
public void OnHitByExplosion(PlayerInfo attacker)
{
    _selector.NotifyHitBy(attacker);    // ← SAME CALL
    TransitionTo(BearAIState.Stunned);
}
```

`OnHitByPlayer` is subscribed to `OnBearHitByPlayer`. `HandleHit` raises
`OnBearHitByPlayer` AND then directly calls `Behaviour.OnHitByExplosion`. Both
call `_selector.NotifyHitBy(attacker)`. So for every hit, `NotifyHitBy` is
called *twice* — once via the event and once directly.

**Fix:** Remove the `OnBearHitByPlayer` event subscription in `BearBehaviour`
entirely. Have `BearHitReceiver.HandleHit` just call `Behaviour.OnHitByExplosion`
directly (which already handles the aggro call). Remove `BearBehaviour.OnHitByPlayer`,
the event subscription in `Start()`, and the unsubscription in `OnDestroy()`.
Also remove `OnBearHitByPlayer` from `BearHitReceiver` if it has no other
subscribers — simplifying both classes.

---

### 2-G · `BearClientSetup` calls `GetComponent` three times in Awake for components that are already on the prefab if the bundle was set up correctly

**File:** `BearClientSetup.cs`

```csharp
if (gameObject.GetComponent<BearMarker>() == null)
    gameObject.AddComponent<BearMarker>();
if (gameObject.GetComponent<Entity>() == null)
    gameObject.AddComponent<Entity>();
if (gameObject.GetComponent<LockOnTarget>() == null)
    gameObject.AddComponent<LockOnTarget>();
if (gameObject.GetComponent<BearAnimatorDriver>() == null)
    gameObject.AddComponent<BearAnimatorDriver>();
```

The null-guards are correct defensive practice (same pattern as AC130ClientSetup),
but unlike Entity and LockOnTarget — which may or may not be on the prefab —
`BearMarker` and `BearAnimatorDriver` are *always* added here since the prefab
cannot have them pre-baked (they live in the mod DLL). The null-check is
unnecessary overhead for those two. Minor, but worth noting.

No action strictly required — the pattern is consistent with the rest of the
codebase. Leave it for consistency.

---

## 3. Gaps & Incomplete Implementation

---

### 3-A · No `OrbitalLaser` support for bears — laser does not target or damage bears

**Files:** `OrbitalLaserPatches.cs` (existing), bear files (new)

The existing `OrbitalLaserGetTargetPatch.Postfix` checks for `AC130GunshipMarker`
and `BomberMarker` and `DonutMarker` but not `BearMarker`. The
`OrbitalLaserServerActivatePatch` and `OrbitalLaserOnBUpdatePatch` similarly do
not handle bears.

Bears are valid lock-on targets (via `BearLockOnIsValidPatch`), and players can
aim the orbital laser at them via `OrbitalLaserLockOnIndicatorPatch` — but the
actual laser beam will not home to the bear or damage it.

This is a gap in the `ExistingFileEdits.cs` integration instructions — the
orbital laser patches needed for bears are not listed.

**Fix:** In `OrbitalLaserPatches.cs`, add `BearMarker` handling alongside
`DonutMarker` in `OrbitalLaserGetTargetPatch`, `OrbitalLaserServerActivatePatch`,
and `OrbitalLaserOnBUpdatePatch`. Wire `BearHitReceiver.OnHit` as the `onHit`
callback in `FindClosestAircraft`.

---

### 3-B · No `BearItem` entry in `GetPrefabForItem` for hand-held model

**File:** `ItemUsagePatches.cs` — `GetPrefabForItem()` (existing file, per `ExistingFileEdits.cs`)

The edit instruction says:
```csharp
if (type == BearItem.BearItemType)
    return AssetLoader.BearPrefab;   // held as a visual prop
```

But `BearPrefab` is the full networked bear vehicle — a large 3D model with a
`Rigidbody`, `NetworkIdentity`, etc. Using it as a held hand model will:

1. Look wrong (full bear model clipped into the player's hand).
2. Potentially cause the `StripNetworkComponents` issue — the NetworkBehaviour
   on the held instance may start ticking.

The `ExistingFileEdits.cs` note says "you may prefer a smaller handheld prop,"
but no separate handheld asset is loaded in `AssetLoader`.

**Fix:** Either add a small separate prop (e.g. a bear paw icon model) or return
`null` from `GetPrefabForItem` for `BearItemType`, which will skip the held
model entirely and show only the OrbitalLaser arm pose. Given the bear is
a one-shot activation item (you press use and the bears appear), having no held
visual is acceptable. Document this explicitly.

---

### 3-C · `BearAnimatorDriver.ApplyState` rotates the transform client-side in `AttackCooldown`-mapped `Idle` state

**File:** `BearMarkerAndAnimatorDriver.cs` — `ApplyState()`

```csharp
case BearAIState.Idle:
case BearAIState.AttackCooldown:
    _animator.SetBool(HashIsIdle, true);
    break;
```

Both `Idle` and `AttackCooldown` map to the idle animation — fine. But the
rotation block at the bottom only fires for `Pursuing`, `Charging`, `Enraged`.
When the bear is in `AttackCooldown` facing its just-attacked target, the client
visual rotation is not updated. After an attack, the bear visually snaps to
whatever direction NetworkTransform pushes it to, which may cause a jarring
visual spin. This is a minor cosmetic issue, not a gameplay bug.

**Optional fix:** Add `BearAIState.AttackCooldown` to the rotation block so the
visual keeps facing the last known target position during cooldown.

---

### 3-D · `BearOverlay` is not added to `Plugin.cs` in `ExistingFileEdits.cs` section 12

**File:** `ExistingFileEdits.cs` — section 12

The instruction reads:
```
Add after the last gameObject.AddComponent<...> call:
    gameObject.AddComponent<BearOverlay>();
```

This is present and correct. ✓ No gap here — just confirming it is included.

---

### 3-E · `ExistingFileEdits.cs` section for `PlayerAnimatorOnEquippedChangedPatch` is missing

**File:** `ExistingFileEdits.cs`

`ItemUsagePatches.cs` has `PlayerAnimatorOnEquippedChangedPatch.Prefix` which
maps custom items to animator controllers. The edit instruction in section 6
only covers `PlayerAnimatorSetEquippedItemPatch`. The `OnEquippedChanged` patch
also needs a `BearItem` line — otherwise remote clients show the wrong animation
controller when the bear item is equipped.

**Fix:** Add to section 6 of `ExistingFileEdits.cs`:
```
In PlayerAnimatorOnEquippedChangedPatch.Prefix, add:
    else if (equippedItem == BearItem.BearItemType)
        equippedItem = ItemType.OrbitalLaser;
```

---

### 3-F · `BearOverlay` session timer uses `Time.time` captured in `Update` but `_startTime` is set in `SetActive` which runs from a NetworkClient handler (could be same frame or different frame)

**File:** `BearOverlay.cs` — `SetActive`, `OnGUI`

```csharp
public void SetActive(bool active, int bearCount, float sessionDuration)
{
    _startTime = Time.time;  // captured at message receipt
    // ...
}
```

The `BearOverlayBeginMessage` arrives from the server. On a listen-server, this
is the same frame as `ServerSummonBears`. On a remote client, it arrives one or
more frames later (network RTT). The server's `BearSessionDuration` timeout
starts immediately at spawn. If the client's `_startTime` is recorded 200ms
after the server started the timer, the overlay will show a countdown that ends
200ms after the actual server timeout — the bears will despawn before the client
countdown hits zero.

This is a cosmetic inaccuracy, not a gameplay bug. For correctness, the server
should include the elapsed time since session start in `BearOverlayBeginMessage`,
and the client should subtract it from `sessionDuration` to set `_startTime`
appropriately.

**Fix:** Add `float ElapsedSinceStart` to `BearOverlayBeginMessage` (default 0
for listen-server). Set it in the server: time since `ServerSummonBears` was
called. Client uses `_sessionDuration - msg.ElapsedSinceStart` as the remaining
time. Or simply: the client counts down from `sessionDuration` with the
knowledge that it may be slightly off — acceptable for a game item timer.

---

## 4. Performance & Allocation Issues

---

### 4-A · `BearRocketExplosionPatch.Postfix` allocates `HashSet<GameObject>` on every rocket explosion

**File:** `BearRocketPatch.cs` — `Postfix()`

```csharp
var notified = new System.Collections.Generic.HashSet<GameObject>();
```

Every rocket explosion (which includes all game rockets) allocates a `HashSet`.
With heavy AC130 fire (one rocket every 0.8s), Bomber runs (15+ rockets),
and player rockets, this is frequent.

**Fix:** Reuse a static `HashSet<GameObject>` cleared at the start of each
postfix — same as `BearBehaviour._playerScratchpad`:

```csharp
private static readonly HashSet<GameObject> _notifiedBears = new HashSet<GameObject>();

static void Postfix(...)
{
    if (!NetworkServer.active) return;
    _notifiedBears.Clear();
    try {
        foreach (var col in hits)
        {
            var r = col.GetComponentInParent<BearHitReceiver>();
            if (r == null || !_notifiedBears.Add(r.gameObject)) continue;
            receiver.OnHit?.Invoke();
        }
    }
    finally { BearExplosionAttackerContext.CurrentAttacker = null; }
}
```

---

### 4-B · `BearLockOnDetectionPatch.FindNearestBearInView` calls `FindObjectsByType` every frame

**File:** `BearLockOnPatches.cs`

```csharp
var bears = Object.FindObjectsByType<BearMarker>(FindObjectsSortMode.None);
```

`TryGetBestLockOnTarget` is called every frame for the local player. With few
bears (1–4) this is low cost, but `FindObjectsByType` still enumerates the full
scene hierarchy. The existing `GunshipLockOnDetectionPatch` does the same for
`AC130GunshipMarker` — so this matches established patterns. No action required
unless bear counts become large.

---

### 4-C · `BearBehaviour.ComputeContextSteering` recalculates `360f / ContextRayCount` inside a hot loop

**File:** `BearBehaviour.cs`

```csharp
for (int i = 0; i < ContextRayCount; i++)
{
    float angle = i * (360f / ContextRayCount);   // division per iteration
```

Minor — the compiler may constant-fold this since `ContextRayCount` is `const`.
Verify with `const float AngleStep = 360f / ContextRayCount` pre-calculated.

---

## 5. Confusion & Clarity Issues

---

### 5-A · `BearHitReceiver` inherits `CustomHittable` but never lets the base class's `OnHit` pathway do anything useful

**File:** `BearHitReceiver.cs`

The class inherits `CustomHittable` and subscribes `OnHit += HandleHit` in its
own `Awake`. This means the `CustomHittable.OnHit` Action is invoked externally
(by `BearRocketExplosionPatch`) which calls `HandleHit`. The class *also* sets
`OnHitsExceeded` — the base class delegate for when `HitCount >= HitsRequired`.

But `HandleHit` calls `OnHitsExceeded?.Invoke()` directly itself. The base class
never invokes `OnHitsExceeded` — that responsibility is in the subclass. This
means `CustomHittable.OnHitsExceeded` is used as a plain `Action` delegate, not
as a base-class lifecycle hook. It works, but readers will wonder why `HitCount`
and `HitsRequired` are incremented/checked in the subclass rather than the base.

Compared to `AC130HitReceiver` and `DonutHitReceiver` in the original codebase,
those classes increment `HitCount` manually in their `OnHit` handler too — so
the pattern is consistent. No change needed; add a comment explaining the
`CustomHittable` base class does not auto-invoke `OnHitsExceeded`.

---

### 5-B · `BearClientSetup.cs` doc comment says "BearNetworkBridge.ServerStartBears()" but the method is named `ServerSummonBears`

**File:** `BearClientSetup.cs`

```csharp
/// BearBehaviour (server-side AI) is added by BearNetworkBridge.ServerStartBears()
```

The actual method is `ServerSummonBears()`.

**Fix:** Update the comment.

---

### 5-C · `BearNetworkBridge.HandleBearSessionEnd` logs but does nothing else — misleading

**File:** `BearNetworkBridge.cs`

```csharp
public static void HandleBearSessionEnd(BearSessionEndMessage msg)
{
    IssaPluginPlugin.Log.LogInfo("[Bear] Session ended (all clients).");
}
```

This message is broadcast to all clients but the handler does nothing except
log. The comment in `BearMessages.cs` says it's "so clients can clean up any
local VFX" — but no VFX cleanup code exists or is planned. This creates the
impression that something is missing.

**Fix:** Either remove `BearSessionEndMessage` entirely (it serves no purpose
if there is no client-side cleanup), or add a concrete TODO comment explaining
what future VFX it is reserved for. If removed, also remove its Writer/Reader
registration in `NetworkManagerPatches_BearAdditions.cs`.

---

### 5-D · `BearBehaviour` header comment says `UpdateTimers → UpdateStateMachine → MoveAndOrient` but there is no `UpdateTimers` method — the timer is decremented inline in `FixedUpdate`

**File:** `BearBehaviour.cs`

```csharp
/// FixedUpdate  →  UpdateTimers → UpdateStateMachine → MoveAndOrient
```

There is no `UpdateTimers()` or `MoveAndOrient()` method. The timer decrement
is one line in `FixedUpdate`, and movement is inside each state's update method.
Misleading to readers.

**Fix:** Update the summary comment:
```csharp
/// FixedUpdate: decrement state timer → UpdateStateMachine (which calls
/// per-state movement and transition logic).
```

---

## Action Plan

Ordered by priority and dependency. Each item is a single, atomic change.

| # | Priority | File | Action |
|---|----------|------|--------|
| 1 | **Critical** | `BearNetworkBridge.cs` | Fix `EndServerSession` re-entrancy: set `_serverSessionActive = false` first, copy list before clearing, then destroy (fixes 1-A) |
| 2 | **Critical** | `BearHitReceiver.cs` | Add `return` after `OnHitsExceeded?.Invoke()` to prevent fall-through to stun (fixes 1-B) |
| 3 | **Critical** | `BearBehaviour.cs` | Add `_destroying` bool flag to guard `NetworkServer.Destroy` in Dead state (fixes 1-C) |
| 4 | **High** | `BearBehaviour.cs` | Remove `static` from `_interest` and `_danger` arrays; make them instance fields (fixes 1-E) |
| 5 | **High** | `BearBehaviour.cs` | Remove unused `ServerExplodeMethod` reflection field (fixes 1-F) |
| 6 | **High** | `BearBehaviour.cs` | Make `GetActivePlayers()` use a static scratch list instead of allocating per call (fixes 1-D) |
| 7 | **High** | `BearRocketPatch.cs` | Make dedup `HashSet` static and reuse it (fixes 4-A) |
| 8 | **High** | `BearBehaviour.cs` | Remove `OnHitByPlayer` event handler and unsubscribe in `OnDestroy`; have `BearHitReceiver` call `OnHitByExplosion` only (fixes 2-F, removes double `NotifyHitBy`) |
| 9 | **High** | `BearHitReceiver.cs` | Remove `OnBearHitByPlayer` event if no subscribers remain after fix 8 |
| 10 | **High** | `BearLockOnPatches.cs` | Send `BearPrepareHomingMessage` on rising edge only (`!_wasTargetingBear`) (fixes 2-B) |
| 11 | **Medium** | `BearBehaviour.cs` | Fix inverted `IsTargetReachable` logic (fixes 2-D) |
| 12 | **Medium** | `BearBehaviour.cs` | Fix `MoveInDirection` ground-stick to use position-based Y rather than accumulating force (fixes 2-E) |
| 13 | **Medium** | `BearMarkerAndAnimatorDriver.cs` | Remove unused `NetId` property and `_ni` field; remove `using Mirror` (fixes 1-G, 1-H) |
| 14 | **Medium** | `Configuration.cs` | Add 5 tuning constants from `BearTargetSelector` as `ConfigEntry<float>` (fixes 2-C) |
| 15 | **Medium** | `BearTargetSelector.cs` | Replace `const` fields with `Configuration.*` reads (fixes 2-C) |
| 16 | **Medium** | `ExistingFileEdits.cs` | Add `BearItem` entry in `PlayerAnimatorOnEquippedChangedPatch.Prefix` (fixes 3-E) |
| 17 | **Medium** | `ExistingFileEdits.cs` | Clarify `GetPrefabForItem` for `BearItemType` — return `null` or add a handheld asset; document the decision (fixes 3-B) |
| 18 | **Low** | `OrbitalLaserPatches.cs` | Add `BearMarker` / `BearHitReceiver` handling in the three orbital laser patches (fixes 3-A) |
| 19 | **Low** | `BearNetworkBridge.cs` | Either remove `BearSessionEndMessage` or add a concrete VFX use for it (fixes 5-C) |
| 20 | **Low** | `BearClientSetup.cs` | Fix doc comment: `ServerStartBears()` → `ServerSummonBears()` (fixes 5-B) |
| 21 | **Low** | `BearBehaviour.cs` | Fix header summary comment to match actual structure (fixes 5-D) |
| 22 | **Low** | `BearOverlay.cs` | Optionally: include elapsed-since-start in `BearOverlayBeginMessage` for accurate countdown (fixes 3-F) |
| 23 | **Low** | `BearBehaviour.cs` | Pre-compute `AngleStep = 360f / ContextRayCount` constant to avoid repeated division in hot loop (fixes 4-C) |
| 24 | **Low** | `BearBehaviour.cs` | Null `_currentTarget` in `TransitionTo(AttackCooldown)` to avoid stale reference (fixes 2-A) |
