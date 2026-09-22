using UnityEngine;

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
    internal static class GlobalSessionLock<T>
        where T : class
    {
        private static bool _active;
        private static T _holder;

        /// <summary>The live bridge instance currently holding the lock, or null.</summary>
        public static T Holder
        {
            get
            {
                ClearStaleHolder();
                return _holder;
            }
        }

        /// <summary>True if any instance currently holds the lock.</summary>
        public static bool IsActive
        {
            get
            {
                ClearStaleHolder();
                return _active;
            }
        }

        /// <summary>
        /// Attempts to acquire the lock for <paramref name="instance"/>.
        /// Returns false if already held — caller should bail out.
        /// </summary>
        public static bool TryAcquire(T instance)
        {
            ClearStaleHolder();
            if (_active)
                return false;
            _active = true;
            _holder = instance;
            return true;
        }

        /// <summary>
        /// Releases the lock only when <paramref name="instance"/> still owns it.
        /// This prevents delayed cleanup from one session releasing a newer session.
        /// </summary>
        public static bool Release(T instance)
        {
            ClearStaleHolder();
            if (!_active || !ReferenceEquals(_holder, instance))
                return false;

            Release();
            return true;
        }

        /// <summary>Releases the lock unconditionally.</summary>
        public static void Release()
        {
            _active = false;
            _holder = null;
        }

        private static void ClearStaleHolder()
        {
            if (
                _active
                && (
                    _holder == null
                    || (_holder is Object unityObject && unityObject == null)
                )
            )
                Release();
        }
    }
}
