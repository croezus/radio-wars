using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using RadioWars.Config;
using RadioWars.Core;
using RadioWars.Components;

namespace RadioWars.Patches
{
    /// <summary>
    /// Suppresses vanilla arcade notch visual aids and manages missile threat line rendering:
    /// 1. Suppresses the floating HUD notch flight director box ([ARH] / [SARH] guidance gate).
    /// 2. Suppresses the dashed notch evasion line drawn on the map / minimap.
    /// 3. Replaces the arcade 1-pixel rigid vector line to incoming ARH/SARH missiles with an authentic RWR threat cone:
    ///    - Anchored at ownship with vertex pivot (0.5, 0.0);
    ///    - Extends 85% of distance towards the missile (leaving a realistic 15% distance margin);
    ///    - Widens angularly into a sector aperture (governed by RWRMissileSectorAngle);
    ///    - Pulses urgent alert red during terminal Pitbull radar homing.
    /// 4. Completely suppresses the vector line for non-radar missiles (IR, Optical, Laser, INS),
    ///    retaining only the MAWS text alert and map marker without cheat lines.
    /// 5. Restores original vanilla visuals seamlessly when toggled via F10 or config.
    /// </summary>
    public static class NotchAidsPatches
    {
        internal class ThreatLineBackup : MonoBehaviour
        {
            public Sprite OriginalSprite;
            public Color OriginalColor;
            public bool OriginalRaycastTarget;
            public Vector2 OriginalPivot;
            public Vector2 OriginalAnchorMin;
            public Vector2 OriginalAnchorMax;
            public Vector2 OriginalSizeDelta;
            public Image Image;
            public RectTransform RectTransform;
            public bool IsModified;
        }

        private static readonly Func<ThreatItem, GameObject> s_getNotchIndicator = FastReflection.CreateFieldGetter<ThreatItem, GameObject>("notchIndicator");
        private static readonly Func<ThreatItem, GameObject> s_getNotchLine = FastReflection.CreateFieldGetter<ThreatItem, GameObject>("notchLine");
        private static readonly Func<ThreatItem, GameObject> s_getVectorLine = FastReflection.CreateFieldGetter<ThreatItem, GameObject>("vectorLine");
        private static readonly Func<ThreatItem, Missile> s_getMissile = FastReflection.CreateFieldGetter<ThreatItem, Missile>("missile");
        private static readonly Func<ThreatItem, Transform> s_getPlayerAircraftIconTransform = FastReflection.CreateFieldGetter<ThreatItem, Transform>("playerAircraftIconTransform");
        private static readonly Func<ThreatItem, Transform> s_getMissileIconTransform = FastReflection.CreateFieldGetter<ThreatItem, Transform>("missileIconTransform");

        public static GameObject GetNotchIndicator(ThreatItem item)
        {
            return (s_getNotchIndicator != null && item != null) ? s_getNotchIndicator(item) : null;
        }

        public static GameObject GetNotchLine(ThreatItem item)
        {
            return (s_getNotchLine != null && item != null) ? s_getNotchLine(item) : null;
        }

        public static GameObject GetVectorLine(ThreatItem item)
        {
            return (s_getVectorLine != null && item != null) ? s_getVectorLine(item) : null;
        }

        public static Missile GetMissile(ThreatItem item)
        {
            return (s_getMissile != null && item != null) ? s_getMissile(item) : null;
        }

        public static Transform GetPlayerAircraftIconTransform(ThreatItem item)
        {
            return (s_getPlayerAircraftIconTransform != null && item != null) ? s_getPlayerAircraftIconTransform(item) : null;
        }

        public static Transform GetMissileIconTransform(ThreatItem item)
        {
            return (s_getMissileIconTransform != null && item != null) ? s_getMissileIconTransform(item) : null;
        }

        private static bool ShouldSuppress
        {
            get
            {
                return RadioWarsConfig.IsModActive &&
                       RadioWarsConfig.DisableVanillaNotchAids != null &&
                       RadioWarsConfig.DisableVanillaNotchAids.Value;
            }
        }

        [HarmonyPatch(typeof(CombatHUD), "ShowNotchIndicator")]
        public static class CombatHUD_ShowNotchIndicator_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(ref GameObject __result)
            {
                if (ShouldSuppress && __result != null)
                {
                    __result.SetActive(false);
                }
            }
        }

        [HarmonyPatch(typeof(DynamicMap), "ShowNotchLine")]
        public static class DynamicMap_ShowNotchLine_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(ref GameObject __result)
            {
                if (ShouldSuppress && __result != null)
                {
                    __result.SetActive(false);
                }
            }
        }

        [HarmonyPatch(typeof(ThreatItem), "AlignNotchIndicator", new Type[] { typeof(Vector3) })]
        public static class ThreatItem_AlignNotchIndicator_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(ThreatItem __instance, GameObject ___notchIndicator)
            {
                if (ShouldSuppress)
                {
                    if (___notchIndicator != null && ___notchIndicator.activeSelf)
                    {
                        ___notchIndicator.SetActive(false);
                    }
                    return false; // Skip HUD notch box alignment and UI updates
                }
                else
                {
                    if (___notchIndicator != null && !___notchIndicator.activeSelf)
                    {
                        ___notchIndicator.SetActive(true);
                    }
                    return true;
                }
            }
        }

        [HarmonyPatch(typeof(ThreatItem), "AlignNotchLine", new Type[] { typeof(Vector3) })]
        public static class ThreatItem_AlignNotchLine_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(ThreatItem __instance, GameObject ___notchLine)
            {
                if (ShouldSuppress)
                {
                    if (___notchLine != null && ___notchLine.activeSelf)
                    {
                        ___notchLine.SetActive(false);
                    }
                    return false; // Skip map notch line alignment and rendering
                }
                else
                {
                    if (___notchLine != null && !___notchLine.activeSelf)
                    {
                        ___notchLine.SetActive(true);
                    }
                    return true;
                }
            }
        }

        [HarmonyPatch(typeof(ThreatItem), "AlignVectorLine")]
        public static class ThreatItem_AlignVectorLine_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(
                ThreatItem __instance,
                GameObject ___vectorLine,
                Missile ___missile,
                Transform ___playerAircraftIconTransform,
                Transform ___missileIconTransform)
            {
                // When mod is toggled off (F10), restore original vanilla visuals and allow original method to run
                if (!RadioWarsConfig.IsModActive)
                {
                    if (___vectorLine != null)
                    {
                        ThreatLineBackup b = ___vectorLine.GetComponent<ThreatLineBackup>();
                        if (b != null && b.IsModified)
                        {
                            Image img = b.Image != null ? b.Image : ___vectorLine.GetComponent<Image>();
                            RectTransform rt = b.RectTransform != null ? b.RectTransform : ___vectorLine.GetComponent<RectTransform>();
                            if (img != null)
                            {
                                img.sprite = b.OriginalSprite;
                                img.color = b.OriginalColor;
                                img.raycastTarget = b.OriginalRaycastTarget;
                            }
                            if (rt != null)
                            {
                                rt.pivot = b.OriginalPivot;
                                rt.anchorMin = b.OriginalAnchorMin;
                                rt.anchorMax = b.OriginalAnchorMax;
                                rt.sizeDelta = b.OriginalSizeDelta;
                            }
                            b.IsModified = false;
                        }
                    }
                    return true;
                }

                GameObject vLine = ___vectorLine;
                if (vLine == null) return false;

                ThreatLineBackup backup = vLine.GetComponent<ThreatLineBackup>();

                Missile missile = ___missile;
                if (missile == null || missile.disabled)
                {
                    if (vLine.activeSelf) vLine.SetActive(false);
                    return false;
                }

                string seekerType = missile.GetSeekerType();
                bool isRadarGuided = string.Equals(seekerType, "ARH", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(seekerType, "SARH", StringComparison.OrdinalIgnoreCase);

                // 1. Infrared (IR), Optical, Laser, INS, and Anti-Radiation seekers emit no radar signals towards ownship.
                // Suppress arcade cheat vector line entirely.
                if (!isRadarGuided)
                {
                    if (vLine.activeSelf) vLine.SetActive(false);
                    return false;
                }

                // 2. Active Radar Homing (ARH) missiles:
                // Check if the seeker transmitter is active (Terminal Pitbull acquisition phase)
                Aircraft playerAircraft = CombatHUD.i != null ? CombatHUD.i.aircraft : null;
                bool isTransmitting = true;
                if (string.Equals(seekerType, "ARH", StringComparison.OrdinalIgnoreCase))
                {
                    bool enableSilentMidcourse = RadioWarsConfig.EnableSilentARHMidcourse == null || RadioWarsConfig.EnableSilentARHMidcourse.Value;
                    if (enableSilentMidcourse && playerAircraft != null)
                    {
                        isTransmitting = TacScreenTacticalDisplay.IsTerminalPitbullMissile(missile, playerAircraft);
                    }
                }

                // If flying silently in midcourse (datalink / inertial), suppress threat cone
                if (!isTransmitting)
                {
                    if (vLine.activeSelf) vLine.SetActive(false);
                    return false;
                }

                // 3. User configuration check: bearing cones enabled
                bool useCones = RadioWarsConfig.EnableRWRBearingCone == null || RadioWarsConfig.EnableRWRBearingCone.Value;
                if (!useCones)
                {
                    if (backup != null && backup.IsModified)
                    {
                        Image img = backup.Image != null ? backup.Image : vLine.GetComponent<Image>();
                        RectTransform rt = backup.RectTransform != null ? backup.RectTransform : vLine.GetComponent<RectTransform>();
                        if (img != null)
                        {
                            img.sprite = backup.OriginalSprite;
                            img.color = backup.OriginalColor;
                            img.raycastTarget = backup.OriginalRaycastTarget;
                        }
                        if (rt != null)
                        {
                            rt.pivot = backup.OriginalPivot;
                            rt.anchorMin = backup.OriginalAnchorMin;
                            rt.anchorMax = backup.OriginalAnchorMax;
                            rt.sizeDelta = backup.OriginalSizeDelta;
                        }
                        backup.IsModified = false;
                    }
                    return true;
                }

                Transform playerIcon = ___playerAircraftIconTransform;
                Transform missileIcon = ___missileIconTransform;
                if (playerIcon == null || missileIcon == null)
                {
                    if (vLine.activeSelf) vLine.SetActive(false);
                    return false;
                }

                Image coneImg = (backup != null && backup.Image != null) ? backup.Image : vLine.GetComponent<Image>();
                RectTransform coneRt = (backup != null && backup.RectTransform != null) ? backup.RectTransform : vLine.GetComponent<RectTransform>();

                if (backup == null)
                {
                    backup = vLine.AddComponent<ThreatLineBackup>();
                    backup.Image = coneImg;
                    backup.RectTransform = coneRt;
                    if (coneImg != null)
                    {
                        backup.OriginalSprite = coneImg.sprite;
                        backup.OriginalColor = coneImg.color;
                        backup.OriginalRaycastTarget = coneImg.raycastTarget;
                    }
                    if (coneRt != null)
                    {
                        backup.OriginalPivot = coneRt.pivot;
                        backup.OriginalAnchorMin = coneRt.anchorMin;
                        backup.OriginalAnchorMax = coneRt.anchorMax;
                        backup.OriginalSizeDelta = coneRt.sizeDelta;
                    }
                }
                else
                {
                    if (backup.Image == null) backup.Image = coneImg;
                    if (backup.RectTransform == null) backup.RectTransform = coneRt;
                }
                backup.IsModified = true;

                if (coneImg != null)
                {
                    coneImg.sprite = RwrConeGraphic.GetOrCreateConeSprite();
                    coneImg.raycastTarget = false;
                }

                if (coneRt != null)
                {
                    coneRt.anchorMin = new Vector2(0.5f, 0.5f);
                    coneRt.anchorMax = new Vector2(0.5f, 0.5f);
                    coneRt.pivot = new Vector2(0.5f, 0.0f); // Apex fixed at ownship
                }

                // Position apex at player aircraft map icon
                vLine.transform.position = playerIcon.position;

                // Orient along line of bearing towards missile
                Vector3 delta = missileIcon.position - playerIcon.position;
                float angle = -Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg;
                vLine.transform.eulerAngles = new Vector3(0f, 0f, angle);

                Transform iconLayer = DynamicMap.i != null && DynamicMap.i.iconLayer != null
                    ? DynamicMap.i.iconLayer.transform
                    : null;
                float layerScale = (iconLayer != null && iconLayer.lossyScale.y > 0.0001f)
                    ? iconLayer.lossyScale.y
                    : 1.0f;

                // Proportional distance: 85% of distance to missile
                float screenDist = delta.magnitude;
                float distRatio = DynamicMap.mapMaximized
                    ? (RadioWarsConfig.RWRThreatDistanceRatio != null ? Mathf.Clamp(RadioWarsConfig.RWRThreatDistanceRatio.Value, 0.05f, 2.0f) : 0.85f)
                    : (RadioWarsConfig.MinimapThreatDistanceRatio != null ? Mathf.Clamp(RadioWarsConfig.MinimapThreatDistanceRatio.Value, 0.05f, 2.0f) : 0.85f);

                float minScreenLen = DynamicMap.mapMaximized ? 22.0f : 16.0f;
                float maxScreenLen = DynamicMap.mapMaximized ? 3500.0f : 1500.0f;
                float clampedScreenDist = Mathf.Clamp(screenDist * distRatio, minScreenLen, maxScreenLen);

                float coneLength = clampedScreenDist / layerScale;

                PhysRWRReceiver rwr = PhysRWRReceiver.Get(playerAircraft);
                RwrTier tier = (rwr != null) ? rwr.Capabilities.Tier : RwrTier.Tier2_Standard;
                float halfAngle = RwrConeGraphic.GetMapConeHalfAngle(RwrThreatState.MissileGuidance, tier);

                float coneWidth = RwrConeGraphic.GetConeBaseWidth(coneLength, halfAngle);

                if (coneRt != null)
                {
                    coneRt.sizeDelta = new Vector2(coneWidth, coneLength);
                }
                vLine.transform.localScale = Vector3.one;

                // Urgent pulsing red alert color for missile active radar homing
                float missileAlpha = RadioWarsConfig.RWRMissileConeOpacity != null
                    ? Mathf.Clamp(RadioWarsConfig.RWRMissileConeOpacity.Value, 0.01f, 1.0f)
                    : 0.75f;
                float pulse = Mathf.Lerp(missileAlpha * 0.65f, missileAlpha, 0.5f + 0.5f * Mathf.Sin(Time.time * 26.0f));
                if (coneImg != null)
                {
                    coneImg.color = new Color(1.0f, 0.15f, 0.15f, pulse);
                }

                if (!vLine.activeSelf) vLine.SetActive(true);
                return false; // Suppress vanilla 1-pixel line
            }
        }

        [HarmonyPatch(typeof(ThreatItem), "FoundIcon")]
        public static class ThreatItem_FoundIcon_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(
                ThreatItem __instance,
                GameObject ___notchIndicator,
                GameObject ___notchLine,
                GameObject ___vectorLine,
                Missile ___missile)
            {
                if (ShouldSuppress)
                {
                    if (___notchIndicator != null && ___notchIndicator.activeSelf)
                    {
                        ___notchIndicator.SetActive(false);
                    }

                    if (___notchLine != null && ___notchLine.activeSelf)
                    {
                        ___notchLine.SetActive(false);
                    }
                }

                if (RadioWarsConfig.IsModActive)
                {
                    if (___missile != null)
                    {
                        string seekerType = ___missile.GetSeekerType();
                        bool isRadar = string.Equals(seekerType, "ARH", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(seekerType, "SARH", StringComparison.OrdinalIgnoreCase);
                        if (!isRadar)
                        {
                            if (___vectorLine != null && ___vectorLine.activeSelf)
                            {
                                ___vectorLine.SetActive(false);
                            }
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(ThreatItem), "OnEnable")]
        public static class ThreatItem_OnEnable_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(
                ThreatItem __instance,
                GameObject ___notchIndicator,
                GameObject ___notchLine,
                GameObject ___vectorLine,
                Missile ___missile)
            {
                if (ShouldSuppress)
                {
                    if (___notchLine != null && ___notchLine.activeSelf)
                    {
                        ___notchLine.SetActive(false);
                    }

                    if (___notchIndicator != null && ___notchIndicator.activeSelf)
                    {
                        ___notchIndicator.SetActive(false);
                    }
                }

                if (RadioWarsConfig.IsModActive)
                {
                    if (___missile != null)
                    {
                        string seekerType = ___missile.GetSeekerType();
                        bool isRadar = string.Equals(seekerType, "ARH", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(seekerType, "SARH", StringComparison.OrdinalIgnoreCase);
                        if (!isRadar)
                        {
                            if (___vectorLine != null && ___vectorLine.activeSelf)
                            {
                                ___vectorLine.SetActive(false);
                            }
                        }
                    }
                }
            }
        }
    }
}
