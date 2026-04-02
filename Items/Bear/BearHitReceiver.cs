using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached server-side to each bear alongside BearBehaviour.
    /// Tracks HP and kills the bear when it reaches zero.
    ///
    /// Inherits CustomHittable so the existing RocketPatches overlap-sphere
    /// check naturally discovers this component. However, callers should prefer
    /// DealDamage(float) over OnHit so that weapon-specific damage values are
    /// applied correctly.
    ///
    /// Sends BearHPUpdateMessage to all clients whenever HP changes so that
    /// BearHealthBarOverlay can display the current health above each bear.
    /// </summary>
    public class BearHitReceiver : CustomHittable
    {
        /// Back-reference to the driving behaviour component.
        public BearBehaviour Behaviour { get; set; }

        public float CurrentHP { get; private set; }
        public float MaxHP { get; private set; }

        private NetworkIdentity _ni;

        private void Awake()
        {
            MaxHP = ModConfig.Bear.MaxHP.Value;
            CurrentHP = MaxHP;
        }

        private void Start()
        {
            _ni = GetComponent<NetworkIdentity>();
        }

        /// <summary>
        /// Applies <paramref name="amount"/> HP damage to this bear on the server.
        /// Kills the bear if HP reaches zero; otherwise triggers a stun/aggro
        /// transition and broadcasts the new HP to all clients.
        /// </summary>
        public void DealDamage(float amount)
        {
            if (!NetworkServer.active)
                return;
            if (CurrentHP <= 0f)
                return;
            if (MaxHP <= 0f)
                return;

            CurrentHP -= amount;

            IssaPluginPlugin.Log.LogInfo(
                $"[Bear] Hit for {amount} damage. HP: {CurrentHP}/{MaxHP}."
            );

            // Broadcast new HP to all clients for the health bar.
            if (_ni != null)
            {
                NetworkServer.SendToAll(
                    new BearHPUpdateMessage
                    {
                        BearNetId = _ni.netId,
                        CurrentHP = Mathf.Max(0f, CurrentHP),
                        MaxHP = MaxHP,
                    }
                );
            }

            if (CurrentHP <= 0f)
            {
                IssaPluginPlugin.Log.LogInfo("[Bear] Bear killed.");
                Behaviour?.OnKilled();
                return;
            }

            // Stun + aggro update for non-lethal hits.
            PlayerInfo attacker = BearExplosionAttackerContext.CurrentAttacker;
            Behaviour?.OnHitByExplosion(attacker);
        }
    }

    /// <summary>
    /// Thread-local-ish context set by weapon patches immediately before invoking
    /// BearHitReceiver.DealDamage so the hit receiver can identify the attacker
    /// without storing state on the weapon itself.
    ///
    /// Unity is single-threaded so a simple static field is safe here.
    /// </summary>
    public static class BearExplosionAttackerContext
    {
        public static PlayerInfo CurrentAttacker { get; set; }
    }
}
