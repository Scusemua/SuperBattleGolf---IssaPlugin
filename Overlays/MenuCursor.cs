using UnityEngine;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Keeps the cursor free while any in-game menu is open.
    /// The game stores that as one flag, so each open menu holds a count and the
    /// cursor locks again only when the last menu closes.
    /// </summary>
    public static class MenuCursor
    {
        private static int _holders;

        public static void Acquire()
        {
            _holders++;
            if (_holders == 1)
                CursorManager.SetCursorForceUnlocked(true);
        }

        public static void Release()
        {
            if (_holders == 0)
                return;

            _holders--;
            if (_holders == 0)
                CursorManager.SetCursorForceUnlocked(false);
        }
    }
}
