using System.Collections;
using System.IO;
using System.Reflection;
using IssaPlugin.Patches;
using Mirror;
using UnityEngine;
using UnityEngine.Networking;

namespace IssaPlugin.Items
{
    public static class BatItem
    {
        public static readonly ItemType BatItemType = (ItemType)100;
        public static AudioClip HomerunClip { get; private set; }

        private static MethodInfo _cmdAddItemMethod;

        public static IEnumerator LoadHomerunSound()
        {
            string pluginDir = Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location);

            string[] candidates = { "homerun.wav", "homerun.ogg", "homerun.mp3" };
            string audioPath = null;

            foreach (var name in candidates)
            {
                string candidate = Path.Combine(pluginDir, name);
                if (File.Exists(candidate))
                {
                    audioPath = candidate;
                    break;
                }
            }

            if (audioPath == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[Bat] No homerun audio file found in: {pluginDir}  " +
                    "(supported: homerun.wav, homerun.ogg, homerun.mp3)");
                yield break;
            }

            string fileUrl = "file:///" + audioPath.Replace('\\', '/');
            IssaPluginPlugin.Log.LogInfo($"[Bat] Loading audio from: {fileUrl}");

            var handler = new DownloadHandlerAudioClip(fileUrl, AudioType.UNKNOWN);
            handler.compressed = false;
            var request = new UnityWebRequest(fileUrl, "GET", handler, null);

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                IssaPluginPlugin.Log.LogError(
                    $"[Bat] Failed to load audio: {request.error}");
                request.Dispose();
                yield break;
            }

            HomerunClip = handler.audioClip;
            request.Dispose();

            if (HomerunClip == null)
            {
                IssaPluginPlugin.Log.LogError(
                    "[Bat] Audio clip decoded as null. " +
                    "Try converting to WAV (PCM 16-bit) or OGG format.");
                yield break;
            }

            HomerunClip.name = "homerun";
            IssaPluginPlugin.Log.LogInfo(
                $"[Bat] Loaded {Path.GetFileName(audioPath)} " +
                $"({HomerunClip.length:F1}s, {HomerunClip.channels}ch, {HomerunClip.frequency}Hz).");
        }

        public static void PlayHomerunSound(Vector3 position)
        {
            if (HomerunClip != null)
                AudioSource.PlayClipAtPoint(HomerunClip, position);
        }

        public static void GiveBatToLocalPlayer()
        {
            var inventory = GameManager.LocalPlayerInventory;
            if (inventory == null)
            {
                IssaPluginPlugin.Log.LogWarning("[Bat] No local player inventory.");
                return;
            }

            if (NetworkServer.active)
            {
                bool added = InventoryPatches.DirectAddCustomItem(
                    inventory,
                    BatItemType,
                    Configuration.BaseballBatUses.Value
                );
                if (!added)
                    IssaPluginPlugin.Log.LogWarning("[Bat] Failed to add bat (inventory full?).");
            }
            else
            {
                if (_cmdAddItemMethod == null)
                {
                    _cmdAddItemMethod = typeof(PlayerInventory).GetMethod(
                        "CmdAddItem",
                        BindingFlags.NonPublic | BindingFlags.Instance
                    );
                }

                if (_cmdAddItemMethod != null)
                {
                    _cmdAddItemMethod.Invoke(inventory, new object[] { BatItemType });
                    IssaPluginPlugin.Log.LogInfo("[Bat] Requested bat via server command.");
                }
                else
                {
                    IssaPluginPlugin.Log.LogError("[Bat] Could not find CmdAddItem method.");
                }
            }
        }
    }
}
