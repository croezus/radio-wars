using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using RadioWars.Core;
using RadioWars.Config;
using RadioWars.Components;

namespace RadioWars.Patches
{
    [HarmonyPatch(typeof(CockpitWarningLights))]
    public static class CockpitWarningLightsPatches
    {
        [HarmonyPatch("Update")]
        [HarmonyPostfix]
        public static void Update_Postfix(CockpitWarningLights __instance, Aircraft ___aircraft, Renderer[] ___lightRenderers)
        {
            if (!RadioWarsConfig.IsModActive || __instance == null) return;

            Aircraft aircraft = ___aircraft;
            if (aircraft == null || aircraft.disabled) return;

            PhysRWRReceiver rwr = PhysRWRReceiver.Get(aircraft);
            if (rwr == null) return;

            RwrThreatState state = rwr.GetHighestThreatState();
            if (state == RwrThreatState.MissileGuidance)
            {
                Renderer[] lights = ___lightRenderers;
                if (lights != null)
                {
                    bool flash = (Mathf.Sin(Time.timeSinceLevelLoad * 24.0f) > 0.0f);
                    for (int i = 0; i < lights.Length; i++)
                    {
                        if (lights[i] != null)
                        {
                            lights[i].enabled = flash;
                        }
                    }
                }
                __instance.enabled = true;
            }
        }
    }
}
