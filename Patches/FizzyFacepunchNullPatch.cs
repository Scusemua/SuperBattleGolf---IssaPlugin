using System;
using System.Reflection;
using HarmonyLib;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Guards against a NullReferenceException in FizzyFacepunch.ClientSend.
    ///
    /// Root cause: FizzySteam's FizzyFacepunch transport stores the active Steam P2P
    /// socket in a private _client field. During initial connection setup and during
    /// disconnect/reconnect transitions there is a window where Mirror's
    /// NetworkConnection.Update() tries to flush its send queue while _client is still
    /// null (transport not yet connected) or already null (transport already torn down).
    ///
    /// The resulting NullReferenceException propagates through:
    ///   FizzyFacepunch.ClientSend
    ///   NetworkConnectionToServer.SendToTransport
    ///   NetworkConnection.Update
    ///   NetworkClient.NetworkLateUpdate
    ///   NetworkLoop.NetworkLateUpdate
    ///
    /// This is a bug in FizzySteam, not in this mod. The Finalizer below catches the
    /// NullReferenceException and suppresses it so it does not spam the log. The queued
    /// data is simply dropped, which is the correct behaviour when there is no live
    /// transport connection anyway.
    ///
    /// Note: FizzyFacepunch lives in a different assembly from Mirror.dll. We locate
    /// it at runtime via AccessTools.TypeByName so no hard assembly reference is needed.
    /// If the type is not found (e.g. the transport changes) the patch silently does nothing.
    /// </summary>
    [HarmonyPatch]
    static class FizzyFacepunchClientSendPatch
    {
        static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("Mirror.FizzySteam.FizzyFacepunch");
            if (type == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[FizzyPatch] Mirror.FizzySteam.FizzyFacepunch not found — patch skipped."
                );
                return null;
            }

            var method = AccessTools.Method(type, "ClientSend");
            if (method == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[FizzyPatch] FizzyFacepunch.ClientSend not found — patch skipped."
                );
            }

            return method;
        }

        static Exception Finalizer(Exception __exception)
        {
            if (__exception is NullReferenceException)
            {
                IssaPluginPlugin.Log.LogDebug(
                    "[FizzyPatch] Suppressed NullReferenceException in FizzyFacepunch.ClientSend "
                        + "(transport connection was null — normal during connect/disconnect transitions)."
                );
                return null; // swallow; Mirror's flush loop continues normally
            }

            return __exception; // rethrow everything else unmodified
        }
    }
}
