using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using RadioWars.Config;
using RadioWars.Components;
using RadioWars.Patches;

namespace RadioWars.Core
{
    /// <summary>
    /// Tracks emitter state, cooperative multi-bearing Datalink triangulation,
    /// single-ship kinematic baseline observations, and distance-proportional spatial offsets.
    /// In v1.6.0, supports continuous radar dwell quality refinement, uncertainty bubble expansion/contraction,
    /// and dynamic Predicted Intercept Point (PIP) displacement for missile guidance.
    /// </summary>
    public class TriangulationTrack
    {
        public Unit EmitterUnit;
        public bool IsTriangulated;
        public float LastUpdateTime;
        public float LastTriangulatedTime;
        public Vector3 TriangulatedWorldPos;       // Target position (live or last known)
        public Vector3 EstimatedCenterWorldPos;    // Estimated center for untriangulated ambiguity circle
        public float UncertaintyRadiusMeters;      // Radius of ambiguity circle in meters

        // Tracking Quality & Radar Illumination
        public float TrackingQuality;             // 0.0 (lost/untracked) to 1.0 (refined pinpoint lock)
        public float ContinuousDwellSeconds;      // Seconds of continuous active radar illumination
        public float LastRadarIlluminatedTime;    // Timestamp of last active radar detection/sweep
        public Vector3 DynamicPipOffset;          // Zero in unified mode (retained for compatibility)
        public Vector3 TargetPipPosition;         // Physical or last known coordinates

        // Discrete tick and cooldown tracking
        public float LastRwrTickTime;
        public float LastVisualTickTime;
        public float LastRadarTickTime;
        public float LastVisualBoostTime;         // Timestamp of last one-time visual intelligence boost (cooldown: 180s)

        // Lost Contact Memory State (120-second retention)
        public bool HadActiveContact;             // Has this target ever been actively identified/detected by radar/optics/datalink?
        public float LastActiveDetectionTime;     // Timestamp of last active positive radar/visual/datalink detection
        public GlobalPosition LastKnownGlobalPosition; // Frozen coordinates when contact was lost
        public Vector3 LastKnownHeading;          // Frozen heading when contact was lost

        public bool IsActivelyDetected
        {
            get
            {
                // Active if positively observed/illuminated within 5.0 seconds
                return (Time.timeSinceLevelLoad - LastActiveDetectionTime < 5.0f);
            }
        }

        public bool IsInMemoryState(float memoryDurationSeconds = 120.0f)
        {
            if (!HadActiveContact) return false;
            if (IsActivelyDetected) return false;
            float elapsed = Time.timeSinceLevelLoad - LastActiveDetectionTime;
            return elapsed >= 0f && elapsed <= memoryDurationSeconds;
        }

        public bool IsExpired(float memoryDurationSeconds = 120.0f)
        {
            if (!HadActiveContact)
            {
                return Time.timeSinceLevelLoad - LastUpdateTime > memoryDurationSeconds;
            }
            return (Time.timeSinceLevelLoad - LastActiveDetectionTime) > memoryDurationSeconds;
        }

        // Single-ship synthetic baseline history
        public Vector3 FirstObservedOwnshipPos;
        public Vector3 FirstObservedBearing;
        public float FirstObservedTime;
        public bool HasInitialObservation;

        // Persistent spatial offset seed for stable displacement
        public Vector3 StableOffsetUnitVector;
        public float SeedAngleRad;
    }

    /// <summary>
    /// Electronic Support Measures (ESM) triangulation & target tracking quality engine.
    /// Evaluates whether radar threats are resolved to pinpoint locations via multi-platform Datalink,
    /// continuous radar dwell, or single-ship maneuver baselines, generating distance-proportional spatial error offsets.
    /// Operates as a universal, symmetric multi-faction network supporting bots, players, and co-op/MP teams.
    /// </summary>
    public static class RWRTriangulationProcessor
    {
        private static readonly Dictionary<FactionHQ, Dictionary<Unit, TriangulationTrack>> _factionTracks = new Dictionary<FactionHQ, Dictionary<Unit, TriangulationTrack>>();
        private static readonly Dictionary<Unit, TriangulationTrack> _fallbackTracks = new Dictionary<Unit, TriangulationTrack>();
        private static readonly List<Unit> _staleList = new List<Unit>();
        private static readonly HashSet<Unit> s_alliedDetectedTargets = new HashSet<Unit>();
        private static FactionHQ _lastPlayerHQ = null;

        public static float VisualIdentificationRange
        {
            get
            {
                return (RadioWarsConfig.VisualRadarIdentificationRangeMeters != null)
                    ? Mathf.Max(RadioWarsConfig.VisualRadarIdentificationRangeMeters.Value, 100.0f)
                    : 2500.0f;
            }
        }

        public static FactionHQ GetPlayerFactionHQ()
        {
            if (CombatHUD.i != null && CombatHUD.i.aircraft != null && CombatHUD.i.aircraft.NetworkHQ != null)
            {
                _lastPlayerHQ = CombatHUD.i.aircraft.NetworkHQ;
            }
            if (_lastPlayerHQ == null && DynamicMap.i != null && DynamicMap.i.HQ != null)
            {
                _lastPlayerHQ = DynamicMap.i.HQ;
            }
            if (_lastPlayerHQ == null)
            {
                try
                {
                    Faction localFaction;
                    if (GameManager.GetLocalFaction(out localFaction) && localFaction != null)
                    {
                        _lastPlayerHQ = FactionRegistry.HQFromFaction(localFaction);
                    }
                }
                catch { }
            }
            return _lastPlayerHQ;
        }

        public static Dictionary<Unit, TriangulationTrack> GetTrackDictionary(FactionHQ faction)
        {
            if (faction == null) faction = GetPlayerFactionHQ();
            if (faction == null) return _fallbackTracks;

            Dictionary<Unit, TriangulationTrack> dict;
            if (!_factionTracks.TryGetValue(faction, out dict))
            {
                dict = new Dictionary<Unit, TriangulationTrack>();
                _factionTracks[faction] = dict;
            }
            return dict;
        }

        public static TriangulationTrack GetTrack(FactionHQ faction, Unit emitter)
        {
            if (emitter == null) return null;
            Dictionary<Unit, TriangulationTrack> tracks = GetTrackDictionary(faction);
            TriangulationTrack track;
            if (tracks.TryGetValue(emitter, out track))
            {
                return track;
            }

            // If unit is currently tracked by player radar or allied datalink, auto-register track
            if (CombatHUD.i != null && CombatHUD.i.aircraft != null)
            {
                Aircraft player = CombatHUD.i.aircraft;
                Radar playerRadar = CombatHUDPatches.GetCachedPlayerRadar(player);
                if (playerRadar != null && playerRadar.IsOperational())
                {
                    if (playerRadar.CheckIsTarget(emitter) || (playerRadar.detectedTargets != null && playerRadar.detectedTargets.Contains(emitter)))
                    {
                        RecordRadarIllumination(faction, emitter, playerRadar);
                        tracks.TryGetValue(emitter, out track);
                        return track;
                    }
                }
            }

            return null;
        }

        public static TriangulationTrack GetTrack(Unit emitter)
        {
            return GetTrack(GetPlayerFactionHQ(), emitter);
        }

        public static bool HasTrack(FactionHQ faction, Unit emitter)
        {
            if (emitter == null) return false;
            Dictionary<Unit, TriangulationTrack> tracks = GetTrackDictionary(faction);
            return tracks != null && tracks.ContainsKey(emitter);
        }

        public static bool HasTrack(Unit emitter)
        {
            return HasTrack(GetPlayerFactionHQ(), emitter);
        }

        private static readonly FieldInfo f_onDiscoverUnit = AccessTools.Field(typeof(FactionHQ), "onDiscoverUnit");

        /// <summary>
        /// Registers a discovered or actively tracked target into the faction HQ tracking database.
        /// Synchronizes intelligence across all friendly units, tactical maps, and HUD marker managers.
        /// Ensures contacts persist across player deaths and respawns.
        /// </summary>
        public static void RegisterTargetInFactionDatabase(FactionHQ faction, Unit target)
        {
            if (faction == null || target == null || target.disabled || target is Missile) return;
            if (target.NetworkHQ != null && target.NetworkHQ == faction) return;

            try
            {
                if (faction.trackingDatabase != null)
                {
                    if (!faction.trackingDatabase.ContainsKey(target.persistentID))
                    {
                        TrackingInfo info = new TrackingInfo(target);
                        faction.trackingDatabase.Add(target.persistentID, info);
                        try { target.onDisableUnit += faction.DeregisterTrackedUnit; } catch {}
                        
                        try
                        {
                            Action<PersistentID> onDisc = f_onDiscoverUnit != null ? (Action<PersistentID>)f_onDiscoverUnit.GetValue(faction) : null;
                            if (onDisc != null) onDisc(target.persistentID);
                        }
                        catch {}

                        if (DynamicMap.i != null)
                        {
                            try { DynamicMap.i.AddIcon(target.persistentID); } catch {}
                        }
                    }
                    else
                    {
                        TrackingInfo existing = faction.trackingDatabase[target.persistentID];
                        if (existing != null)
                        {
                            existing.UpdateInfo(GlobalPositionExtensions.GlobalPosition(target));
                        }
                    }
                }

                // Call vanilla RpcUpdateTrackingInfo for multiplayer synchronization
                faction.RpcUpdateTrackingInfo(target.persistentID);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RadioWars] RegisterTargetInFactionDatabase error: " + ex.Message);
            }
        }

        /// <summary>
        /// Registers or refreshes active visual reconnaissance contact on a target from any operational friendly unit (bot or player).
        /// Fuses high-confidence visual intelligence into the faction's Tactical Datalink.
        /// </summary>
        public static void RecordVisualContact(FactionHQ faction, Unit target, Unit observer)
        {
            if (target == null || target.disabled || target is Missile) return;
            if (faction == null) faction = (observer != null) ? observer.NetworkHQ : GetPlayerFactionHQ();
            if (faction == null) return;

            // Only track valid radar threats (excludes hangars, static obstacles, simple wheeled vehicles)
            if (!CombatHUDPatches.IsTrackableThreat(target, faction)) return;

            float now = Time.timeSinceLevelLoad;
            Dictionary<Unit, TriangulationTrack> tracks = GetTrackDictionary(faction);
            TriangulationTrack track;
            if (!tracks.TryGetValue(target, out track))
            {
                track = new TriangulationTrack();
                track.EmitterUnit = target;
                int seed = target.GetInstanceID() ^ (target.unitName != null ? target.unitName.GetHashCode() : 0);
                float angle = Mathf.Abs((seed % 3600) * 0.1f) * Mathf.Deg2Rad;
                track.SeedAngleRad = angle;
                track.StableOffsetUnitVector = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 obsPos = (observer != null) ? observer.transform.position : target.transform.position;
                track.FirstObservedOwnshipPos = obsPos;
                track.FirstObservedBearing = (observer != null) ? (target.transform.position - obsPos).normalized : target.transform.forward;
                track.FirstObservedTime = now;
                track.HasInitialObservation = true;
                tracks[target] = track;
            }

            TrackUncertaintyCalculator.RecordVisualContact(track);
            track.LastKnownGlobalPosition = GlobalPositionExtensions.GlobalPosition(target);
            track.LastKnownHeading = target.transform.forward;
            track.TargetPipPosition = target.transform.position;
            track.LastUpdateTime = now;

            // Register with FactionHQ tracking database for persistent datalink memory across death/respawn
            RegisterTargetInFactionDatabase(faction, target);
        }

        public static void RecordVisualContact(Unit target)
        {
            Aircraft player = (CombatHUD.i != null) ? CombatHUD.i.aircraft : null;
            RecordVisualContact(GetPlayerFactionHQ(), target, player);
        }

        /// <summary>
        /// Registers or refreshes active radar illumination on a target from an allied radar.
        /// Refines tracking quality cumulatively over consecutive radar sweeps.
        /// Heavy ground/naval radar stations inject higher energy pulses to compensate for rotation intervals.
        /// </summary>
        public static void RecordRadarIllumination(FactionHQ faction, Unit target, Radar sourceRadar)
        {
            if (target == null || target.disabled || target is Missile || sourceRadar == null) return;
            Unit sourceUnit = sourceRadar.GetAttachedUnit();
            if (faction == null) faction = (sourceUnit != null) ? sourceUnit.NetworkHQ : GetPlayerFactionHQ();
            if (faction == null) return;

            // Only track valid radar threats (excludes hangars, static obstacles, simple wheeled vehicles)
            if (!CombatHUDPatches.IsTrackableThreat(target, faction)) return;

            float now = Time.timeSinceLevelLoad;
            Dictionary<Unit, TriangulationTrack> tracks = GetTrackDictionary(faction);
            TriangulationTrack track;
            if (!tracks.TryGetValue(target, out track))
            {
                track = new TriangulationTrack();
                track.EmitterUnit = target;
                int seed = target.GetInstanceID() ^ (target.unitName != null ? target.unitName.GetHashCode() : 0);
                float angle = Mathf.Abs((seed % 3600) * 0.1f) * Mathf.Deg2Rad;
                track.SeedAngleRad = angle;
                track.StableOffsetUnitVector = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                track.TrackingQuality = 0.0f; // Initial coarse track accumulated via RecordRadarSweep
                track.ContinuousDwellSeconds = 1.0f;
                track.LastRadarIlluminatedTime = now;
                Vector3 srcPos = (sourceUnit != null) ? sourceUnit.transform.position : target.transform.position;
                track.FirstObservedOwnshipPos = srcPos;
                track.FirstObservedBearing = (target.transform.position - srcPos).normalized;
                track.FirstObservedTime = now;
                track.HasInitialObservation = true;
                tracks[target] = track;
            }

            // Team-wide intelligence accumulation via radar sweep
            float sweepDist = (sourceUnit != null) ? Vector3.Distance(sourceUnit.transform.position, target.transform.position) : 30000f;
            bool isOwnship = (CombatHUD.i != null && CombatHUD.i.aircraft != null && sourceUnit == CombatHUD.i.aircraft);
            TrackUncertaintyCalculator.RecordRadarSweep(track, sweepDist, isOwnship);

            // Heavy ground/naval radar stations inject an extra boost (+0.15) to compensate for slow mechanical rotation
            bool isHeavyStation = (sourceRadar.RadarParameters.maxRange > 50000f) ||
                                  (sourceUnit != null && sourceUnit.definition != null && sourceUnit.definition.code == "RDR");
            if (isHeavyStation)
            {
                track.TrackingQuality = Mathf.Clamp01(track.TrackingQuality + 0.15f);
            }

            track.LastKnownGlobalPosition = GlobalPositionExtensions.GlobalPosition(target);
            track.LastKnownHeading = target.transform.forward;
            track.LastUpdateTime = now;
            track.TargetPipPosition = target.transform.position;

            // Register with FactionHQ tracking database for persistent datalink memory
            RegisterTargetInFactionDatabase(faction, target);
        }

        public static void RecordRadarIllumination(Unit target, Radar sourceRadar)
        {
            Unit sourceUnit = (sourceRadar != null) ? sourceRadar.GetAttachedUnit() : null;
            FactionHQ hq = (sourceUnit != null && sourceUnit.NetworkHQ != null) ? sourceUnit.NetworkHQ : GetPlayerFactionHQ();
            RecordRadarIllumination(hq, target, sourceRadar);
        }

        public static void UpdateTrack(Unit emitter, Aircraft playerAircraft, Vector3 worldBearing, RwrThreatState state, float signalStrength01)
        {
            if (emitter == null || playerAircraft == null || emitter.disabled || playerAircraft.disabled)
            {
                return;
            }

            FactionHQ hq = playerAircraft.NetworkHQ != null ? playerAircraft.NetworkHQ : GetPlayerFactionHQ();
            if (hq != null && emitter.NetworkHQ != null && emitter.NetworkHQ == hq)
            {
                return; // Friendly radar emission, never treat as threat
            }
            Dictionary<Unit, TriangulationTrack> tracks = GetTrackDictionary(hq);

            float now = Time.timeSinceLevelLoad;
            TriangulationTrack track;
            if (!tracks.TryGetValue(emitter, out track))
            {
                track = new TriangulationTrack();
                track.EmitterUnit = emitter;

                // Deterministic pseudo-random seed based on emitter persistent identity
                int seed = emitter.GetInstanceID() ^ (emitter.unitName != null ? emitter.unitName.GetHashCode() : 0);
                float angle = Mathf.Abs((seed % 3600) * 0.1f) * Mathf.Deg2Rad;
                track.SeedAngleRad = angle;
                track.StableOffsetUnitVector = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                track.TrackingQuality = 0.0f; // Initial quality starts at 0, accumulates via sensor ticks

                track.FirstObservedOwnshipPos = playerAircraft.transform.position;
                track.FirstObservedBearing = worldBearing;
                track.FirstObservedTime = now;
                track.HasInitialObservation = true;

                tracks[emitter] = track;
            }

            track.LastUpdateTime = now;
            track.HadActiveContact = true;
            track.LastActiveDetectionTime = now;
            track.LastKnownGlobalPosition = GlobalPositionExtensions.GlobalPosition(emitter);
            track.LastKnownHeading = emitter.transform.forward;
            track.TargetPipPosition = emitter.transform.position;

            // Compute hardware tier
            PhysRWRReceiver rwr = PhysRWRReceiver.Get(playerAircraft);
            RwrTier tier = rwr != null ? rwr.Capabilities.Tier : RwrTier.Tier2_Standard;

            Vector3 playerPos = playerAircraft.transform.position;
            Vector3 emitterPos = emitter.transform.position;
            float trueDist = Vector3.Distance(playerPos, emitterPos);

            // Record RWR detection tick with distance coefficient and receiver tier weighting
            TrackUncertaintyCalculator.RecordRWRDetection(track, trueDist, tier, true);

            bool isTriangulated = false;

            // 1. Direct active friendly radar lock check
            Radar playerRadar = CombatHUDPatches.GetCachedPlayerRadar(playerAircraft);
            if (playerRadar != null && playerRadar.IsOperational())
            {
                if (playerRadar.CheckIsTarget(emitter) || (playerRadar.detectedTargets != null && playerRadar.detectedTargets.Contains(emitter)))
                {
                    isTriangulated = true;
                    track.LastRadarIlluminatedTime = now;
                }
            }

            // 2. Direct visual range identification
            float visualRange = VisualIdentificationRange;
            if (!isTriangulated && trueDist < visualRange)
            {
                isTriangulated = true;
            }

            // 3. Cooperative Allied Datalink: confirmed active radar track by ally
            bool enableDatalink = RadioWarsConfig.EnableAlliedDatalink == null || RadioWarsConfig.EnableAlliedDatalink.Value;
            if (!isTriangulated && enableDatalink)
            {
                if (s_alliedDetectedTargets.Contains(emitter))
                {
                    isTriangulated = true;
                    track.LastRadarIlluminatedTime = now;
                }
                else
                {
                    List<Radar> alliedRadars = DatalinkNetwork.RegisteredRadars;
                    FactionHQ playerHq = playerAircraft.NetworkHQ != null ? playerAircraft.NetworkHQ : GetPlayerFactionHQ();

                    for (int i = 0; i < alliedRadars.Count; i++)
                    {
                        Radar allyRadar = alliedRadars[i];
                        if (allyRadar == null || !allyRadar.IsOperational()) continue;

                        Unit allyUnit = DatalinkNetwork.GetRadarAttachedUnit(allyRadar);
                        if (allyUnit == null || allyUnit == playerAircraft || allyUnit.disabled) continue;

                        FactionHQ allyHq = DatalinkNetwork.GetUnitFactionHQ(allyUnit);
                        if (playerHq != null && allyHq != null && allyHq != playerHq) continue;

                        // An allied radar only triangulates if it actively detects/tracks the emitter
                        if (allyRadar.detectedTargets != null && allyRadar.detectedTargets.Contains(emitter))
                        {
                            isTriangulated = true;
                            track.LastRadarIlluminatedTime = now;
                            s_alliedDetectedTargets.Add(emitter);
                            break;
                        }
                    }
                }
            }

            // 4. Single-ship synthetic kinematic baseline triangulation (cross-bearing flight maneuver)
            if (!isTriangulated && track.HasInitialObservation)
            {
                float timeSinceFirst = now - track.FirstObservedTime;
                if (timeSinceFirst > 3.0f)
                {
                    Vector3 deltaShip = playerPos - track.FirstObservedOwnshipPos;
                    float alongRay = Vector3.Dot(deltaShip, track.FirstObservedBearing);
                    Vector3 orthoVec = deltaShip - track.FirstObservedBearing * alongRay;
                    float orthoDist = orthoVec.magnitude;

                    float bearingDelta = Vector3.Angle(track.FirstObservedBearing, worldBearing);

                    // Tier-specific kinematic requirements
                    float reqBaseline = (tier == RwrTier.Tier3_Advanced) ? 1800.0f : ((tier == RwrTier.Tier1_Basic) ? 3500.0f : 2500.0f);
                    float reqAngle = (tier == RwrTier.Tier3_Advanced) ? 6.0f : ((tier == RwrTier.Tier1_Basic) ? 14.0f : 8.5f);

                    if (orthoDist >= reqBaseline && bearingDelta >= reqAngle)
                    {
                        isTriangulated = true;
                    }
                }
            }

            track.IsTriangulated = isTriangulated;
            if (isTriangulated)
            {
                track.LastTriangulatedTime = now;
            }

            // Process tracking quality and memory status
            ProcessTrackQualityAndPip(track, emitter, playerAircraft, trueDist, visualRange, tier);

            // ESM triangulation error and ambiguity circle calculation for map display
            float maxCapMeters = (tier == RwrTier.Tier1_Basic) ? 1000.0f : ((tier == RwrTier.Tier3_Advanced) ? 200.0f : 500.0f);
            float tierRate = (tier == RwrTier.Tier1_Basic) ? 0.012f : ((tier == RwrTier.Tier3_Advanced) ? 0.003f : 0.006f);
            float customErrMult = RadioWarsConfig.RWRTriangulationErrorFactor != null
                ? Mathf.Clamp(RadioWarsConfig.RWRTriangulationErrorFactor.Value / 0.035f, 0.2f, 2.5f)
                : 1.0f;
            float rawError = trueDist * tierRate * customErrMult;
            float errorMagnitude = Mathf.Min(rawError, maxCapMeters * customErrMult);
            if (trueDist < visualRange)
            {
                errorMagnitude *= Mathf.Clamp01(trueDist / visualRange);
            }

            Vector3 offset = track.StableOffsetUnitVector * errorMagnitude;
            track.TriangulatedWorldPos = isTriangulated ? emitterPos : (emitterPos + offset);
            track.EstimatedCenterWorldPos = track.TriangulatedWorldPos;

            // Authoritative CEP calculated from team-wide intelligence, distance, and triangulation geometry
            float memElapsed = (track.LastActiveDetectionTime > 0f) ? (Time.timeSinceLevelLoad - track.LastActiveDetectionTime) : 0f;
            float rawCep = TrackUncertaintyCalculator.CalculateDispersionRadius(
                trueDist, track.TrackingQuality, track.IsInMemoryState(120.0f), memElapsed, isTriangulated);

            float sigFactor = 1.0f + (1.0f - Mathf.Clamp01(signalStrength01)) * 0.5f;
            float circleScale = RadioWarsConfig.RWRUncertaintyCircleScale != null
                ? Mathf.Clamp(RadioWarsConfig.RWRUncertaintyCircleScale.Value, 0.2f, 3.0f)
                : 1.0f;
            track.UncertaintyRadiusMeters = Mathf.Max(15.0f, rawCep * sigFactor * circleScale);

            // Register target into faction tracking database for persistent memory
            RegisterTargetInFactionDatabase(hq, emitter);
        }

        /// <summary>
        /// Computes TrackingQuality accumulation via radar dwell or decay via passive RWR/loss of lock,
        /// and maintains the 120-second Last Known Position memory state for lost contacts.
        /// </summary>
        public static void ProcessTrackQualityAndPip(
            TriangulationTrack track,
            Unit emitter,
            Aircraft playerAircraft,
            float trueDist,
            float visualRange,
            RwrTier tier)
        {
            if (track == null || emitter == null) return;

            float now = Time.timeSinceLevelLoad;
            float dt = Mathf.Clamp(now - track.LastUpdateTime, 0.001f, 0.5f);

            // Check if actively illuminated by ownship radar
            bool ownshipIlluminating = false;
            Radar playerRadar = (playerAircraft != null) ? CombatHUDPatches.GetCachedPlayerRadar(playerAircraft) : null;
            if (playerRadar != null && playerRadar.IsOperational())
            {
                if (playerRadar.CheckIsTarget(emitter) || (playerRadar.detectedTargets != null && playerRadar.detectedTargets.Contains(emitter)))
                {
                    ownshipIlluminating = true;
                    track.LastRadarIlluminatedTime = now;
                }
            }

            // Check if actively illuminated by any allied datalink radar
            bool allyIlluminating = false;
            bool enableDatalink = RadioWarsConfig.EnableAlliedDatalink == null || RadioWarsConfig.EnableAlliedDatalink.Value;
            if (!ownshipIlluminating && enableDatalink)
            {
                if (s_alliedDetectedTargets.Contains(emitter))
                {
                    allyIlluminating = true;
                    track.LastRadarIlluminatedTime = now;
                }
            }

            float timeSinceHit = now - track.LastRadarIlluminatedTime;
            bool isDirectlyIlluminated = ownshipIlluminating || allyIlluminating;

            float refineTime = (RadioWarsConfig.RadarTrackingRefineTimeSeconds != null)
                ? Mathf.Max(RadioWarsConfig.RadarTrackingRefineTimeSeconds.Value, 0.5f)
                : 3.0f;
            float decayTime = (RadioWarsConfig.RadarTrackingDecayTimeSeconds != null)
                ? Mathf.Max(RadioWarsConfig.RadarTrackingDecayTimeSeconds.Value, 1.0f)
                : 10.0f;

            if (trueDist < visualRange)
            {
                // Direct visual contact: instantaneous 100% precision
                TrackUncertaintyCalculator.RecordVisualContact(track);
            }
            else if (isDirectlyIlluminated)
            {
                // Continuous active radar dwell: refine accuracy towards 1.0 with distance weighting
                TrackUncertaintyCalculator.RecordRadarDwell(track, dt, trueDist);
            }
            else if (timeSinceHit < 6.0f)
            {
                // ZERO-DECAY RETENTION WINDOW:
                // Rotating radars (e.g. RadarStation1, RadarSam1) sweep every 3-5 seconds.
                // Within this 6-second memory window, signal quality does NOT decay between antenna revolutions!
            }
            else
            {
                // Lost radar track for > 6.0s: gradual decay of continuous dwell time
                track.ContinuousDwellSeconds = Mathf.Max(0f, track.ContinuousDwellSeconds - dt);

                // 120-Second Sensor Memory Rollback:
                // If no sensor has detected this target for 120 seconds, resets intelligence progress to 0.0
                TrackUncertaintyCalculator.CheckInactivityExpiration(track);
            }

            track.DynamicPipOffset = Vector3.zero;

            // Update Active Detection vs Last Known Position Memory State
            bool hasActiveLock = (trueDist < visualRange) || isDirectlyIlluminated || (timeSinceHit < 6.0f);
            if (hasActiveLock)
            {
                track.HadActiveContact = true;
                track.LastActiveDetectionTime = now;
                track.LastKnownGlobalPosition = GlobalPositionExtensions.GlobalPosition(emitter);
                track.LastKnownHeading = emitter.transform.forward;
                track.TargetPipPosition = emitter.transform.position;
            }
            else
            {
                if (track.HadActiveContact)
                {
                    // Freeze target coordinates at point of lost contact
                    track.TargetPipPosition = track.LastKnownGlobalPosition.ToLocalPosition();
                }
                else
                {
                    track.TargetPipPosition = emitter.transform.position;
                }
            }
        }

        private static readonly List<Dictionary<Unit, TriangulationTrack>> _allDictsBuffer = new List<Dictionary<Unit, TriangulationTrack>>();

        /// <summary>
        /// Frame-by-frame update called from RadioWarsPlugin.Update().
        /// Continuously updates tracking quality, decays passive tracks, prunes dead units,
        /// and dynamically recalculates smooth fake target positions following moving aircraft in real-time.
        /// Runs universally across all factions regardless of player status.
        /// </summary>
        public static void UpdateAllTracks(float dt)
        {
            _allDictsBuffer.Clear();
            foreach (var kvp in _factionTracks)
            {
                if (kvp.Value != null && kvp.Value.Count > 0)
                {
                    _allDictsBuffer.Add(kvp.Value);
                }
            }
            if (_fallbackTracks.Count > 0)
            {
                _allDictsBuffer.Add(_fallbackTracks);
            }

            if (_allDictsBuffer.Count == 0) return;

            Aircraft player = (CombatHUD.i != null) ? CombatHUD.i.aircraft : null;
            Vector3 playerPos = (player != null && !player.disabled) ? player.transform.position : Vector3.zero;
            PhysRWRReceiver rwr = (player != null) ? PhysRWRReceiver.Get(player) : null;
            RwrTier tier = (rwr != null) ? rwr.Capabilities.Tier : RwrTier.Tier2_Standard;
            float visualRange = VisualIdentificationRange;

            s_alliedDetectedTargets.Clear();
            bool enableDatalink = RadioWarsConfig.EnableAlliedDatalink == null || RadioWarsConfig.EnableAlliedDatalink.Value;
            if (enableDatalink)
            {
                FactionHQ playerHq = (player != null && player.NetworkHQ != null) ? player.NetworkHQ : GetPlayerFactionHQ();
                List<Radar> alliedRadars = DatalinkNetwork.RegisteredRadars;
                for (int i = 0; i < alliedRadars.Count; i++)
                {
                    Radar allyRadar = alliedRadars[i];
                    if (allyRadar == null || !allyRadar.IsOperational()) continue;
                    Unit allyUnit = DatalinkNetwork.GetRadarAttachedUnit(allyRadar);
                    if (allyUnit == player || (allyUnit != null && allyUnit.disabled)) continue;

                    FactionHQ allyHq = allyUnit != null ? DatalinkNetwork.GetUnitFactionHQ(allyUnit) : null;
                    if (playerHq != null && allyHq != null && allyHq != playerHq) continue;

                    if (allyRadar.detectedTargets != null)
                    {
                        var targets = allyRadar.detectedTargets;
                        for (int t = 0; t < targets.Count; t++)
                        {
                            Unit u = targets[t];
                            if (u != null)
                            {
                                s_alliedDetectedTargets.Add(u);
                            }
                        }
                    }
                }

                // Also query native playerHq radars for ground stations and SAM sites
                List<Radar> hqRadars = DatalinkNetwork.GetHqRadars(playerHq);
                if (hqRadars != null)
                {
                    for (int i = 0; i < hqRadars.Count; i++)
                    {
                        Radar allyRadar = hqRadars[i];
                        if (allyRadar == null || !allyRadar.IsOperational()) continue;
                        Unit allyUnit = DatalinkNetwork.GetRadarAttachedUnit(allyRadar);
                        if (allyUnit == player || (allyUnit != null && allyUnit.disabled)) continue;

                        if (allyRadar.detectedTargets != null)
                        {
                            var targets = allyRadar.detectedTargets;
                            for (int t = 0; t < targets.Count; t++)
                            {
                                Unit u = targets[t];
                                if (u != null)
                                {
                                    s_alliedDetectedTargets.Add(u);
                                }
                            }
                        }
                    }
                }
            }

            float memDuration = (RadioWarsConfig.TargetMemoryDurationSeconds != null)
                ? RadioWarsConfig.TargetMemoryDurationSeconds.Value
                : 120.0f;

            for (int d = 0; d < _allDictsBuffer.Count; d++)
            {
                Dictionary<Unit, TriangulationTrack> dict = _allDictsBuffer[d];
                _staleList.Clear();

                foreach (var kvp in dict)
                {
                    Unit target = kvp.Key;
                    TriangulationTrack track = kvp.Value;

                    if (target == null || target.disabled || track.IsExpired(memDuration))
                    {
                        _staleList.Add(target);
                        continue;
                    }

                    float dist = (player != null && !player.disabled) ? Vector3.Distance(playerPos, target.transform.position) : 99999f;
                    ProcessTrackQualityAndPip(track, target, player, dist, visualRange, tier);
                }

                for (int i = 0; i < _staleList.Count; i++)
                {
                    dict.Remove(_staleList[i]);
                }
            }
        }

        public static void RemoveTrack(Unit emitter)
        {
            if (emitter == null) return;
            foreach (var kvp in _factionTracks)
            {
                if (kvp.Value != null) kvp.Value.Remove(emitter);
            }
            _fallbackTracks.Remove(emitter);
        }

        public static void PruneStaleTracks(float maxAgeSeconds = 120.0f)
        {
            float memDuration = (RadioWarsConfig.TargetMemoryDurationSeconds != null)
                ? RadioWarsConfig.TargetMemoryDurationSeconds.Value
                : maxAgeSeconds;

            _allDictsBuffer.Clear();
            foreach (var kvp in _factionTracks)
            {
                if (kvp.Value != null) _allDictsBuffer.Add(kvp.Value);
            }
            _allDictsBuffer.Add(_fallbackTracks);

            for (int d = 0; d < _allDictsBuffer.Count; d++)
            {
                Dictionary<Unit, TriangulationTrack> dict = _allDictsBuffer[d];
                _staleList.Clear();
                foreach (var kvp in dict)
                {
                    if (kvp.Key == null || kvp.Key.disabled || kvp.Value.IsExpired(memDuration))
                    {
                        _staleList.Add(kvp.Key);
                    }
                }
                for (int i = 0; i < _staleList.Count; i++)
                {
                    dict.Remove(_staleList[i]);
                }
            }
        }

        public static void ClearAll()
        {
            foreach (var kvp in _factionTracks)
            {
                if (kvp.Value != null) kvp.Value.Clear();
            }
            _factionTracks.Clear();
            _fallbackTracks.Clear();
            _staleList.Clear();
            s_alliedDetectedTargets.Clear();
            _allDictsBuffer.Clear();
            _lastPlayerHQ = null;
        }
    }

    /// <summary>
    /// Backward-compatibility alias for RWRTriangulationProcessor.
    /// </summary>
    public static class RwrTriangulationProcessor
    {
        public static float VisualIdentificationRange { get { return RWRTriangulationProcessor.VisualIdentificationRange; } }
        public static TriangulationTrack GetTrack(Unit emitter) { return RWRTriangulationProcessor.GetTrack(emitter); }
        public static TriangulationTrack GetTrack(FactionHQ faction, Unit emitter) { return RWRTriangulationProcessor.GetTrack(faction, emitter); }
        public static bool HasTrack(Unit emitter) { return RWRTriangulationProcessor.HasTrack(emitter); }
        public static bool HasTrack(FactionHQ faction, Unit emitter) { return RWRTriangulationProcessor.HasTrack(faction, emitter); }
        public static void UpdateTrack(Unit emitter, Aircraft ownship, Vector3 observedBearing, RwrThreatState state, float signalStrength01)
        {
            RWRTriangulationProcessor.UpdateTrack(emitter, ownship, observedBearing, state, signalStrength01);
        }
        public static void RecordRadarIllumination(Unit targetUnit, Radar radar)
        {
            RWRTriangulationProcessor.RecordRadarIllumination(targetUnit, radar);
        }
        public static void RecordRadarIllumination(FactionHQ faction, Unit targetUnit, Radar radar)
        {
            RWRTriangulationProcessor.RecordRadarIllumination(faction, targetUnit, radar);
        }
        public static void RecordVisualContact(FactionHQ faction, Unit target, Unit observer)
        {
            RWRTriangulationProcessor.RecordVisualContact(faction, target, observer);
        }
        public static void RecordVisualContact(Unit target)
        {
            RWRTriangulationProcessor.RecordVisualContact(target);
        }
        public static void UpdateAllTracks(float dt) { RWRTriangulationProcessor.UpdateAllTracks(dt); }
        public static void PruneStaleTracks(float maxAgeSeconds) { RWRTriangulationProcessor.PruneStaleTracks(maxAgeSeconds); }
        public static void ClearAll() { RWRTriangulationProcessor.ClearAll(); }
    }
}
