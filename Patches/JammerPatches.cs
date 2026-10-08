using System;
using HarmonyLib;
using UnityEngine;
using RadioWars.Core;
using RadioWars.Config;
using RadioWars.Components;

namespace RadioWars.Patches
{
    [HarmonyPatch(typeof(RadarJammer), "Fire")]
    public static class RadarJammer_Fire_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(RadarJammer __instance)
        {
            if (!RadioWarsConfig.IsModActive) return;
            if (__instance == null || __instance.aircraft == null) return;

            PhysRadarTarget target = __instance.aircraft.GetComponent<PhysRadarTarget>();
            if (target != null)
            {
                target.HasActiveJammer = true;
                target.Jammer.PowerWatts = Mathf.Max(250.0f, __instance.GetMaxJammingIntensity() * 150.0f);
            }
        }
    }

    [HarmonyPatch(typeof(RadarJammer), "Update")]
    public static class RadarJammer_Update_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(RadarJammer __instance)
        {
            if (!RadioWarsConfig.IsModActive) return;
            if (__instance == null || __instance.aircraft == null) return;

            if (!__instance.enabled)
            {
                PhysRadarTarget target = __instance.aircraft.GetComponent<PhysRadarTarget>();
                if (target != null)
                {
                    target.HasActiveJammer = false;
                }
            }
        }
    }
}
