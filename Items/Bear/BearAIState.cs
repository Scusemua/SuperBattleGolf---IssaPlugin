namespace IssaPlugin.Items
{
    /// <summary>
    /// All possible AI states for a server-simulated bear.
    ///
    /// This enum is serialized as a byte in BearStateMessage, so it must
    /// remain under 256 values and the numeric assignments must not change
    /// between mod versions (they are sent over the network).
    ///
    /// Client-side BearAnimatorDriver maps each value to Animator parameters.
    /// Server-side BearBehaviour drives transitions between states.
    /// </summary>
    public enum BearAIState : byte
    {
        /// Brief entry sequence: plays the Buff animation. Transitions to Idle.
        Spawning = 0,

        /// No target nearby or reachable. Bear wanders slowly.
        Idle = 1,

        /// Bear has a locked target and is running toward it.
        Pursuing = 2,

        /// Bear is within charge range and sprinting at maximum speed toward target.
        /// No longer re-evaluates target or steers around obstacles — committed.
        Charging = 3,

        /// Attack animation playing. Hit is applied mid-animation by the server.
        Attacking = 4,

        /// Brief pause after an attack before returning to Pursuing.
        AttackCooldown = 5,

        /// Bear was hit by a rocket/explosion. Plays GetHitFromFront then StunnedLoop.
        /// After the stun duration, transitions to Enraged.
        Stunned = 6,

        /// Temporary speed boost after recovering from a stun.
        /// Same as Pursuing but at enrage speed. Returns to Pursuing after duration.
        Enraged = 7,

        /// Death animation playing. Bear cannot interact with anything.
        Dying = 8,

        /// Death animation finished. Server will call NetworkServer.Destroy this frame.
        Dead = 9,

        /// No target nearby. Bear is actively walking to a wander destination.
        /// Distinct from Idle so clients can play the Walk animation instead of Idle.
        Wander = 10,
    }
}
