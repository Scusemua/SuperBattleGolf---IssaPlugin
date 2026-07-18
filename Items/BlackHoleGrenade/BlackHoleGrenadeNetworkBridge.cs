using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// Server side:  Validates the throw, consumes the item, spawns and
    ///               configures the grenade GameObject.
    ///
    /// Client side:  Derives throw velocity from the camera and sends
    ///               BlackHoleGrenadeThrowMessage.  Also hosts the static
    ///               handlers for BlackHoleGrenadeLandedMessage and
    ///               BlackHoleGrenadeSpitMessage.
    /// </summary>
    public class BlackHoleGrenadeNetworkBridge : NetworkBridgeBase
    {
        // ----------------------------------------------------------------
        //  Client → Server  (called from message handler in NetworkManagerPatches)
        // ----------------------------------------------------------------

        public void ServerHandleThrow(Vector3 throwOrigin, Vector3 throwVelocity)
        {
            var inventory = GetComponent<PlayerInventory>();
            if (inventory == null)
            {
                IssaPluginPlugin.Log.LogError(
                    "[BlackHoleGrenade] No PlayerInventory on bridge object."
                );
                return;
            }

            var equipped = inventory.GetEffectivelyEquippedItem(true);
            if (equipped != ItemRegistry.BlackHoleGrenadeItemType)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[BlackHoleGrenade] Player does not have BlackHoleGrenade equipped."
                );
                return;
            }

            if (AssetLoader.BlackHoleGrenadePrefab == null)
            {
                IssaPluginPlugin.Log.LogError(
                    "[BlackHoleGrenade] Grenade prefab not loaded — cannot throw."
                );
                return;
            }

            ItemHelper.ConsumeEquippedItem(inventory);

            // Clamp throw speed server-side to prevent exploits.
            float maxSpeed = ModConfig.BlackHoleGrenade.MaxThrowSpeed.Value;
            Vector3 velocity = Vector3.ClampMagnitude(throwVelocity, maxSpeed);

            var grenadeGo = Object.Instantiate(
                AssetLoader.BlackHoleGrenadePrefab,
                throwOrigin,
                Quaternion.LookRotation(velocity.normalized, Vector3.up)
            );

            if (grenadeGo == null)
            {
                IssaPluginPlugin.Log.LogError(
                    "[BlackHoleGrenade] Grenade prefab failed to instantiate."
                );
                return;
            }

            // Apply initial velocity before Spawn so the first NetworkTransform
            // update already has the correct velocity baked in.
            var rb = grenadeGo.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.useGravity = true;
                rb.isKinematic = false;
                rb.linearVelocity = velocity;

                // Spin around the axis perpendicular to the throw so the grenade
                // tumbles naturally while in flight.
                Vector3 spinAxis = Vector3.Cross(velocity.normalized, Vector3.up).normalized;
                if (spinAxis.sqrMagnitude < 0.01f)
                    spinAxis = Vector3.right;
                rb.angularVelocity = spinAxis * 10f;
            }

            NetworkServer.Spawn(grenadeGo);

            var playerInfo = inventory.PlayerInfo;
            var itemUseId = new ItemUseId(
                playerInfo.PlayerId.Guid,
                BlackHoleGrenadeItem.NextUseIndex(),
                ItemType.RocketLauncher,
                false
            );

            var behaviour = grenadeGo.AddComponent<BlackHoleGrenadeBehaviour>();
            behaviour.ThrowerInfo = playerInfo;
            behaviour.ItemUseId = itemUseId;
            behaviour.GraceTime = ModConfig.BlackHoleGrenade.GraceTime.Value;
            behaviour.SuckDuration = ModConfig.BlackHoleGrenade.SuckDuration.Value;
            behaviour.SuckRadius = ModConfig.BlackHoleGrenade.SuckRadius.Value;
            behaviour.SuckForce = ModConfig.BlackHoleGrenade.SuckForce.Value;
            behaviour.MaxSuckForce = ModConfig.BlackHoleGrenade.MaxSuckForce.Value;
            behaviour.SpitForce = ModConfig.BlackHoleGrenade.SpitForce.Value;

            IssaPluginPlugin.Log.LogInfo(
                $"[BlackHoleGrenade] Grenade thrown from {throwOrigin} "
                    + $"velocity={velocity.magnitude:F1} m/s"
            );
        }

        // ----------------------------------------------------------------
        //  Client throw — called from TryUseItemPatch
        // ----------------------------------------------------------------

        public void ClientThrow()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[BlackHoleGrenade] No main camera for throw direction."
                );
                return;
            }

            Vector3 forward = cam.transform.forward;
            Vector3 throwOrigin = cam.transform.position + forward * 1.2f + Vector3.up * 0.3f;

            Vector3 throwDir = (
                forward + Vector3.up * ModConfig.BlackHoleGrenade.LobAngle.Value
            ).normalized;
            Vector3 velocity = throwDir * ModConfig.BlackHoleGrenade.ThrowSpeed.Value;

            NetworkClient.Send(
                new BlackHoleGrenadeThrowMessage
                {
                    ThrowOrigin = throwOrigin,
                    ThrowVelocity = velocity,
                }
            );

            IssaPluginPlugin.Log.LogInfo(
                $"[BlackHoleGrenade] Client throw: origin={throwOrigin} "
                    + $"speed={velocity.magnitude:F1}"
            );
        }

        // ----------------------------------------------------------------
        //  Server → All Clients message handlers  (static)
        // ----------------------------------------------------------------

        // Tracks active suction coroutines keyed by the black hole's netId.
        // Each coroutine runs on the local PlayerMovement.
        private static readonly Dictionary<
            uint,
            (Coroutine coroutine, PlayerMovement movement)
        > s_activeSuctions = [];

        // Tracks the local-only VFX instance spawned during the suction phase,
        // keyed by the black hole's netId.  Destroyed when the spit fires or the
        // suction coroutine times out.
        private static readonly Dictionary<uint, GameObject> s_activeVfx = [];

        /// Called on every client when the black hole grenade lands.
        public static void HandleBlackHoleGrenadeLanded(BlackHoleGrenadeLandedMessage msg)
        {
            var localInfo = GameManager.LocalPlayerInfo;
            if (localInfo == null)
                return;

            // Stop any existing suction/VFX for this black hole instance (re-land guard).
            StopSuction(msg.BlackHoleNetId);

            // Spawn the local-only black hole VFX at the landed position for everyone.
            if (AssetLoader.BlackHoleVfxPrefab != null)
            {
                var vfx = Instantiate(
                    AssetLoader.BlackHoleVfxPrefab,
                    msg.BlackHolePosition,
                    Quaternion.identity
                );
                s_activeVfx[msg.BlackHoleNetId] = vfx;
            }

            // If configured to exclude the thrower, skip suction for them.
            if (ModConfig.BlackHoleGrenade.ExcludeThrower.Value && localInfo == msg.ThrowerInfo)
                return;

            var movement = localInfo.Movement;
            if (movement == null)
                return;

            var coroutine = movement.StartCoroutine(
                SuctionCoroutine(
                    msg.BlackHoleNetId,
                    msg.BlackHolePosition,
                    msg.ThrowerInfo,
                    msg.SuckRadius,
                    msg.SuckForce,
                    msg.MaxSuckForce,
                    msg.SuckDuration
                )
            );

            s_activeSuctions[msg.BlackHoleNetId] = (coroutine, movement);

            IssaPluginPlugin.Log.LogInfo(
                $"[BlackHoleGrenade] Client: suction started (netId={msg.BlackHoleNetId})."
            );
        }

        /// Called on every client when the black hole spits.
        public static void HandleBlackHoleGrenadeSpit(BlackHoleGrenadeSpitMessage msg)
        {
            // Stop the suction coroutine for this black hole.
            StopSuction(msg.BlackHoleNetId);

            var localInfo = GameManager.LocalPlayerInfo;
            if (localInfo == null)
                return;

            // Don't spit the thrower if the config says to exclude them.
            if (ModConfig.BlackHoleGrenade.ExcludeThrower.Value && localInfo == msg.ThrowerInfo)
            {
                PlaySpitVfx(msg.BlackHolePosition);
                return;
            }

            // Electro shield blocks the spit ejection.
            if (
                ModConfig.BlackHoleGrenade.ElectroShieldPreventsEffect.Value
                && localInfo.IsElectromagnetShieldActive
            )
            {
                PlaySpitVfx(msg.BlackHolePosition);
                return;
            }

            // Only apply spit if the local player is within range.
            float dist = Vector3.Distance(localInfo.transform.position, msg.BlackHolePosition);
            if (dist > msg.SpitRadius)
            {
                PlaySpitVfx(msg.BlackHolePosition);
                return;
            }

            var movement = localInfo.Movement;
            var rb = localInfo.GetComponentInParent<Rigidbody>();

            // When in a golf cart, spit the cart instead (driver has authority).
            var spitSeat = localInfo.ActiveGolfCartSeat;
            Rigidbody spitTarget =
                spitSeat.IsValid() && spitSeat.golfCart != null
                    ? spitSeat.golfCart.AsEntity.Rigidbody
                    : rb;

            if (movement == null || spitTarget == null)
            {
                PlaySpitVfx(msg.BlackHolePosition);
                return;
            }

            // Random direction biased upward, same as server-side spit.
            Vector3 dir = Random.onUnitSphere;
            dir.y = Mathf.Abs(dir.y) + 0.5f;
            dir = dir.normalized;
            Vector3 velocityChange = dir * msg.SpitForce;

            spitTarget.AddForce(velocityChange, ForceMode.VelocityChange);

            if (
                !localInfo.ActiveGolfCartSeat.IsValid() /* not in golf cart */
            )
            {
                bool _;
                movement.TryKnockOut(
                    msg.ThrowerInfo,
                    KnockoutType.Rocket,
                    false,
                    movement.transform.InverseTransformPoint(msg.BlackHolePosition),
                    dist,
                    velocityChange,
                    ElectromagnetShieldHitBlockType.FullyBlocked,
                    msg.ItemUseId,
                    false,
                    true,
                    out _,
                    out _
                );
            }

            PlaySpitVfx(msg.BlackHolePosition);

            IssaPluginPlugin.Log.LogInfo(
                $"[BlackHoleGrenade] Client: spit applied (netId={msg.BlackHoleNetId}, "
                    + $"dir={dir:F2}, force={msg.SpitForce:F1})."
            );
        }

        // ----------------------------------------------------------------
        //  Suction coroutine (client-side, local player only)
        // ----------------------------------------------------------------

        private static IEnumerator SuctionCoroutine(
            uint blackHoleNetId,
            Vector3 blackHolePos,
            PlayerInfo throwerInfo,
            float radius,
            float baseForce,
            float maxForce,
            float duration
        )
        {
            float elapsed = 0f;
            // Add a small buffer so the coroutine outlives the server suck phase
            // and the spit message arrives before the timeout kills it.
            float timeout = duration + 3f;

            float knockdownCooldown = 0f;

            while (elapsed < timeout)
            {
                var localInfo = GameManager.LocalPlayerInfo;
                if (localInfo == null)
                    yield break;

                var rb = localInfo.GetComponentInParent<Rigidbody>();

                // Electro shield blocks all suction and knockdown for the shielded player.
                if (
                    ModConfig.BlackHoleGrenade.ElectroShieldPreventsEffect.Value
                    && localInfo.IsElectromagnetShieldActive
                )
                {
                    elapsed += Time.fixedDeltaTime;
                    yield return new WaitForFixedUpdate();
                    continue;
                }

                Vector3 playerPos = rb != null ? rb.position : localInfo.transform.position;
                Vector3 toCenter = blackHolePos - playerPos;
                float dist = toCenter.magnitude;

                // When the player is in a golf cart the server applies suction
                // directly to the cart's Rigidbody (a server-authoritative object).
                // The player follows the cart, so we skip client-side force here to
                // avoid fighting the NetworkTransform sync.
                var seat = localInfo.ActiveGolfCartSeat;
                bool inCart = seat.IsValid();

                if (dist < radius && dist > 0.01f)
                {
                    // When the player is in a golf cart the driver's client has
                    // authority over the cart's Rigidbody, so apply force to the
                    // cart rather than to the player.  The server skips occupied
                    // carts, so there is no double-application.
                    Rigidbody forceTarget =
                        inCart && seat.golfCart != null ? seat.golfCart.AsEntity.Rigidbody : rb;

                    if (forceTarget != null)
                    {
                        Vector3 dir = toCenter.normalized;

                        // Quadratic falloff: stays gentle at the edge but builds
                        // rapidly as the player nears the center, making the
                        // proximity difference clearly perceptible.
                        float t = 1f - dist / radius;
                        t *= t;
                        float targetSpeed = Mathf.Lerp(baseForce, maxForce, t);

                        // Enforce a minimum toward-center speed rather than just
                        // adding acceleration.  Player movement systems typically
                        // set rb.linearVelocity directly each FixedUpdate, which
                        // overwrites accumulated AddForce values before they take
                        // effect.  By measuring the existing toward-center component
                        // and only adding the deficit as VelocityChange, the pull
                        // wins regardless of when in the frame order it runs.
                        float currentTowardCenter = Vector3.Dot(forceTarget.linearVelocity, dir);
                        if (currentTowardCenter < targetSpeed)
                            forceTarget.AddForce(
                                dir * (targetSpeed - currentTowardCenter),
                                ForceMode.VelocityChange
                            );
                    }
                }

                if (dist >= radius || dist <= 0.01f)
                {
                    elapsed += Time.fixedDeltaTime;
                    yield return new WaitForFixedUpdate();
                    continue;
                }

                // Knock the player down once they enter the knockdown radius,
                // then respect a cooldown so we don't hammer the server every frame.
                float knockdownRadius = ModConfig.BlackHoleGrenade.KnockdownRadius.Value;
                knockdownCooldown -= Time.fixedDeltaTime;
                if (
                    knockdownRadius > 0f
                    && dist < knockdownRadius
                    && knockdownCooldown <= 0f
                    && localInfo.Movement != null
                    && !inCart
                )
                {
                    var movement = localInfo.Movement;

                    float tKnock = 1f - dist / radius;
                    tKnock *= tKnock;
                    float knockSpeed = Mathf.Min(Mathf.Lerp(baseForce, maxForce, tKnock), 8f);
                    // Small inward impulse so the knockdown direction matches
                    // the pull rather than feeling random.
                    Vector3 knockVelocity = toCenter.normalized * knockSpeed;
                    bool _;
                    movement.TryKnockOut(
                        throwerInfo,
                        KnockoutType.Rocket,
                        false,
                        movement.transform.InverseTransformPoint(blackHolePos),
                        dist,
                        knockVelocity,
                        ElectromagnetShieldHitBlockType.FullyBlocked,
                        new ItemUseId(
                            throwerInfo.PlayerId.Guid,
                            BlackHoleGrenadeItem.NextUseIndex(),
                            ItemType.RocketLauncher,
                            false
                        ),
                        false,
                        true,
                        out _,
                        out _
                    );

                    // Cooldown long enough to cover a typical knockout duration
                    // so we don't re-trigger before the player stands back up.
                    knockdownCooldown = 3f;
                }

                elapsed += Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }

            // Timed out — remove from tracker (spit message may have been lost).
            s_activeSuctions.Remove(blackHoleNetId);
        }

        // ----------------------------------------------------------------
        //  Helpers
        // ----------------------------------------------------------------

        private static void StopSuction(uint netId)
        {
            if (s_activeSuctions.TryGetValue(netId, out var entry))
            {
                if (entry.coroutine != null && entry.movement != null)
                    entry.movement.StopCoroutine(entry.coroutine);
                s_activeSuctions.Remove(netId);
            }

            if (s_activeVfx.TryGetValue(netId, out var vfxInstance))
            {
                if (vfxInstance != null)
                    Destroy(vfxInstance);
                s_activeVfx.Remove(netId);
            }
        }

        private static void PlaySpitVfx(Vector3 position)
        {
            VfxManager.PlayPooledVfxLocalOnly(
                VfxType.RocketLauncherRocketExplosion,
                position,
                Quaternion.identity,
                Vector3.one * ModConfig.BlackHoleGrenade.SpitVfxScale.Value
            );

            CameraModuleController.Shake(
                GameManager.CameraGameplaySettings.RocketExplosionScreenshakeSettings,
                position
            );
        }

        // ----------------------------------------------------------------
        //  Hole-transition cleanup
        // ----------------------------------------------------------------

        public override void ServerHoleCleanup()
        {
            // The grenade is a self-managing networked object — it destroys itself
            // when its timer expires.  No additional server state to clear.
            IssaPluginPlugin.Log.LogInfo(
                "[BlackHoleGrenade] ServerHoleCleanup called (no persistent state to clear)."
            );
        }

        public override void ClientHoleCleanup() { }
    }
}
