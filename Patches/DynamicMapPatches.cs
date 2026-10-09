using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using RadioWars.Core;
using RadioWars.Config;
using RadioWars.Components;

namespace RadioWars.Patches
{
    public class RealisticRWRMapStrobe
    {
        public Image VectorImage;
        public Image UncertaintyCircleImage;
        public Unit Emitter;
        public Vector3 WorldDirection;
        public RwrThreatState State;
        public float PingTime;
        public float Duration;
    }

    [HarmonyPatch(typeof(DynamicMap))]
    public static class DynamicMapPatches
    {
        private static readonly Func<DynamicMap, GameObject> s_getRadarVisPrefab =
            FastReflection.CreateFieldGetter<DynamicMap, GameObject>("radarVisPrefab");
        private static readonly List<RealisticRWRMapStrobe> _activeStrobes = new List<RealisticRWRMapStrobe>();
        private static readonly List<RealisticRWRMapStrobe> _staleStrobes = new List<RealisticRWRMapStrobe>();

        [HarmonyPatch("ShowRadarPing")]
        [HarmonyPrefix]
        public static bool ShowRadarPing_Prefix(DynamicMap __instance, Aircraft.OnRadarWarning source)
        {
            bool isMaximized = DynamicMap.mapMaximized;
            bool enableStrobes = isMaximized
                ? (RadioWarsConfig.EnableRealisticRWRMapStrobes != null ? RadioWarsConfig.EnableRealisticRWRMapStrobes.Value : true)
                : (RadioWarsConfig.EnableMinimapRWRStrobes != null ? RadioWarsConfig.EnableMinimapRWRStrobes.Value : true);

            if (!RadioWarsConfig.IsModActive || !enableStrobes)
            {
                return true; // Let vanilla omniscient line execute
            }

            if (__instance == null || source.emitter == null) return false;

            Aircraft playerAircraft = CombatHUD.i != null ? CombatHUD.i.aircraft : null;
            if (playerAircraft == null || playerAircraft.disabled) return false;

            PhysRWRReceiver rwr = PhysRWRReceiver.Get(playerAircraft);
            Vector3 worldDir;
            RwrThreatState threatState = RwrThreatState.Search;

            if (rwr != null)
            {
                RwrThreatEntry threatEntry = null;
                foreach (var t in rwr.ActiveThreats)
                {
                    if (t.EmitterUnit == source.emitter)
                    {
                        threatEntry = t;
                        break;
                    }
                }

                if (threatEntry != null)
                {
                    worldDir = threatEntry.LastDirection;
                    threatState = threatEntry.State;
                }
                else
                {
                    Vector3 toEmitter = source.emitter.transform.position - playerAircraft.transform.position;
                    float jitterRange = RadioWarsConfig.RWRJitterDegrees != null ? RadioWarsConfig.RWRJitterDegrees.Value : 3.0f;
                    Quaternion errorJitter = Quaternion.Euler(0.0f, UnityEngine.Random.Range(-jitterRange, jitterRange), 0.0f);
                    worldDir = (errorJitter * toEmitter).normalized;
                    threatState = (source.emitter is Missile) ? RwrThreatState.MissileGuidance : (source.isTarget ? RwrThreatState.Track : RwrThreatState.Search);
                }
            }
            else
            {
                Vector3 toEmitter = source.emitter.transform.position - playerAircraft.transform.position;
                worldDir = toEmitter.normalized;
                threatState = (source.emitter is Missile) ? RwrThreatState.MissileGuidance : (source.isTarget ? RwrThreatState.Track : RwrThreatState.Search);
            }

            // Look for existing active strobe for this emitter to refresh it
            RealisticRWRMapStrobe existing = null;
            for (int i = 0; i < _activeStrobes.Count; i++)
            {
                if (_activeStrobes[i].Emitter == source.emitter)
                {
                    existing = _activeStrobes[i];
                    break;
                }
            }

            float now = Time.timeSinceLevelLoad;
            float persistence = RadioWarsConfig.RWRStrobePersistenceSeconds != null ? RadioWarsConfig.RWRStrobePersistenceSeconds.Value : 3.0f;

            if (existing != null && existing.VectorImage != null)
            {
                existing.PingTime = now;
                existing.WorldDirection = worldDir;
                existing.State = threatState;
                existing.Duration = persistence;
            }
            else
            {
                GameObject prefab = (s_getRadarVisPrefab != null) ? s_getRadarVisPrefab(__instance) : null;
                if (prefab == null || __instance.iconLayer == null) return false;

                GameObject go = UnityEngine.Object.Instantiate(prefab, __instance.iconLayer.transform);
                Image img = go.GetComponent<Image>();
                if (img == null) return false;

                // Instantiate companion procedural ESM Uncertainty Circle
                Image circleImg = null;
                GameObject circleGo = UnityEngine.Object.Instantiate(prefab, __instance.iconLayer.transform);
                circleGo.name = "RWR_UncertaintyCircle";
                circleImg = circleGo.GetComponent<Image>();
                if (circleImg != null)
                {
                    circleImg.sprite = RwrConeGraphic.GetOrCreateUncertaintyCircleSprite();
                    circleImg.type = Image.Type.Simple;
                    circleImg.preserveAspect = true;
                    circleImg.raycastTarget = false;
                    RectTransform cRt = circleGo.GetComponent<RectTransform>();
                    if (cRt != null)
                    {
                        cRt.anchorMin = new Vector2(0.5f, 0.5f);
                        cRt.anchorMax = new Vector2(0.5f, 0.5f);
                        cRt.pivot = new Vector2(0.5f, 0.5f); // Center pivot for area display
                    }
                    circleGo.SetActive(false);
                }

                RealisticRWRMapStrobe strobe = new RealisticRWRMapStrobe
                {
                    VectorImage = img,
                    UncertaintyCircleImage = circleImg,
                    Emitter = source.emitter,
                    WorldDirection = worldDir,
                    State = threatState,
                    PingTime = now,
                    Duration = persistence
                };
                _activeStrobes.Add(strobe);
            }

            // Immediately register/refresh ESM triangulation track for this emitter
            if (RadioWarsConfig.EnableRwrTriangulationSystem == null || RadioWarsConfig.EnableRwrTriangulationSystem.Value)
            {
                RwrTriangulationProcessor.UpdateTrack(source.emitter, playerAircraft, worldDir, threatState, 0.5f);
            }

            return false; // Skip vanilla omniscient ShowRadarPing
        }

        [HarmonyPatch("UpdateMap")]
        [HarmonyPostfix]
        public static void UpdateMap_Postfix(DynamicMap __instance)
        {
            try
            {
                if (__instance != null)
                {
                    MissileMapDispersionVisualizer.UpdateVisuals(__instance);
                }

                bool isMaximized = DynamicMap.mapMaximized;
                bool enableStrobes = isMaximized
                    ? (RadioWarsConfig.EnableRealisticRWRMapStrobes != null ? RadioWarsConfig.EnableRealisticRWRMapStrobes.Value : true)
                    : (RadioWarsConfig.EnableMinimapRWRStrobes != null ? RadioWarsConfig.EnableMinimapRWRStrobes.Value : true);

                if (!RadioWarsConfig.IsModActive || !enableStrobes)
                {
                    ClearStrobeVisuals();
                    return;
                }

            if (__instance == null || _activeStrobes.Count == 0) return;

            Aircraft playerAircraft = CombatHUD.i != null ? CombatHUD.i.aircraft : null;
            if (playerAircraft == null || playerAircraft.disabled)
            {
                ClearStrobeVisuals();
                return;
            }

            // Get player aircraft icon position on the map
            UnitMapIcon playerIcon = null;
            DynamicMap.TryGetMapIcon(playerAircraft, out playerIcon);
            Transform playerIconTrans = (playerIcon != null) ? playerIcon.transform : null;
            Transform playerAnchorTrans = (playerIcon != null && playerIcon.iconImage != null)
                ? playerIcon.iconImage.transform
                : playerIconTrans;

            Vector3 playerFallbackLocalPos = Vector3.zero;
            if (playerAnchorTrans == null)
            {
                Vector3 playerGlobal = playerAircraft.GlobalPosition().AsVector3();
                playerFallbackLocalPos = new Vector3(playerGlobal.x * __instance.mapDisplayFactor, playerGlobal.z * __instance.mapDisplayFactor, 0f);
            }

            float now = Time.timeSinceLevelLoad;
            _staleStrobes.Clear();

            float layerScale = (__instance.iconLayer != null && __instance.iconLayer.transform.lossyScale.y > 0.0001f)
                ? __instance.iconLayer.transform.lossyScale.y
                : 1.0f;

            // Current map rotation around Z (0 when maximized; aircraft yaw heading when minimized / track-up minimap)
            float mapZ = 0f;
            if (__instance.mapImage != null)
            {
                mapZ = __instance.mapImage.transform.eulerAngles.z;
            }

            PhysRWRReceiver rwr = PhysRWRReceiver.Get(playerAircraft);

            // Synchronize Allied Tactical Datalink Strobes (Only when visual datalink strobes are enabled)
            bool showAllDl = RadioWarsConfig.EnableDatalinkVisualStrobes != null && RadioWarsConfig.EnableDatalinkVisualStrobes.Value;
            if (showAllDl && rwr != null && rwr.ActiveDatalinkThreats != null)
            {
                foreach (var dl in rwr.ActiveDatalinkThreats)
                {
                    if (dl.ThreatUnit == null || dl.ThreatUnit.disabled) continue;

                    RealisticRWRMapStrobe existing = null;
                    for (int j = 0; j < _activeStrobes.Count; j++)
                    {
                        if (_activeStrobes[j].Emitter == dl.ThreatUnit)
                        {
                            existing = _activeStrobes[j];
                            break;
                        }
                    }

                    if (existing == null)
                    {
                        GameObject prefab = (s_getRadarVisPrefab != null) ? s_getRadarVisPrefab(__instance) : null;
                        if (prefab != null && __instance.iconLayer != null)
                        {
                            GameObject go = UnityEngine.Object.Instantiate(prefab, __instance.iconLayer.transform);
                            Image img = go.GetComponent<Image>();

                            Image circleImg = null;
                            GameObject circleGo = UnityEngine.Object.Instantiate(prefab, __instance.iconLayer.transform);
                            circleGo.name = "RWR_UncertaintyCircle";
                            circleImg = circleGo.GetComponent<Image>();
                            if (circleImg != null)
                            {
                                circleImg.sprite = RwrConeGraphic.GetOrCreateUncertaintyCircleSprite();
                                circleImg.type = Image.Type.Simple;
                                circleImg.preserveAspect = true;
                                circleImg.raycastTarget = false;
                                RectTransform cRt = circleGo.GetComponent<RectTransform>();
                                if (cRt != null)
                                {
                                    cRt.anchorMin = new Vector2(0.5f, 0.5f);
                                    cRt.anchorMax = new Vector2(0.5f, 0.5f);
                                    cRt.pivot = new Vector2(0.5f, 0.5f);
                                }
                                circleGo.SetActive(false);
                            }

                            if (img != null)
                            {
                                existing = new RealisticRWRMapStrobe
                                {
                                    VectorImage = img,
                                    UncertaintyCircleImage = circleImg,
                                    Emitter = dl.ThreatUnit,
                                    WorldDirection = dl.LastDirection,
                                    State = RwrThreatState.Search,
                                    PingTime = now,
                                    Duration = 3.5f
                                };
                                _activeStrobes.Add(existing);
                            }
                        }
                    }
                    else
                    {
                        existing.PingTime = now;
                        existing.WorldDirection = dl.LastDirection;
                        existing.State = RwrThreatState.Search;
                        existing.Duration = 3.5f;
                    }
                }
            }

            bool enableTriangulation = RadioWarsConfig.EnableRwrTriangulationSystem == null || RadioWarsConfig.EnableRwrTriangulationSystem.Value;

            for (int i = 0; i < _activeStrobes.Count; i++)
            {
                RealisticRWRMapStrobe strobe = _activeStrobes[i];
                if (strobe.VectorImage == null)
                {
                    _staleStrobes.Add(strobe);
                    continue;
                }

                float age = now - strobe.PingTime;
                if (age >= strobe.Duration || strobe.Emitter == null || strobe.Emitter.disabled)
                {
                    _staleStrobes.Add(strobe);
                    continue;
                }

                Transform imgTrans = strobe.VectorImage.transform;
                RectTransform imgRt = strobe.VectorImage.rectTransform;

                // 1. Fetch fresh threat state & direction from PhysRWRReceiver if available
                float sigStrength = 0.5f;
                if (rwr != null && strobe.Emitter != null)
                {
                    foreach (var t in rwr.ActiveThreats)
                    {
                        if (t.EmitterUnit == strobe.Emitter)
                        {
                            strobe.State = t.State;
                            strobe.WorldDirection = t.LastDirection;
                            sigStrength = t.SignalStrength01;
                            break;
                        }
                    }
                }

                bool useCones = RadioWarsConfig.EnableRWRBearingCone == null || RadioWarsConfig.EnableRWRBearingCone.Value;
                bool originateFromOwnship = RadioWarsConfig.MapStrobesOriginateFromOwnship == null || RadioWarsConfig.MapStrobesOriginateFromOwnship.Value;

                if (useCones && originateFromOwnship)
                {
                    // 2. Anchor strobe cone directly at player's aircraft (RWR receiving platform)
                    if (playerAnchorTrans != null)
                    {
                        imgTrans.position = playerAnchorTrans.position;
                    }
                    else
                    {
                        imgTrans.localPosition = playerFallbackLocalPos;
                    }

                    // 3. Configure procedural phosphor cone sprite, pivot at receiver apex
                    if (imgRt != null)
                    {
                        imgRt.anchorMin = new Vector2(0.5f, 0.5f);
                        imgRt.anchorMax = new Vector2(0.5f, 0.5f);
                        imgRt.pivot = new Vector2(0.5f, 0.0f);
                    }
                    strobe.VectorImage.sprite = RwrConeGraphic.GetOrCreateConeSprite();

                    RwrTier tier = (rwr != null) ? rwr.Capabilities.Tier : RwrTier.Tier2_Standard;
                    Vector3 pPos = playerAircraft.transform.position;
                    Vector3 ePos = (strobe.Emitter != null && !strobe.Emitter.disabled)
                        ? strobe.Emitter.transform.position
                        : (pPos + strobe.WorldDirection * 15000.0f);

                    // Update ESM Triangulation & Ambiguity Zone
                    TriangulationTrack track = null;
                    if (enableTriangulation && strobe.Emitter != null && !strobe.Emitter.disabled)
                    {
                        RwrTriangulationProcessor.UpdateTrack(strobe.Emitter, playerAircraft, strobe.WorldDirection, strobe.State, sigStrength);
                        track = RwrTriangulationProcessor.GetTrack(strobe.Emitter);
                    }

                    float coneWorldLength;
                    float halfAngle;

                    if (enableTriangulation && track != null)
                    {
                        // Sleek narrow wedge strobe aperture
                        halfAngle = RwrConeGraphic.GetNarrowWedgeHalfAngle(tier);

                        if (track.IsTriangulated)
                        {
                            // Target is triangulated: hide circle, strobe connects directly to displaced icon
                            if (strobe.UncertaintyCircleImage != null && strobe.UncertaintyCircleImage.gameObject.activeSelf)
                            {
                                strobe.UncertaintyCircleImage.gameObject.SetActive(false);
                            }

                            Vector3 toTarget = track.TriangulatedWorldPos - pPos;
                            coneWorldLength = toTarget.magnitude;

                            Vector3 dir = toTarget.sqrMagnitude > 1.0f ? toTarget.normalized : strobe.WorldDirection;
                            float baseAngle = -Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                            float finalAngle = baseAngle + mapZ;
                            imgTrans.eulerAngles = new Vector3(0f, 0f, finalAngle);
                        }
                        else
                        {
                            // Target is untriangulated: display ambiguity circle
                            if (strobe.UncertaintyCircleImage != null)
                            {
                                if (!strobe.UncertaintyCircleImage.gameObject.activeSelf)
                                {
                                    strobe.UncertaintyCircleImage.gameObject.SetActive(true);
                                }

                                Vector3 circleGlobal = GlobalPositionExtensions.ToGlobalPosition(track.EstimatedCenterWorldPos).AsVector3();
                                Vector3 circleMapPos = circleGlobal * __instance.mapDisplayFactor;
                                strobe.UncertaintyCircleImage.transform.localPosition = new Vector3(circleMapPos.x, circleMapPos.z, 0f);

                                float circleDiamScreen = 2.0f * track.UncertaintyRadiusMeters * __instance.mapDisplayFactor * layerScale;
                                float minDiam = DynamicMap.mapMaximized ? 24.0f : 16.0f;
                                float maxDiam = DynamicMap.mapMaximized ? 3500.0f : 2000.0f;
                                float clampedDiamScreen = Mathf.Clamp(circleDiamScreen, minDiam, maxDiam);

                                float circleDiam = clampedDiamScreen / layerScale;
                                strobe.UncertaintyCircleImage.rectTransform.sizeDelta = new Vector2(circleDiam, circleDiam);
                                strobe.UncertaintyCircleImage.transform.localScale = Vector3.one;
                            }

                            Vector3 toCenter = track.EstimatedCenterWorldPos - pPos;
                            // The wide end of the cone abuts directly into the uncertainty circle zone
                            coneWorldLength = Mathf.Max(toCenter.magnitude - track.UncertaintyRadiusMeters * 0.35f, 1000.0f);

                            Vector3 dir = toCenter.sqrMagnitude > 1.0f ? toCenter.normalized : strobe.WorldDirection;
                            float baseAngle = -Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                            float finalAngle = baseAngle + mapZ;
                            imgTrans.eulerAngles = new Vector3(0f, 0f, finalAngle);
                        }
                    }
                    else
                    {
                        // Fallback: Legacy sector cone
                        if (strobe.UncertaintyCircleImage != null && strobe.UncertaintyCircleImage.gameObject.activeSelf)
                        {
                            strobe.UncertaintyCircleImage.gameObject.SetActive(false);
                        }

                        Vector3 dir = strobe.WorldDirection;
                        float baseAngle = -Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                        float finalAngle = baseAngle + mapZ;
                        imgTrans.eulerAngles = new Vector3(0f, 0f, finalAngle);

                        halfAngle = RwrConeGraphic.GetMapConeHalfAngle(strobe.State, tier);

                        float worldDist = Vector3.Distance(pPos, ePos);
                        float distRatio = isMaximized
                            ? (RadioWarsConfig.RWRThreatDistanceRatio != null
                                ? Mathf.Clamp(RadioWarsConfig.RWRThreatDistanceRatio.Value, 0.05f, 2.0f)
                                : 0.85f)
                            : (RadioWarsConfig.MinimapThreatDistanceRatio != null
                                ? Mathf.Clamp(RadioWarsConfig.MinimapThreatDistanceRatio.Value, 0.05f, 2.0f)
                                : 0.85f);
                        coneWorldLength = worldDist * distRatio;
                    }

                    // Convert physical distance to map coordinate space and screen pixels
                    float mapLength = coneWorldLength * __instance.mapDisplayFactor;
                    float screenLength = mapLength * layerScale;

                    // Safeguards: ensure minimum visibility at point-blank range,
                    // and cap extreme pixel coordinates under massive close-up zoom.
                    float minScreenLen = DynamicMap.mapMaximized ? 22.0f : 16.0f;
                    float maxScreenLen = DynamicMap.mapMaximized ? 3500.0f : 1500.0f;
                    float clampedScreenLength = Mathf.Clamp(screenLength, minScreenLen, maxScreenLen);

                    float coneLength = clampedScreenLength / layerScale;
                    float coneWidth = RwrConeGraphic.GetConeBaseWidth(coneLength, halfAngle);

                    if (imgRt != null)
                    {
                        imgRt.sizeDelta = new Vector2(coneWidth, coneLength);
                    }
                    imgTrans.localScale = Vector3.one;
                }
                else
                {
                    // Fallback: Legacy omniscient line from emitter to player
                    if (strobe.UncertaintyCircleImage != null && strobe.UncertaintyCircleImage.gameObject.activeSelf)
                    {
                        strobe.UncertaintyCircleImage.gameObject.SetActive(false);
                    }

                    UnitMapIcon emitterIcon = null;
                    Vector3 emitterPos = strobe.Emitter.GlobalPosition().AsVector3() * __instance.mapDisplayFactor;

                    if (DynamicMap.TryGetMapIcon(strobe.Emitter, out emitterIcon) && emitterIcon != null)
                    {
                        imgTrans.position = emitterIcon.transform.position;
                    }
                    else
                    {
                        imgTrans.localPosition = new Vector3(emitterPos.x, emitterPos.z, 0f);
                    }

                    if (playerAnchorTrans != null)
                    {
                        Vector3 toPlayer = playerAnchorTrans.position - imgTrans.position;
                        float angle = -Mathf.Atan2(toPlayer.x, toPlayer.y) * Mathf.Rad2Deg;
                        imgTrans.eulerAngles = new Vector3(0f, 0f, angle);
                        imgTrans.localScale = new Vector3(1.2f, toPlayer.magnitude, 1.0f) / layerScale;
                    }
                    else
                    {
                        Vector3 playerWorldPos = playerAircraft.GlobalPosition().AsVector3() * __instance.mapDisplayFactor;
                        Vector3 delta = new Vector3(playerWorldPos.x - emitterPos.x, playerWorldPos.z - emitterPos.z, 0f);
                        float angle = -Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg;
                        imgTrans.localEulerAngles = new Vector3(0f, 0f, angle);
                        imgTrans.localScale = new Vector3(1.2f, delta.magnitude, 1.0f);
                    }
                }

                // 5. Color and opacity modulation based on threat state or Datalink
                bool isDatalink = false;
                if (rwr != null && strobe.Emitter != null)
                {
                    foreach (var dl in rwr.ActiveDatalinkThreats)
                    {
                        if (dl.ThreatUnit == strobe.Emitter)
                        {
                            isDatalink = true;
                            break;
                        }
                    }
                }

                float normLife = Mathf.Clamp01(1.0f - (age / strobe.Duration));
                Color c;

                float searchAlpha = RadioWarsConfig.RWRSearchConeOpacity != null ? Mathf.Clamp(RadioWarsConfig.RWRSearchConeOpacity.Value, 0.01f, 1.0f) : 0.10f;
                float trackAlpha = RadioWarsConfig.RWRTrackConeOpacity != null ? Mathf.Clamp(RadioWarsConfig.RWRTrackConeOpacity.Value, 0.01f, 1.0f) : 0.40f;
                float missileAlpha = RadioWarsConfig.RWRMissileConeOpacity != null ? Mathf.Clamp(RadioWarsConfig.RWRMissileConeOpacity.Value, 0.01f, 1.0f) : 0.75f;
                float dlAlpha = RadioWarsConfig.RWRDatalinkConeOpacity != null ? Mathf.Clamp(RadioWarsConfig.RWRDatalinkConeOpacity.Value, 0.01f, 1.0f) : 0.35f;
                float zoneAlpha = RadioWarsConfig.RWRUncertaintyZoneOpacity != null ? Mathf.Clamp(RadioWarsConfig.RWRUncertaintyZoneOpacity.Value, 0.01f, 1.0f) : 0.10f;

                if (isDatalink)
                {
                    bool dlFlash = (Mathf.Sin(Time.time * 20.0f) > 0.0f);
                    c = dlFlash
                        ? new Color(0.2f, 0.85f, 1.0f, dlAlpha)
                        : new Color(0.1f, 0.5f, 0.8f, dlAlpha * 0.5f * normLife);
                }
                else
                {
                    switch (strobe.State)
                    {
                        case RwrThreatState.MissileGuidance:
                            bool mFlash = (Mathf.Sin(Time.time * 26.0f) > 0.0f);
                            c = mFlash
                                ? new Color(1.0f, 0.15f, 0.15f, missileAlpha)
                                : new Color(0.85f, 0.08f, 0.08f, missileAlpha * 0.5f * normLife);
                            break;

                        case RwrThreatState.Track:
                            c = new Color(1.0f, 0.55f, 0.0f, trackAlpha * Mathf.Lerp(0.40f, 1.0f, normLife));
                            break;

                        case RwrThreatState.Search:
                        default:
                            c = new Color(1.0f, 0.85f, 0.15f, searchAlpha * normLife);
                            break;
                    }
                }

                strobe.VectorImage.color = c;
                if (strobe.UncertaintyCircleImage != null && strobe.UncertaintyCircleImage.gameObject.activeSelf)
                {
                    Color circleColor = new Color(c.r, c.g, c.b, zoneAlpha * Mathf.Lerp(0.5f, 1.0f, normLife));
                    strobe.UncertaintyCircleImage.color = circleColor;
                }
            }

            if (_staleStrobes.Count > 0)
            {
                for (int i = 0; i < _staleStrobes.Count; i++)
                {
                    RealisticRWRMapStrobe s = _staleStrobes[i];
                    if (s.VectorImage != null)
                    {
                        UnityEngine.Object.Destroy(s.VectorImage.gameObject);
                    }
                    if (s.UncertaintyCircleImage != null)
                    {
                        UnityEngine.Object.Destroy(s.UncertaintyCircleImage.gameObject);
                    }
                    _activeStrobes.Remove(s);
                }
            }

            if (enableTriangulation)
            {
                float memDuration = (RadioWarsConfig.TargetMemoryDurationSeconds != null)
                    ? RadioWarsConfig.TargetMemoryDurationSeconds.Value
                    : 120.0f;
                RwrTriangulationProcessor.PruneStaleTracks(memDuration);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("[RadioWars] UpdateMap_Postfix error: " + ex);
        }
    }

        [HarmonyPatch("ClearMap")]
        [HarmonyPostfix]
        public static void ClearMap_Postfix()
        {
            ClearStrobeVisuals();
        }

        [HarmonyPatch("OnDestroy")]
        [HarmonyPostfix]
        public static void OnDestroy_Postfix()
        {
            ClearStrobeVisuals();
        }

        private static void ClearStrobeVisuals()
        {
            MissileMapDispersionVisualizer.ClearAll();

            for (int i = 0; i < _activeStrobes.Count; i++)
            {
                if (_activeStrobes[i].VectorImage != null)
                {
                    UnityEngine.Object.Destroy(_activeStrobes[i].VectorImage.gameObject);
                }
                if (_activeStrobes[i].UncertaintyCircleImage != null)
                {
                    UnityEngine.Object.Destroy(_activeStrobes[i].UncertaintyCircleImage.gameObject);
                }
            }
            _activeStrobes.Clear();
            _staleStrobes.Clear();
        }

        public static void ClearAll()
        {
            ClearStrobeVisuals();
        }

        private static void ClearAllStrobes()
        {
            ClearStrobeVisuals();
        }
    }

    /// <summary>
    /// Patches UnitMapIcon to enforce authentic ESM operational secrecy:
    /// 1. Suppresses pinpoint red enemy radar icons until resolved via Datalink or kinematic triangulation.
    /// 2. Spatially displaces triangulated icons proportionally to physical distance (bearing measurement error).
    /// 3. Instantly restores vanilla coordinates and visibility upon F10 toggle or within 2.5km visual range.
    /// </summary>
    [HarmonyPatch(typeof(UnitMapIcon), "UpdateIcon", new Type[] { typeof(float), typeof(float), typeof(Transform), typeof(bool) })]
    public static class UnitMapIcon_UpdateIcon_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(UnitMapIcon __instance, float mapDisplayFactor, float mapInverseScale, Transform mapTransform, bool mapMaximized)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive ||
                    RadioWarsConfig.EnableRwrTriangulationSystem == null ||
                    !RadioWarsConfig.EnableRwrTriangulationSystem.Value)
                {
                    if (__instance.iconImage != null && !__instance.iconImage.enabled)
                    {
                        __instance.iconImage.enabled = true;
                    }
                    return;
                }

                if (__instance == null || __instance.unit == null || __instance.unit is Missile) return;

                FactionHQ playerHq = RWRTriangulationProcessor.GetPlayerFactionHQ();
                Aircraft playerAircraft = CombatHUD.i != null ? CombatHUD.i.aircraft : null;

                // Friendly and neutral units, simple wheeled vehicles, hangars, and obstacles are 100% vanilla
                if (!CombatHUDPatches.IsTrackableThreat(__instance.unit, playerHq))
                {
                    if (__instance.iconImage != null && !__instance.iconImage.enabled)
                    {
                        __instance.iconImage.enabled = true;
                    }
                    return;
                }

                // Close visual identification override: within direct visual range, unit is identified
                if (playerAircraft != null && !playerAircraft.disabled)
                {
                    float distToPlayer = Vector3.Distance(__instance.unit.transform.position, playerAircraft.transform.position);
                    if (distToPlayer < CombatHUDPatches.VisualIdentificationRange)
                    {
                        RWRTriangulationProcessor.RecordVisualContact(playerHq, __instance.unit, playerAircraft);
                        if (__instance.iconImage != null && !__instance.iconImage.enabled)
                        {
                            __instance.iconImage.enabled = true;
                        }
                        return;
                    }

                    // Direct active radar track reveals icon normally
                    Radar playerRadar = CombatHUDPatches.GetCachedPlayerRadar(playerAircraft);
                    if (playerRadar != null && playerRadar.IsOperational())
                    {
                        if (playerRadar.CheckIsTarget(__instance.unit) || (playerRadar.detectedTargets != null && playerRadar.detectedTargets.Contains(__instance.unit)))
                        {
                            if (__instance.iconImage != null && !__instance.iconImage.enabled)
                            {
                                __instance.iconImage.enabled = true;
                            }
                            return;
                        }
                    }
                }

                float memDuration = RWRTriangulationProcessor.GetTargetMemoryDuration(__instance.unit);

                // Layered Intelligence Filtering for Tactical Map & Minimap:
                // Exact vehicle/ship icons on the map are revealed strictly once TrackingQuality exceeds threshold (default: 0.75 = 75%).
                // Triangulation contributes +0.45 to Q, but never bypasses the Q threshold directly.
                // If intelligence is insufficient (Q < 0.75), pinpoint icons are suppressed, and the player relies on realistic RWR bearing strobes and ESM ambiguity circles.
                float mapThreshold = (RadioWarsConfig.MapReconnaissanceQualityThreshold != null)
                    ? RadioWarsConfig.MapReconnaissanceQualityThreshold.Value
                    : 0.75f;

                TriangulationTrack track = RWRTriangulationProcessor.GetTrack(playerHq, __instance.unit);
                float trackingQuality = (track != null) ? track.TrackingQuality : 0.0f;
                bool hideIcons = RadioWarsConfig.HideUntriangulatedRadarIcons == null || RadioWarsConfig.HideUntriangulatedRadarIcons.Value;

                if (hideIcons && trackingQuality < mapThreshold)
                {
                    if (__instance.iconImage != null && __instance.iconImage.enabled)
                    {
                        __instance.iconImage.enabled = false;
                    }
                    return;
                }

                // 1. Actively detected target (radar sweep <6s, dwell, or datalink):
                // Display at live physical position with normal orientation
                if (track != null && track.IsActivelyDetected)
                {
                    if (__instance.iconImage != null && !__instance.iconImage.enabled)
                    {
                        __instance.iconImage.enabled = true;
                    }
                    return;
                }

                // 2. Lost contact memory state (120-second retention window):
                // Display frozen at last known position, unoriented (rotation 0), dimmed opacity
                if (track != null && track.IsInMemoryState(memDuration))
                {
                    if (__instance.iconImage != null)
                    {
                        if (!__instance.iconImage.enabled)
                        {
                            __instance.iconImage.enabled = true;
                        }

                        Vector3 frozenGlobal = track.LastKnownGlobalPosition.AsVector3();
                        Vector3 frozenMapPos = frozenGlobal * mapDisplayFactor;
                        __instance.iconImage.transform.localPosition = new Vector3(frozenMapPos.x, frozenMapPos.z, 0f);
                        __instance.iconImage.transform.localRotation = Quaternion.identity;

                        Color col = __instance.iconImage.color;
                        col.a = 0.5f;
                        __instance.iconImage.color = col;
                    }
                    return;
                }

                // 3. Never actively detected or memory expired (>120s): suppress pinpoint icon completely
                if (hideIcons && __instance.iconImage != null && __instance.iconImage.enabled)
                {
                    __instance.iconImage.enabled = false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] UnitMapIcon_UpdateIcon_Patch error: " + ex);
            }
        }
    }
}
