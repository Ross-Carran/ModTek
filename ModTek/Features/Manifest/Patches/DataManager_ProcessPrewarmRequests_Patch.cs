﻿using System.Collections.Generic;
using System.Linq;
using BattleTech.Data;
using Harmony;

namespace ModTek.Features.Manifest.Patches;

[HarmonyPatch(typeof(DataManager), nameof(DataManager.ProcessPrewarmRequests))]
internal static class DataManager_ProcessPrewarmRequests_Patch
{
    public static bool Prepare()
    {
        return ModTek.Enabled && ModTek.Config.DelayPrewarmToMainMenu;
    }

    public static List<PrewarmRequest> GetAndClearPrewarmRequests()
    {
        var copy = PrewarmRequests.ToList();
        PrewarmRequests.Clear();
        return copy;
    }
    private static readonly List<PrewarmRequest> PrewarmRequests = new();
    public static bool Prefix(IEnumerable<PrewarmRequest> toPrewarm)
    {
        if (toPrewarm != null)
        {
            PrewarmRequests.AddRange(toPrewarm);
        }
        return false;
    }
}