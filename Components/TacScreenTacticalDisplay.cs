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
    /// <summary>
    /// Unified, clean tactical cockpit display for TacScreen.
    /// Combines F-15/F-16 Fire Control Radar (airborne target symbology, velocity vector leader lines,
    /// single-target data block) with clean RWR directional threat strobes.
    /// </summary>
    public class TacScreenTacticalDisplay : MonoBehaviour
    {
        private class TrackItem
        {
            public Unit TargetUnit;
            public float LastPaintedTime;

            public GameObject RootObject;
            public Image SymbolImage;
            public RectTransform SymbolRect;

            public GameObject PrimaryBoxObject;
            public Image PrimaryBoxImage;

            public GameObject VectorObject;
            public RectTransform VectorRect;
            public Image VectorImage;

            public GameObject DataTagObject;
            public TextMeshProUGUI DataTagText;
        }

        private class ThreatStrobeItem
        {
            public Unit EmitterUnit;
            public GameObject StrobeObject;
            public Image StrobeImage;
            public RectTransform StrobeRect;
        }

        private static readonly Func<TacScreen, Transform> s_getIconLayer = FastReflection.CreateFieldGetter<TacScreen, Transform>("iconLayer");
        private static readonly Func<TacScreen, GameObject> s_getPingPrefab = FastReflection.CreateFieldGetter<TacScreen, GameObject>("pingPrefab");
        private static readonly Func<TacScreen, float> s_getMetersPerPixel = FastReflection.CreateFieldGetter<TacScreen, float>("metersPerPixel");
        private static readonly Func<TacScreen, GameObject> s_getTargetCamDisplay = FastReflection.CreateFieldGetter<TacScreen, GameObject>("targetCamDisplay");
        private static readonly Func<TacScreen, GameObject> s_getLandingCamDisplay = FastReflection.CreateFieldGetter<TacScreen, GameObject>("landingCamDisplay");
        private static readonly Func<TacScreen, TextMeshProUGUI> s_getTimeDisplay = FastReflection.CreateFieldGetter<TacScreen, TextMeshProUGUI>("timeDisplay");
        private static readonly Func<ARHSeeker, bool> s_getArhRadarLock = FastReflection.CreateFieldGetter<ARHSeeker, bool>("radarLockEstablished");
        private static readonly Func<MissileSeeker, Unit> s_getSeekerTargetUnit = FastReflection.CreateFieldGetter<MissileSeeker, Unit>("targetUnit");
        private static readonly Func<Missile, Unit> s_getMissileTarget = FastReflection.CreateFieldGetter<Missile, Unit>("target");
        private static readonly Func<ARHSeeker, float> s_getArhTerminalRange = FastReflection.CreateFieldGetter<ARHSeeker, float>("terminalRange");

        private TacScreen _tacScreen;
        private Aircraft _aircraft;
        private PhysRWRReceiver _rwr;

        private Transform _iconLayer;
        private RectTransform _iconRectTransform;
        private TargetDetector _opticalDetector;
        private GameObject _pingPrefab;
        private GameObject _targetCamDisplay;
        private GameObject _landingCamDisplay;

        // Dedicated fixed non-rotating tactical layer
        private GameObject _displayLayer;

        private TMP_FontAsset _mfdFont;
        private float _radarRadius = 185f;
        private float _metersPerPixel = 0.0037f;

        // Radar tracks (Air-to-Air)
        private readonly Dictionary<Unit, TrackItem> _tracks = new Dictionary<Unit, TrackItem>();
        private readonly List<Unit> _staleTracks = new List<Unit>();

        // RWR threat strobes
        private readonly Dictionary<Unit, ThreatStrobeItem> _strobes = new Dictionary<Unit, ThreatStrobeItem>();
        private readonly List<Unit> _staleStrobes = new List<Unit>();
        private readonly HashSet<Unit> _activeUnits = new HashSet<Unit>();

        private bool _isInitialized;

        public void Initialize(TacScreen tacScreen, Aircraft aircraft)
        {
            _tacScreen = tacScreen;
            _aircraft = aircraft;

            if (_tacScreen == null || _aircraft == null) return;

            _rwr = PhysRWRReceiver.Get(_aircraft);
            if (_rwr == null)
            {
                _rwr = _aircraft.gameObject.AddComponent<PhysRWRReceiver>();
            }

            _opticalDetector = _aircraft.GetComponentInChildren<TargetDetector>();

            try
            {
                if (s_getIconLayer != null) _iconLayer = s_getIconLayer(_tacScreen);
                if (s_getPingPrefab != null) _pingPrefab = s_getPingPrefab(_tacScreen);
                if (s_getTargetCamDisplay != null) _targetCamDisplay = s_getTargetCamDisplay(_tacScreen);
                if (s_getLandingCamDisplay != null) _landingCamDisplay = s_getLandingCamDisplay(_tacScreen);
                if (s_getMetersPerPixel != null) _metersPerPixel = s_getMetersPerPixel(_tacScreen);
            }
            catch (Exception ex)
            {
                if (RadioWarsPlugin.Log != null) RadioWarsPlugin.Log.LogError(string.Format("Error reflecting TacScreen fields in TacScreenTacticalDisplay: {0}", ex));
            }

            if (_iconLayer == null)
            {
                if (RadioWarsPlugin.Log != null) RadioWarsPlugin.Log.LogWarning("TacScreenTacticalDisplay: iconLayer is null!");
                return;
            }

            _iconRectTransform = _iconLayer.GetComponent<RectTransform>();

            // Cache native MFD font from TacScreen.timeDisplay or fallback
            TextMeshProUGUI timeComp = s_getTimeDisplay != null ? s_getTimeDisplay(_tacScreen) : null;
            if (timeComp != null && timeComp.font != null)
            {
                _mfdFont = timeComp.font;
            }
            else
            {
                TMP_FontAsset[] allFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                if (allFonts != null && allFonts.Length > 0)
                {
                    _mfdFont = allFonts[0];
                }
            }

            // Radar scope radius
            if (_iconRectTransform != null && _iconRectTransform.rect.height > 10f)
            {
                _radarRadius = _iconRectTransform.rect.height * 0.48f;
            }
            else
            {
                _radarRadius = 185f;
            }

            CreateDisplayLayer();
            _isInitialized = true;
        }

        private void CreateDisplayLayer()
        {
            if (_displayLayer != null)
            {
                UnityEngine.Object.Destroy(_displayLayer);
                _displayLayer = null;
            }

            Transform parentTransform = _iconLayer.parent != null ? _iconLayer.parent : _iconLayer;

            // Single fixed non-rotating tactical layer: parented to iconLayer's stationary parent
            _displayLayer = new GameObject("TacScreen_TacticalLayer");
            _displayLayer.layer = _iconLayer.gameObject.layer;
            _displayLayer.transform.SetParent(parentTransform, false);

            RectTransform layerRt = _displayLayer.AddComponent<RectTransform>();
            RectTransform iconRt = _iconLayer.GetComponent<RectTransform>();
            if (iconRt != null)
            {
                layerRt.anchorMin = iconRt.anchorMin;
                layerRt.anchorMax = iconRt.anchorMax;
                layerRt.pivot = iconRt.pivot;
                layerRt.anchoredPosition = iconRt.anchoredPosition;
                layerRt.sizeDelta = iconRt.sizeDelta;
                layerRt.localScale = iconRt.localScale;
            }
            else
            {
                layerRt.localPosition = _iconLayer.localPosition;
                layerRt.localScale = _iconLayer.localScale;
            }
            layerRt.localEulerAngles = Vector3.zero;

            // Place on top of iconLayer
            _displayLayer.transform.SetSiblingIndex(_iconLayer.GetSiblingIndex() + 1);
        }

        #region Factory Methods

        private TrackItem CreateTrackItem(Unit target)
        {
            TrackItem item = new TrackItem();
            item.TargetUnit = target;
            item.LastPaintedTime = Time.time;

            int layer = _displayLayer.layer;
            Transform parent = _displayLayer.transform;

            // 1. Root anchor at target position
            item.RootObject = new GameObject(string.Format("Tgt_{0}", target.unitName));
            item.RootObject.layer = layer;
            item.RootObject.transform.SetParent(parent, false);

            RectTransform rootRt = item.RootObject.AddComponent<RectTransform>();
            rootRt.anchorMin = new Vector2(0.5f, 0.5f);
            rootRt.anchorMax = new Vector2(0.5f, 0.5f);
            rootRt.pivot = new Vector2(0.5f, 0.5f);
            rootRt.sizeDelta = new Vector2(1f, 1f);
            rootRt.localScale = Vector3.one;
            rootRt.localEulerAngles = Vector3.zero;

            // 2. Velocity vector leader line (radiating from target center in direction of travel)
            item.VectorObject = new GameObject("Vector");
            item.VectorObject.layer = layer;
            item.VectorObject.transform.SetParent(item.RootObject.transform, false);

            item.VectorRect = item.VectorObject.AddComponent<RectTransform>();
            item.VectorRect.anchorMin = new Vector2(0.5f, 0.5f);
            item.VectorRect.anchorMax = new Vector2(0.5f, 0.5f);
            item.VectorRect.pivot = new Vector2(0.5f, 0f); // Pivot at target center
            item.VectorRect.anchoredPosition = Vector2.zero;
            item.VectorRect.sizeDelta = new Vector2(1.6f, 18f);

            item.VectorImage = item.VectorObject.AddComponent<Image>();
            item.VectorImage.raycastTarget = false;

            // 3. Track Symbol (Diamond 8x8 rotated 45°)
            GameObject symObj = new GameObject("Symbol");
            symObj.layer = layer;
            symObj.transform.SetParent(item.RootObject.transform, false);

            item.SymbolRect = symObj.AddComponent<RectTransform>();
            item.SymbolRect.anchorMin = new Vector2(0.5f, 0.5f);
            item.SymbolRect.anchorMax = new Vector2(0.5f, 0.5f);
            item.SymbolRect.pivot = new Vector2(0.5f, 0.5f);
            item.SymbolRect.sizeDelta = new Vector2(7f, 7f);
            item.SymbolRect.localEulerAngles = new Vector3(0f, 0f, 45f);

            item.SymbolImage = symObj.AddComponent<Image>();
            item.SymbolImage.raycastTarget = false;

            // 4. Primary Target Designated Box / Brackets [ ]
            item.PrimaryBoxObject = new GameObject("PrimaryBox");
            item.PrimaryBoxObject.layer = layer;
            item.PrimaryBoxObject.transform.SetParent(item.RootObject.transform, false);

            RectTransform boxRt = item.PrimaryBoxObject.AddComponent<RectTransform>();
            boxRt.anchorMin = new Vector2(0.5f, 0.5f);
            boxRt.anchorMax = new Vector2(0.5f, 0.5f);
            boxRt.pivot = new Vector2(0.5f, 0.5f);
            boxRt.sizeDelta = new Vector2(16f, 16f);
            boxRt.localEulerAngles = Vector3.zero;

            item.PrimaryBoxImage = item.PrimaryBoxObject.AddComponent<Image>();
            item.PrimaryBoxImage.color = new Color(1.0f, 0.25f, 0.25f, 0.95f);
            item.PrimaryBoxImage.raycastTarget = false;
            item.PrimaryBoxObject.SetActive(false);

            // 5. Tactical Data Tag (Active ONLY on Primary/Locked Target)
            item.DataTagObject = new GameObject("DataTag");
            item.DataTagObject.layer = layer;
            item.DataTagObject.transform.SetParent(item.RootObject.transform, false);

            RectTransform tagRt = item.DataTagObject.AddComponent<RectTransform>();
            tagRt.anchorMin = new Vector2(0.5f, 0.5f);
            tagRt.anchorMax = new Vector2(0.5f, 0.5f);
            tagRt.pivot = new Vector2(0f, 0.5f); // Left-aligned beside symbol
            tagRt.anchoredPosition = new Vector2(11f, 0f);
            tagRt.sizeDelta = new Vector2(120f, 34f);
            tagRt.localEulerAngles = Vector3.zero;

            item.DataTagText = item.DataTagObject.AddComponent<TextMeshProUGUI>();
            if (_mfdFont != null) item.DataTagText.font = _mfdFont;
            item.DataTagText.fontSize = 8.5f;
            item.DataTagText.lineSpacing = -14f;
            item.DataTagText.fontStyle = FontStyles.Bold;
            item.DataTagText.alignment = TextAlignmentOptions.Left;
            item.DataTagText.raycastTarget = false;
            item.DataTagText.enableWordWrapping = false;
            item.DataTagObject.SetActive(false);

            return item;
        }

        private ThreatStrobeItem CreateThreatStrobe(Unit emitter)
        {
            ThreatStrobeItem item = new ThreatStrobeItem();
            item.EmitterUnit = emitter;

            int layer = _displayLayer.layer;
            Transform parent = _displayLayer.transform;

            if (_pingPrefab != null)
            {
                item.StrobeObject = UnityEngine.Object.Instantiate(_pingPrefab, parent);
                item.StrobeObject.layer = layer;
                item.StrobeObject.transform.localPosition = Vector3.zero;
                item.StrobeObject.transform.localScale = Vector3.one;
                item.StrobeImage = item.StrobeObject.GetComponent<Image>();
                if (item.StrobeImage == null) item.StrobeImage = item.StrobeObject.GetComponentInChildren<Image>();
                item.StrobeRect = item.StrobeObject.GetComponent<RectTransform>();
                if (item.StrobeRect != null)
                {
                    item.StrobeRect.pivot = new Vector2(0.5f, 0f);
                    item.StrobeRect.anchoredPosition = Vector2.zero;
                }
            }
            else
            {
                item.StrobeObject = new GameObject("ThreatStrobe");
                item.StrobeObject.layer = layer;
                item.StrobeObject.transform.SetParent(parent, false);

                item.StrobeRect = item.StrobeObject.AddComponent<RectTransform>();
                item.StrobeRect.anchorMin = new Vector2(0.5f, 0.5f);
                item.StrobeRect.anchorMax = new Vector2(0.5f, 0.5f);
                item.StrobeRect.pivot = new Vector2(0.5f, 0f);
                item.StrobeRect.anchoredPosition = Vector2.zero;
                item.StrobeRect.sizeDelta = new Vector2(2.5f, _radarRadius);

                item.StrobeImage = item.StrobeObject.AddComponent<Image>();
            }

            bool useCones = RadioWarsConfig.EnableRWRBearingCone == null || RadioWarsConfig.EnableRWRBearingCone.Value;
            if (useCones && item.StrobeImage != null)
            {
                item.StrobeImage.sprite = RwrConeGraphic.GetOrCreateConeSprite();
            }

            if (item.StrobeImage != null) item.StrobeImage.raycastTarget = false;
            return item;
        }

        #endregion

        private void Update()
        {
            if (!_isInitialized || !RadioWarsConfig.IsModActive) return;

            if (_aircraft == null || _aircraft.disabled)
            {
                ClearAll();
                return;
            }

            // Check if cockpit display switched to cameras
            bool camActive = (_targetCamDisplay != null && _targetCamDisplay.activeSelf) ||
                             (_landingCamDisplay != null && _landingCamDisplay.activeSelf);

            if (camActive)
            {
                SetVisible(false);
                return;
            }
            else
            {
                SetVisible(true);
            }

            if (_rwr == null)
            {
                _rwr = PhysRWRReceiver.Get(_aircraft);
            }

            // Refresh metersPerPixel and radius dynamically
            if (s_getMetersPerPixel != null && _tacScreen != null)
            {
                _metersPerPixel = s_getMetersPerPixel(_tacScreen);
            }
            if (_iconRectTransform != null && _iconRectTransform.rect.height > 50f)
            {
                _radarRadius = _iconRectTransform.rect.height * 0.48f;
            }

            float now = Time.time;

            // Update Air-to-Air Radar Tracks
            UpdateRadarTracks(now);

            // Update RWR Directional Threat Strobes
            UpdateThreatStrobes(now);
        }

        #region Air-to-Air Radar Tracking

        private void UpdateRadarTracks(float now)
        {
            float persistence = 3.5f;

            // Primary designated target (bugged target)
            Unit primaryTarget = null;
            if (CombatHUD.i != null && CombatHUD.i.HasTargets)
            {
                List<Unit> targetList = CombatHUD.i.GetTargetList();
                if (targetList != null && targetList.Count > 0)
                {
                    primaryTarget = targetList[0];
                }
            }

            // 1. Gather strictly hostile aircraft from radar & optical scanner
            if (_aircraft.radar != null && _aircraft.radar.detectedTargets != null)
            {
                List<Unit> radarTargets = _aircraft.radar.detectedTargets;
                for (int i = 0; i < radarTargets.Count; i++)
                {
                    Unit u = radarTargets[i];
                    if (IsHostileAirTarget(u))
                    {
                        PaintTrack(u, now);
                    }
                }
            }

            TargetDetector optical = _opticalDetector;
            if (optical != null && optical.detectedTargets != null)
            {
                List<Unit> optTargets = optical.detectedTargets;
                for (int i = 0; i < optTargets.Count; i++)
                {
                    Unit u = optTargets[i];
                    if (IsHostileAirTarget(u))
                    {
                        PaintTrack(u, now);
                    }
                }
            }

            // If primary target is designated, ensure it is tracked only if it is a hostile aircraft
            if (primaryTarget != null && IsHostileAirTarget(primaryTarget))
            {
                PaintTrack(primaryTarget, now);
            }

            // Tactical Datalink: gather hostile aircraft shared by allied radars (AWACS, ground radar stations)
            if (_rwr != null && _rwr.ActiveDatalinkThreats != null)
            {
                foreach (var dl in _rwr.ActiveDatalinkThreats)
                {
                    if (dl.ThreatUnit != null && !dl.ThreatUnit.disabled && !dl.IsMissile)
                    {
                        if (IsHostileAirTarget(dl.ThreatUnit))
                        {
                            PaintTrack(dl.ThreatUnit, now);
                        }
                    }
                }
            }

            // 2. Render and position all active tracks
            Vector3 ownshipPos = _aircraft.transform.position;
            Vector3 ownshipVel = (_aircraft.rb != null) ? _aircraft.rb.velocity : _aircraft.transform.forward * _aircraft.speed;

            // Stable 2D horizontal heading vectors (yaw-only ground plane, zero pitch & roll)
            Vector3 yawForward = _aircraft.transform.forward;
            yawForward.y = 0f;
            if (yawForward.sqrMagnitude < 0.0001f)
            {
                yawForward = -_aircraft.transform.up;
                yawForward.y = 0f;
                if (yawForward.sqrMagnitude < 0.0001f)
                {
                    yawForward = Vector3.forward;
                }
            }
            yawForward.Normalize();

            Vector3 yawRight = new Vector3(yawForward.z, 0f, -yawForward.x);

            float vecScale = RadioWarsConfig.RadarVelocityVectorScale != null ? RadioWarsConfig.RadarVelocityVectorScale.Value : 1.0f;
            string unitSystem = RadioWarsConfig.RadarDisplayUnitSystem != null ? RadioWarsConfig.RadarDisplayUnitSystem.Value : "Metric";

            _staleTracks.Clear();

            foreach (var kvp in _tracks)
            {
                Unit target = kvp.Key;
                TrackItem item = kvp.Value;

                if (!IsHostileAirTarget(target))
                {
                    _staleTracks.Add(target);
                    continue;
                }

                float age = now - item.LastPaintedTime;
                if (age > persistence)
                {
                    _staleTracks.Add(target);
                    continue;
                }

                // 2D horizontal ground displacement (Plan Position Indicator)
                // Discards vertical altitude delta and ignores aircraft pitch/bank attitude tumbling
                Vector3 delta = target.transform.position - ownshipPos;
                float lateralMeters = delta.x * yawRight.x + delta.z * yawRight.z;
                float forwardMeters = delta.x * yawForward.x + delta.z * yawForward.z;

                float screenX = lateralMeters * _metersPerPixel;
                float screenY = forwardMeters * _metersPerPixel;
                float distFromCenter = Mathf.Sqrt(screenX * screenX + screenY * screenY);

                // Cull targets outside radar scope radius
                if (distFromCenter > _radarRadius)
                {
                    if (item.RootObject.activeSelf) item.RootObject.SetActive(false);
                    continue;
                }
                else
                {
                    if (!item.RootObject.activeSelf) item.RootObject.SetActive(true);
                }

                item.RootObject.transform.localPosition = new Vector3(screenX, screenY, 0f);

                // Velocity vector leader line (projected in the same 2D horizontal plane)
                Vector3 tgtWorldVel = (target.rb != null && target.rb.velocity.sqrMagnitude > 1f)
                    ? target.rb.velocity
                    : target.transform.forward * target.speed;

                float velLateral = tgtWorldVel.x * yawRight.x + tgtWorldVel.z * yawRight.z;
                float velForward = tgtWorldVel.x * yawForward.x + tgtWorldVel.z * yawForward.z;
                float headingAngle = -Mathf.Atan2(velLateral, velForward) * Mathf.Rad2Deg;
                float groundSpeed = Mathf.Sqrt(velLateral * velLateral + velForward * velForward);

                // Hostile Aircraft Symbol: Diamond 7x7 rotated 45°
                if (item.SymbolRect != null)
                {
                    item.SymbolRect.sizeDelta = new Vector2(7f, 7f);
                    item.SymbolRect.localEulerAngles = new Vector3(0f, 0f, 45f);
                }

                // Velocity vector leader line for hostile aircraft moving > 15 m/s
                if (groundSpeed > 15f)
                {
                    if (item.VectorObject != null && !item.VectorObject.activeSelf) item.VectorObject.SetActive(true);
                    if (item.VectorRect != null)
                    {
                        float vecLen = Mathf.Clamp(groundSpeed * 0.045f * _metersPerPixel * vecScale * 1000f, 5f, 26f);
                        item.VectorRect.sizeDelta = new Vector2(1.5f, vecLen);
                        item.VectorObject.transform.localEulerAngles = new Vector3(0f, 0f, headingAngle);
                    }
                }
                else
                {
                    if (item.VectorObject != null && item.VectorObject.activeSelf) item.VectorObject.SetActive(false);
                }

                bool isPrimary = (target == primaryTarget);

                // Hostile Aircraft: Vibrant tactical red
                float normLife = Mathf.Clamp01(1.0f - (age / persistence));
                Color trackColor;
                if (isPrimary)
                {
                    trackColor = new Color(1.0f, 0.25f, 0.25f, 1.0f);
                }
                else
                {
                    trackColor = new Color(1.0f, 0.15f, 0.15f, Mathf.Lerp(0.35f, 0.95f, normLife));
                }

                if (item.SymbolImage != null) item.SymbolImage.color = trackColor;
                if (item.VectorImage != null) item.VectorImage.color = trackColor;

                // Primary Target Designated Brackets [ ]
                if (item.PrimaryBoxObject != null)
                {
                    if (isPrimary)
                    {
                        item.PrimaryBoxObject.SetActive(true);
                        if (item.PrimaryBoxImage != null) item.PrimaryBoxImage.color = new Color(1.0f, 0.25f, 0.25f, 0.95f);
                    }
                    else
                    {
                        item.PrimaryBoxObject.SetActive(false);
                    }
                }

                // F-15/F-16 Strict Declutter: Show data block ONLY on the primary/locked target!
                if (item.DataTagObject != null)
                {
                    if (isPrimary)
                    {
                        item.DataTagObject.SetActive(true);

                        string nameStr = !string.IsNullOrEmpty(target.unitName) ? target.unitName.ToUpper() : "TGT";
                        if (nameStr.Length > 9) nameStr = nameStr.Substring(0, 9);

                        string altStr;
                        string spdStr;

                        if (string.Equals(unitSystem, "Aviation", StringComparison.OrdinalIgnoreCase))
                        {
                            altStr = string.Format("FL{0:00}", target.transform.position.y * 3.28084f / 100f);
                            spdStr = string.Format("{0:0}KT", groundSpeed * 1.94384f);
                        }
                        else
                        {
                            altStr = string.Format("{0:0.0}k", target.transform.position.y / 1000f);
                            spdStr = string.Format("{0:0}", groundSpeed * 3.6f);
                        }

                        // Compute closure rate (Vc) and aspect
                        Vector3 toTarget = target.transform.position - ownshipPos;
                        Vector3 dirToTarget = toTarget.normalized;
                        float closureRate = Vector3.Dot(ownshipVel - tgtWorldVel, dirToTarget);

                        float aspectAngle = Vector3.Angle(tgtWorldVel, -toTarget);
                        string aspectStr = "BEAM";
                        if (aspectAngle < 45f) aspectStr = "HOT";
                        else if (aspectAngle > 135f) aspectStr = "COLD";

                        item.DataTagText.text = string.Format(".{0}\n{1}/{2}\nVc:{3:+0;-0} {4}",
                            nameStr, altStr, spdStr, Mathf.RoundToInt(closureRate), aspectStr);
                        item.DataTagText.color = new Color(1.0f, 0.35f, 0.35f, 1.0f);
                    }
                    else
                    {
                        item.DataTagObject.SetActive(false);
                    }
                }
            }

            // Cleanup stale tracks
            for (int i = 0; i < _staleTracks.Count; i++)
            {
                Unit u = _staleTracks[i];
                TrackItem item = _tracks[u];
                if (item.RootObject != null) UnityEngine.Object.Destroy(item.RootObject);
                _tracks.Remove(u);
            }
        }

        private bool IsHostileAirTarget(Unit unit)
        {
            if (unit == null || unit.disabled || unit == _aircraft) return false;

            // Must strictly be an aircraft (excludes ground radars, SAMs, vehicles, and missiles)
            if (!(unit is Aircraft)) return false;

            // Exclude friendly aircraft
            if (_aircraft.NetworkHQ != null && unit.NetworkHQ != null && _aircraft.NetworkHQ == unit.NetworkHQ)
            {
                return false;
            }

            return true;
        }

        private void PaintTrack(Unit target, float now)
        {
            if (target == null) return;

            TrackItem item;
            if (!_tracks.TryGetValue(target, out item) || item == null)
            {
                item = CreateTrackItem(target);
                _tracks[target] = item;
            }

            item.LastPaintedTime = now;
        }

        #endregion

        #region RWR Threat Strobes

        /// <summary>
        /// Evaluates whether an in-flight missile has transitioned to autonomous active radar homing
        /// (Terminal Pitbull phase) directed at the specified aircraft.
        /// Infrared heatseekers, optical weapons, silent midcourse missiles, and missiles targeting other units return false.
        /// </summary>
        public static bool IsTerminalPitbullMissile(Missile m, Aircraft ownship)
        {
            if (m == null || m.disabled || !m.gameObject.activeInHierarchy) return false;

            // Must have an active radar homing seeker (ARH)
            ARHSeeker arh = m.GetComponentInChildren<ARHSeeker>();
            if (arh == null) return false;

            // Determine if the missile is tracking/targeting ownship
            Unit tgt = (s_getSeekerTargetUnit != null) ? s_getSeekerTargetUnit(arh) : null;
            if (tgt == null && s_getMissileTarget != null)
            {
                tgt = s_getMissileTarget(m);
            }

            // If missile has an established target and it is not ownship, it is not pitbull against us
            if (tgt != null && ownship != null && tgt != ownship)
            {
                return false;
            }

            // 1. Primary check: Seeker active radar lock established (Terminal Pitbull phase)
            if (s_getArhRadarLock != null && s_getArhRadarLock(arh))
            {
                return true;
            }

            // 2. Secondary check: Within autonomous terminal acquisition range targeting ownship
            if (ownship != null && tgt == ownship && s_getArhTerminalRange != null)
            {
                float termRange = s_getArhTerminalRange(arh);
                float dist = Vector3.Distance(m.transform.position, ownship.transform.position);
                if (termRange > 500f && dist <= termRange)
                {
                    return true;
                }
            }

            return false;
        }

        private void UpdateThreatStrobes(float now)
        {
            if (_rwr == null) return;

            float persistence = RadioWarsConfig.RWRStrobePersistenceSeconds != null ? RadioWarsConfig.RWRStrobePersistenceSeconds.Value : 3.0f;
            if (persistence <= 0.5f) persistence = 3.0f;

            ICollection<RwrThreatEntry> threats = _rwr.ActiveThreats;
            _activeUnits.Clear();

            foreach (RwrThreatEntry threat in threats)
            {
                if (threat.EmitterUnit == null) continue;

                // If emitter is a missile, verify it is strictly in terminal Pitbull phase targeting ownship
                if (threat.EmitterUnit is Missile)
                {
                    if (!IsTerminalPitbullMissile((Missile)threat.EmitterUnit, _aircraft))
                    {
                        continue;
                    }
                }

                _activeUnits.Add(threat.EmitterUnit);

                ThreatStrobeItem item;
                if (!_strobes.TryGetValue(threat.EmitterUnit, out item) || item == null)
                {
                    item = CreateThreatStrobe(threat.EmitterUnit);
                    _strobes[threat.EmitterUnit] = item;
                }

                bool useCones = RadioWarsConfig.EnableRWRBearingCone == null || RadioWarsConfig.EnableRWRBearingCone.Value;
                RwrTier tier = (_rwr != null) ? _rwr.Capabilities.Tier : RwrTier.Tier2_Standard;
                float halfAngle = RwrConeGraphic.GetCockpitConeHalfAngle(threat.State, tier);

                // Orient strobe ray in fixed tactical layer coordinates:
                // 0° = nose (UP), 90° = right, etc.
                if (item.StrobeObject != null)
                {
                    item.StrobeObject.transform.localPosition = Vector3.zero;
                    item.StrobeObject.transform.localEulerAngles = new Vector3(0f, 0f, -threat.RelativeBearing);
                }

                if (item.StrobeRect != null)
                {
                    float distRatio = (RadioWarsConfig.CockpitRWRThreatDistanceRatio != null)
                        ? Mathf.Clamp(RadioWarsConfig.CockpitRWRThreatDistanceRatio.Value, 0.05f, 2.0f)
                        : ((RadioWarsConfig.RWRThreatDistanceRatio != null)
                            ? Mathf.Clamp(RadioWarsConfig.RWRThreatDistanceRatio.Value, 0.05f, 2.0f)
                            : 0.85f);
                    float coneLength = _radarRadius;

                    if (threat.EmitterUnit != null && _aircraft != null)
                    {
                        float worldDist = Vector3.Distance(_aircraft.transform.position, threat.EmitterUnit.transform.position);
                        float screenDist = worldDist * _metersPerPixel;
                        coneLength = Mathf.Clamp(screenDist * distRatio, 18.0f, _radarRadius);
                    }

                    if (useCones)
                    {
                        item.StrobeRect.pivot = new Vector2(0.5f, 0f);
                        if (item.StrobeImage != null) item.StrobeImage.sprite = RwrConeGraphic.GetOrCreateConeSprite();
                        float coneWidth = RwrConeGraphic.GetConeBaseWidth(coneLength, halfAngle);
                        item.StrobeRect.sizeDelta = new Vector2(coneWidth, coneLength);
                    }
                    else if (_pingPrefab == null)
                    {
                        item.StrobeRect.sizeDelta = new Vector2(2.5f, coneLength);
                    }
                }

                float age = now - threat.LastPingTime;
                float normLife = Mathf.Clamp01(1.0f - (age / persistence));

                float searchAlpha = RadioWarsConfig.RWRSearchConeOpacity != null ? Mathf.Clamp(RadioWarsConfig.RWRSearchConeOpacity.Value, 0.01f, 1.0f) : 0.10f;
                float trackAlpha = RadioWarsConfig.RWRTrackConeOpacity != null ? Mathf.Clamp(RadioWarsConfig.RWRTrackConeOpacity.Value, 0.01f, 1.0f) : 0.40f;
                float missileAlpha = RadioWarsConfig.RWRMissileConeOpacity != null ? Mathf.Clamp(RadioWarsConfig.RWRMissileConeOpacity.Value, 0.01f, 1.0f) : 0.75f;

                switch (threat.State)
                {
                    case RwrThreatState.MissileGuidance:
                        float mPulse = Mathf.Lerp(0.70f, 1.0f, 0.5f + 0.5f * Mathf.Sin(Time.time * 8.0f));
                        Color mColor = new Color(1.0f, 0.12f, 0.12f, missileAlpha * mPulse);
                        if (item.StrobeImage != null) item.StrobeImage.color = mColor;
                        if (item.StrobeObject != null) item.StrobeObject.transform.SetAsLastSibling();
                        break;

                    case RwrThreatState.Track:
                        Color tColor = new Color(1.0f, 0.55f, 0.0f, trackAlpha * Mathf.Lerp(0.35f, 1.0f, normLife));
                        if (item.StrobeImage != null) item.StrobeImage.color = tColor;
                        break;

                    case RwrThreatState.Search:
                    default:
                        Color sColor = new Color(1.0f, 0.85f, 0.15f, searchAlpha * normLife);
                        if (item.StrobeImage != null) item.StrobeImage.color = sColor;
                        break;
                }
            }

            // Render active inbound missiles tracked by ownship missile warning system
            // Strictly ONLY when the missile is in terminal Pitbull stage targeting ownship!
            MissileWarning mw = _aircraft.GetMissileWarningSystem();
            if (mw == null && CombatHUD.i != null && CombatHUD.i.aircraft != null)
            {
                mw = CombatHUD.i.aircraft.GetMissileWarningSystem();
            }

            if (mw != null && mw.knownMissiles != null)
            {
                List<Missile> known = mw.knownMissiles;
                for (int mIdx = 0; mIdx < known.Count; mIdx++)
                {
                    Missile m = known[mIdx];
                    if (m == null || m.disabled || !m.gameObject.activeInHierarchy) continue;

                    // Strictly filter: ONLY missiles in terminal Pitbull phase targeting ownship!
                    if (!IsTerminalPitbullMissile(m, _aircraft)) continue;

                    _activeUnits.Add(m);

                    ThreatStrobeItem item;
                    if (!_strobes.TryGetValue(m, out item) || item == null)
                    {
                        item = CreateThreatStrobe(m);
                        _strobes[m] = item;
                    }

                    Vector3 fwd = _aircraft.transform.forward;
                    fwd.y = 0.0f;
                    Vector3 toM = m.transform.position - _aircraft.transform.position;
                    toM.y = 0.0f;

                    float relBearing = 0.0f;
                    if (fwd.sqrMagnitude > 0.001f && toM.sqrMagnitude > 0.001f)
                    {
                        relBearing = Vector3.SignedAngle(fwd, toM, Vector3.up);
                    }

                    bool useCones = RadioWarsConfig.EnableRWRBearingCone == null || RadioWarsConfig.EnableRWRBearingCone.Value;
                    RwrTier tier = (_rwr != null) ? _rwr.Capabilities.Tier : RwrTier.Tier2_Standard;
                    float mHalfAngle = RwrConeGraphic.GetCockpitConeHalfAngle(RwrThreatState.MissileGuidance, tier);

                    if (item.StrobeObject != null)
                    {
                        item.StrobeObject.transform.localPosition = Vector3.zero;
                        item.StrobeObject.transform.localEulerAngles = new Vector3(0f, 0f, -relBearing);
                        item.StrobeObject.transform.SetAsLastSibling();
                    }

                    if (item.StrobeRect != null)
                    {
                        float distRatio = (RadioWarsConfig.CockpitRWRThreatDistanceRatio != null)
                            ? Mathf.Clamp(RadioWarsConfig.CockpitRWRThreatDistanceRatio.Value, 0.05f, 2.0f)
                            : ((RadioWarsConfig.RWRThreatDistanceRatio != null)
                                ? Mathf.Clamp(RadioWarsConfig.RWRThreatDistanceRatio.Value, 0.05f, 2.0f)
                                : 0.85f);
                        float mWorldDist = Vector3.Distance(_aircraft.transform.position, m.transform.position);
                        float mScreenDist = mWorldDist * _metersPerPixel;
                        float mConeLength = Mathf.Clamp(mScreenDist * distRatio, 18.0f, _radarRadius);

                        if (useCones)
                        {
                            item.StrobeRect.pivot = new Vector2(0.5f, 0f);
                            if (item.StrobeImage != null) item.StrobeImage.sprite = RwrConeGraphic.GetOrCreateConeSprite();
                            float mConeWidth = RwrConeGraphic.GetConeBaseWidth(mConeLength, mHalfAngle);
                            item.StrobeRect.sizeDelta = new Vector2(mConeWidth, mConeLength);
                        }
                        else if (_pingPrefab == null)
                        {
                            item.StrobeRect.sizeDelta = new Vector2(3.0f, mConeLength);
                        }
                    }

                    float mPulse = Mathf.Lerp(0.70f, 1.0f, 0.5f + 0.5f * Mathf.Sin(Time.time * 8.0f));
                    float missileAlpha = RadioWarsConfig.RWRMissileConeOpacity != null ? Mathf.Clamp(RadioWarsConfig.RWRMissileConeOpacity.Value, 0.01f, 1.0f) : 0.75f;
                    Color mColor = new Color(1.0f, 0.12f, 0.12f, missileAlpha * mPulse);
                    if (item.StrobeImage != null) item.StrobeImage.color = mColor;
                }
            }

            // Render Allied Tactical Datalink Strobes (Strictly rendered ONLY when EnableDatalinkVisualStrobes is explicitly enabled)
            bool showAllDl = RadioWarsConfig.EnableDatalinkVisualStrobes != null && RadioWarsConfig.EnableDatalinkVisualStrobes.Value;
            if (showAllDl)
            {
                ICollection<DatalinkThreatEntry> dlThreats = _rwr.ActiveDatalinkThreats;
                if (dlThreats != null)
                {
                    foreach (DatalinkThreatEntry dl in dlThreats)
                    {
                        if (dl.ThreatUnit == null || dl.ThreatUnit.disabled) continue;
                        if (_activeUnits.Contains(dl.ThreatUnit)) continue;

                        _activeUnits.Add(dl.ThreatUnit);

                        ThreatStrobeItem item;
                        if (!_strobes.TryGetValue(dl.ThreatUnit, out item) || item == null)
                        {
                            item = CreateThreatStrobe(dl.ThreatUnit);
                            _strobes[dl.ThreatUnit] = item;
                        }

                        bool useCones = RadioWarsConfig.EnableRWRBearingCone == null || RadioWarsConfig.EnableRWRBearingCone.Value;
                        RwrTier tier = (_rwr != null) ? _rwr.Capabilities.Tier : RwrTier.Tier2_Standard;
                        float dlHalfAngle = RwrConeGraphic.GetCockpitConeHalfAngle(RwrThreatState.Search, tier);

                        if (item.StrobeObject != null)
                        {
                            item.StrobeObject.transform.localPosition = Vector3.zero;
                            item.StrobeObject.transform.localEulerAngles = new Vector3(0f, 0f, -dl.RelativeBearing);
                        }

                        if (item.StrobeRect != null)
                        {
                            float distRatio = (RadioWarsConfig.CockpitRWRThreatDistanceRatio != null)
                                ? Mathf.Clamp(RadioWarsConfig.CockpitRWRThreatDistanceRatio.Value, 0.05f, 2.0f)
                                : ((RadioWarsConfig.RWRThreatDistanceRatio != null)
                                    ? Mathf.Clamp(RadioWarsConfig.RWRThreatDistanceRatio.Value, 0.05f, 2.0f)
                                    : 0.85f);
                            float dlConeLength = _radarRadius;

                            if (dl.ThreatUnit != null && _aircraft != null)
                            {
                                float dlWorldDist = Vector3.Distance(_aircraft.transform.position, dl.ThreatUnit.transform.position);
                                float dlScreenDist = dlWorldDist * _metersPerPixel;
                                dlConeLength = Mathf.Clamp(dlScreenDist * distRatio, 18.0f, _radarRadius);
                            }

                            if (useCones)
                            {
                                item.StrobeRect.pivot = new Vector2(0.5f, 0f);
                                if (item.StrobeImage != null) item.StrobeImage.sprite = RwrConeGraphic.GetOrCreateConeSprite();
                                float dlConeWidth = RwrConeGraphic.GetConeBaseWidth(dlConeLength, dlHalfAngle);
                                item.StrobeRect.sizeDelta = new Vector2(dlConeWidth, dlConeLength);
                            }
                            else if (_pingPrefab == null)
                            {
                                item.StrobeRect.sizeDelta = new Vector2(2.5f, dlConeLength);
                            }
                        }

                        float age = now - dl.LastPingTime;
                        float normLife = Mathf.Clamp01(1.0f - (age / 3.5f));

                        // Datalink strobes are ALWAYS clean cyan - NEVER red!
                        float dlAlpha = RadioWarsConfig.RWRDatalinkConeOpacity != null ? Mathf.Clamp(RadioWarsConfig.RWRDatalinkConeOpacity.Value, 0.01f, 1.0f) : 0.35f;
                        Color dlColor = new Color(0.2f, 0.85f, 1.0f, dlAlpha * Mathf.Lerp(0.4f, 1.0f, normLife));
                        if (item.StrobeImage != null) item.StrobeImage.color = dlColor;
                    }
                }
            }

            // Remove expired strobes
            _staleStrobes.Clear();
            foreach (var kvp in _strobes)
            {
                if (!_activeUnits.Contains(kvp.Key))
                {
                    _staleStrobes.Add(kvp.Key);
                }
            }

            for (int i = 0; i < _staleStrobes.Count; i++)
            {
                Unit u = _staleStrobes[i];
                ThreatStrobeItem item = _strobes[u];
                if (item.StrobeObject != null) UnityEngine.Object.Destroy(item.StrobeObject);
                _strobes.Remove(u);
            }
        }

        #endregion

        private void SetVisible(bool visible)
        {
            if (_displayLayer != null && _displayLayer.activeSelf != visible)
            {
                _displayLayer.SetActive(visible);
            }
        }

        private void ClearAll()
        {
            foreach (var kvp in _tracks)
            {
                TrackItem item = kvp.Value;
                if (item.RootObject != null) UnityEngine.Object.Destroy(item.RootObject);
            }
            _tracks.Clear();

            foreach (var kvp in _strobes)
            {
                ThreatStrobeItem item = kvp.Value;
                if (item.StrobeObject != null) UnityEngine.Object.Destroy(item.StrobeObject);
            }
            _strobes.Clear();
        }

        private void OnDestroy()
        {
            ClearAll();
            if (_displayLayer != null)
            {
                UnityEngine.Object.Destroy(_displayLayer);
            }
        }
    }
}
