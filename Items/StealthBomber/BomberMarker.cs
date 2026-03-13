namespace IssaPlugin.Items
{
    /// <summary>
    /// Tag component added to the BomberProxy GameObject on all clients.
    /// Used by GunshipLockOnPatches to identify the bomber as a custom lock-on
    /// target, and by GunshipRocketHomingPatch to attach homing behaviour to
    /// rockets fired while the bomber is locked on.
    /// </summary>
    public class BomberMarker : UnityEngine.MonoBehaviour { }
}
