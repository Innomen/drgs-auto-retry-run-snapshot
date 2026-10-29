using HarmonyLib;
using Il2CppInterop.Runtime;

namespace DRGSAutoRetry;

// Scene reload can leave destroyed Unity targets in this static multicast
// delegate. One throwing subscriber aborts cleanup and the stage transition.
// Remove only destroyed Unity targets; preserve static/non-Unity subscribers,
// live targets, order, and the game's normal exception behavior.
[HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.CleanUp))]
internal static class RecallCleanup
{
    static void Prefix()
    {
        var callbacks = MapGenerator.OnRecallAll;
        if (callbacks == null) return;
        var cleaned = callbacks.Cast<Il2CppSystem.Delegate>();
        int removed = 0;
        foreach (var callback in callbacks.GetInvocationList())
        {
            if (cleaned == null) break;
            var target = callback.Target;
            if (target == null) continue;
            var unityTarget = target.TryCast<UnityEngine.Object>();
            if (ReferenceEquals(unityTarget, null) || unityTarget) continue;
            cleaned = Il2CppSystem.Delegate.Remove(cleaned, callback);
            removed++;
        }
        if (removed == 0) return;
        MapGenerator.OnRecallAll = cleaned?.Cast<Il2CppSystem.Action>();
        Plugin.Instance.Log.LogInfo("Removed destroyed map-recall subscribers: " + removed);
    }
}
