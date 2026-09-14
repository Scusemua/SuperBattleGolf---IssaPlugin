using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace IssaPlugin.Network
{
    /// <summary>
    /// Queries the GitHub releases API once at startup and, if the latest published
    /// release is newer than <see cref="PluginInfo.PLUGIN_VERSION"/>, hands the result
    /// to <see cref="Overlays.UpdateAvailableOverlay"/> for a brief on-screen notice.
    ///
    /// Purely informational and entirely local -- nothing is downloaded or installed,
    /// and no request is made when GlobalConfig.UpdateCheckEnabled is false.
    ///
    /// The /releases/latest endpoint excludes prereleases, so the DEBUG diagnostics
    /// builds that `make release DEBUG=1` publishes never prompt anyone to update.
    ///
    /// Added to the Plugin's persistent GameObject in Plugin.cs.
    /// </summary>
    public class UpdateChecker : MonoBehaviour
    {
        private const string ReleasesUrl =
            "https://api.github.com/repos/Scusemua/SuperBattleGolf---IssaPlugin/releases/latest";

        private const string ReleasesPageUrl =
            "https://github.com/Scusemua/SuperBattleGolf---IssaPlugin/releases/latest";

        private const int TimeoutSeconds = 10;

        // Pulls "tag_name": "v0.0.48" out of the response without needing a JSON parser.
        private static readonly Regex TagPattern = new Regex(
            "\"tag_name\"\\s*:\\s*\"([^\"]+)\"",
            RegexOptions.Compiled
        );

        /// <summary>Latest version seen on GitHub, or null if the check has not succeeded.</summary>
        public static string LatestVersion { get; private set; }

        /// <summary>True once a release newer than the running build has been found.</summary>
        public static bool UpdateAvailable { get; private set; }

        /// <summary>Set by Awake so the `checkUpdate` console command can reach the instance.</summary>
        public static UpdateChecker Instance { get; private set; }

        /// <summary>
        /// The version we compare the GitHub release against: normally the running
        /// build, or GlobalConfig.UpdateCheckSpoofVersion when that is set for testing.
        /// </summary>
        private static string EffectiveVersion
        {
            get
            {
                string spoof = ModConfig.Global.UpdateCheckSpoofVersion.Value;
                return string.IsNullOrWhiteSpace(spoof) ? PluginInfo.PLUGIN_VERSION : spoof.Trim();
            }
        }

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Start()
        {
            if (!ModConfig.Global.UpdateCheckEnabled.Value)
                return;

            StartCoroutine(CheckForUpdate(ModConfig.Global.UpdateCheckDelay.Value));
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Runs the check again immediately, bypassing the startup delay. Used by the
        /// `checkUpdate` console command to test the notice without restarting.
        /// </summary>
        public void CheckNow() => StartCoroutine(CheckForUpdate(0f));

        // ── Check ─────────────────────────────────────────────────────────────

        private IEnumerator CheckForUpdate(float delaySeconds)
        {
            // Let the game finish loading before spending bandwidth on this.
            if (delaySeconds > 0f)
                yield return new WaitForSeconds(delaySeconds);

            using (var request = UnityWebRequest.Get(ReleasesUrl))
            {
                request.timeout = TimeoutSeconds;
                // GitHub rejects API requests that do not set a User-Agent.
                request.SetRequestHeader("User-Agent", PluginInfo.PLUGIN_NAME);
                request.SetRequestHeader("Accept", "application/vnd.github+json");

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    // A failed check is never worth bothering the player about.
                    IssaPluginPlugin.Log.LogInfo($"[UpdateCheck] Skipped: {request.error}");
                    yield break;
                }

                var match = TagPattern.Match(request.downloadHandler.text);
                if (!match.Success)
                {
                    IssaPluginPlugin.Log.LogInfo("[UpdateCheck] No tag_name in the response.");
                    yield break;
                }

                LatestVersion = match.Groups[1].Value;
            }

            string installed = EffectiveVersion;
            bool spoofed = installed != PluginInfo.PLUGIN_VERSION;
            string spoofNote = spoofed ? $" [SPOOFED, actual {PluginInfo.PLUGIN_VERSION}]" : "";

            if (!IsNewer(LatestVersion, installed))
            {
                IssaPluginPlugin.Log.LogInfo(
                    $"[UpdateCheck] Up to date (running {installed}{spoofNote}, "
                        + $"latest {LatestVersion})."
                );
                yield break;
            }

            UpdateAvailable = true;
            IssaPluginPlugin.Log.LogInfo(
                $"[UpdateCheck] Update available: {LatestVersion} "
                    + $"(running {installed}{spoofNote}). {ReleasesPageUrl}"
            );

            var overlay = Overlays.UpdateAvailableOverlay.Instance;
            if (overlay == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[UpdateCheck] Overlay instance is null -- notice cannot be shown."
                );
                yield break;
            }

            overlay.Show(Normalize(installed), Normalize(LatestVersion));
        }

        // ── Version comparison ────────────────────────────────────────────────

        /// <summary>
        /// Strips a leading "v" and any suffix after the numeric part, so tags like
        /// "v0.0.48" and "0.0.48-debug" both compare as 0.0.48.
        /// </summary>
        private static string Normalize(string version)
        {
            if (string.IsNullOrEmpty(version))
                return string.Empty;

            string trimmed = version.Trim();
            if (trimmed.Length > 0 && (trimmed[0] == 'v' || trimmed[0] == 'V'))
                trimmed = trimmed.Substring(1);

            int cut = trimmed.IndexOfAny(new[] { '-', '+', ' ' });
            return cut >= 0 ? trimmed.Substring(0, cut) : trimmed;
        }

        /// <summary>
        /// Component-wise numeric compare. Anything unparseable is treated as "not
        /// newer", so a malformed tag can never nag the player to update.
        /// </summary>
        public static bool IsNewer(string candidate, string current)
        {
            string[] a = Normalize(candidate).Split('.');
            string[] b = Normalize(current).Split('.');

            int length = Mathf.Max(a.Length, b.Length);
            for (int i = 0; i < length; i++)
            {
                if (!TryPart(a, i, out int av) || !TryPart(b, i, out int bv))
                    return false;

                if (av != bv)
                    return av > bv;
            }

            return false;
        }

        /// <summary>Reads one dotted component, treating a missing trailing one as 0.</summary>
        private static bool TryPart(string[] parts, int index, out int value)
        {
            if (index >= parts.Length)
            {
                value = 0;
                return true;
            }

            return int.TryParse(parts[index], out value);
        }
    }
}
