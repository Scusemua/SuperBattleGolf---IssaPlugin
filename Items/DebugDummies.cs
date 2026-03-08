using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public static class DebugDummies
    {
        internal static readonly List<GameObject> DebugDummiesList = new List<GameObject>();

        public static void ToggleDebugDummies()
        {
            if (DebugDummiesList.Count > 0)
            {
                foreach (var dummy in DebugDummiesList)
                {
                    if (dummy != null)
                        Object.Destroy(dummy);
                }
                DebugDummiesList.Clear();
                IssaPluginPlugin.Log.LogInfo("[Missile] Debug dummies removed.");
                return;
            }

            var playerPos = GameManager.LocalPlayerInfo?.transform.position ?? Vector3.zero;

            Vector3[] offsets =
            {
                new Vector3(10f, 0f, 10f),
                new Vector3(-12f, 0f, 5f),
                new Vector3(5f, 0f, -15f),
                new Vector3(-8f, 0f, -8f),
                new Vector3(18f, 0f, 0f),
            };

            foreach (var offset in offsets)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = "MissileDummy";
                go.transform.position = playerPos + offset;
                go.transform.localScale = new Vector3(0.6f, 1f, 0.6f);

                var col = go.GetComponent<Collider>();
                if (col != null)
                    Object.Destroy(col);

                var renderer = go.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.material.color = new Color(1f, 0.3f, 0.3f, 0.7f);

                DebugDummiesList.Add(go);
            }

            IssaPluginPlugin.Log.LogInfo(
                $"[Missile] Spawned {offsets.Length} debug dummies near {playerPos}."
            );
        }
    }
}
