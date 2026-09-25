using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    /// <summary>
    /// One trigger pull of a hitscan firearm. Callers own uses, the shot sound,
    /// and any recovery wait. <see cref="Firearm.FireShell"/> aims once, shakes
    /// once, then casts each pellet through the shared gun raycast buffer.
    /// </summary>
    public struct FirearmShellProfile
    {
        public ItemType ItemType;
        public ItemType HitResponseType;
        public float MaxAimingDistance;
        public float MaxShotDistance;
        public float Inaccuracy;
        public int PelletCount;
        public float ScreenShakeIntensity;
        public float BonusKnockback;

        /// <summary>
        /// When set, miss, shield, and hit VFX share one clock for this item type.
        /// The first effect of a shell is skipped if it is under 0.1 s after the
        /// previous shell. Every target in the shell that does play still gets
        /// its own effect. The AK-47 and AA-12 set this. Single-shot guns do not.
        /// </summary>
        public bool ThrottleVfx;
    }

    public static class Firearm
    {
        // Same interval the AK-47 used. Its old comment claimed a 0.25 s server
        // floor; the constant that shipped is 0.1 s, so this keeps it.
        private const float VfxMinInterval = 0.1f;

        private static readonly HashSet<ItemType> Busy = new HashSet<ItemType>();
        private static readonly Dictionary<ItemType, float> LastVfxTime = new Dictionary<ItemType, float>();
        private static bool _shellVfxOpen;

        private static readonly Dictionary<Hittable, PelletImpact> DamageHits =
            new Dictionary<Hittable, PelletImpact>();
        private static readonly Dictionary<Hittable, PelletImpact> ShieldHits =
            new Dictionary<Hittable, PelletImpact>();
        private static readonly Dictionary<Hittable, PelletImpact> GlancingHits =
            new Dictionary<Hittable, PelletImpact>();

        private static readonly List<PelletVisual> PelletVisuals = new List<PelletVisual>();
        private static readonly object[] ParseArgs = new object[5];
        private static bool _hasNullGlancing;
        private static PelletImpact _nullGlancing;

        private static readonly MethodInfo TryParseFirearmRaycastResultsMethod =
            typeof(PlayerInventory).GetMethod(
                "TryParseFirearmRaycastResults",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

        private static readonly MethodInfo CanHitWithGunshotMethod =
            typeof(PlayerInventory).GetMethod(
                "CanHitWithGunshot",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

        private static readonly MethodInfo IncrementAndGetCurrentItemUseIdMethod =
            typeof(PlayerInventory).GetMethod(
                "IncrementAndGetCurrentItemUseId",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

        private struct PelletImpact
        {
            public Vector3 Direction;
            public Vector3 LocalHitPoint;
            public Vector3 WorldPoint;
            public float Distance;
        }

        private struct PelletVisual
        {
            public Vector3 Direction;
            public bool Connected;
            public Vector3 WorldPoint;
        }

        public static void FireShell(PlayerInventory inventory, FirearmShellProfile profile)
        {
            if (inventory == null)
                return;

            DamageHits.Clear();
            ShieldHits.Clear();
            GlancingHits.Clear();
            PelletVisuals.Clear();
            _hasNullGlancing = false;
            _shellVfxOpen = false;

            Vector3 barrelEnd = inventory.GetElephantGunBarrelEndPosition();
            Vector3 barrelForward = inventory.GetElephantGunBarrelForward();
            int layerMask = GameManager.LayerSettings.GunHittablesMask;

            Vector3 aimPoint = inventory.GetFirearmAimPoint(
                barrelEnd,
                barrelForward,
                profile.MaxAimingDistance,
                layerMask,
                out float localYaw
            );

            if (Mathf.Abs(localYaw) > 45f)
            {
                inventory.PlayerInfo.Movement.AlignWithCameraImmediately();
                aimPoint = inventory.GetFirearmAimPoint(
                    barrelEnd,
                    barrelForward,
                    profile.MaxAimingDistance,
                    layerMask,
                    out _
                );
            }

            ScreenShakeHelper.ApplyScreenShake(profile.ScreenShakeIntensity);

            Vector3 aimDirection = aimPoint - barrelEnd;
            if (aimDirection.sqrMagnitude < 0.0001f)
                aimDirection = barrelForward;
            else
                aimDirection.Normalize();

            int pellets = profile.PelletCount < 1 ? 1 : profile.PelletCount;
            bool anyParsed = false;

            for (int i = 0; i < pellets; i++)
            {
                Vector3 dir = (aimPoint - barrelEnd).RandomlyRotatedDeg(profile.Inaccuracy);
                if (dir.sqrMagnitude < 0.0001f)
                    dir = barrelForward;

                Ray ray = new Ray(barrelEnd, dir);
                var visual = new PelletVisual { Direction = ray.direction };
                int hitCount = Physics.RaycastNonAlloc(
                    ray,
                    PlayerGolfer.raycastHitBuffer,
                    profile.MaxShotDistance,
                    layerMask,
                    QueryTriggerInteraction.Ignore
                );

                ParseArgs[0] = PlayerGolfer.raycastHitBuffer;
                ParseArgs[1] = hitCount;
                ParseArgs[2] = null;
                ParseArgs[3] = null;
                ParseArgs[4] = null;

                bool parsed = (bool)(
                    TryParseFirearmRaycastResultsMethod?.Invoke(inventory, ParseArgs) ?? false
                );
                if (!parsed)
                {
                    PelletVisuals.Add(visual);
                    continue;
                }

                anyParsed = true;
                var raycastHit = (RaycastHit)ParseArgs[3];
                visual.Connected = true;
                visual.WorldPoint = raycastHit.point;
                PelletVisuals.Add(visual);
                var hittable = ParseArgs[4] as Hittable;

                bool canHit =
                    hittable != null
                    && (bool)(
                        CanHitWithGunshotMethod?.Invoke(inventory, new object[] { hittable, null })
                        ?? false
                    );

                var impact = new PelletImpact
                {
                    Direction = ray.direction,
                    WorldPoint = raycastHit.point,
                    Distance = raycastHit.distance,
                    LocalHitPoint = canHit
                        ? hittable.transform.InverseTransformPoint(raycastHit.point)
                        : Vector3.zero,
                };

                if (!canHit)
                {
                    if (hittable != null)
                        KeepClosest(GlancingHits, hittable, impact);
                    else if (!_hasNullGlancing || impact.Distance < _nullGlancing.Distance)
                    {
                        _nullGlancing = impact;
                        _hasNullGlancing = true;
                    }
                    continue;
                }

                bool shield =
                    hittable.AsEntity.IsPlayer
                    && hittable.AsEntity.PlayerInfo.IsElectromagnetShieldActive;

                if (shield)
                    KeepClosest(ShieldHits, hittable, impact);
                else
                    KeepClosest(DamageHits, hittable, impact);
            }

            if (!anyParsed)
            {
                PlayPelletVisuals(inventory, profile, barrelEnd, aimDirection);
                return;
            }

            foreach (var pair in DamageHits)
                ApplyDamage(inventory, profile, barrelEnd, pair.Key, pair.Value);

            foreach (var pair in ShieldHits)
                PlayHit(inventory, profile, pair.Key, pair.Value, shield: true);

            foreach (var pair in GlancingHits)
            {
                if (DamageHits.ContainsKey(pair.Key) || ShieldHits.ContainsKey(pair.Key))
                    continue;

                PlayHit(inventory, profile, pair.Key, pair.Value, shield: false);
            }

            if (_hasNullGlancing)
                PlayHit(inventory, profile, null, _nullGlancing, shield: false);

            PlayPelletVisuals(inventory, profile, barrelEnd, aimDirection);
        }

        private static void KeepClosest(
            Dictionary<Hittable, PelletImpact> map,
            Hittable hittable,
            PelletImpact impact
        )
        {
            if (map.TryGetValue(hittable, out var existing) && existing.Distance <= impact.Distance)
                return;

            map[hittable] = impact;
        }

        private static void ApplyDamage(
            PlayerInventory inventory,
            FirearmShellProfile profile,
            Vector3 barrelEnd,
            Hittable hittable,
            PelletImpact impact
        )
        {
            var useId = (ItemUseId)(
                IncrementAndGetCurrentItemUseIdMethod?.Invoke(
                    inventory,
                    new object[] { profile.ItemType }
                ) ?? default(ItemUseId)
            );

            hittable.HitWithItem(
                profile.HitResponseType,
                useId,
                impact.LocalHitPoint,
                impact.Direction,
                hittable.transform.InverseTransformPoint(barrelEnd),
                impact.Distance,
                inventory,
                false,
                false,
                false,
                NetworkTime.time,
                0UL
            );

            FirearmKnockback.Send(hittable, impact.Direction, profile.BonusKnockback);

            PlayHit(inventory, profile, hittable, impact, shield: false);
        }

        /// <summary>
        /// Hold-to-fire loop. The shot sound plays once per trigger pull.
        /// Each shell consumes one use. Repeats while the left mouse button stays down.
        /// A use action that is not the mouse still fires the first shell.
        /// </summary>
        public static IEnumerator HoldFire(
            PlayerInventory inventory,
            ItemType itemType,
            System.Func<FirearmShellProfile> profile,
            System.Func<float> fireRate
        )
        {
            if (inventory == null || !Busy.Add(itemType))
                yield break;

            try
            {
                ItemHelper.SetCurrentItemUse(inventory, ItemUseType.Regular);
                inventory.PlayerInfo.PlayerAudio.PlayElephantGunShotForAllClients();

                do
                {
                    int slot = inventory.EquippedItemIndex;
                    FireShell(inventory, profile());
                    ItemHelper.DecrementAndRemove(inventory, slot);

                    if (inventory.GetEffectivelyEquippedItem(true) != itemType)
                        break;

                    yield return new WaitForSeconds(fireRate());
                } while (Mouse.current != null && Mouse.current.leftButton.isPressed);
            }
            finally
            {
                ItemHelper.SetCurrentItemUse(inventory, ItemUseType.None);
                Busy.Remove(itemType);
            }
        }

        /// <summary>
        /// One shell per use, then a recovery wait that ignores further uses.
        /// The shot sound plays once per shell. A use action that is not the mouse still fires.
        /// </summary>
        public static IEnumerator SingleShot(
            PlayerInventory inventory,
            ItemType itemType,
            System.Func<FirearmShellProfile> profile,
            System.Func<float> recovery
        )
        {
            if (inventory == null || !Busy.Add(itemType))
                yield break;

            try
            {
                ItemHelper.SetCurrentItemUse(inventory, ItemUseType.Regular);

                FireShell(inventory, profile());

                int slot = inventory.EquippedItemIndex;
                ItemHelper.DecrementAndRemove(inventory, slot);

                inventory.PlayerInfo.PlayerAudio.PlayElephantGunShotForAllClients();

                float elapsed = 0f;
                float duration = recovery();
                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }
            }
            finally
            {
                ItemHelper.SetCurrentItemUse(inventory, ItemUseType.None);
                Busy.Remove(itemType);
            }
        }

        /// <summary>
        /// A single pellet keeps the rifle path: one elephant-gun miss, or the hit
        /// effects already played, never both. A shotgun draws a pooled tracer for
        /// every pellet, hit or miss, and a pooled impact for each pellet that
        /// connected. Those pooled effects do not run the bear ray. One elephant-gun
        /// miss, down the aim center, runs only when every pellet missed.
        /// </summary>
        private static void PlayPelletVisuals(
            PlayerInventory inventory,
            FirearmShellProfile profile,
            Vector3 barrelEnd,
            Vector3 aimDirection
        )
        {
            if (PelletVisuals.Count <= 1)
            {
                if (PelletVisuals.Count == 1 && !PelletVisuals[0].Connected)
                    PlayMiss(inventory, profile, PelletVisuals[0].Direction);
                return;
            }

            bool anyConnected = false;
            for (int i = 0; i < PelletVisuals.Count; i++)
            {
                if (PelletVisuals[i].Connected)
                    anyConnected = true;
            }

            if (!AllowVfx(profile))
            {
                if (!anyConnected)
                    PlayMiss(inventory, profile, aimDirection);
                return;
            }

            for (int i = 0; i < PelletVisuals.Count; i++)
            {
                PelletVisual visual = PelletVisuals[i];
                if (visual.Direction.sqrMagnitude < 0.0001f)
                    continue;

                Quaternion rotation = Quaternion.LookRotation(visual.Direction);
                ItemHelper.PlayShotVfx(VfxType.ShotgunTracer, barrelEnd, rotation);
                if (visual.Connected)
                    ItemHelper.PlayShotVfx(VfxType.ShotgunImpact, visual.WorldPoint, rotation);
            }

            if (!anyConnected)
                PlayMiss(inventory, profile, aimDirection);
        }

        private static void PlayMiss(
            PlayerInventory inventory,
            FirearmShellProfile profile,
            Vector3 direction
        )
        {
            if (!AllowVfx(profile))
                return;

            VfxManager.PlayElephantGunMissForAllClients(inventory, direction);
        }

        private static void PlayHit(
            PlayerInventory inventory,
            FirearmShellProfile profile,
            Hittable hittable,
            PelletImpact impact,
            bool shield
        )
        {
            if (!AllowVfx(profile))
                return;

            VfxManager.PlayElephantGunHitForAllClients(
                inventory,
                new VfxManager.GunShotHitVfxData(
                    hittable,
                    shield,
                    impact.LocalHitPoint,
                    impact.WorldPoint
                )
            );
        }

        private static bool AllowVfx(FirearmShellProfile profile)
        {
            if (!profile.ThrottleVfx)
                return true;

            // The clock gates shell from shell. Once this shell has been allowed,
            // each target still gets its own effect.
            if (_shellVfxOpen)
                return true;

            float now = Time.time;
            if (
                LastVfxTime.TryGetValue(profile.ItemType, out float last)
                && now - last < VfxMinInterval
            )
                return false;

            LastVfxTime[profile.ItemType] = now;
            _shellVfxOpen = true;
            return true;
        }
    }
}
