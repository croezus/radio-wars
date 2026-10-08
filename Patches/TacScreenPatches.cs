using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using RadioWars.Core;
using RadioWars.Config;
using RadioWars.Components;

namespace RadioWars.Patches
{
    [HarmonyPatch(typeof(TacScreen))]
    public static class TacScreenPatches
    {
        private static bool IsAdvancedCanvasActive()
        {
            if (!RadioWarsConfig.IsModActive) return false;
            if (RadioWarsConfig.EnableAdvancedCockpitRadarUI != null && !RadioWarsConfig.EnableAdvancedCockpitRadarUI.Value) return false;
            return RadioWarsConfig.TacScreenDisplayPreset == null || RadioWarsConfig.TacScreenDisplayPreset.Value == RadioWarsConfig.TacScreenPreset.AdvancedCanvas;
        }

        [HarmonyPatch("Initialize")]
        [HarmonyPostfix]
        public static void Initialize_Postfix(TacScreen __instance, Aircraft aircraft, Cockpit cockpit)
        {
            if (!RadioWarsConfig.IsModActive || aircraft == null) return;

            try
            {
                if (IsAdvancedCanvasActive())
                {
                    TacScreenTacticalDisplay tacticalDisplay = __instance.gameObject.GetComponent<TacScreenTacticalDisplay>();
                    if (tacticalDisplay == null)
                    {
                        tacticalDisplay = __instance.gameObject.AddComponent<TacScreenTacticalDisplay>();
                    }
                    tacticalDisplay.Initialize(__instance, aircraft);
                }
                else
                {
                    TacScreenTacticalDisplay tacticalDisplay = __instance.gameObject.GetComponent<TacScreenTacticalDisplay>();
                    if (tacticalDisplay != null)
                    {
                        UnityEngine.Object.Destroy(tacticalDisplay);
                    }
                }
            }
            catch (Exception ex)
            {
                if (RadioWarsPlugin.Log != null) RadioWarsPlugin.Log.LogError(string.Format("Error in TacScreen.Initialize_Postfix: {0}", ex));
            }
        }

        [HarmonyPatch("TacScreen_OnRadarWarning")]
        [HarmonyPrefix]
        public static bool TacScreen_OnRadarWarning_Prefix(TacScreen __instance, Aircraft.OnRadarWarning source, Aircraft ___aircraft)
        {
            if (!RadioWarsConfig.IsModActive)
            {
                return true; // Let vanilla run if mod is completely disabled
            }

            if (__instance == null || source.emitter == null) return false;

            Aircraft aircraft = ___aircraft;
            if (aircraft == null || aircraft.disabled) return false;

            // Register threat into PhysRWRReceiver for clean physical tracking & RWR azimuth display
            PhysRWRReceiver rwr = PhysRWRReceiver.Get(aircraft);
            if (rwr != null)
            {
                bool isMissile = (source.emitter is Missile);
                RwrThreatState threatState = isMissile ? RwrThreatState.MissileGuidance : (source.isTarget ? RwrThreatState.Track : RwrThreatState.Search);
                rwr.RegisterRadarPing(source.emitter, null, threatState, 0.7f, isMissile);
            }

            if (IsAdvancedCanvasActive())
            {
                // In AdvancedCanvas mode, suppress vanilla pingPrefab and radarUnitPrefab
                // TacScreenTacticalDisplay handles high-resolution vector rendering
                return false;
            }

            // In NativeEnhanced mode, let vanilla run to instantiate native pingPrefab
            return true;
        }

        [HarmonyPatch("TacScreen_OnRadarWarning")]
        [HarmonyPostfix]
        public static void TacScreen_OnRadarWarning_Postfix(TacScreen __instance, Aircraft.OnRadarWarning source, Transform ___iconLayer)
        {
            if (!RadioWarsConfig.IsModActive || IsAdvancedCanvasActive()) return;
            if (__instance == null || source.emitter == null) return;

            try
            {
                Transform iconLayer = ___iconLayer;
                if (iconLayer != null && iconLayer.childCount > 0)
                {
                    Transform newestItem = iconLayer.GetChild(iconLayer.childCount - 1);
                    if (newestItem != null)
                    {
                        Image img = newestItem.GetComponent<Image>();
                        if (img != null)
                        {
                            bool isMissile = (source.emitter is Missile);
                            Color threatColor;
                            if (isMissile)
                            {
                                threatColor = new Color(1.0f, 0.15f, 0.10f, 0.95f); // Red (Terminal Pitbull)
                            }
                            else if (source.isTarget)
                            {
                                threatColor = new Color(1.0f, 0.55f, 0.05f, 0.90f); // Orange (Track/Lock)
                            }
                            else
                            {
                                threatColor = new Color(1.0f, 0.88f, 0.20f, 0.85f); // Yellow (Search)
                            }
                            img.color = threatColor;
                        }
                    }
                }
            }
            catch { }
        }

        [HarmonyPatch("UpdateMissileWarning")]
        [HarmonyPrefix]
        public static bool UpdateMissileWarning_Prefix(TacScreen __instance)
        {
            if (!RadioWarsConfig.IsModActive)
            {
                return true;
            }

            if (IsAdvancedCanvasActive())
            {
                // Suppress vanilla's exact-range missile icon placement on the radar scope.
                // Active missile guidance and seeker azimuths are handled physically by TacScreenTacticalDisplay.
                return false;
            }

            return true;
        }

        [HarmonyPatch("TacScreen_OnRadarScan")]
        [HarmonyPrefix]
        public static bool TacScreen_OnRadarScan_Prefix(TacScreen __instance, Aircraft ___aircraft, ref Vector3 ___headingAtScan)
        {
            if (!RadioWarsConfig.IsModActive)
            {
                return true;
            }

            if (__instance == null) return false;

            // Maintain headingAtScan for radar line sweep animation
            if (___aircraft != null)
            {
                ___headingAtScan = ___aircraft.transform.forward;
            }

            if (IsAdvancedCanvasActive())
            {
                // Suppress vanilla's generic dot iconPrefab - TacScreenTacticalDisplay handles
                // high-fidelity F-15/F-16 symbology with velocity vectors.
                return false;
            }

            // In NativeEnhanced mode, let vanilla draw native iconPrefab for physically detected contacts
            return true;
        }

        [HarmonyPatch("TacScreen_OnOpticalScan")]
        [HarmonyPrefix]
        public static bool TacScreen_OnOpticalScan_Prefix(TacScreen __instance, Aircraft ___aircraft, ref Vector3 ___headingAtScan)
        {
            if (!RadioWarsConfig.IsModActive)
            {
                return true;
            }

            if (__instance == null) return false;

            if (___aircraft != null)
            {
                ___headingAtScan = ___aircraft.transform.forward;
            }

            if (IsAdvancedCanvasActive())
            {
                return false;
            }

            return true;
        }
    }
}
