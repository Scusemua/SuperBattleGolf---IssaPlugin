using Mirror;
using UnityEngine;

namespace IssaPlugin.Network
{
    /// <summary>
    /// Forces server authority on a spawned prefab's NetworkTransform components.
    ///
    /// Mirror's <c>NetworkTransformBase.Reset()</c> defaults <c>syncDirection</c> to
    /// <see cref="SyncDirection.ClientToServer"/>, and prefabs authored in the mod's
    /// Unity project carry that default. On a listen-server host (server and client
    /// in one process) the local client then owns the transform, and its snapshot
    /// interpolation writes the transform every tick — silently overwriting any
    /// position the server-side AI sets in FixedUpdate.
    ///
    /// The symptom is distinctive: the object animates and rotates correctly but
    /// has little or no net translation, because rotation is usually written
    /// directly to <c>transform.rotation</c> while position loses the race.
    ///
    /// Call this on any server-spawned object whose position is driven by
    /// server-side code, BEFORE <c>NetworkServer.Spawn</c> so the first sync is
    /// already correct.
    /// </summary>
    internal static class ServerAuthoritativeTransform
    {
        /// <summary>
        /// Sets every NetworkTransform on <paramref name="go"/> to server authority.
        /// Logs a warning naming any component that had to be corrected, so a prefab
        /// shipping the wrong default is visible in the log rather than silently
        /// costing movement.
        /// </summary>
        /// <param name="go">Root of the object about to be spawned.</param>
        /// <param name="context">Short label used in the log (e.g. "Drone").</param>
        internal static void Apply(GameObject go, string context)
        {
            if (go == null)
                return;

            foreach (var nt in go.GetComponentsInChildren<NetworkTransformBase>(true))
            {
                if (nt.syncDirection == SyncDirection.ServerToClient)
                    continue;

                nt.syncDirection = SyncDirection.ServerToClient;

                IssaPluginPlugin.Log.LogWarning(
                    $"[{context}] {nt.GetType().Name} shipped with "
                        + "syncDirection=ClientToServer; forced to ServerToClient. "
                        + "Server-driven movement would otherwise be overwritten by "
                        + "the local client's interpolation."
                );
            }
        }
    }
}
