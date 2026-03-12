using System.Collections.Generic;
using IssaPlugin.Items;
using Unity.Collections;
using UnityEngine;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Applies ice physics to ground/terrain colliders during a Freeze World session.
    ///
    /// The ice PhysicsMaterial uses frictionCombine = Minimum, so even if the
    /// ball, cart, or ragdoll collider has high friction, Unity picks the lower
    /// value and the object slips. Walking (CharacterController) is handled
    /// separately by FreezeMovementPatch.
    ///
    /// ContactModifyEvent is kept as a belt-and-suspenders fallback for any
    /// ball-terrain contacts already flagged as modifiable by PhysicsManager.
    ///
    /// Added to the Plugin's persistent gameObject in Plugin.cs.
    /// </summary>
    public class FreezePhysicsHandler : MonoBehaviour
    {
        private PhysicsMaterial _iceMat;
        private readonly Dictionary<Collider, PhysicsMaterial> _originalMaterials = new();
        private readonly Dictionary<Collider, PhysicsMaterial> _originalBallMaterials = new();
        private readonly Dictionary<WheelCollider, (WheelFrictionCurve fwd, WheelFrictionCurve side)> _savedWheelFriction = new();
        private bool _freezeApplied;

        private static readonly int TerrainLayer = LayerMask.NameToLayer("Terrain");
        private static readonly int DefaultLayer  = LayerMask.NameToLayer("Default");

        private void Awake()
        {
            _iceMat = new PhysicsMaterial("IceMat")
            {
                staticFriction  = 0.02f,
                dynamicFriction = 0.02f,
                bounciness      = 0.15f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine   = PhysicsMaterialCombine.Maximum,
            };
        }

        private void OnDestroy()
        {
            if (_freezeApplied)
                RestoreOriginalMaterials();
            if (_iceMat != null)
                Destroy(_iceMat);
        }

        private void OnEnable()  => Physics.ContactModifyEvent += OnContactModify;
        private void OnDisable() => Physics.ContactModifyEvent -= OnContactModify;

        private void Update()
        {
            if (FreezeItem.IsFrozen && !_freezeApplied)
                ApplyIceMaterials();
            else if (!FreezeItem.IsFrozen && _freezeApplied)
                RestoreOriginalMaterials();
        }

        private bool IsGroundCollider(Collider col)
        {
            if (col.isTrigger) return false;
            if (col is TerrainCollider) return true;
            int layer = col.gameObject.layer;
            return layer == TerrainLayer || layer == DefaultLayer;
        }

        private void ApplyIceMaterials()
        {
            _freezeApplied = true;
            _originalMaterials.Clear();

            _iceMat.staticFriction  = Configuration.FreezeFriction.Value;
            _iceMat.dynamicFriction = Configuration.FreezeFriction.Value;
            _iceMat.bounciness      = Configuration.FreezeBounciness.Value;

            foreach (var col in FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (!IsGroundCollider(col)) continue;
                _originalMaterials[col] = col.sharedMaterial;
                col.sharedMaterial = _iceMat;
            }

            ApplyBallIce();
            ApplyCartIce();

            IssaPluginPlugin.Log.LogInfo(
                $"[Freeze] Ice material applied to {_originalMaterials.Count} ground collider(s), "
                    + $"{_originalBallMaterials.Count} ball collider(s), "
                    + $"{_savedWheelFriction.Count} wheel collider(s)."
            );
        }

        private void RestoreOriginalMaterials()
        {
            _freezeApplied = false;
            foreach (var (col, mat) in _originalMaterials)
            {
                if (col != null)
                    col.sharedMaterial = mat;
            }
            _originalMaterials.Clear();

            RestoreBallMaterials();
            RestoreWheelFriction();

            IssaPluginPlugin.Log.LogInfo("[Freeze] Ground, ball, and cart physics restored.");
        }

        private void ApplyBallIce()
        {
            _originalBallMaterials.Clear();
            foreach (var ball in FindObjectsByType<GolfBall>(FindObjectsSortMode.None))
            {
                var col = ball.Collider;
                if (col == null) continue;
                _originalBallMaterials[col] = col.sharedMaterial;
                col.sharedMaterial = _iceMat;
            }
        }

        private void RestoreBallMaterials()
        {
            foreach (var (col, mat) in _originalBallMaterials)
                if (col != null) col.sharedMaterial = mat;
            _originalBallMaterials.Clear();
        }

        private void ApplyCartIce()
        {
            _savedWheelFriction.Clear();
            float sideStiffness = Configuration.FreezeCartSidewaysStiffness.Value;

            foreach (var wc in FindObjectsByType<WheelCollider>(FindObjectsSortMode.None))
            {
                _savedWheelFriction[wc] = (wc.forwardFriction, wc.sidewaysFriction);

                var side = wc.sidewaysFriction;
                side.stiffness = sideStiffness;
                wc.sidewaysFriction = side;
            }
        }

        private void RestoreWheelFriction()
        {
            foreach (var (wc, (fwd, side)) in _savedWheelFriction)
            {
                if (wc == null) continue;
                wc.forwardFriction = fwd;
                wc.sidewaysFriction = side;
            }
            _savedWheelFriction.Clear();
        }

        private void OnContactModify(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs)
        {
            if (!FreezeItem.IsFrozen) return;

            float friction = Configuration.FreezeFriction.Value;
            float bounce   = Configuration.FreezeBounciness.Value;

            for (int i = 0; i < pairs.Length; i++)
            {
                var pair = pairs[i];
                for (int j = 0; j < pair.contactCount; j++)
                {
                    pair.SetStaticFriction(j, friction);
                    pair.SetDynamicFriction(j, friction);
                    pair.SetBounciness(j, bounce);
                }
                pairs[i] = pair;
            }
        }
    }
}
