using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    /// Attached to the bomber prefab instance so it flies smoothly
    /// from spawn to destination independent of the rocket-drop coroutine.
    ///
    /// BomberNetworkBridge.RpcBomberShotDown disables it before attaching BomberCrashBehaviour.
    public class BomberFlyBehaviour : MonoBehaviour
    {
        public Vector3 destination;
        public float speed;

        private void Update()
        {
            if (!enabled)
                return;

            transform.position = Vector3.MoveTowards(
                transform.position,
                destination,
                speed * Time.deltaTime
            );

            if (Vector3.Distance(transform.position, destination) < 0.5f)
            {
                StealthBomberItem.ActiveBomberVisual = null;
                Destroy(gameObject);
            }
        }
    }
}
