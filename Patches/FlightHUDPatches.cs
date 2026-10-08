using System;
using HarmonyLib;
using UnityEngine;
using RadioWars.Config;
using RadioWars.Components;

namespace RadioWars.Patches
{
    [HarmonyPatch(typeof(FlightHud))]
    public static class FlightHUDPatches
    {
        [HarmonyPatch("SetAircraft")]
        [HarmonyPostfix]
        public static void SetAircraft_Postfix(FlightHud __instance, Aircraft aircraft)
        {
            if (!RadioWarsConfig.IsModActive || __instance == null || aircraft == null) return;

            try
            {
                if (RadioWarsConfig.EnableHUDNotchIndicator != null && RadioWarsConfig.EnableHUDNotchIndicator.Value)
                {
                    FlightHUDEWIndicator indicator = __instance.gameObject.GetComponent<FlightHUDEWIndicator>();
                    if (indicator == null)
                    {
                        indicator = __instance.gameObject.AddComponent<FlightHUDEWIndicator>();
                    }
                    indicator.Initialize(__instance, aircraft);
                }

                if (RadioWarsConfig.EnableMissileTrajectoryHUD == null || RadioWarsConfig.EnableMissileTrajectoryHUD.Value)
                {
                    MissileTrajectoryHUDVisualizer trajVis = __instance.gameObject.GetComponent<MissileTrajectoryHUDVisualizer>();
                    if (trajVis == null)
                    {
                        trajVis = __instance.gameObject.AddComponent<MissileTrajectoryHUDVisualizer>();
                    }
                }
            }
            catch (Exception ex)
            {
                if (RadioWarsPlugin.Log != null)
                {
                    RadioWarsPlugin.Log.LogError(string.Format("Error attaching components in FlightHud.SetAircraft: {0}", ex));
                }
            }
        }
    }
}
