using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Added to the AC130 gunship GameObject on every client.
    /// Used as a lightweight tag so Harmony patches and other components
    /// can identify the gunship without relying on AC130FlyBehaviour
    /// (which only exists server-side).
    /// </summary>
    public class AC130GunshipMarker : MonoBehaviour { }
}
