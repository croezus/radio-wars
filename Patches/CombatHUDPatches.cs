using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RadioWars.Core;
using RadioWars.Config;

namespace RadioWars.Patches
{
    public static class CombatHUDPatches
    {
        private static readonly List<Graphic> s_graphicBuffer = new List<Graphic>(16);
        private static int s_lastRadarFrame = -1;
        private static Aircraft s_cachedPlayerAircraft = null;
        private static Radar s_cachedPlayerRadar = null;

        public static Radar GetCachedPlayerRadar(Aircraft playerAircraft)
        {
            if (playerAircraft == null) return null;
            int curFrame = Time.frameCount;
            if (s_lastRadarFrame != curFrame || s_cachedPlayerAircraft != playerAircraft)
            {
                s_lastRadarFrame = curFrame;
                s_cachedPlayerAircraft = playerAircraft;
                s_cachedPlayerRadar = playerAircraft.GetComponentInChildren<Radar>();
            }
            return s_cachedPlayerRadar;
        }

        private static bool ContainsIgnoreCase(string source, string value)
        {
            return source != null && source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsTrackableThreat(Unit unit, FactionHQ friendlyHq = null)
        {
            if (unit == null || unit.disabled || unit is Missile) return false;

            // 1. Friendly units are never treated as hostile radar threats
            if (friendlyHq != null)
            {
                FactionHQ uHq = DatalinkNetwork.GetUnitFactionHQ(unit);
                if (uHq != null && uHq == friendlyHq)
                {
                    return false;
                }
            }

            // 2. Active or equipped radar systems (direct field, child component, or classified emission)
            // Evaluated first so ground radar stations (Building subclasses) and SAM sites are recognized!
            if (unit.radar != null && unit.radar.IsOperational()) return true;
            Radar childRadar = unit.GetComponentInChildren<Radar>();
            if (childRadar != null && childRadar.IsOperational()) return true;
            if (unit.HasRadarEmission()) return true;

            // 3. Classified UnitDefinition radar signatures
            if (unit.definition != null)
            {
                if (unit.definition.code == "RDR" || unit.definition.code == "SAM") return true;
                if (unit.definition.typeIdentity.radar > 0.0f) return true;
                if (unit.definition.roleIdentity.antiAir > 0.5f && unit.weaponStations != null && unit.weaponStations.Count > 0)
                {
                    return true;
                }
            }

            // 4. If an ESM Triangulation or Datalink track already exists for this unit, it is DEFINITIVELY a trackable threat!
            if (RWRTriangulationProcessor.HasTrack(friendlyHq, unit))
            {
                return true;
            }

            // 5. Airborne combat aircraft
            if (unit is Aircraft)
            {
                return true;
            }

            // 6. Warships with combat radar systems
            if (unit is Ship)
            {
                return true;
            }

            // 7. Exclude static non-sensor structures, obstacles, and civilian scenery
            if (unit is Building || unit is Scenery || unit is Container) return false;
            if (unit.definition != null && unit.definition.IsObstacle) return false;

            string code = (unit.definition != null) ? unit.definition.code : null;
            if (!string.IsNullOrEmpty(code))
            {
                if (code == "HGR" || code == "BLD" || code == "STR" || code == "RUN" || code == "DEP")
                {
                    return false;
                }
            }

            string uName = unit.unitName ?? unit.name;
            if (uName != null)
            {
                if (ContainsIgnoreCase(uName, "hangar") || ContainsIgnoreCase(uName, "shelter") || ContainsIgnoreCase(uName, "warehouse") ||
                    ContainsIgnoreCase(uName, "depot") || ContainsIgnoreCase(uName, "building") || ContainsIgnoreCase(uName, "runway") ||
                    ContainsIgnoreCase(uName, "factory") || ContainsIgnoreCase(uName, "fuel") || ContainsIgnoreCase(uName, "tower"))
                {
                    return false;
                }
            }

            // 8. Name heuristic for dedicated radar/SAM platforms
            if (uName != null)
            {
                if (ContainsIgnoreCase(uName, "radar") || ContainsIgnoreCase(uName, "sam") || ContainsIgnoreCase(uName, "spaag") ||
                    ContainsIgnoreCase(uName, "boltstrike") || ContainsIgnoreCase(uName, "cursor") || ContainsIgnoreCase(uName, "flak") ||
                    ContainsIgnoreCase(uName, "tarantula") || ContainsIgnoreCase(uName, "chicane"))
                {
                    return true;
                }
            }

            // All simple wheeled vehicles (trucks, APCs, transports, utility vehicles) and non-radar armor are 100% vanilla
            return false;
        }

        public static bool IsRadarThreatUnit(Unit unit)
        {
            FactionHQ hq = RWRTriangulationProcessor.GetPlayerFactionHQ();
            return IsTrackableThreat(unit, hq);
        }

        public static float VisualIdentificationRange
        {
            get
            {
                return (RadioWarsConfig.VisualRadarIdentificationRangeMeters != null)
                    ? Mathf.Max(RadioWarsConfig.VisualRadarIdentificationRangeMeters.Value, 100.0f)
                    : 10000.0f;
            }
        }

        public static void SuppressMarker(HUDUnitMarker marker)
        {
            if (marker == null || marker.image == null) return;

            if (marker.image.gameObject.activeSelf)
            {
                marker.image.gameObject.SetActive(false);
            }
            marker.image.enabled = false;

            s_graphicBuffer.Clear();
            marker.image.GetComponentsInChildren(true, s_graphicBuffer);
            for (int i = 0; i < s_graphicBuffer.Count; i++)
            {
                s_graphicBuffer[i].enabled = false;
            }
            s_graphicBuffer.Clear();

            marker.image.transform.position = new Vector3(-9999f, -9999f, 0f);

            if (marker.selected && CombatHUD.i != null)
            {
                CombatHUD.i.SetTargetArrow(false, Vector3.zero, Vector3.zero);
            }
        }

        public static void SetMarkerVisible(HUDUnitMarker marker)
        {
            if (marker == null || marker.image == null) return;

            if (!marker.image.gameObject.activeSelf)
            {
                marker.image.gameObject.SetActive(true);
            }
            marker.image.enabled = true;

            s_graphicBuffer.Clear();
            marker.image.GetComponentsInChildren(true, s_graphicBuffer);
            for (int i = 0; i < s_graphicBuffer.Count; i++)
            {
                s_graphicBuffer[i].enabled = true;
            }
            s_graphicBuffer.Clear();
        }

        private static readonly Action<HUDUnitMarker, bool> s_setOutdated =
            AccessTools.MethodDelegate<Action<HUDUnitMarker, bool>>(AccessTools.Method(typeof(HUDUnitMarker), "SetOutdated", new Type[] { typeof(bool) }));

        public static void SetMarkerOutdated(HUDUnitMarker marker, bool outdated)
        {
            if (marker == null) return;
            try
            {
                if (s_setOutdated != null)
                {
                    s_setOutdated(marker, outdated);
                }
                else
                {
                    marker.outdated = outdated;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] SetMarkerOutdated error: " + ex);
            }
        }
    }

    /// <summary>
    /// Synchronizes Helmet-Mounted Display (HMD) / CombatHUD unit markers and target designation
    /// with intelligence quality rules:
    /// 1. Suppresses pinpoint HUD markers and designation text when TrackingQuality < HMD threshold (default: 0.30).
    /// 2. Displays target markers once Q >= 0.30, with status/uncertainty conveyed via reticle coloring.
    /// 3. Prevents target selection for low-intelligence emitters (blocking arcade lock and TargetCam exploitation).
    /// </summary>
    [HarmonyPatch(typeof(HUDUnitMarker), "UpdatePosition", new Type[] { typeof(FactionHQ), typeof(GlobalPosition), typeof(Vector3) })]
    public static class HUDUnitMarker_UpdatePosition_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(HUDUnitMarker __instance, FactionHQ hq, GlobalPosition viewPosition, Vector3 cameraForward)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive ||
                    RadioWarsConfig.EnableRwrTriangulationSystem == null ||
                    !RadioWarsConfig.EnableRwrTriangulationSystem.Value)
                {
                    return;
                }

                if (__instance == null || __instance.unit == null || __instance.image == null) return;
                if (__instance.unit is Missile) return;

                Aircraft playerAircraft = CombatHUD.i != null ? CombatHUD.i.aircraft : null;
                FactionHQ playerHq = (playerAircraft != null && playerAircraft.NetworkHQ != null)
                    ? playerAircraft.NetworkHQ
                    : RWRTriangulationProcessor.GetPlayerFactionHQ();

                // If not a trackable radar threat (e.g. friendly unit, simple wheeled truck, hangar), leave 100% vanilla!
                if (!CombatHUDPatches.IsTrackableThreat(__instance.unit, playerHq))
                {
                    return;
                }

                // Close visual identification range reveals marker normally at true position
                if (playerAircraft != null && !playerAircraft.disabled)
                {
                    float distToPlayer = Vector3.Distance(__instance.unit.transform.position, playerAircraft.transform.position);
                    if (distToPlayer < CombatHUDPatches.VisualIdentificationRange)
                    {
                        RWRTriangulationProcessor.RecordVisualContact(playerHq, __instance.unit, playerAircraft);
                        CombatHUDPatches.SetMarkerVisible(__instance);
                        ApplyTrackQualityColor(__instance, playerAircraft);
                        return;
                    }

                    // Direct active radar track reveals marker normally at true position
                    Radar playerRadar = CombatHUDPatches.GetCachedPlayerRadar(playerAircraft);
                    if (playerRadar != null && playerRadar.IsOperational())
                    {
                        if (playerRadar.CheckIsTarget(__instance.unit) || (playerRadar.detectedTargets != null && playerRadar.detectedTargets.Contains(__instance.unit)))
                        {
                            ApplyTrackQualityColor(__instance, playerAircraft, null);
                            return;
                        }
                    }
                }

                // Query Faction Datalink Track for this threat
                TriangulationTrack track = RWRTriangulationProcessor.GetTrack(playerHq, __instance.unit);

                float memDuration = RWRTriangulationProcessor.GetTargetMemoryDuration(__instance.unit);

                // Layered Intelligence Filtering for HMD / CombatHUD:
                // Radar threats only reveal HMD marker reticles once TrackingQuality exceeds threshold (default: 0.30 = 30%).
                // If intelligence is insufficient (Q < 0.30), marker is suppressed, forcing pilot to rely on cockpit RWR instruments.
                float hmdThreshold = (RadioWarsConfig.HMDReconnaissanceQualityThreshold != null)
                    ? RadioWarsConfig.HMDReconnaissanceQualityThreshold.Value
                    : 0.30f;

                float trackingQuality = (track != null) ? track.TrackingQuality : 0.0f;
                bool hideIcons = RadioWarsConfig.HideUntriangulatedRadarIcons == null || RadioWarsConfig.HideUntriangulatedRadarIcons.Value;

                if (hideIcons && trackingQuality < hmdThreshold)
                {
                    CombatHUDPatches.SuppressMarker(__instance);
                    return;
                }

                // 1. Actively detected target (direct radar track, recent sweep <6s, or datalink)
                if (track != null && track.IsActivelyDetected)
                {
                    CombatHUDPatches.SetMarkerVisible(__instance);
                    if (__instance.outdated)
                    {
                        CombatHUDPatches.SetMarkerOutdated(__instance, false);
                    }
                    if (playerAircraft != null) ApplyTrackQualityColor(__instance, playerAircraft, track);
                    return;
                }

                // 2. Lost contact memory state (120-second retention window)
                if (track != null && track.IsInMemoryState(memDuration))
                {
                    CombatHUDPatches.SetMarkerVisible(__instance);
                    if (!__instance.outdated)
                    {
                        CombatHUDPatches.SetMarkerOutdated(__instance, true);
                    }
                    if (playerAircraft != null) ApplyTrackQualityColor(__instance, playerAircraft, track);
                    return;
                }

                // 3. Never actively detected or memory expired (>120s): suppress marker completely
                if (hideIcons)
                {
                    CombatHUDPatches.SuppressMarker(__instance);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] HUDUnitMarker_UpdatePosition_Patch error: " + ex);
            }
        }

        private static void ApplyTrackQualityColor(HUDUnitMarker marker, Aircraft playerAircraft, TriangulationTrack track = null)
        {
            if (marker == null || marker.image == null || marker.unit == null || playerAircraft == null) return;
            if (RadioWarsConfig.EnableHMDTrackQualityColoring == null || !RadioWarsConfig.EnableHMDTrackQualityColoring.Value) return;

            if (track == null)
            {
                track = RWRTriangulationProcessor.GetTrack(playerAircraft.NetworkHQ, marker.unit);
            }
            if (track == null) return;

            float q = Mathf.Clamp01(track.TrackingQuality);
            // Ensure minimum alpha floor (0.75) so outdated/memory reticles stay crisp and luminous over terrain
            float a = Mathf.Max(0.75f, marker.image.color.a);

            // Dual-phase gradient: Pale Luminous White (0.0) -> Vivid Amber (0.5) -> Vivid Crimson Red (1.0)
            Color lowColor = new Color(0.95f, 0.95f, 0.98f, a);
            Color midColor = new Color(1.00f, 0.80f, 0.15f, a);
            Color highColor = new Color(1.00f, 0.25f, 0.25f, a);

            Color targetColor = (q < 0.5f)
                ? Color.Lerp(lowColor, midColor, q * 2.0f)
                : Color.Lerp(midColor, highColor, (q - 0.5f) * 2.0f);

            marker.image.color = targetColor;
        }
    }

    /// <summary>
    /// Intercepts CombatHUD target designation to suppress target information text (code + distance)
    /// for untriangulated threats. Actively detected targets and memory contacts rely on native vanilla
    /// ShowTargetInfo layout with authentic 3-newline spacing and native '?' outdated sprite symbology.
    /// </summary>
    [HarmonyPatch(typeof(CombatHUD), "ShowTargetInfo")]
    public static class CombatHUD_ShowTargetInfo_Patch
    {
        private static readonly Func<CombatHUD, TextMeshProUGUI> s_getTargetInfo =
            FastReflection.CreateFieldGetter<CombatHUD, TextMeshProUGUI>("targetInfo");

        [HarmonyPrefix]
        public static bool Prefix(CombatHUD __instance, ref bool __result)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive ||
                    RadioWarsConfig.EnableRwrTriangulationSystem == null ||
                    !RadioWarsConfig.EnableRwrTriangulationSystem.Value)
                {
                    return true;
                }

                List<Unit> targetList = __instance.GetTargetList();
                if (targetList == null || targetList.Count == 0) return true;
                Unit target = targetList[0];
                if (target == null || __instance.aircraft == null || target is Missile) return true;

                FactionHQ playerHq = (__instance.aircraft != null && __instance.aircraft.NetworkHQ != null)
                    ? __instance.aircraft.NetworkHQ
                    : RWRTriangulationProcessor.GetPlayerFactionHQ();

                if (!CombatHUDPatches.IsTrackableThreat(target, playerHq)) return true;

                // Visual or active radar track check
                float distToPlayer = Vector3.Distance(target.transform.position, __instance.aircraft.transform.position);
                if (distToPlayer < CombatHUDPatches.VisualIdentificationRange) return true;

                Radar playerRadar = CombatHUDPatches.GetCachedPlayerRadar(__instance.aircraft);
                if (playerRadar != null && playerRadar.IsOperational())
                {
                    if (playerRadar.CheckIsTarget(target) || (playerRadar.detectedTargets != null && playerRadar.detectedTargets.Contains(target)))
                    {
                        return true;
                    }
                }

                TriangulationTrack track = RWRTriangulationProcessor.GetTrack(playerHq, target);

                float memDuration = RWRTriangulationProcessor.GetTargetMemoryDuration(target);

                float hmdThreshold = (RadioWarsConfig.HMDReconnaissanceQualityThreshold != null)
                    ? RadioWarsConfig.HMDReconnaissanceQualityThreshold.Value
                    : 0.30f;

                if (track != null && track.TrackingQuality >= hmdThreshold && (track.IsActivelyDetected || track.IsInMemoryState(memDuration)))
                {
                    return true;
                }

                // Untracked threat - force deselect and suppress target info text
                __instance.DeSelectUnit(target);
                TextMeshProUGUI targetInfoText = (s_getTargetInfo != null) ? s_getTargetInfo(__instance) : null;
                if (targetInfoText != null)
                {
                    targetInfoText.enabled = false;
                }
                __result = false;
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] CombatHUD_ShowTargetInfo_Patch Prefix error: " + ex);
                return true;
            }
        }
    }

    /// <summary>
    /// Blocks selecting or locking low-intelligence RWR threats via CombatHUD.SelectUnit.
    /// In unified mode, actively detected targets and targets in the memory state can be selected once Q >= threshold (default: 0.30).
    /// </summary>
    [HarmonyPatch(typeof(CombatHUD), "SelectUnit", new Type[] { typeof(Unit) })]
    public static class CombatHUD_SelectUnit_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(CombatHUD __instance, Unit unit)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive ||
                    RadioWarsConfig.EnableRwrTriangulationSystem == null ||
                    !RadioWarsConfig.EnableRwrTriangulationSystem.Value)
                {
                    return true;
                }

                if (unit == null || __instance.aircraft == null || unit is Missile) return true;

                FactionHQ playerHq = (__instance.aircraft != null && __instance.aircraft.NetworkHQ != null)
                    ? __instance.aircraft.NetworkHQ
                    : RWRTriangulationProcessor.GetPlayerFactionHQ();

                if (!CombatHUDPatches.IsTrackableThreat(unit, playerHq)) return true;

                // Visual or active radar check
                float distToPlayer = Vector3.Distance(unit.transform.position, __instance.aircraft.transform.position);
                if (distToPlayer < CombatHUDPatches.VisualIdentificationRange) return true;

                Radar playerRadar = CombatHUDPatches.GetCachedPlayerRadar(__instance.aircraft);
                if (playerRadar != null && playerRadar.IsOperational())
                {
                    if (playerRadar.CheckIsTarget(unit) || (playerRadar.detectedTargets != null && playerRadar.detectedTargets.Contains(unit)))
                    {
                        return true;
                    }
                }

                TriangulationTrack track = RWRTriangulationProcessor.GetTrack(playerHq, unit);

                float memDuration = RWRTriangulationProcessor.GetTargetMemoryDuration(unit);

                float hmdThreshold = (RadioWarsConfig.HMDReconnaissanceQualityThreshold != null)
                    ? RadioWarsConfig.HMDReconnaissanceQualityThreshold.Value
                    : 0.30f;

                if (track != null && track.TrackingQuality >= hmdThreshold && (track.IsActivelyDetected || track.IsInMemoryState(memDuration)))
                {
                    return true;
                }

                // Reject selecting untracked threat
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] CombatHUD_SelectUnit_Patch Prefix error: " + ex);
                return true;
            }
        }
    }

    /// <summary>
    /// Backward-compatibility alias for CombatHUDPatches.
    /// </summary>
    public static class CombatHudPatches
    {
        public static float VisualIdentificationRange { get { return CombatHUDPatches.VisualIdentificationRange; } }
        public static bool IsRadarThreatUnit(Unit unit) { return CombatHUDPatches.IsRadarThreatUnit(unit); }
        public static void SetMarkerVisible(HUDUnitMarker marker) { CombatHUDPatches.SetMarkerVisible(marker); }
        public static void SetMarkerOutdated(HUDUnitMarker marker, bool outdated) { CombatHUDPatches.SetMarkerOutdated(marker, outdated); }
        public static void SuppressMarker(HUDUnitMarker marker) { CombatHUDPatches.SuppressMarker(marker); }
    }
}
