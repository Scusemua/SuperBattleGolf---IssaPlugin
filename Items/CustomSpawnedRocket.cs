using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    // Added to rockets spawned by the AC130 / Stealth Bomber so that they can be ignored
    //  by the RocketHomingPatch::Postfix method.
    public class CustomSpawnedRocket : MonoBehaviour { }
}
