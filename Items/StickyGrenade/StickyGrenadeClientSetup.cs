using System.Collections;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Added to the StickyGrenade grenade prefab in AssetLoader so every instance —
    /// server and client — automatically gets client-side VFX behaviour.
    ///
    /// Responsibilities:
    ///   1. Drive the blink animation (particle system rate + point light intensity)
    ///      at increasing frequency as the fuse burns down.  This runs purely
    ///      from local Time.time — no network messages needed.
    ///   2. When a StickyGrenadeStuckMessage arrives, start tracking the stuck target's
    ///      transform so the grenade visual smoothly follows moving targets on
    ///      every client independently.
    ///
    /// The server drives ground-truth position via NetworkTransform.  This component
    /// only overrides position locally for stuck-to-moving-target smoothness;
    /// for static stuck grenades and in-flight grenades the NetworkTransform value
    /// is used as-is.
    /// </summary>
    public class StickyGrenadeClientSetup : MonoBehaviour
    {
        // ----------------------------------------------------------------
        //  Inspector-wired references (set by StickyGrenadeClientSetup.Awake via
        //  GetComponent searches — no Unity Editor required)
        // ----------------------------------------------------------------

        private Light _blinkLight;
        private ParticleSystem _blinkParticles;

        // ----------------------------------------------------------------
        //  State set by StickyGrenadeNetworkBridge.HandleStuck
        // ----------------------------------------------------------------

        /// Set to the world-space stuck position or the tracked target.
        /// Null while the grenade is still in flight.
        public Transform StickyTarget { get; set; }
        public Vector3 LocalOffset { get; set; } // in StickyTarget's local space
        public bool IsStuck { get; private set; }
        public float FuseRemaining { get; set; }
        private float _stuckTime;

        // ----------------------------------------------------------------
        //  Unity lifecycle
        // ----------------------------------------------------------------

        private void Awake()
        {
            _blinkLight = GetComponentInChildren<Light>(includeInactive: true);
            _blinkParticles = GetComponentInChildren<ParticleSystem>(includeInactive: true);
        }

        private void Update()
        {
            if (!IsStuck)
                return;

            // ── Track moving target client-side ──────────────────────────
            if (StickyTarget != null)
                transform.position = StickyTarget.TransformPoint(LocalOffset);

            // ── Blink animation ───────────────────────────────────────────
            float fuseElapsed = Time.time - _stuckTime;
            float fuseProgress = Mathf.Clamp01(fuseElapsed / Mathf.Max(FuseRemaining, 0.01f));

            // Blink frequency ramps from 1 Hz at stick time to 8 Hz at detonation
            float blinkHz = Mathf.Lerp(1f, 8f, fuseProgress);
            float blinkPeriod = 1f / blinkHz;
            float blinkPhase = (Time.time % blinkPeriod) / blinkPeriod;
            bool blinkOn = blinkPhase < 0.5f;

            if (_blinkLight != null)
            {
                _blinkLight.enabled = blinkOn;
                _blinkLight.intensity = Mathf.Lerp(0.5f, 3f, fuseProgress);
                _blinkLight.color = Color.Lerp(new Color(1f, 0.4f, 0f), Color.red, fuseProgress);
            }

            if (_blinkParticles != null)
            {
                var emission = _blinkParticles.emission;
                emission.rateOverTime = blinkOn ? Mathf.Lerp(5f, 30f, fuseProgress) : 0f;
            }
        }

        // ----------------------------------------------------------------
        //  Called by StickyGrenadeNetworkBridge.HandleStuck
        // ----------------------------------------------------------------

        public void OnStuck(Transform target, Vector3 localOffset, float fuseRemaining)
        {
            IsStuck = true;
            StickyTarget = target;
            LocalOffset = localOffset;
            FuseRemaining = fuseRemaining;
            _stuckTime = Time.time;

            // Enable the blink light/particles if they exist
            if (_blinkLight != null)
                _blinkLight.enabled = true;
            if (_blinkParticles != null)
                _blinkParticles.Play();
        }
    }
}
