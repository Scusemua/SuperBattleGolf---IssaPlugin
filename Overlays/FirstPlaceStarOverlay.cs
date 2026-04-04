using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Spawns a local-only gold star prefab above whichever player is currently in
    /// first place. The star follows the player by being parented to their transform.
    /// Not networked — each client manages its own instance.
    /// </summary>
    public class FirstPlaceStarOverlay : MonoBehaviour
    {
        private const float CheckInterval = 0.5f;

        private GameObject _starInstance;
        private ulong _currentFirstPlaceGuid;
        private float _checkTimer;

        private void Update()
        {
            if (!ModConfig.Global.FirstPlaceStarEnabled.Value)
            {
                if (_starInstance != null && _starInstance.activeSelf)
                    HideStar();
                return;
            }

            _checkTimer -= Time.deltaTime;
            if (_checkTimer > 0f)
                return;
            _checkTimer = CheckInterval;

            RefreshStar();
        }

        private void OnDestroy()
        {
            if (_starInstance != null)
                Destroy(_starInstance);
        }

        private void RefreshStar()
        {
            // Gate: only show during active gameplay with multiple players.
            // Mirrors the game's own CanMarkFirstPlacePlayer logic.
            // Also prevents calling GetFirstPlaceState() when playerStates may be empty.
            var matchState = CourseManager.MatchState;
            bool activeState =
                matchState == MatchState.TeeOff
                || matchState == MatchState.Ongoing
                || matchState == MatchState.CountingDownToEnd
                || matchState == MatchState.Overtime
                || matchState == MatchState.Ended;
            if (!activeState || CourseManager.CountActivePlayers() < 1)
            {
                HideStar();
                return;
            }

            ulong newGuid;
            try
            {
                newGuid = CourseManager.GetFirstPlaceState().playerGuid;
            }
            catch
            {
                HideStar();
                return;
            }

            if (newGuid == _currentFirstPlaceGuid)
            {
                // Same player — star is already parented correctly; update position in case
                // the height config changed since we last attached.
                if (_starInstance != null)
                {
                    _starInstance.SetActive(true);
                    _starInstance.transform.localPosition = new Vector3(
                        0f,
                        ModConfig.Global.FirstPlaceStarHeight.Value,
                        0f
                    );
                }
                return;
            }

            _currentFirstPlaceGuid = newGuid;

            if (newGuid == 0 || AssetLoader.GoldStarPrefab == null)
            {
                HideStar();
                return;
            }

            if (
                !PlayerInfo.playerInfoPerPlayerGuid.TryGetValue(newGuid, out var playerInfo)
                || playerInfo == null
            )
            {
                HideStar();
                return;
            }

            AttachStarTo(playerInfo);
        }

        private void AttachStarTo(PlayerInfo playerInfo)
        {
            if (_starInstance == null)
            {
                _starInstance = Instantiate(AssetLoader.GoldStarPrefab);
                foreach (var col in _starInstance.GetComponentsInChildren<Collider>())
                    col.enabled = false;
            }

            // Parent to HeadBone (same as the game's own crown VFX) so the offset
            // is relative to the head rather than the player's root (feet).
            Transform anchor =
                playerInfo.HeadBone != null ? playerInfo.HeadBone : playerInfo.transform;
            float height = ModConfig.Global.FirstPlaceStarHeight.Value;
            _starInstance.transform.SetParent(anchor, false);
            _starInstance.transform.localPosition = new Vector3(0f, height, 0f);
            _starInstance.transform.localRotation = Quaternion.identity;
            _starInstance.SetActive(true);
        }

        private void HideStar()
        {
            // Reset guid so the next check always tries to re-attach rather than
            // calling SetActive(true) on an unparented star.
            _currentFirstPlaceGuid = 0;
            if (_starInstance == null)
                return;
            _starInstance.transform.SetParent(null, false);
            _starInstance.SetActive(false);
        }
    }
}
