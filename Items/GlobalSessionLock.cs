namespace IssaPlugin.Items
{
    /// <summary>
    /// Per-item-type global session lock. C# generic statics give each closed type
    /// (GlobalSessionLock&lt;AC130NetworkBridge&gt;, GlobalSessionLock&lt;DonutNetworkBridge&gt;)
    /// its own independent fields — no dictionary, no boxing.
    ///
    /// Usage pattern:
    ///   Acquire: if (!GlobalSessionLock&lt;T&gt;.IsActive) { _ = GlobalSessionLock&lt;T&gt;.TryAcquire(this); }
    ///   Release: GlobalSessionLock&lt;T&gt;.Release();
    ///   Holder:  GlobalSessionLock&lt;T&gt;.Holder?._someField
    /// </summary>
    internal static class GlobalSessionLock<T> where T : class
    {
        private static bool _active;
        private static T _holder;

        /// <summary>The bridge instance currently holding the lock, or null.</summary>
        public static T Holder => _holder;

        /// <summary>True if any instance currently holds the lock.</summary>
        public static bool IsActive => _active;

        /// <summary>
        /// Attempts to acquire the lock for <paramref name="instance"/>.
        /// Returns false if already held — caller should bail out.
        /// </summary>
        public static bool TryAcquire(T instance)
        {
            if (_active) return false;
            _active = true;
            _holder = instance;
            return true;
        }

        /// <summary>Releases the lock unconditionally.</summary>
        public static void Release()
        {
            _active = false;
            _holder = null;
        }
    }
}
