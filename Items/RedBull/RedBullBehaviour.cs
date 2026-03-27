using System.Collections;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Tracks whether the local player is currently under the Red Bull effect.
    /// Activated by RedBullItemDefinition.OnUse; read by RedBullPatches.
    /// </summary>
    public static class RedBullBehaviour
    {
        public static bool IsActive { get; private set; }

        public static void Activate(float duration)
        {
            IsActive = true;
            IssaPluginPlugin.Instance.StartCoroutine(DeactivateAfter(duration));
        }

        private static IEnumerator DeactivateAfter(float duration)
        {
            yield return new WaitForSeconds(duration);
            IsActive = false;
        }
    }
}
