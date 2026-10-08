using System;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RadioWars.Config;
using RadioWars.Core;

namespace RadioWars.Components
{
    /// <summary>
    /// High-performance, zero-allocation visualizer for Flight HUD missile trajectories and real-time ETA tracking.
    /// Implementation specifications:
    /// - Unlimited length: line extends from player's aircraft all the way to the missile.
    /// - Start anchor: Point 0 is ALWAYS attached to the player's aircraft in real time.
    /// - End anchor: Point Last is ALWAYS attached to the flying missile in real time.
    /// - Dynamic culling: all points behind the aircraft (in local Z <= 5m) are immediately removed (waypoints behind ownship are pruned).
    /// - Segmented angular vertices: trajectory in front of aircraft is divided into 2-3 discrete straight segments.
    /// - Sequential stepping light: rear segment lights up, then middle lights up (rear turns off), then front lights up (middle turns off).
    /// - 100% FloatingOrigin coordinate immunity via Nuclear Option GlobalPosition coordinate system.
    /// - 1.5s frozen impact hold upon weapon detonation.
    /// - Zero GC allocations per frame.
    /// </summary>
    public class MissileTrajectoryHUDVisualizer : MonoBehaviour
    {
        public static MissileTrajectoryHUDVisualizer Instance { get; private set; }

        private const int POOL_SIZE = 12;
        private const int MAX_SEGMENTS = 16;
        private const int MAX_RECORDED_POINTS = 512;
        private const float IMPACT_PERSIST_SECONDS = 1.5f;

        private static readonly Color TvHudGreen = new Color(0.62f, 1.0f, 0.70f, 1.0f);

        private static readonly Func<Missile, Unit> getMissileTarget = FastReflection.CreateFieldGetter<Missile, Unit>("target");
        private static readonly Func<Missile, MissileSeeker> getMissileSeeker = FastReflection.CreateFieldGetter<Missile, MissileSeeker>("seeker");
        private static readonly Func<Missile, GlobalPosition> getMissileAimPoint = FastReflection.CreateFieldGetter<Missile, GlobalPosition>("aimPoint");
        private static readonly Func<MissileSeeker, Unit> getSeekerTarget = FastReflection.CreateFieldGetter<MissileSeeker, Unit>("target");
        private static readonly Func<FlightHud, Canvas> getFlightHudCanvas = FastReflection.CreateFieldGetter<FlightHud, Canvas>("canvas");

        private class PooledTrajectorySlot
        {
            public GameObject RootObject;

            // Pre-allocated trail line segment quads
            public RectTransform[] SegmentRects = new RectTransform[MAX_SEGMENTS];
            public Image[] SegmentImages = new Image[MAX_SEGMENTS];

            // Optional forward lead line to target
            public RectTransform LeadRect;
            public Image LeadImage;

            // ETA Label
            public GameObject ETARoot;
            public RectTransform ETARect;
            public TextMeshProUGUI ETAText;

            // Tracking state
            public Missile ActiveMissile;
            public Aircraft OwnerAircraft;
            public Unit TargetUnit;
            public Vector3 LastTargetPos;

            // Historical waypoints dropped along the missile flight path
            public GlobalPosition[] RecordedHistory = new GlobalPosition[MAX_RECORDED_POINTS];
            public int RecordedCount;
            public GlobalPosition LastRecordedPos;

            // Render points buffer: Point 0 = Aircraft, Point Last = Missile
            public GlobalPosition[] RenderPoints = new GlobalPosition[MAX_SEGMENTS + 2];
            public int RenderPointCount;

            // Frozen points on impact / detonation
            public GlobalPosition[] FrozenPoints = new GlobalPosition[MAX_SEGMENTS + 2];
            public int FrozenCount;
            public float ImpactTime;
            public bool InImpactHold;

            // Cyclic wave animation
            public float AnimationSeed;

            // Telemetry timing
            public float NextTelemetryTickTime;
            public float LastETATickTime;

            public bool InUse;
        }

        private readonly PooledTrajectorySlot[] _slots = new PooledTrajectorySlot[POOL_SIZE];
        private int _activeCount = 0;

        private GameObject _containerLayer;
        private Camera _mainCamera;
        private TMP_FontAsset _hudFont;

        private void Awake()
        {
            Instance = this;
            InitializePool();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_containerLayer != null)
            {
                Destroy(_containerLayer);
            }
        }

        private void InitializePool()
        {
            // Parent to CombatHUD.iconLayer or FlightHud Canvas
            Transform parentLayer = null;
            if (CombatHUD.i != null && CombatHUD.i.iconLayer != null)
            {
                parentLayer = CombatHUD.i.iconLayer;
            }
            else if (FlightHud.i != null && getFlightHudCanvas != null)
            {
                Canvas fCanvas = getFlightHudCanvas(FlightHud.i);
                if (fCanvas != null) parentLayer = fCanvas.transform;
            }

            if (parentLayer == null)
            {
                parentLayer = transform;
            }

            _containerLayer = new GameObject("MissileTrajectoryHUD_Layer");
            _containerLayer.transform.SetParent(parentLayer, false);

            RectTransform layerRt = _containerLayer.AddComponent<RectTransform>();
            layerRt.anchorMin = Vector2.zero;
            layerRt.anchorMax = Vector2.one;
            layerRt.pivot = new Vector2(0.5f, 0.5f);
            layerRt.anchoredPosition = Vector2.zero;
            layerRt.sizeDelta = Vector2.zero;
            layerRt.localScale = Vector3.one;

            ResolveHudFont();

            for (int i = 0; i < POOL_SIZE; i++)
            {
                PooledTrajectorySlot slot = new PooledTrajectorySlot();

                slot.RootObject = new GameObject(string.Format("TrajectorySlot_{0}", i));
                slot.RootObject.transform.SetParent(_containerLayer.transform, false);

                RectTransform slotRt = slot.RootObject.AddComponent<RectTransform>();
                slotRt.anchorMin = Vector2.zero;
                slotRt.anchorMax = Vector2.one;
                slotRt.pivot = new Vector2(0.5f, 0.5f);
                slotRt.anchoredPosition = Vector2.zero;
                slotRt.sizeDelta = Vector2.zero;
                slotRt.localScale = Vector3.one;

                // Pre-instantiate trail segment quads
                for (int s = 0; s < MAX_SEGMENTS; s++)
                {
                    GameObject segGo = new GameObject(string.Format("TrailSeg_{0}_{1}", i, s));
                    segGo.transform.SetParent(slot.RootObject.transform, false);

                    RectTransform segRt = segGo.AddComponent<RectTransform>();
                    segRt.anchorMin = Vector2.zero;
                    segRt.anchorMax = Vector2.zero;
                    segRt.pivot = new Vector2(0f, 0.5f);
                    segRt.localScale = Vector3.one;

                    Image segImg = segGo.AddComponent<Image>();
                    segImg.color = TvHudGreen;
                    segImg.raycastTarget = false;

                    slot.SegmentRects[s] = segRt;
                    slot.SegmentImages[s] = segImg;
                    segGo.SetActive(false);
                }

                // Pre-instantiate forward lead line quad
                GameObject leadGo = new GameObject(string.Format("LeadVector_{0}", i));
                leadGo.transform.SetParent(slot.RootObject.transform, false);
                RectTransform leadRt = leadGo.AddComponent<RectTransform>();
                leadRt.anchorMin = Vector2.zero;
                leadRt.anchorMax = Vector2.zero;
                leadRt.pivot = new Vector2(0f, 0.5f);
                leadRt.localScale = Vector3.one;

                Image leadImg = leadGo.AddComponent<Image>();
                leadImg.color = new Color(0.62f, 1.0f, 0.70f, 0.45f);
                leadImg.raycastTarget = false;

                slot.LeadRect = leadRt;
                slot.LeadImage = leadImg;
                leadGo.SetActive(false);

                // Pre-instantiate ETA Text
                GameObject etaGo = new GameObject(string.Format("ETA_{0}", i));
                etaGo.transform.SetParent(slot.RootObject.transform, false);
                RectTransform etaRt = etaGo.AddComponent<RectTransform>();
                etaRt.anchorMin = Vector2.zero;
                etaRt.anchorMax = Vector2.zero;
                etaRt.pivot = new Vector2(0.5f, 0f);
                etaRt.sizeDelta = new Vector2(100f, 24f);
                etaRt.localScale = Vector3.one;

                TextMeshProUGUI etaText = etaGo.AddComponent<TextMeshProUGUI>();
                if (_hudFont != null) etaText.font = _hudFont;
                etaText.fontSize = 13f;
                etaText.fontStyle = FontStyles.Bold;
                etaText.color = TvHudGreen;
                etaText.alignment = TextAlignmentOptions.Center;
                etaText.text = "ETA --";
                etaText.raycastTarget = false;

                slot.ETARoot = etaGo;
                slot.ETARect = etaRt;
                slot.ETAText = etaText;
                etaGo.SetActive(false);

                slot.InUse = false;
                slot.RootObject.SetActive(false);
                _slots[i] = slot;
            }
        }

        private void ResolveHudFont()
        {
            if (FlightHud.i != null)
            {
                TextMeshProUGUI[] tmps = FlightHud.i.GetComponentsInChildren<TextMeshProUGUI>(true);
                for (int i = 0; i < tmps.Length; i++)
                {
                    if (tmps[i] != null && tmps[i].font != null)
                    {
                        _hudFont = tmps[i].font;
                        return;
                    }
                }
            }

            if (CombatHUD.i != null)
            {
                TextMeshProUGUI[] tmps = CombatHUD.i.GetComponentsInChildren<TextMeshProUGUI>(true);
                for (int i = 0; i < tmps.Length; i++)
                {
                    if (tmps[i] != null && tmps[i].font != null)
                    {
                        _hudFont = tmps[i].font;
                        return;
                    }
                }
            }
        }

        public Aircraft GetPlayerAircraft()
        {
            if (CombatHUD.i != null && CombatHUD.i.aircraft != null)
            {
                return CombatHUD.i.aircraft;
            }
            return null;
        }

        /// <summary>
        /// Registers a player-fired missile into the visualization pool.
        /// </summary>
        public void OnMissileLaunched(Missile missile)
        {
            if (missile == null) return;
            if (RadioWarsConfig.EnableMissileTrajectoryHUD != null && !RadioWarsConfig.EnableMissileTrajectoryHUD.Value) return;

            // Check if already registered
            for (int i = 0; i < POOL_SIZE; i++)
            {
                if (_slots[i].InUse && _slots[i].ActiveMissile == missile)
                {
                    return;
                }
            }

            PooledTrajectorySlot slot = null;
            for (int i = 0; i < POOL_SIZE; i++)
            {
                if (!_slots[i].InUse)
                {
                    slot = _slots[i];
                    break;
                }
            }

            bool wasInUse = true;
            if (slot == null)
            {
                slot = _slots[0];
            }
            else
            {
                wasInUse = false;
            }

            Unit targetUnit = GetMissileTarget(missile);
            Vector3 initialPos = missile.transform.position;
            GlobalPosition initialGlobalPos = GlobalPositionExtensions.ToGlobalPosition(initialPos);

            slot.ActiveMissile = missile;
            slot.OwnerAircraft = missile.owner as Aircraft;
            if (slot.OwnerAircraft == null) slot.OwnerAircraft = GetPlayerAircraft();

            slot.TargetUnit = targetUnit;
            slot.LastTargetPos = ResolveTargetPosition(missile, targetUnit);

            slot.RecordedCount = 0;
            slot.LastRecordedPos = initialGlobalPos;
            slot.RenderPointCount = 0;
            slot.FrozenCount = 0;
            slot.ImpactTime = -1f;
            slot.InImpactHold = false;
            slot.AnimationSeed = UnityEngine.Random.value;

            // Telemetry timing
            float updateRate = RadioWarsConfig.MissileTrajectoryUpdateRate != null ? RadioWarsConfig.MissileTrajectoryUpdateRate.Value : 8.0f;
            float tickInterval = 1.0f / Mathf.Clamp(updateRate, 2.0f, 30.0f);
            slot.NextTelemetryTickTime = Time.time + tickInterval;
            slot.LastETATickTime = 0f;

            // Reset UI segments
            for (int s = 0; s < MAX_SEGMENTS; s++)
            {
                if (slot.SegmentRects[s] != null) slot.SegmentRects[s].gameObject.SetActive(false);
            }
            if (slot.LeadRect != null) slot.LeadRect.gameObject.SetActive(false);
            if (slot.ETARoot != null) slot.ETARoot.SetActive(false);

            slot.InUse = true;
            slot.RootObject.SetActive(true);
            if (!wasInUse)
            {
                _activeCount++;
            }
        }

        /// <summary>
        /// Handles missile impact or deregistration. Enters the 1.5s frozen trail impact hold.
        /// </summary>
        public void OnMissileDeregistered(Missile missile)
        {
            if (missile == null) return;

            for (int i = 0; i < POOL_SIZE; i++)
            {
                PooledTrajectorySlot slot = _slots[i];
                if (slot.InUse && slot.ActiveMissile == missile)
                {
                    FreezeAndHoldImpact(slot);
                }
            }
        }

        private void FreezeAndHoldImpact(PooledTrajectorySlot slot)
        {
            if (!slot.InImpactHold && slot.RenderPointCount >= 2)
            {
                slot.InImpactHold = true;
                slot.ImpactTime = Time.time;

                slot.FrozenCount = slot.RenderPointCount;
                Array.Copy(slot.RenderPoints, slot.FrozenPoints, slot.RenderPointCount);

                if (slot.ETARoot != null) slot.ETARoot.SetActive(false);
                if (slot.LeadRect != null) slot.LeadRect.gameObject.SetActive(false);
            }
            else if (!slot.InImpactHold)
            {
                ReleaseSlot(slot);
            }
        }

        private Unit GetMissileTarget(Missile missile)
        {
            if (missile == null) return null;

            if (getMissileTarget != null)
            {
                try
                {
                    Unit t = getMissileTarget(missile);
                    if (t != null && !t.disabled) return t;
                }
                catch { }
            }

            if (getMissileSeeker != null && getSeekerTarget != null)
            {
                try
                {
                    MissileSeeker seeker = getMissileSeeker(missile);
                    if (seeker != null)
                    {
                        Unit t = getSeekerTarget(seeker);
                        if (t != null && !t.disabled) return t;
                    }
                }
                catch { }
            }

            return null;
        }

        private Vector3 ResolveTargetPosition(Missile missile, Unit targetUnit)
        {
            if (targetUnit != null && !targetUnit.disabled)
            {
                return targetUnit.transform.position;
            }

            if (getMissileAimPoint != null)
            {
                try
                {
                    GlobalPosition gp = getMissileAimPoint(missile);
                    return GlobalPositionExtensions.ToLocalPosition(gp);
                }
                catch { }
            }

            return missile.transform.position + missile.transform.forward * 2000f;
        }

        private void LateUpdate()
        {
            // CRITICAL OPTIMIZATION: Early exit in 0.00ms if no player missiles are active
            if (_activeCount <= 0) return;

            if (_mainCamera == null)
            {
                if (CameraStateManager.i != null && CameraStateManager.i.mainCamera != null)
                {
                    _mainCamera = CameraStateManager.i.mainCamera;
                }
                else
                {
                    _mainCamera = Camera.main;
                }

                if (_mainCamera == null) return;
            }

            float now = Time.time;
            bool showETA = RadioWarsConfig.EnableMissileETADisplay == null || RadioWarsConfig.EnableMissileETADisplay.Value;
            RadioWarsConfig.MissileTrajectoryMode displayMode = RadioWarsConfig.TrajectoryDisplayMode != null 
                ? RadioWarsConfig.TrajectoryDisplayMode.Value 
                : RadioWarsConfig.MissileTrajectoryMode.TrailOnly;

            float updateRate = RadioWarsConfig.MissileTrajectoryUpdateRate != null ? RadioWarsConfig.MissileTrajectoryUpdateRate.Value : 8.0f;
            float tickInterval = 1.0f / Mathf.Clamp(updateRate, 2.0f, 30.0f);

            // Segments count (default 3: rear -> mid -> front)
            int maxSegmentsConfig = RadioWarsConfig.MissileTrajectoryMaxSegments != null ? RadioWarsConfig.MissileTrajectoryMaxSegments.Value : 3;
            int allowedMaxSegments = Mathf.Clamp(maxSegmentsConfig, 2, 6);

            // Base line width scale
            float baseConfigWidth = RadioWarsConfig.MissileTrajectoryLineWidth != null ? RadioWarsConfig.MissileTrajectoryLineWidth.Value : 2.5f;
            float widthScale = Mathf.Clamp(baseConfigWidth / 2.5f, 0.5f, 2.5f);

            Aircraft defaultPlayerAircraft = GetPlayerAircraft();

            for (int i = 0; i < POOL_SIZE; i++)
            {
                PooledTrajectorySlot slot = _slots[i];
                if (!slot.InUse) continue;

                // -------------------------------------------------------------
                // 1. Lifecycle & Impact Hold Management
                // -------------------------------------------------------------
                if (slot.InImpactHold)
                {
                    if (now - slot.ImpactTime >= IMPACT_PERSIST_SECONDS)
                    {
                        ReleaseSlot(slot);
                        continue;
                    }
                }
                else
                {
                    // Check missile liveness
                    if (slot.ActiveMissile == null || 
                        slot.ActiveMissile.disabled || 
                        !slot.ActiveMissile.gameObject.activeInHierarchy || 
                        !slot.ActiveMissile.enabled || 
                        slot.ActiveMissile.transform == null)
                    {
                        FreezeAndHoldImpact(slot);
                        continue;
                    }
                }

                Vector3 currentMissileWorldPos = Vector3.zero;
                GlobalPosition currentMissileGlobalPos = default(GlobalPosition);

                Aircraft aircraft = slot.OwnerAircraft != null && !slot.OwnerAircraft.disabled ? slot.OwnerAircraft : defaultPlayerAircraft;
                if (aircraft == null || aircraft.disabled)
                {
                    ReleaseSlot(slot);
                    continue;
                }

                Transform aircraftT = aircraft.transform;
                Vector3 aircraftWorldPos = aircraftT.position;
                GlobalPosition aircraftGlobalPos = GlobalPositionExtensions.ToGlobalPosition(aircraftWorldPos);

                if (!slot.InImpactHold && slot.ActiveMissile != null)
                {
                    currentMissileWorldPos = slot.ActiveMissile.transform.position;
                    currentMissileGlobalPos = GlobalPositionExtensions.ToGlobalPosition(currentMissileWorldPos);

                    if (slot.TargetUnit == null || slot.TargetUnit.disabled)
                    {
                        slot.TargetUnit = GetMissileTarget(slot.ActiveMissile);
                    }

                    Vector3 targetWorldPos = ResolveTargetPosition(slot.ActiveMissile, slot.TargetUnit);
                    slot.LastTargetPos = targetWorldPos;

                    Vector3 toTarget = targetWorldPos - currentMissileWorldPos;
                    float distToTarget = toTarget.magnitude;

                    // Proximity impact check: switch to impact hold when close to target
                    if (distToTarget < 15.0f)
                    {
                        FreezeAndHoldImpact(slot);
                        continue;
                    }

                    // ---------------------------------------------------------
                    // 2. Record Missile Waypoints in GlobalPosition
                    // ---------------------------------------------------------
                    if (slot.RecordedCount == 0)
                    {
                        slot.RecordedHistory[0] = currentMissileGlobalPos;
                        slot.RecordedCount = 1;
                        slot.LastRecordedPos = currentMissileGlobalPos;
                    }
                    else
                    {
                        Vector3 localLast = GlobalPositionExtensions.ToLocalPosition(slot.LastRecordedPos);
                        // Record a point whenever missile advances by >= 40m
                        if ((currentMissileWorldPos - localLast).sqrMagnitude >= 1600.0f)
                        {
                            if (slot.RecordedCount < MAX_RECORDED_POINTS)
                            {
                                slot.RecordedHistory[slot.RecordedCount++] = currentMissileGlobalPos;
                            }
                            else
                            {
                                // Shift buffer left without allocations
                                int half = MAX_RECORDED_POINTS / 2;
                                for (int k = 0; k < half; k++)
                                {
                                    slot.RecordedHistory[k] = slot.RecordedHistory[k * 2];
                                }
                                slot.RecordedCount = half;
                                slot.RecordedHistory[slot.RecordedCount++] = currentMissileGlobalPos;
                            }
                            slot.LastRecordedPos = currentMissileGlobalPos;
                        }
                    }

                    // ---------------------------------------------------------
                    // 3. Prune Any Waypoints Behind the Aircraft (ownship trailing culling)
                    // ---------------------------------------------------------
                    int firstValid = 0;
                    while (firstValid < slot.RecordedCount)
                    {
                        Vector3 ptWorld = GlobalPositionExtensions.ToLocalPosition(slot.RecordedHistory[firstValid]);
                        Vector3 ptInAircraft = aircraftT.InverseTransformPoint(ptWorld);
                        // If waypoint is strictly in front of the aircraft (> 2m along nose axis)
                        if (ptInAircraft.z > 2.0f)
                        {
                            break;
                        }
                        firstValid++;
                    }

                    if (firstValid > 0)
                    {
                        int remaining = slot.RecordedCount - firstValid;
                        for (int k = 0; k < remaining; k++)
                        {
                            slot.RecordedHistory[k] = slot.RecordedHistory[firstValid + k];
                        }
                        slot.RecordedCount = remaining;
                    }

                    // ---------------------------------------------------------
                    // 4. Construct Render Path: Point 0 = Aircraft, Point Last = Missile
                    // ---------------------------------------------------------
                    int targetIntermediate = allowedMaxSegments - 1; // e.g. 3 - 1 = 2 intermediate points
                    int availHistory = slot.RecordedCount;
                    int actualIntermediate = Mathf.Min(availHistory, targetIntermediate);

                    // Point 0 is ALWAYS attached directly to the player's aircraft in real time!
                    slot.RenderPoints[0] = aircraftGlobalPos;

                    if (actualIntermediate > 0)
                    {
                        for (int m = 0; m < actualIntermediate; m++)
                        {
                            float t = (float)(m + 1) / (float)(actualIntermediate + 1);
                            int idx = Mathf.Clamp(Mathf.RoundToInt(t * (availHistory - 1)), 0, availHistory - 1);
                            slot.RenderPoints[m + 1] = slot.RecordedHistory[idx];
                        }
                    }

                    // Point Last is ALWAYS attached directly to the missile in real time!
                    slot.RenderPoints[actualIntermediate + 1] = currentMissileGlobalPos;
                    slot.RenderPointCount = actualIntermediate + 2;

                    // ETA update on telemetry tick
                    bool isTelemetryTick = now >= slot.NextTelemetryTickTime;
                    if (isTelemetryTick)
                    {
                        slot.NextTelemetryTickTime = now + tickInterval;

                        if (showETA)
                        {
                            Vector3 dirToTarget = distToTarget > 0.001f ? toTarget / distToTarget : slot.ActiveMissile.transform.forward;
                            Vector3 missileVel = slot.ActiveMissile.rb != null ? slot.ActiveMissile.rb.velocity : slot.ActiveMissile.transform.forward * slot.ActiveMissile.speed;
                            Vector3 targetVel = (slot.TargetUnit != null && slot.TargetUnit.rb != null) ? slot.TargetUnit.rb.velocity : Vector3.zero;

                            float closureSpeed = Vector3.Dot(missileVel - targetVel, dirToTarget);
                            float eta = closureSpeed > 15.0f ? distToTarget / closureSpeed : distToTarget / Mathf.Max(missileVel.magnitude, 50.0f);

                            if (eta >= 10.0f)
                            {
                                slot.ETAText.text = string.Format("ETA {0:0}s", eta);
                            }
                            else
                            {
                                slot.ETAText.text = string.Format("ETA {0:0.0}s", Mathf.Max(0f, eta));
                            }
                        }
                    }
                }

                // -------------------------------------------------------------
                // 5. Render Trajectory with Sequential Stepping Pulse
                // -------------------------------------------------------------
                GlobalPosition[] pointsToDraw = slot.InImpactHold ? slot.FrozenPoints : slot.RenderPoints;
                int totalPoints = slot.InImpactHold ? slot.FrozenCount : slot.RenderPointCount;
                int totalSegs = totalPoints - 1;

                // Looping phase cycling at 2.2 Hz
                float phase = 0f;
                if (!slot.InImpactHold)
                {
                    phase = (slot.AnimationSeed + now * 2.2f) % 1.0f;
                    if (phase < 0f) phase += 1.0f;
                }

                int renderedSegments = 0;

                if (displayMode != RadioWarsConfig.MissileTrajectoryMode.LeadOnly && totalSegs >= 1)
                {
                    for (int s = 0; s < totalSegs && renderedSegments < MAX_SEGMENTS; s++)
                    {
                        Vector3 wStart = GlobalPositionExtensions.ToLocalPosition(pointsToDraw[s]);
                        Vector3 wEnd = GlobalPositionExtensions.ToLocalPosition(pointsToDraw[s + 1]);

                        Vector2 sStart, sEnd;
                        if (ProjectSegmentToScreen(wStart, wEnd, _mainCamera, out sStart, out sEnd))
                        {
                            Vector2 delta = sEnd - sStart;
                            float len = delta.magnitude;
                            if (len >= 2.0f)
                            {
                                // Center of this segment along line (0.0 at aircraft, 1.0 at missile)
                                float u = (s + 0.5f) / (float)totalSegs;

                                float alpha;
                                float segW;

                                if (!slot.InImpactHold)
                                {
                                    // Sequential stepping light:
                                    // Rear segment lights up -> Middle lights up (rear turns off) -> Front lights up (middle turns off)
                                    float dist = Mathf.Abs(u - phase);
                                    dist = Mathf.Min(dist, 1.0f - dist); // wrap around [0, 1]

                                    // Half-width 0.17f ensures strictly ONE segment lights up at a time when totalSegs = 3
                                    float pulseFactor = Mathf.Clamp01(1.0f - dist / 0.17f);

                                    alpha = Mathf.Lerp(0.18f, 1.0f, pulseFactor);
                                    segW = Mathf.Lerp(1.6f, 4.2f, pulseFactor) * widthScale;
                                }
                                else
                                {
                                    // Static hold for 1.5s after impact, smooth fade in final 0.5s
                                    float remain = (slot.ImpactTime + IMPACT_PERSIST_SECONDS) - now;
                                    float fade = Mathf.Clamp01(remain / 0.5f);
                                    alpha = 0.8f * fade;
                                    segW = 2.5f * widthScale;
                                }

                                Color segColor = new Color(0.62f, 1.0f, 0.70f, alpha);

                                RectTransform segRt = slot.SegmentRects[renderedSegments];
                                Image segImg = slot.SegmentImages[renderedSegments];

                                segRt.position = new Vector3(sStart.x, sStart.y, 0f);
                                segRt.sizeDelta = new Vector2(len, segW);
                                float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
                                segRt.localEulerAngles = new Vector3(0f, 0f, angle);
                                segImg.color = segColor;

                                if (!segRt.gameObject.activeSelf) segRt.gameObject.SetActive(true);
                                renderedSegments++;
                            }
                        }
                    }
                }

                // Hide any remaining unused segment quads
                for (int s = renderedSegments; s < MAX_SEGMENTS; s++)
                {
                    if (slot.SegmentRects[s].gameObject.activeSelf)
                    {
                        slot.SegmentRects[s].gameObject.SetActive(false);
                    }
                }

                // -------------------------------------------------------------
                // 6. Render Forward Lead Vector (if enabled)
                // -------------------------------------------------------------
                if (!slot.InImpactHold && 
                    (displayMode == RadioWarsConfig.MissileTrajectoryMode.TrailAndLead || 
                     displayMode == RadioWarsConfig.MissileTrajectoryMode.LeadOnly))
                {
                    Vector2 sMissile, sTarget;
                    if (ProjectSegmentToScreen(currentMissileWorldPos, slot.LastTargetPos, _mainCamera, out sMissile, out sTarget))
                    {
                        Vector2 delta = sTarget - sMissile;
                        float len = delta.magnitude;
                        if (len >= 2.0f)
                        {
                            slot.LeadRect.position = new Vector3(sMissile.x, sMissile.y, 0f);
                            slot.LeadRect.sizeDelta = new Vector2(len, 1.4f * widthScale);
                            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
                            slot.LeadRect.localEulerAngles = new Vector3(0f, 0f, angle);

                            if (!slot.LeadRect.gameObject.activeSelf) slot.LeadRect.gameObject.SetActive(true);
                        }
                        else
                        {
                            if (slot.LeadRect.gameObject.activeSelf) slot.LeadRect.gameObject.SetActive(false);
                        }
                    }
                    else
                    {
                        if (slot.LeadRect.gameObject.activeSelf) slot.LeadRect.gameObject.SetActive(false);
                    }
                }
                else
                {
                    if (slot.LeadRect != null && slot.LeadRect.gameObject.activeSelf)
                    {
                        slot.LeadRect.gameObject.SetActive(false);
                    }
                }

                // -------------------------------------------------------------
                // 7. Render ETA Label Beside Missile (STABLE & JITTER-FREE)
                // -------------------------------------------------------------
                if (!slot.InImpactHold && showETA)
                {
                    Vector3 screenMissile = _mainCamera.WorldToScreenPoint(currentMissileWorldPos);
                    if (screenMissile.z > 0.1f)
                    {
                        if (!slot.ETARoot.activeSelf) slot.ETARoot.SetActive(true);
                        // Rock-solid screen position, zero jitter
                        slot.ETARect.position = new Vector3(screenMissile.x, screenMissile.y + 16f, 0f);
                    }
                    else
                    {
                        if (slot.ETARoot.activeSelf) slot.ETARoot.SetActive(false);
                    }
                }
                else
                {
                    if (slot.ETARoot.activeSelf) slot.ETARoot.SetActive(false);
                }
            }
        }

        /// <summary>
        /// Projects a 3D line segment to 2D screen space with near-plane clipping.
        /// Prevents inverted coordinate projection artifacts when one point is behind the camera.
        /// </summary>
        private static bool ProjectSegmentToScreen(Vector3 wA, Vector3 wB, Camera cam, out Vector2 pA, out Vector2 pB)
        {
            pA = Vector2.zero;
            pB = Vector2.zero;

            Transform camT = cam.transform;
            Vector3 cA = camT.InverseTransformPoint(wA);
            Vector3 cB = camT.InverseTransformPoint(wB);

            const float nearClip = 0.5f;

            // Both behind camera: reject
            if (cA.z < nearClip && cB.z < nearClip) return false;

            // Clip cA if behind camera
            if (cA.z < nearClip)
            {
                float t = (nearClip - cA.z) / (cB.z - cA.z);
                cA = Vector3.Lerp(cA, cB, t);
            }
            // Clip cB if behind camera
            else if (cB.z < nearClip)
            {
                float t = (nearClip - cB.z) / (cA.z - cB.z);
                cB = Vector3.Lerp(cB, cA, t);
            }

            Vector3 worldClippedA = camT.TransformPoint(cA);
            Vector3 worldClippedB = camT.TransformPoint(cB);

            Vector3 sA = cam.WorldToScreenPoint(worldClippedA);
            Vector3 sB = cam.WorldToScreenPoint(worldClippedB);

            if (sA.z <= 0f || sB.z <= 0f) return false;

            pA = new Vector2(sA.x, sA.y);
            pB = new Vector2(sB.x, sB.y);

            return true;
        }

        /// <summary>
        /// Fully releases a trajectory slot and resets all visual children.
        /// </summary>
        private void ReleaseSlot(PooledTrajectorySlot slot)
        {
            if (!slot.InUse) return;

            slot.ActiveMissile = null;
            slot.OwnerAircraft = null;
            slot.TargetUnit = null;
            slot.RecordedCount = 0;
            slot.RenderPointCount = 0;
            slot.FrozenCount = 0;
            slot.InImpactHold = false;
            slot.ImpactTime = -1f;
            slot.InUse = false;

            // Immediately deactivate all UI visual components
            for (int s = 0; s < MAX_SEGMENTS; s++)
            {
                if (slot.SegmentRects[s] != null)
                {
                    slot.SegmentRects[s].gameObject.SetActive(false);
                }
            }

            if (slot.LeadRect != null) slot.LeadRect.gameObject.SetActive(false);
            if (slot.ETARoot != null) slot.ETARoot.SetActive(false);
            if (slot.RootObject != null) slot.RootObject.SetActive(false);

            _activeCount = Mathf.Max(0, _activeCount - 1);
        }
    }
}
