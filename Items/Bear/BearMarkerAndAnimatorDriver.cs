using UnityEngine;

namespace IssaPlugin.Items
{
    // ────────────────────────────────────────────────────────────────────────────
    //  BearMarker
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Lightweight tag component added to every bear GameObject on all clients.
    /// Used by Harmony patches to identify bears without depending on
    /// BearBehaviour (which only exists server-side).
    /// </summary>
    public class BearMarker : MonoBehaviour { }

    // ────────────────────────────────────────────────────────────────────────────
    //  BearAnimatorDriver
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Client-side MonoBehaviour that translates <see cref="BearAIState"/> values
    /// received from the server (via <see cref="BearStateMessage"/>) into the exact
    /// Animator parameters defined in BearAnimator.controller.
    ///
    /// ═══════════════════════════════════════════════════════════════════════════
    ///  VERIFIED PARAMETER MAP  (parsed from BearAnimator.controller YAML)
    /// ═══════════════════════════════════════════════════════════════════════════
    ///
    ///  TRIGGERS  (m_Type: 9) — fire once, auto-reset:
    ///    "Attack1"       → Attack1 state  (exits to Combat Idle on completion)
    ///    "Attack2"       → Attack2 state  (exits to Combat Idle on completion)
    ///    "Attack3"       → Attack3 state  (exits to Combat Idle on completion)
    ///    "Attack5"       → Attack5 state  (exits to Combat Idle on completion)
    ///    "Buff"          → Buff state     (exits to Combat Idle on completion)
    ///    "Get Hit Front" → Hit Front state (exits to Combat Idle on completion)
    ///    "Jump"          → Jump state     (exits to Combat Idle on completion)
    ///    NOTE: Attack4/6/7/8 triggers exist in the parameter list but have no
    ///    corresponding states — do not use them.
    ///
    ///  BOOLS  (m_Type: 4) — must be set true and cleared manually:
    ///    "Idle"          → Idle state     (standard idle loop)
    ///    "Combat Idle"   → Combat Idle    (default state; combat-ready idle loop)
    ///    "Run Forward"   → RunForward state
    ///    "Run Backward"  → RunBack state
    ///    "WalkForward"   → Walk Forward state
    ///    "WalkBackward"  → Walk Backward state
    ///    "Stunned Loop"  → Stunned Loop state (looping stun; set AFTER Get Hit Front plays)
    ///    "Death"         → Death state    (looping; no exit transition)
    ///
    ///  STATES NOT USED BY THE AI (kept for completeness):
    ///    Eat, Sit, Sleep — driven by "Eat", "Sit", "Sleep" bools; no exit transitions.
    ///    Run Left/Right, Strafe, Circling — directional strafing; not needed.
    ///
    /// ═══════════════════════════════════════════════════════════════════════════
    ///  STATE MACHINE DESIGN NOTES
    /// ═══════════════════════════════════════════════════════════════════════════
    ///
    ///  Default state: "Combat Idle" (the default when no parameters are set).
    ///  All transitions are AnyState → destination, so there is no transition
    ///  graph to navigate — we simply fire the correct trigger or set the correct
    ///  bool and the Animator jumps there immediately.
    ///
    ///  Stunned sequence:
    ///    1. Fire "Get Hit Front" trigger → plays Hit Front animation once.
    ///    2. Hit Front's exit transition automatically returns to Combat Idle.
    ///    3. Then set "Stunned Loop" bool true → transitions to Stunned Loop.
    ///    4. When stun ends, clear "Stunned Loop" bool → falls back to Combat Idle.
    ///  We implement this with a two-step approach: trigger Get Hit Front on
    ///  BearAIState.Stunned entry, then set Stunned Loop bool.  The controller's
    ///  own exit-time transition handles the sequencing automatically.
    ///
    ///  Death:
    ///    Set "Death" bool true → Death state plays and loops (no exit).
    ///    The server destroys the GameObject after the death animation duration,
    ///    so we never need to clear the bool.
    ///
    ///  Enraged:
    ///    No dedicated enraged state exists. We use "Run Forward" (same as
    ///    Pursuing) — the bear visually runs faster because the server drives
    ///    the actual movement speed; the animation is identical.
    ///
    ///  AttackCooldown:
    ///    No separate state needed. Once any Attack trigger fires, the controller
    ///    handles the full play + auto-return to Combat Idle via HasExitTime.
    ///    Setting "Combat Idle" bool true while the attack plays has no effect
    ///    (AnyState transitions have CanTransitionToSelf: 0 for triggers) so we
    ///    simply ensure "Combat Idle" is true for the cooldown.
    /// </summary>
    public class BearAnimatorDriver : MonoBehaviour
    {
        private Animator _animator;

        // ── Cached parameter hashes ───────────────────────────────────────────
        // Triggers
        private static readonly int TrigAttack1 = Animator.StringToHash("Attack1");
        private static readonly int TrigAttack2 = Animator.StringToHash("Attack2");
        private static readonly int TrigAttack3 = Animator.StringToHash("Attack3");
        private static readonly int TrigAttack5 = Animator.StringToHash("Attack5");
        private static readonly int TrigBuff = Animator.StringToHash("Buff");
        private static readonly int TrigGetHitFront = Animator.StringToHash("Get Hit Front");

        // Bools
        private static readonly int BoolIdle = Animator.StringToHash("Idle");
        private static readonly int BoolCombatIdle = Animator.StringToHash("Combat Idle");
        private static readonly int BoolRunForward = Animator.StringToHash("Run Forward");
        private static readonly int BoolWalkForward = Animator.StringToHash("WalkForward");
        private static readonly int BoolStunnedLoop = Animator.StringToHash("Stunned Loop");
        private static readonly int BoolDeath = Animator.StringToHash("Death");

        // ── Attack cycling ────────────────────────────────────────────────────
        // Cycles through the four available attack animations (Attack1/2/3/5)
        // so the bear doesn't repeat the same swing every time.
        private static readonly int[] AttackTriggers =
        {
            TrigAttack1,
            TrigAttack2,
            TrigAttack3,
            TrigAttack5,
        };
        private int _attackIndex;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Called by <see cref="BearNetworkBridge.HandleBearState"/> on every client
        /// when the server broadcasts a state change.
        /// </summary>
        public void ApplyState(BearAIState state, Vector3 targetPosition)
        {
            if (_animator == null)
                return;

            switch (state)
            {
                // ── Idle states ───────────────────────────────────────────────

                case BearAIState.Spawning:
                    // Clear all movement bools, play Buff animation once.
                    // Controller auto-returns to Combat Idle when Buff finishes.
                    ClearMovementBools();
                    _animator.SetTrigger(TrigBuff);
                    break;

                case BearAIState.Idle:
                    // Use the relaxed Idle loop (not combat-ready).
                    ClearMovementBools();
                    _animator.SetBool(BoolIdle, true);
                    break;

                case BearAIState.AttackCooldown:
                    // Attack animation is already playing and will auto-return to
                    // Combat Idle via its HasExitTime transition. Just ensure
                    // Combat Idle bool is true so it lands there correctly.
                    ClearMovementBools();
                    _animator.SetBool(BoolCombatIdle, true);
                    break;

                // ── Movement states ───────────────────────────────────────────

                case BearAIState.Wander:
                    ClearMovementBools();
                    _animator.SetBool(BoolWalkForward, true);
                    break;

                case BearAIState.Pursuing:
                case BearAIState.Enraged:
                    // Both use the RunForward state.
                    // Actual speed difference is driven by server physics, not animation.
                    ClearMovementBools();
                    _animator.SetBool(BoolRunForward, true);
                    break;

                case BearAIState.Charging:
                    // Also RunForward — committed sprint toward target.
                    ClearMovementBools();
                    _animator.SetBool(BoolRunForward, true);
                    break;

                // ── Attack ────────────────────────────────────────────────────

                case BearAIState.Attacking:
                    // Fire the next attack trigger in rotation.
                    // The controller's AnyState→AttackN transition fires immediately,
                    // plays the attack clip, then HasExitTime returns to Combat Idle.
                    ClearMovementBools();
                    _animator.SetBool(BoolCombatIdle, true); // landing state after attack
                    _animator.SetTrigger(AttackTriggers[_attackIndex]);
                    _attackIndex = (_attackIndex + 1) % AttackTriggers.Length;
                    break;

                // ── Hit / stun ────────────────────────────────────────────────

                case BearAIState.Stunned:
                    // Step 1: Fire Get Hit Front trigger — plays the hit reaction once,
                    //         then HasExitTime auto-returns to Combat Idle.
                    // Step 2: Immediately set Stunned Loop bool — this queues the
                    //         looping stun state. The AnyState→Stunned Loop transition
                    //         fires as soon as Hit Front's exit puts us back at Combat Idle.
                    //
                    // CrossFadeInFixedTime is also called to force-interrupt attack
                    // animations that use HasExitTime and would otherwise block the
                    // AnyState→GetHitFront trigger until the attack clip finishes.
                    ClearMovementBools();
                    foreach (var t in AttackTriggers)
                        _animator.ResetTrigger(t);
                    _animator.CrossFadeInFixedTime("Hit Front", 0.05f);
                    _animator.SetTrigger(TrigGetHitFront);
                    _animator.SetBool(BoolStunnedLoop, true);
                    break;

                // ── Death ─────────────────────────────────────────────────────

                case BearAIState.Dying:
                    // Death bool drives AnyState→Death transition. No exit transition
                    // exists — the Death state loops until the server destroys the object.
                    ClearMovementBools();
                    _animator.SetBool(BoolDeath, true);
                    break;

                case BearAIState.Dead:
                    // GameObject is about to be destroyed by the server.
                    // Nothing to do — Death animation is already playing.
                    break;
            }

            // ── Client-side facing ────────────────────────────────────────────
            // Rotate the visual toward the last known target position while the
            // bear is moving. The server drives the authoritative position via
            // NetworkTransform; this is purely cosmetic and smooths the facing
            // direction between NetworkTransform ticks.
            if (
                targetPosition != Vector3.zero
                && (
                    state == BearAIState.Pursuing
                    || state == BearAIState.Charging
                    || state == BearAIState.Enraged
                    || state == BearAIState.Attacking
                )
            )
            {
                Vector3 toTarget = targetPosition - transform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
            }

            // ── Clear stun loop when leaving Stunned ──────────────────────────
            // If we receive any state other than Stunned, make sure Stunned Loop
            // bool is cleared so the bear doesn't stay frozen.
            if (state != BearAIState.Stunned)
                _animator.SetBool(BoolStunnedLoop, false);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Clears all movement-related bool parameters so only the intended state
        /// is active. Triggers are self-clearing and do not need to be reset here.
        /// Death is intentionally excluded — once set it should persist until the
        /// GameObject is destroyed.
        /// </summary>
        private void ClearMovementBools()
        {
            _animator.SetBool(BoolIdle, false);
            _animator.SetBool(BoolCombatIdle, false);
            _animator.SetBool(BoolRunForward, false);
            _animator.SetBool(BoolWalkForward, false);
            // BoolStunnedLoop is cleared conditionally in ApplyState, not here,
            // because clearing it in the Stunned case itself would cancel the stun.
            // BoolDeath is never cleared — death is terminal.
        }
    }
}
