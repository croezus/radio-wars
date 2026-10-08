using System;
using System.Collections.Generic;
using UnityEngine;
using RadioWars.Core;
using RadioWars.Config;

namespace RadioWars.Components
{
    public class RwrThreatEntry
    {
        public Unit EmitterUnit;
        public Radar EmitterRadar;
        public RwrThreatState State;
        public string ThreatCategory; // "AIR", "SAM", "NAV", "ACT M", "GND", "RAD"
        public string ThreatName;
        public float LastPingTime;
        public Vector3 LastDirection; // Normalized world direction with antenna jitter
        public float RelativeBearing; // Degrees (-180 to +180 relative to aircraft forward)
        public int ClockPosition;      // 1 to 12 o'clock
        public float SignalStrength01; // 0.0 to 1.0 normalized intensity
        public bool IsActiveSeeker;
    }

    public class PhysRWRReceiver : MonoBehaviour
    {
        public Aircraft AttachedAircraft;
        public RadarWarning VanillaRWR;
        public RwrCapabilities Capabilities;

        public event Action<RwrThreatEntry> OnThreatUpdated;
        public event Action<Unit> OnThreatRemoved;
        public event Action OnThreatsChanged;

        private readonly Dictionary<Unit, RwrThreatEntry> _threats = new Dictionary<Unit, RwrThreatEntry>();
        private readonly List<Unit> _staleList = new List<Unit>();

        // Allied Tactical Datalink Tracks
        private readonly Dictionary<Unit, DatalinkThreatEntry> _datalinkThreats = new Dictionary<Unit, DatalinkThreatEntry>();
        private readonly List<Unit> _staleDatalinkList = new List<Unit>();

        private static readonly Dictionary<Aircraft, PhysRWRReceiver> s_registry = new Dictionary<Aircraft, PhysRWRReceiver>();

        public static PhysRWRReceiver Get(Aircraft aircraft)
        {
            if (aircraft == null) return null;
            PhysRWRReceiver rwr;
            if (!s_registry.TryGetValue(aircraft, out rwr))
            {
                rwr = aircraft.GetComponent<PhysRWRReceiver>();
                if (rwr != null) s_registry[aircraft] = rwr;
            }
            return rwr;
        }

        public static PhysRWRReceiver GetOrCreate(Aircraft aircraft)
        {
            if (aircraft == null) return null;
            PhysRWRReceiver rwr = Get(aircraft);
            if (rwr == null)
            {
                rwr = aircraft.gameObject.GetComponent<PhysRWRReceiver>();
                if (rwr == null)
                {
                    rwr = aircraft.gameObject.AddComponent<PhysRWRReceiver>();
                }
                s_registry[aircraft] = rwr;
            }
            return rwr;
        }

        public static void ClearAll()
        {
            s_registry.Clear();
        }

        public ICollection<RwrThreatEntry> ActiveThreats
        {
            get { return _threats.Values; }
        }

        public ICollection<DatalinkThreatEntry> ActiveDatalinkThreats
        {
            get { return _datalinkThreats.Values; }
        }

        private void Awake()
        {
            AttachedAircraft = GetComponent<Aircraft>();
            if (AttachedAircraft != null) s_registry[AttachedAircraft] = this;
            VanillaRWR = GetComponent<RadarWarning>();

            string acName = AttachedAircraft != null ? AttachedAircraft.unitName : null;
            bool enableTiers = RadioWarsConfig.EnableRWRTierGrading == null || RadioWarsConfig.EnableRWRTierGrading.Value;
            Capabilities = enableTiers ? RwrTierDatabase.GetCapabilities(acName) : RwrTierDatabase.GetTier2Capabilities();
        }

        private void OnDestroy()
        {
            if (AttachedAircraft != null) s_registry.Remove(AttachedAircraft);
        }

        public void RegisterRadarPing(Unit emitter, Radar radar, RwrThreatState threatLevel, float signalStrength01 = 0.5f, bool isSeeker = false)
        {
            if (emitter == null || transform == null) return;

            Vector3 toEmitter = emitter.transform.position - transform.position;
            float distToEmitter = toEmitter.magnitude;
            if (distToEmitter < 1.0f) distToEmitter = 1.0f;
            Vector3 dirToEmitter = toEmitter / distToEmitter;

            // 1. RWR Elevation Blind Cones check (Zenith & Nadir)
            bool checkBlindZones = RadioWarsConfig.EnableRwrBlindZones == null || RadioWarsConfig.EnableRwrBlindZones.Value;
            if (checkBlindZones)
            {
                // Convert LOS vector to aircraft body frame (+Z forward, +X right, +Y up)
                Vector3 localDir = transform.InverseTransformDirection(dirToEmitter);
                float elevDeg = Mathf.Asin(Mathf.Clamp(localDir.y, -1.0f, 1.0f)) * Mathf.Rad2Deg;

                if (elevDeg < Capabilities.ElevationMinDeg || elevDeg > Capabilities.ElevationMaxDeg)
                {
                    // Emitter is inside the zenith blind cone (diving from high above) or belly blind cone!
                    return;
                }

                // 2. Bank / Fuselage Masking check for Tier 1 and Tier 2
                float rollAngle = Mathf.Abs(transform.localEulerAngles.z);
                if (rollAngle > 180.0f) rollAngle = 360.0f - rollAngle;
                if (rollAngle > Capabilities.BankMaskingAngleDeg)
                {
                    if (localDir.x * Mathf.Sign(transform.right.y) < -0.25f)
                    {
                        // Signal shadowed by high wing / fuselage structure during steep roll
                        return;
                    }
                }
            }

            // 3. Ku-Band Active Seeker Sensitivity Check (Tiered)
            if (isSeeker || emitter is Missile)
            {
                if (!Capabilities.SupportsKuBand && distToEmitter > Capabilities.KuBandMaxDetectRange)
                {
                    // Primitive RWR is deaf to Ku-band active seekers at long ranges (> 3.5 km)
                    return;
                }
            }

            float now = Time.time;
            RwrThreatEntry entry;
            bool isNew = !_threats.TryGetValue(emitter, out entry);
            if (isNew)
            {
                entry = new RwrThreatEntry();
                entry.EmitterUnit = emitter;
                entry.EmitterRadar = radar;
                _threats[emitter] = entry;
            }

            if (!isNew && entry.State == RwrThreatState.MissileGuidance && threatLevel < RwrThreatState.MissileGuidance && (now - entry.LastPingTime < 3.0f))
            {
                // Preserve higher-priority active missile guidance state
            }
            else
            {
                entry.State = threatLevel;
            }

            entry.LastPingTime = now;
            entry.SignalStrength01 = Mathf.Clamp01(signalStrength01);
            if (isSeeker || emitter is Missile) entry.IsActiveSeeker = true;
            else entry.IsActiveSeeker = isSeeker;

            // Determine threat category
            if (!Capabilities.CanClassifyThreats)
            {
                entry.ThreatCategory = "RAD";
                entry.ThreatName = "Radar";
            }
            else if (isSeeker || emitter is Missile)
            {
                entry.ThreatCategory = "ACT M";
                entry.ThreatName = !string.IsNullOrEmpty(emitter.unitName) ? emitter.unitName : "Active Seeker";
            }
            else if (emitter is Aircraft)
            {
                entry.ThreatCategory = "AIR";
                entry.ThreatName = !string.IsNullOrEmpty(emitter.unitName) ? emitter.unitName : "Fighter";
            }
            else
            {
                string uName = emitter.unitName != null ? emitter.unitName.ToLowerInvariant() : "";
                if (uName.Contains("ship") || uName.Contains("corvette") || uName.Contains("carrier") || uName.Contains("cruiser"))
                {
                    entry.ThreatCategory = "NAV";
                    entry.ThreatName = !string.IsNullOrEmpty(emitter.unitName) ? emitter.unitName : "Warship";
                }
                else if (uName.Contains("sam") || uName.Contains("flak") || uName.Contains("spaa") || uName.Contains("anti-air"))
                {
                    entry.ThreatCategory = "SAM";
                    entry.ThreatName = !string.IsNullOrEmpty(emitter.unitName) ? emitter.unitName : "SAM Site";
                }
                else
                {
                    entry.ThreatCategory = "GND";
                    entry.ThreatName = !string.IsNullOrEmpty(emitter.unitName) ? emitter.unitName : "Ground Radar";
                }
            }

            // Direction calculation with realistic antenna measurement jitter by tier
            float jitterRange = Capabilities.AzimuthJitterDegrees;
            if (RadioWarsConfig.RWRJitterDegrees != null)
            {
                jitterRange = Mathf.Max(jitterRange, RadioWarsConfig.RWRJitterDegrees.Value);
            }

            Quaternion errorJitter = Quaternion.Euler(0.0f, UnityEngine.Random.Range(-jitterRange, jitterRange), 0.0f);
            entry.LastDirection = (errorJitter * dirToEmitter).normalized;

            UpdateRelativeBearing(entry);

            // Feed bearing directly into ESM Triangulation Processor for cooperative multi-bearing tracking
            Aircraft hostAircraft = AttachedAircraft != null ? AttachedAircraft : GetComponent<Aircraft>();
            if (hostAircraft != null && !hostAircraft.disabled && emitter != null && !emitter.disabled)
            {
                if (RadioWarsConfig.EnableRwrTriangulationSystem == null || RadioWarsConfig.EnableRwrTriangulationSystem.Value)
                {
                    RWRTriangulationProcessor.UpdateTrack(emitter, hostAircraft, entry.LastDirection, threatLevel, entry.SignalStrength01);
                }
            }

            try
            {
                if (OnThreatUpdated != null) OnThreatUpdated(entry);
                if (isNew && OnThreatsChanged != null) OnThreatsChanged();
            }
            catch (Exception ex)
            {
                if (RadioWarsConfig.DebugLogging != null && RadioWarsConfig.DebugLogging.Value)
                {
                    if (RadioWarsPlugin.Log != null) RadioWarsPlugin.Log.LogError(string.Format("Error in RWR OnThreatUpdated event: {0}", ex));
                }
            }
        }

        public void RegisterDatalinkThreat(Unit sourceAlliedUnit, Unit threatUnit, Vector3 estPos, Vector3 estVel, bool isMissile)
        {
            if (threatUnit == null || transform == null) return;

            float now = Time.time;
            DatalinkThreatEntry dlEntry;
            if (!_datalinkThreats.TryGetValue(threatUnit, out dlEntry))
            {
                dlEntry = new DatalinkThreatEntry();
                dlEntry.ThreatUnit = threatUnit;
                _datalinkThreats[threatUnit] = dlEntry;
            }

            dlEntry.SourceAlliedUnit = sourceAlliedUnit;
            dlEntry.SourceName = (sourceAlliedUnit != null && !string.IsNullOrEmpty(sourceAlliedUnit.unitName)) ? sourceAlliedUnit.unitName : "Allied Radar";
            dlEntry.EstimatedPosition = estPos;
            dlEntry.EstimatedVelocity = estVel;
            dlEntry.IsMissile = isMissile;
            dlEntry.LastPingTime = now;

            Vector3 toThreat = estPos - transform.position;
            dlEntry.Distance = toThreat.magnitude;
            dlEntry.LastDirection = toThreat.sqrMagnitude > 1.0f ? toThreat.normalized : transform.forward;

            Vector3 fwd = transform.forward;
            fwd.y = 0.0f;
            Vector3 dir = dlEntry.LastDirection;
            dir.y = 0.0f;

            if (fwd.sqrMagnitude > 0.001f && dir.sqrMagnitude > 0.001f)
            {
                float angle = Vector3.SignedAngle(fwd, dir, Vector3.up);
                dlEntry.RelativeBearing = angle;

                float clockAngle = angle < 0.0f ? angle + 360.0f : angle;
                int clock = Mathf.RoundToInt(clockAngle / 30.0f);
                if (clock <= 0) clock = 12;
                if (clock > 12) clock = 12;
                dlEntry.ClockPosition = clock;
            }
        }

        private void UpdateRelativeBearing(RwrThreatEntry entry)
        {
            if (entry == null || transform == null) return;

            // Dynamically refresh live line-of-sight vector if emitter is active (crucial for in-flight missiles)
            if (entry.EmitterUnit != null && !entry.EmitterUnit.disabled)
            {
                Vector3 toEmitter = entry.EmitterUnit.transform.position - transform.position;
                if (toEmitter.sqrMagnitude > 1.0f)
                {
                    entry.LastDirection = toEmitter.normalized;
                }
            }

            Vector3 fwd = transform.forward;
            fwd.y = 0.0f;
            Vector3 dir = entry.LastDirection;
            dir.y = 0.0f;

            if (fwd.sqrMagnitude > 0.001f && dir.sqrMagnitude > 0.001f)
            {
                float angle = Vector3.SignedAngle(fwd, dir, Vector3.up);
                entry.RelativeBearing = angle;

                float clockAngle = angle < 0.0f ? angle + 360.0f : angle;
                int clock = Mathf.RoundToInt(clockAngle / 30.0f);
                if (clock <= 0) clock = 12;
                if (clock > 12) clock = 12;
                entry.ClockPosition = clock;
            }
        }

        private void Update()
        {
            float now = Time.time;
            float maxStale = RadioWarsConfig.RWRStrobePersistenceSeconds != null ? RadioWarsConfig.RWRStrobePersistenceSeconds.Value : 3.0f;
            _staleList.Clear();

            // Continually update relative bearings as our aircraft turns and maneuvers
            foreach (var kvp in _threats)
            {
                RwrThreatEntry entry = kvp.Value;
                if (now - entry.LastPingTime > maxStale)
                {
                    _staleList.Add(kvp.Key);
                }
                else
                {
                    UpdateRelativeBearing(entry);
                }
            }

            if (_staleList.Count > 0)
            {
                for (int i = 0; i < _staleList.Count; i++)
                {
                    Unit unit = _staleList[i];
                    _threats.Remove(unit);
                    try
                    {
                        if (OnThreatRemoved != null) OnThreatRemoved(unit);
                    }
                    catch { }
                }
                try
                {
                    if (OnThreatsChanged != null) OnThreatsChanged();
                }
                catch { }
            }

            // Update Datalink sensor fusion cycle on player aircraft
            bool isPlayer = (CombatHUD.i != null && CombatHUD.i.aircraft == AttachedAircraft);
            if (isPlayer)
            {
                DatalinkNetwork.ProcessDatalinkCycle();
            }

            // Clean up stale Datalink threats (> 3.5s)
            _staleDatalinkList.Clear();
            foreach (var kvp in _datalinkThreats)
            {
                DatalinkThreatEntry dl = kvp.Value;
                if (now - dl.LastPingTime > 3.5f || dl.ThreatUnit == null || dl.ThreatUnit.disabled)
                {
                    _staleDatalinkList.Add(kvp.Key);
                }
                else
                {
                    // Refresh relative bearing as aircraft turns
                    Vector3 fwd = transform.forward;
                    fwd.y = 0.0f;
                    Vector3 dir = (dl.EstimatedPosition - transform.position);
                    dir.y = 0.0f;
                    if (fwd.sqrMagnitude > 0.001f && dir.sqrMagnitude > 0.001f)
                    {
                        float angle = Vector3.SignedAngle(fwd, dir.normalized, Vector3.up);
                        dl.RelativeBearing = angle;
                        float clockAngle = angle < 0.0f ? angle + 360.0f : angle;
                        int clock = Mathf.RoundToInt(clockAngle / 30.0f);
                        if (clock <= 0) clock = 12;
                        if (clock > 12) clock = 12;
                        dl.ClockPosition = clock;
                    }
                }
            }

            for (int i = 0; i < _staleDatalinkList.Count; i++)
            {
                _datalinkThreats.Remove(_staleDatalinkList[i]);
            }
        }

        public RwrThreatEntry GetHighestThreatEntry()
        {
            RwrThreatEntry highestEntry = null;
            RwrThreatState highestState = RwrThreatState.None;

            foreach (var kvp in _threats)
            {
                RwrThreatEntry e = kvp.Value;
                if (e.State > highestState)
                {
                    highestState = e.State;
                    highestEntry = e;
                }
                else if (e.State == highestState && highestEntry != null && e.SignalStrength01 > highestEntry.SignalStrength01)
                {
                    highestEntry = e;
                }
            }

            return highestEntry;
        }

        public RwrThreatState GetHighestThreatState()
        {
            RwrThreatState highest = RwrThreatState.None;
            foreach (var kvp in _threats)
            {
                if (kvp.Value.State > highest)
                {
                    highest = kvp.Value.State;
                }
            }
            return highest;
        }

        public DatalinkThreatEntry GetHighestDatalinkThreat()
        {
            DatalinkThreatEntry closestMissile = null;
            float minDist = float.MaxValue;

            foreach (var kvp in _datalinkThreats)
            {
                DatalinkThreatEntry e = kvp.Value;
                if (e.IsMissile && e.Distance < minDist)
                {
                    minDist = e.Distance;
                    closestMissile = e;
                }
            }

            if (closestMissile != null) return closestMissile;

            foreach (var kvp in _datalinkThreats)
            {
                return kvp.Value;
            }

            return null;
        }
    }
}
