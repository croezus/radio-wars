using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RadioWars.Core;
using RadioWars.Config;

namespace RadioWars.Components
{
    public class FlightHUDEWIndicator : MonoBehaviour
    {
        private FlightHud _flightHud;
        private Aircraft _aircraft;
        private PhysRWRReceiver _rwr;

        private GameObject _indicatorObject;
        private RectTransform _indicatorRect;
        private TextMeshProUGUI _indicatorText;
        private TMP_FontAsset _hudFont;

        private float _flashTimer = 0.0f;
        private bool _flashState = true;
        private bool _isInitialized = false;

        private static readonly Color ColorHudGreen = new Color(0.28f, 1.0f, 0.42f, 0.95f);
        private static readonly Color ColorBrightGreen = new Color(0.35f, 1.0f, 0.5f, 1.0f);
        private static readonly Color ColorAmberCaution = new Color(1.0f, 0.82f, 0.25f, 0.95f);
        private static readonly Color ColorRedAlert = new Color(1.0f, 0.32f, 0.22f, 0.98f);
        private static readonly Color ColorDimGreen = new Color(0.28f, 1.0f, 0.42f, 0.55f);
        private static readonly Color ColorDatalinkCyan = new Color(0.2f, 0.85f, 1.0f, 0.95f);

        public void Initialize(FlightHud flightHud, Aircraft aircraft)
        {
            _flightHud = flightHud;
            _aircraft = aircraft;

            if (_flightHud == null || _aircraft == null) return;

            _rwr = _aircraft.GetComponent<PhysRWRReceiver>();
            if (_rwr == null)
            {
                _rwr = _aircraft.gameObject.AddComponent<PhysRWRReceiver>();
            }

            CreateIndicatorUI();
            _isInitialized = true;
        }

        private static readonly FieldInfo f_compass = AccessTools.Field(typeof(FlightHud), "compass");
        private static readonly FieldInfo f_hudCenter = AccessTools.Field(typeof(FlightHud), "HUDCenter");
        private static readonly FieldInfo f_targetText = AccessTools.Field(typeof(CombatHUD), "targetText");
        private static readonly FieldInfo f_cmName = AccessTools.Field(typeof(CombatHUD), "countermeasureName");

        private void CreateIndicatorUI()
        {
            if (_indicatorObject != null) return;

            RawImage compassImage = f_compass != null ? f_compass.GetValue(_flightHud) as RawImage : null;
            Transform hudCenter = f_hudCenter != null ? f_hudCenter.GetValue(_flightHud) as Transform : null;

            // Find parent: ideally compass parent or canvas
            Transform parentTransform = null;
            if (compassImage != null)
            {
                parentTransform = compassImage.transform.parent != null
                    ? compassImage.transform.parent
                    : compassImage.transform;
            }
            else if (hudCenter != null)
            {
                parentTransform = hudCenter;
            }
            else
            {
                parentTransform = _flightHud.transform;
            }

            _indicatorObject = new GameObject("RadioWars_FlightHudEWIndicator");
            _indicatorObject.transform.SetParent(parentTransform, false);

            _indicatorRect = _indicatorObject.AddComponent<RectTransform>();
            _indicatorRect.anchorMin = new Vector2(0.5f, 1.0f);
            _indicatorRect.anchorMax = new Vector2(0.5f, 1.0f);
            _indicatorRect.pivot = new Vector2(0.5f, 0.5f);
            _indicatorRect.sizeDelta = new Vector2(260f, 32f);

            // Position right above the top heading compass tape so it never overlaps the 0-360 azimuth numbers
            if (compassImage != null)
            {
                RectTransform compassRect = compassImage.rectTransform;
                _indicatorRect.anchorMin = compassRect.anchorMin;
                _indicatorRect.anchorMax = compassRect.anchorMax;
                _indicatorRect.pivot = new Vector2(0.5f, 0.5f);
                Vector2 compPos = compassRect.anchoredPosition;
                // Place 28px above compass center (above the numbers tape)
                _indicatorRect.anchoredPosition = new Vector2(compPos.x, compPos.y + 28f);
            }
            else
            {
                _indicatorRect.anchoredPosition = new Vector2(0f, -25f);
            }

            // Find native HUD font
            _hudFont = FindNativeHudFont();

            _indicatorText = _indicatorObject.AddComponent<TextMeshProUGUI>();
            if (_hudFont != null)
            {
                _indicatorText.font = _hudFont;
            }
            _indicatorText.fontSize = 18f;
            _indicatorText.alignment = TextAlignmentOptions.Center;
            _indicatorText.color = ColorHudGreen;
            _indicatorText.raycastTarget = false;
            _indicatorText.text = "";
        }

        private TMP_FontAsset FindNativeHudFont()
        {
            try
            {
                if (CombatHUD.i != null)
                {
                    TextMeshProUGUI tText = f_targetText != null ? f_targetText.GetValue(CombatHUD.i) as TextMeshProUGUI : null;
                    if (tText != null && tText.font != null)
                    {
                        return tText.font;
                    }
                    TextMeshProUGUI cmText = f_cmName != null ? f_cmName.GetValue(CombatHUD.i) as TextMeshProUGUI : null;
                    if (cmText != null && cmText.font != null)
                    {
                        return cmText.font;
                    }
                }
            }
            catch { }

            TMP_FontAsset[] allFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            if (allFonts != null && allFonts.Length > 0)
            {
                return allFonts[0];
            }

            return null;
        }

        private void Update()
        {
            if (!RadioWarsConfig.IsModActive ||
                RadioWarsConfig.EnableHUDNotchIndicator == null ||
                !RadioWarsConfig.EnableHUDNotchIndicator.Value)
            {
                if (_indicatorText != null && !string.IsNullOrEmpty(_indicatorText.text))
                {
                    _indicatorText.text = "";
                }
                return;
            }

            if (!_isInitialized || _aircraft == null || _aircraft.disabled || _indicatorText == null)
            {
                return;
            }

            _flashTimer += Time.deltaTime;
            if (_flashTimer >= 0.32f)
            {
                _flashTimer = 0.0f;
                _flashState = !_flashState;
            }

            if (_rwr == null)
            {
                _rwr = _aircraft.GetComponent<PhysRWRReceiver>();
            }

            float ecmIntensity = _aircraft.GetECMIntensity();
            bool ecmActive = ecmIntensity > 0.05f;

            // Search for highest priority threat (MissileGuidance > Track > Search)
            RwrThreatEntry priorityThreat = null;
            if (_rwr != null && _rwr.ActiveThreats != null)
            {
                float now = Time.time;
                foreach (RwrThreatEntry threat in _rwr.ActiveThreats)
                {
                    if (threat == null || threat.EmitterUnit == null || threat.EmitterUnit.disabled) continue;
                    if (now - threat.LastPingTime > 3.0f) continue; // threat expired

                    if (threat.State == RwrThreatState.MissileGuidance)
                    {
                        priorityThreat = threat;
                        break; // Top priority
                    }
                    else if (threat.State == RwrThreatState.Track)
                    {
                        if (priorityThreat == null || priorityThreat.State == RwrThreatState.Search)
                        {
                            priorityThreat = threat;
                        }
                    }
                    else if (threat.State == RwrThreatState.Search)
                    {
                        if (priorityThreat == null)
                        {
                            priorityThreat = threat;
                        }
                    }
                }
            }

            if (priorityThreat != null && priorityThreat.EmitterUnit != null)
            {
                Vector3 threatPos = priorityThreat.EmitterUnit.transform.position;
                Vector3 threatVel = priorityThreat.EmitterUnit.rb != null ? priorityThreat.EmitterUnit.rb.velocity : Vector3.zero;
                Vector3 ownPos = _aircraft.transform.position;
                Vector3 ownVel = _aircraft.rb != null ? _aircraft.rb.velocity : Vector3.zero;
                float dist = Vector3.Distance(threatPos, ownPos);

                float threatAlt = (priorityThreat.EmitterUnit != null) ? priorityThreat.EmitterUnit.radarAlt : 1000.0f;
                float ownAlt = _aircraft.radarAlt;
                float notchThreshold = RadioWarsConfig.EffectiveNotchThreshold;
                DopplerResult doppler = DopplerClutterProcessor.ProcessDopplerAndClutter(
                    threatPos,
                    threatVel,
                    ownPos,
                    ownVel,
                    notchThreshold,
                    false,
                    threatAlt,
                    ecmIntensity,
                    ownAlt
                );

                // Estimated burn-through distance for HUD warning
                float estBurnThroughDist = priorityThreat.IsActiveSeeker ? 3800f : 12000f;
                if (RadioWarsConfig.JammerBurnThroughRatio != null)
                {
                    estBurnThroughDist *= Mathf.Max(0.1f, RadioWarsConfig.JammerBurnThroughRatio.Value);
                }

                bool isBurnThrough = ecmActive && (dist <= estBurnThroughDist);

                if (isBurnThrough && priorityThreat.State == RwrThreatState.MissileGuidance)
                {
                    // Burn-through reached! Missile skin return pierces ECM noise!
                    _indicatorText.text = _flashState ? "[ BURN-THROUGH ]" : "[ > BURN-THROUGH < ]";
                    _indicatorText.color = ColorRedAlert;
                }
                else if (priorityThreat.State == RwrThreatState.MissileGuidance)
                {
                    // Active Missile Terminal Alert
                    if (priorityThreat.IsActiveSeeker)
                    {
                        _indicatorText.text = _flashState ? "[ >> PITBULL << ]" : "[ PITBULL ]";
                    }
                    else
                    {
                        _indicatorText.text = _flashState ? "[ >> MISSILE << ]" : "[ MISSILE ]";
                    }
                    _indicatorText.color = ColorRedAlert;
                }
                else if (doppler.IsInsideClutterNotch)
                {
                    // Evaluate notch indicator based on aircraft RWR tier
                    RwrTier tier = _rwr != null ? _rwr.Capabilities.Tier : RwrTier.Tier2_Standard;

                    if (tier == RwrTier.Tier1_Basic)
                    {
                        // Tier 1 (Cricket/Chicane): NO cheat notch indicator
                        _indicatorText.text = ecmActive ? "[ ECM ON ]" : "";
                        _indicatorText.color = ColorDimGreen;
                    }
                    else if (tier == RwrTier.Tier2_Standard)
                    {
                        // Tier 2 (Revoker/Compass): Approximate beaming advisory
                        _indicatorText.text = "[ BEAMING ]";
                        _indicatorText.color = ColorAmberCaution;
                    }
                    else
                    {
                        // Tier 3 (Vortex/Ifrit/Darkreach): 5th-gen clutter notch computer
                        if (ecmActive)
                        {
                            _indicatorText.text = "[ ECM NOTCH ]";
                            _indicatorText.color = ColorBrightGreen;
                        }
                        else if (doppler.TrackRejectedByNotch)
                        {
                            _indicatorText.text = "[ NOTCH OK ]";
                            _indicatorText.color = ColorHudGreen;
                        }
                        else
                        {
                            _indicatorText.text = "[ NO CLUTTER ]";
                            _indicatorText.color = ColorAmberCaution;
                        }
                    }
                }
                else if (ecmActive && doppler.IsHOJVulnerable && priorityThreat.State == RwrThreatState.MissileGuidance)
                {
                    // Flying straight with ECM active during missile guidance: HOJ risk!
                    _indicatorText.text = _flashState ? "[ HOJ RISK ]" : "[ > HOJ RISK < ]";
                    _indicatorText.color = ColorRedAlert;
                }
                else if (priorityThreat.State == RwrThreatState.Track)
                {
                    _indicatorText.text = "[ LOCK ]";
                    _indicatorText.color = ColorAmberCaution;
                }
                else if (ecmActive)
                {
                    _indicatorText.text = "[ ECM ON ]";
                    _indicatorText.color = ColorDimGreen;
                }
                else
                {
                    _indicatorText.text = "";
                }
            }
            else
            {
                // No onboard direct RWR lock. Check Allied Tactical Datalink!
                DatalinkThreatEntry dlThreat = _rwr != null ? _rwr.GetHighestDatalinkThreat() : null;
                if (dlThreat != null && dlThreat.IsMissile)
                {
                    // Allied radar warned us about an incoming missile!
                    int clock = dlThreat.ClockPosition;
                    string clockStr = clock > 0 ? string.Format("{0}H", clock) : "INBOUND";
                    _indicatorText.text = _flashState
                        ? string.Format("[ DL: MISSILE {0} ]", clockStr)
                        : string.Format("[ > DL: MISSILE {0} < ]", clockStr);
                    _indicatorText.color = ColorDatalinkCyan;
                }
                else if (ecmActive)
                {
                    _indicatorText.text = "[ ECM ON ]";
                    _indicatorText.color = ColorDimGreen;
                }
                else
                {
                    _indicatorText.text = "";
                }
            }
        }
    }

    /// <summary>
    /// Backward-compatibility alias for FlightHUDEWIndicator.
    /// </summary>
    public class FlightHudEWIndicator : FlightHUDEWIndicator
    {
    }
}
