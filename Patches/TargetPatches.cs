using System;
using HarmonyLib;
using UnityEngine;
using RadioWars.Components;
using RadioWars.Core;
using RadioWars.Config;

namespace RadioWars.Patches
{
    [HarmonyPatch(typeof(Unit), "InitializeUnit")]
    public static class Unit_InitializeUnit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Unit __instance)
        {
            if (__instance == null) return;

            PhysRadarTarget target = __instance.gameObject.GetComponent<PhysRadarTarget>();
            if (target == null)
            {
                target = __instance.gameObject.AddComponent<PhysRadarTarget>();
            }
            target.InitializeProfile();

            DatalinkNetwork.RegisterUnit(__instance);
        }
    }

    [HarmonyPatch(typeof(Unit), "OnDestroy")]
    public static class Unit_OnDestroy_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Unit __instance)
        {
            if (__instance == null) return;

            Missile missile = __instance as Missile;
            if (missile != null)
            {
                int id = missile.GetInstanceID();
                ARHSeeker_GetRadarReturn_Patch.ClearCoastTimer(id);
                SARHSeeker_GetTrackingStrength_Patch.ClearCoastTimer(id);
                DatalinkNetwork.DeregisterMissile(missile);
                TrackUncertaintyCalculator.UnregisterMissile(missile);
            }
            else
            {
                DatalinkNetwork.DeregisterUnit(__instance);
            }

            RWRTriangulationProcessor.RemoveTrack(__instance);
        }
    }

    [HarmonyPatch(typeof(FactionHQ), "TryGetKnownPosition")]
    public static class FactionHQ_TryGetKnownPosition_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(FactionHQ __instance, Unit unit, ref GlobalPosition knownPosition, ref bool __result)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive) return true;
                if (unit == null || unit is Missile) return true;

                // Friendly units handled normally by vanilla
                if (unit.NetworkHQ != null && __instance != null && unit.NetworkHQ == __instance)
                {
                    return true;
                }

                // Check if this unit is a classified radar threat
                if (!CombatHUDPatches.IsTrackableThreat(unit, __instance))
                {
                    return true;
                }

                // Query Faction Datalink Track
                TriangulationTrack track = RWRTriangulationProcessor.GetTrack(__instance, unit);
                float memDuration = (RadioWarsConfig.TargetMemoryDurationSeconds != null)
                    ? RadioWarsConfig.TargetMemoryDurationSeconds.Value
                    : 120.0f;

                if (track != null)
                {
                    if (track.IsActivelyDetected)
                    {
                        knownPosition = GlobalPositionExtensions.GlobalPosition(unit);
                        __result = true;
                        return false;
                    }
                    if (track.IsInMemoryState(memDuration))
                    {
                        knownPosition = track.LastKnownGlobalPosition;
                        __result = true;
                        return false;
                    }
                }

                // Threat is untriangulated or memory expired: suppress god-mode pinpoint coordinates
                bool hideIcons = RadioWarsConfig.HideUntriangulatedRadarIcons == null || RadioWarsConfig.HideUntriangulatedRadarIcons.Value;
                if (hideIcons)
                {
                    __result = false;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] FactionHQ_TryGetKnownPosition_Patch error: " + ex);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(FactionHQ), "IsTargetPositionAccurate")]
    public static class FactionHQ_IsTargetPositionAccurate_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(FactionHQ __instance, Unit target, float threshold, ref bool __result)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive) return true;
                if (target == null || target is Missile) return true;

                // Friendly units handled normally by vanilla (always accurate)
                if (target.NetworkHQ != null && __instance != null && target.NetworkHQ == __instance)
                {
                    return true;
                }

                // Check if this unit is a classified radar threat
                if (!CombatHUDPatches.IsTrackableThreat(target, __instance))
                {
                    return true;
                }

                TriangulationTrack track = RWRTriangulationProcessor.GetTrack(__instance, target);
                if (track != null)
                {
                    if (track.IsActivelyDetected)
                    {
                        __result = true;
                        return false;
                    }

                    float memDuration = (RadioWarsConfig.TargetMemoryDurationSeconds != null)
                        ? RadioWarsConfig.TargetMemoryDurationSeconds.Value
                        : 120.0f;

                    if (track.IsInMemoryState(memDuration))
                    {
                        // Target contact was lost, now in memory coasting:
                        // For high-precision requirements (e.g. HUD visual markers, threshold = 20f),
                        // return false so the vanilla HUD displays the "?" memory symbology.
                        // For coarse navigation requirements (e.g. ARH midcourse datalink, threshold = 2000f),
                        // return true so missiles and datalink continue midcourse guidance to the datum.
                        __result = (threshold >= 500f);
                        return false;
                    }
                }

                // Untriangulated or expired threat: suppress accurate lock
                bool hideIcons = RadioWarsConfig.HideUntriangulatedRadarIcons == null || RadioWarsConfig.HideUntriangulatedRadarIcons.Value;
                if (hideIcons)
                {
                    __result = false;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] FactionHQ_IsTargetPositionAccurate_Patch error: " + ex);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(Aircraft), "Awake")]
    public static class Aircraft_Awake_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Aircraft __instance)
        {
            if (__instance == null) return;

            PhysRWRReceiver rwr = __instance.gameObject.GetComponent<PhysRWRReceiver>();
            if (rwr == null)
            {
                __instance.gameObject.AddComponent<PhysRWRReceiver>();
            }
        }
    }

    [HarmonyPatch(typeof(Radar))]
    public static class Radar_Lifecycle_Patches
    {
        [HarmonyPatch("Awake")]
        [HarmonyPostfix]
        public static void Awake_Postfix(Radar __instance)
        {
            if (__instance == null) return;

            PhysRadarEmitter emitter = __instance.gameObject.GetComponent<PhysRadarEmitter>();
            if (emitter == null)
            {
                emitter = __instance.gameObject.AddComponent<PhysRadarEmitter>();
            }
            emitter.InitializeSpecs();
            DatalinkNetwork.RegisterRadar(__instance);
        }

        [HarmonyPatch("AttachToUnit")]
        [HarmonyPostfix]
        public static void AttachToUnit_Postfix(Radar __instance, Unit unit)
        {
            if (__instance == null) return;
            DatalinkNetwork.RegisterRadar(__instance);
        }

        [HarmonyPatch("OnDestroy")]
        [HarmonyPostfix]
        public static void OnDestroy_Postfix(Radar __instance)
        {
            if (__instance == null) return;
            DatalinkNetwork.DeregisterRadar(__instance);
        }
    }
}
