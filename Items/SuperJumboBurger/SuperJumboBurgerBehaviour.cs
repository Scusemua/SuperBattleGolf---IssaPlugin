using System.Collections;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Drives the local player's oversized giant form.
    ///
    /// Networking model — why there is no custom scale replication:
    ///
    ///   PlayerMovement sets syncDirection = SyncDirection.ClientToServer, and
    ///   characterScale is a [SyncVar(hook = OnCharacterScaleChanged)] on it. The owning
    ///   client is therefore authoritative over its own scale: the base game's own grow
    ///   coroutine is a plain local coroutine that writes NetworkcharacterScale, and
    ///   Mirror replicates it outward. Every remote client runs OnCharacterScaleChanged,
    ///   which rescales BonesParent, all five colliders, nametag/UI offsets and VFX.
    ///
    ///   So writing NetworkcharacterScale here makes remote players see the correct
    ///   giant exactly the way they see a vanilla Jumbo Burger. Sending our own message
    ///   would duplicate — and race with — that path.
    ///
    /// State ownership:
    ///
    ///   The isInJumboBurgerGiantForm bool stays owned by the base game. We activate
    ///   through PlayerInfo.LocalPlayerActivateJumboBurgerGiantForm so that all the
    ///   gameplay gated on that bool (giant swing, collision knockouts, camera pull-back,
    ///   golf-cart collider handling, knockout immunity) works, and so the base game's
    ///   own cancel path still runs. We only override the *scale target* afterwards.
    /// </summary>
    public static class SuperJumboBurgerBehaviour
    {
        /// True while the local player is in a super-sized giant form specifically.
        /// Distinguishes our form from a vanilla Jumbo Burger, which we must not resize.
        public static bool LocalSessionActive { get; private set; }

        private static Coroutine _routine;

        /// The inventory whose item-use animation state we drove, if any. Held so the
        /// eat pose can be cleared from any exit path, including ForceReset.
        private static PlayerInventory _animatingInventory;

        /// The MonoBehaviour the routine was started on. Needed because StopCoroutine
        /// must be called on the same behaviour that started it — nulling the handle
        /// alone leaves the routine running and re-inflating the player.
        private static MonoBehaviour _routineHost;

        /// Seconds of giant time removed by knockouts since the last frame, reported by
        /// SuperJumboBurgerTimePatch with the exact value the base game used. Consumed
        /// and cleared by the hold loop.
        private static float _pendingTimeReduction;

        /// Called by SuperJumboBurgerTimePatch when the base game shortens the form.
        /// Accumulates rather than overwrites: several reductions can land between two
        /// frames of our own loop.
        public static void NotifyTimeReduction(float reduction)
        {
            if (reduction > 0f)
                _pendingTimeReduction += reduction;
        }

        /// The base game's giant-form scale, or 0 if ItemSettings is not available yet.
        /// All three scale-derived helpers below compare against it, and each returns
        /// "no effect" when it is 0, so a missing settings singleton disables them
        /// rather than throwing.
        private static float VanillaGiantScale =>
            GameManager.ItemSettings != null
                ? GameManager.ItemSettings.JumboBurgerGiantFormScale
                : 0f;

        /// <summary>
        /// Extra orbit-camera distance needed beyond what the base game already adds
        /// for its own giant form.
        ///
        /// The base game's JumboBurgerCameraDistanceAddition is a constant sized for the
        /// vanilla giant scale, so at larger scales the camera sits far too close. We
        /// add distance in proportion to how much bigger than vanilla we are: at the
        /// vanilla scale this returns 0 (the base addition is already correct), and it
        /// grows linearly from there.
        ///
        /// Returns 0 when no super form is active, so the patch is inert otherwise.
        /// </summary>
        /// <param name="characterScale">
        /// Scale of the player the camera is looking at. Takes the scale as a parameter
        /// rather than reading the local player so it matches GetCameraHeightAddition,
        /// which resolves from the camera's subject: while spectating a super giant the
        /// height and the distance must come from the same player, or the camera ends up
        /// raised but still too close.
        /// </param>
        public static float CameraDistanceAddition(float characterScale)
        {
            float vanillaScale = VanillaGiantScale;
            if (vanillaScale <= 0f || ModConfig.SuperJumboBurger == null)
                return 0f;

            // Derived from the ACTUAL current scale rather than the configured target,
            // so the surplus eases in and out with the grow/shrink animation and reaches
            // exactly 0 at normal size. Keying it to the session flag instead would snap
            // the camera the moment the flag cleared, while the base game's own easing
            // was still running.
            if (characterScale <= vanillaScale)
                return 0f;

            return (characterScale - vanillaScale)
                * Mathf.Max(0f, ModConfig.SuperJumboBurger.CameraDistancePerScale.Value);
        }

        /// <summary>
        /// Movement-speed multiplier for a giant at the given scale, on top of the base
        /// game's own JumboBurgerGiantFormSpeedBoostFactor.
        ///
        /// Returns 1 (no change) at or below the vanilla giant scale, so a vanilla Jumbo
        /// Burger is completely unaffected and the speed patch is inert for it.
        ///
        /// Interpolates between "no bonus" and "speed proportional to size" by the
        /// configured SpeedScaling. At 1 a 10x giant moves 10x as fast relative to a 3x
        /// one; at 0 it moves at the vanilla giant speed.
        ///
        /// Takes scale as a parameter rather than reading the local player, because the
        /// speed patch runs for every player's PlayerMovement — including remote giants,
        /// whose scale arrives via SyncVar. That keeps the multiplier identical on every
        /// client with no extra replication.
        /// </summary>
        public static float GetSpeedMultiplier(float characterScale)
        {
            float vanillaScale = VanillaGiantScale;
            if (vanillaScale <= 0f || characterScale <= vanillaScale)
                return 1f;

            if (ModConfig.SuperJumboBurger == null)
                return 1f;

            float scaling = Mathf.Clamp01(ModConfig.SuperJumboBurger.SpeedScaling.Value);

            // How much bigger than a vanilla giant we are. Full proportional scaling
            // would multiply speed by exactly this.
            float sizeRatio = characterScale / vanillaScale;

            return Mathf.Lerp(1f, sizeRatio, scaling);
        }

        /// <summary>
        /// Extra world-space height for the orbit camera's tracked point at the given
        /// scale. Returns 0 at or below the vanilla giant scale, so a vanilla Jumbo
        /// Burger is untouched.
        ///
        /// Takes scale as a parameter because the camera can track any player — a
        /// spectated giant needs the same treatment as the local one.
        /// </summary>
        public static float GetCameraHeightAddition(float characterScale)
        {
            float vanillaScale = VanillaGiantScale;
            if (vanillaScale <= 0f || characterScale <= vanillaScale)
                return 0f;

            if (ModConfig.SuperJumboBurger == null)
                return 0f;

            return (characterScale - vanillaScale)
                * Mathf.Max(0f, ModConfig.SuperJumboBurger.CameraHeightPerScale.Value);
        }

        /// <summary>
        /// Multiplier for the giant flick's power at the given scale.
        ///
        /// Returns 1 (unchanged) at or below the vanilla giant scale, so a vanilla Jumbo
        /// Burger's flick is untouched and the patch is inert for it.
        ///
        /// Unlike the speed and camera helpers this does NOT scale with size — the
        /// configured multiplier applies in full to any super form. Flick power is a
        /// balance knob rather than a consequence of being large, so tying it to scale
        /// would make it change whenever Scale was retuned.
        ///
        /// Takes scale as a parameter because the swing is resolved on whichever client
        /// owns the target, not the swinger, so the value has to come from the hitter's
        /// CharacterScale (a SyncVar) rather than from local session state.
        /// </summary>
        public static float GetFlickPowerMultiplier(float swingerScale)
        {
            float vanillaScale = VanillaGiantScale;
            if (vanillaScale <= 0f || swingerScale <= vanillaScale)
                return 1f;

            if (ModConfig.SuperJumboBurger == null)
                return 1f;

            return Mathf.Max(1f, ModConfig.SuperJumboBurger.FlickPowerMultiplier.Value);
        }

        /// Starts the giant form. Returns false when it could not be started, so the
        /// caller can leave the item in the player's inventory instead of consuming a
        /// use for nothing.
        public static bool Activate(PlayerInventory inventory)
        {
            var info = GameManager.LocalPlayerInfo;
            var movement = GameManager.LocalPlayerMovement;
            if (info == null || movement == null)
                return false;

            // Already giant — the base game would refuse anyway, and re-entering would
            // restart our routine against a form we did not start.
            if (info.IsInJumboBurgerGiantForm)
                return false;

            // A session is already running. This matters during the eat animation, when
            // the player is NOT yet giant and so the check above does not catch it:
            // without this, a second burger would restart the routine, orphaning the
            // first one's eat state and skipping its consume — a free use.
            if (LocalSessionActive)
                return false;

            // Stop any previous routine on whichever behaviour actually started it.
            StopRoutine();

            _routineHost = inventory;
            _routine = inventory.StartCoroutine(Routine(info, movement, inventory));
            return true;
        }

        private static IEnumerator Routine(
            PlayerInfo info,
            PlayerMovement movement,
            PlayerInventory inventory
        )
        {
            // Clamped defensively: AcceptableValueRange constrains the settings UI, but
            // a hand-edited config file can still contain anything.
            float scale = Mathf.Max(1f, ModConfig.SuperJumboBurger.Scale.Value);
            // Clamped to the config's own documented minimum, not 0: a zero duration
            // would skip the hold loop entirely, and that loop is where the
            // form-was-cancelled check lives.
            float duration = Mathf.Max(0.1f, ModConfig.SuperJumboBurger.Duration.Value);
            float growDuration = Mathf.Max(0f, ModConfig.SuperJumboBurger.GrowDuration.Value);

            LocalSessionActive = true;

            // Play the base game's burger eat animation before the effect lands.
            //
            // The animator is already set up for this: PlayerAnimatorSetEquippedItemPatch
            // substitutes our AnimatorItemType (JumboBurger) into the animator's
            // equipped-item integer, and InheritAnimatorOverrideController gives us the
            // burger's override controller. The only missing input was the item-use
            // integer, which the base game sets from PlayerInventory.CurrentItemUse and
            // which never fires for custom items — so we drive it directly, the same way
            // the Flamethrower and AK-47 do.
            //
            // Waiting JumboBurgerEffectStartTime before growing matches the base game's
            // own EatJumboBurgerRoutine, which applies the giant effect partway through
            // the animation rather than at the start, so the player visibly takes a bite
            // before growing.
            ItemHelper.SetCurrentItemUse(inventory, ItemUseType.Regular);
            _animatingInventory = inventory;

            var settings = GameManager.ItemSettings;
            float eatDelay = settings != null ? settings.JumboBurgerEffectStartTime : 0f;
            float eatDuration = settings != null ? settings.JumboBurgerEatDuration : 0f;
            if (eatDelay > 0f)
                yield return new WaitForSeconds(eatDelay);

            // Bail WITHOUT consuming if the player can no longer become giant.
            //
            // The eat animation takes real time, and a lot can happen during it. The
            // null checks cover the object being destroyed (disconnect, hole change);
            // the state checks cover the player being interrupted. Both matter because
            // the consume is next: activation would be refused a few lines later anyway,
            // and charging a use for an effect that never lands is the one outcome worth
            // avoiding.
            if (info == null || movement == null || inventory == null)
            {
                EndSession();
                yield break;
            }

            if (
                movement.IsKnockedOutOrRecovering
                || movement.IsRespawningOrDrowning
                || (info.AsHittable != null && info.AsHittable.FrozenState == FrozenState.Frozen)
                || info.IsInJumboBurgerGiantForm
            )
            {
                IssaPluginPlugin.Log.LogInfo(
                    "[SuperJumboBurger] Interrupted during the eat animation — "
                        + "item not consumed."
                );
                EndSession();
                yield break;
            }

            // Consume the burger mid-animation rather than in OnUse, matching the base
            // game's EatJumboBurgerRoutine: it removes the item partway through the eat,
            // not the moment the item is used. Consuming in OnUse would also clear the
            // animator's item-use state and cancel the animation before it played.
            ItemHelper.ConsumeEquippedItem(inventory);

            // ConsumeEquippedItem ends with SetCurrentItemUse(None) — and RemoveItemAt
            // additionally trips RemoveItemAtForcedDeselectPatch, which clears it again.
            // Re-assert so the animation runs for the rest of the eat.
            //
            // _animatingInventory is still set, so every cleanup path continues to clear
            // this even though we have re-entered the "using an item" state after the
            // item itself is gone. That matters: CurrentItemUse > None makes
            // IsUsingItemAtAll true, which blocks item switching — leaking it would soft
            // lock the player's inventory, not merely look wrong.
            ItemHelper.SetCurrentItemUse(inventory, ItemUseType.Regular);

            // Hand off to the base game: this sets isInJumboBurgerGiantForm, starts its
            // timer, plays the grow VFX/audio and starts its own scale animation toward
            // the vanilla JumboBurgerGiantFormScale.
            info.LocalPlayerActivateJumboBurgerGiantForm(
                new ItemUseId(
                    info.PlayerId.Guid,
                    Time.frameCount,
                    ItemRegistry.SuperJumboBurgerItemType,
                    false
                )
            );

            // Activation can be REFUSED. LocalPlayerActivateJumboBurgerGiantForm runs an
            // internal CanActivate check (match resolved, spectating, knocked out,
            // frozen, mid-swing, already giant...) and silently returns without setting
            // isInJumboBurgerGiantForm if it fails.
            //
            // We must not grow in that case. The collider rescale is gated on that bool
            // (PlayerMovement.GetLocalBounds scales the capsule only when it is set), so
            // growing anyway produces a giant visual wrapped around a normal-sized
            // collider. That interpenetrates the world deeply enough to blow up contact
            // resolution on the physics worker thread, which corrupts PhysicsManager's
            // collision-ignore sets and sends objects to garbage coordinates.
            if (!info.IsInJumboBurgerGiantForm)
            {
                IssaPluginPlugin.Log.LogInfo(
                    "[SuperJumboBurger] Giant form was refused by the base game "
                        + "(CanActivate returned false) — not growing. The item was "
                        + "already consumed mid-eat, matching how the base game's own "
                        + "burger behaves if the player is interrupted."
                );
                EndSession();
                yield break;
            }

            // Broadcast the eat/grow VFX only once the form is confirmed: in step with
            // the player actually growing rather than a beat early, and never at all if
            // the eat was interrupted or activation refused.
            inventory.GetComponent<SuperJumboBurgerNetworkBridge>()?.ClientRequestEffects();

            // Hold the eating pose for the remainder of the animation. The base game's
            // own routine keeps ItemUseType.Regular set for the full JumboBurgerEatDuration
            // and only clears it at the end; clearing at the effect-start time instead
            // would cut the animation off partway through.
            float remainingEat = eatDuration - eatDelay;
            if (remainingEat > 0f)
                yield return new WaitForSeconds(remainingEat);

            ClearEatAnimation();

            // Activation started the base game's own grow coroutine, which writes
            // NetworkcharacterScale toward the vanilla scale every frame. Two writers on
            // one SyncVar would race, with the winner depending on coroutine ordering.
            // ApplyJumboBurgerGiantModeScaleInstantly stops that coroutine as its first
            // action, so calling it leaves us as the sole writer. It also snaps the scale
            // to the vanilla value, which we immediately animate away from — invisible
            // within one frame, and the correct starting point for the grow.
            movement.ApplyJumboBurgerGiantModeScaleInstantly(false);

            yield return Grow(movement, 1f, scale, growDuration, info);

            // Grow aborts early if the form was cancelled mid-animation, and resets the
            // scale itself. Checked explicitly here rather than relying on the hold
            // loop's own check, which would be skipped entirely for a zero duration.
            if (!info.IsInJumboBurgerGiantForm)
            {
                EndSession();
                yield break;
            }

            // Hold at full size for the configured duration.
            //
            // The base game runs its own countdown (jumboBurgerGiantFormRemainingTime)
            // for the vanilla duration and cancels the form when it reaches zero. Left
            // alone that would cap our configurable Duration at the vanilla one, so we
            // drive that timer ourselves rather than replacing it: it is a SyncVar
            // feeding the on-screen giant-form timer UI, so keeping it live means that
            // UI stays correct on every client.
            //
            // Knockout time reductions must survive that. LocalPlayerReduceJumboBurgerGiantFormTime
            // subtracts from the same field, so an unconditional overwrite each frame
            // would silently erase them. Instead we compare against what we last wrote
            // and charge any extra drop against our own remaining time — so a knockout
            // shortens a super form by the same number of seconds as a vanilla one.
            float held = 0f;
            _pendingTimeReduction = 0f;

            while (held < duration)
            {
                // The player object can be destroyed mid-hold (disconnect, returning to
                // the menu). This loop runs for up to the configured Duration — far
                // longer than the grow animation — so it is the most exposed to that.
                // Unity's overloaded == reports a destroyed object as null.
                if (info == null || movement == null)
                {
                    EndSession();
                    yield break;
                }

                if (!info.IsInJumboBurgerGiantForm)
                {
                    // Base game ended the form (timer hit zero during a swing, a
                    // knockout ended it, or something else cancelled it). It is already
                    // animating the scale back to 1 — leave it alone.
                    EndSession();
                    yield break;
                }

                // Safety net for anything that changes scale without clearing the giant
                // bool. Cancellations that DO clear it (respawn, knockout, the base
                // timer) are caught by the check above and exit before reaching here,
                // so this cannot fight them.
                if (!Mathf.Approximately(movement.CharacterScale, scale))
                    movement.NetworkcharacterScale = scale;

                // Knockout reductions arrive as exact values from
                // SuperJumboBurgerTimePatch, so they are simply consumed here.
                if (_pendingTimeReduction > 0f)
                {
                    held += _pendingTimeReduction;
                    _pendingTimeReduction = 0f;
                }

                held += Time.deltaTime;

                float remaining = duration - held;
                if (remaining <= 0f)
                    break;

                // Drive the base game's timer SyncVar so the overhead progress bar shows
                // OUR remaining time. Its fill is remaining / JumboBurgerGiantFormDuration,
                // so the value is expressed as a fraction of our own duration scaled into
                // that range — a full bar at the start, empty at the end, whatever our
                // configured duration is.
                //
                // The bar's existence is gated on isInJumboBurgerGiantForm rather than on
                // this value, so reusing it costs nothing and keeps it visible to every
                // client, correctly positioned above a giant player.
                //
                // Floored just above zero: the base game cancels the form whenever this
                // value is <= 0 and a swing ends (PlayerInfo.InformIsSwingingChanged),
                // so letting it reach exactly 0 on our final frames would let a
                // mistimed swing end the form a moment early. We end it ourselves by
                // leaving this loop instead.
                float shown =
                    remaining / duration * GameManager.ItemSettings.JumboBurgerGiantFormDuration;
                info.NetworkjumboBurgerGiantFormRemainingTime = Mathf.Max(0.01f, shown);

                yield return null;
            }

            // Shrink back.
            //
            // We drive this ourselves rather than handing off to the base game's shrink,
            // because its animation starts from the vanilla giant scale and would snap a
            // 10x player down to 3x on the first frame.
            //
            // The giant bool is deliberately left SET for the duration of the shrink, so
            // the colliders stay giant while the visual comes down. That direction is
            // safe: a visual smaller than its collider cannot interpenetrate the world.
            // The reverse (clearing the bool first) would shrink the colliders while the
            // player was still visually huge.
            // Re-checked: the hold loop can exit on its own timer on the same frame the
            // player object is destroyed.
            if (movement == null)
            {
                EndSession();
                yield break;
            }

            yield return Grow(movement, movement.CharacterScale, 1f, growDuration);

            EndSession();

            // Clear the form last. This also re-applies scale 1 via
            // ApplyJumboBurgerGiantModeScaleInstantly, which is harmless — we are
            // already there — and leaves the colliders and the visual normalised
            // together on the same frame.
            if (info != null && info.isLocalPlayer && info.IsInJumboBurgerGiantForm)
                info.LocalPlayerCancelJumboBurgerGiantForm(true);
        }

        /// Clears session state from INSIDE the routine, as it finishes or bails.
        ///
        /// Deliberately does not call StopCoroutine — the routine is ending under its
        /// own control, and stopping a coroutine from within itself is both unnecessary
        /// and, if the handles have already been reused, wrong.
        /// Returns the player to the normal upper-body pose. Safe to call repeatedly and
        /// when no animation was started.
        private static void ClearEatAnimation()
        {
            if (_animatingInventory == null)
                return;

            ItemHelper.SetCurrentItemUse(_animatingInventory, ItemUseType.None);
            _animatingInventory = null;
        }

        private static void EndSession()
        {
            // Belt and braces: the normal path clears this as soon as the eat animation
            // finishes, but an early bail (refused activation, cancellation mid-eat)
            // would otherwise leave the player stuck mid-bite.
            ClearEatAnimation();
            LocalSessionActive = false;
            _pendingTimeReduction = 0f;
            _routine = null;
            _routineHost = null;
        }

        /// Stops the active routine on the behaviour that started it, tolerating a
        /// host that has already been destroyed (disconnect, scene teardown).
        private static void StopRoutine()
        {
            if (_routine != null && _routineHost != null)
                _routineHost.StopCoroutine(_routine);

            _routine = null;
            _routineHost = null;

            // A stopped coroutine runs no finally block, so anything it was mid-way
            // through has to be undone here. Without this a routine killed during the
            // eat would leave the player stuck in the eating pose.
            ClearEatAnimation();
        }

        /// Animates scale from → to.
        ///
        /// <paramref name="requireGiantForm"/>, when supplied, is polled every frame and
        /// the animation aborts (resetting to 1) the moment the giant bool clears. That
        /// bool gates the base game's collider rescale, so continuing to grow without it
        /// would leave a giant visual around a normal-sized collider — the state that
        /// corrupts contact resolution. Pass null when shrinking, where the bool being
        /// clear is expected and correct.
        private static IEnumerator Grow(
            PlayerMovement movement,
            float from,
            float to,
            float duration,
            PlayerInfo requireGiantForm = null
        )
        {
            if (movement == null)
                yield break;

            // A hand-edited config can hold a value outside the AcceptableValueRange,
            // so guard the division rather than trusting the bound.
            if (duration <= 0f)
            {
                movement.NetworkcharacterScale = to;
                yield break;
            }

            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                // The player can be cancelled mid-animation (respawn, knockout, hole
                // change). Writing scale after that would re-inflate a player the base
                // game just reset, so stop driving it.
                if (movement == null)
                    yield break;

                // Giant form ended mid-grow: the colliders have already been rescaled
                // back to normal, so keeping the visual large would desync the two.
                if (requireGiantForm != null && !requireGiantForm.IsInJumboBurgerGiantForm)
                {
                    movement.NetworkcharacterScale = 1f;
                    yield break;
                }

                movement.NetworkcharacterScale = Mathf.Lerp(from, to, t / duration);
                yield return null;
            }

            if (movement != null)
                movement.NetworkcharacterScale = to;
        }

        /// Forces the local player back to normal size and clears session state.
        ///
        /// Used for hole cleanup, disconnects and match end. Player objects are
        /// DontDestroyOnLoad, so a routine started on one hole survives into the next
        /// unless it is stopped explicitly — nulling the handle alone would leave it
        /// running and re-inflating the player after cleanup.
        public static void ForceReset()
        {
            StopRoutine(); // also clears the eat animation
            LocalSessionActive = false;
            _pendingTimeReduction = 0f;

            var info = GameManager.LocalPlayerInfo;
            var movement = GameManager.LocalPlayerMovement;

            // Guards check isLocalPlayer, not just null. ForceReset runs during teardown
            // (disconnect, match end), where the objects can still exist while ownership
            // has already been dropped. Writing a ClientToServer SyncVar or calling
            // LocalPlayerCancelJumboBurgerGiantForm in that state is invalid — the
            // latter logs a Unity error on every call.
            //
            // ORDER MATTERS. Cancelling first is what keeps the visual and the colliders
            // in step: LocalPlayerCancelJumboBurgerGiantForm clears the giant bool and
            // only then resets the scale, in that order, inside one call. Writing scale
            // to 1 ourselves BEFORE cancelling would leave a normal-sized visual with
            // still-giant colliders for the intervening moment — the same visual/collider
            // mismatch that corrupts contact resolution, just inverted.
            if (info != null && info.isLocalPlayer && info.IsInJumboBurgerGiantForm)
            {
                // Also resets characterScale to 1 for us.
                info.LocalPlayerCancelJumboBurgerGiantForm(true);
                return;
            }

            // Not in giant form (or no PlayerInfo): nothing has scaled the colliders, so
            // it is safe — and still necessary — to normalise a scale our own grow may
            // have left behind.
            if (movement != null && movement.isLocalPlayer)
                movement.NetworkcharacterScale = 1f;
        }
    }
}
