﻿using BattleTech.Data;
using Harmony;
using UnityEngine;

namespace ModTek.Features.Manifest.Patches
{
    [HarmonyPatch(typeof(LoadRequest), "PopPendingRequest")]
    public static class LoadRequest_PopPendingRequest_Patch
    {
        public static bool Prepare()
        {
            return ModTek.Enabled;
        }

        private static float lastNull;
        // by returning "null" we allow the UI to render again (if only one LoadRequest is active)
        // fixes the issue that vanilla happily loads and loads and loads on main thread
        // which gets worse with modded content and hooks
        public static bool Prefix(LoadRequest __instance, ref DataManager.FileLoadRequest __result)
        {
            var deltaInSecondsMax = ModTek.Config.DataManagerUnfreezeDelta;
            var deltaInSecondsCurrent = Time.realtimeSinceStartup - lastNull;

            if (deltaInSecondsCurrent >= deltaInSecondsMax) {
                // logging just takes space and time
                // MTLogger.Log($"LoadRequest unfreeze delta {deltaInSecondsCurrent:0.##}/{deltaInSecondsMax:0.##}");
                __result = null;
                return false;
            }

            return true;
        }

        public static void Postfix(ref DataManager.FileLoadRequest __result)
        {
            if (__result == null)
            {
                lastNull = Time.realtimeSinceStartup;
            }
        }
    }
}