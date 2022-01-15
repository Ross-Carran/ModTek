using System;
using BattleTech.UI;
using Harmony;
using ModTek.Features.LoadingCurtainEx.DataManagerStats;
using ModTek.Features.Logging;

namespace ModTek.Features.LoadingCurtainEx
{
    [HarmonyPatch(typeof(LoadingCurtain), nameof(LoadingCurtain.Init))]
    internal static class LoadingCurtain_Init_Patch
    {
        public static bool Prepare()
        {
            return ModTek.Enabled && ModTek.Config.ShowDataManagerStatsInLoadingCurtain;
        }

        public static void Postfix(LoadingCurtain __instance)
        {
            try
            {
                LoadingCurtainStatsText.Init(__instance);
            }
            catch (Exception e)
            {
                MTLogger.Error.Log("Failed running postfix", e);
            }
        }
    }
}
