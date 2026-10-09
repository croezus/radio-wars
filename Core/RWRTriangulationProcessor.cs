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

        private struct AlliedBearingObservation
        {
            public Unit Observer;
            public Vector3 Position;
            public Vector3 Bearing;
            public float Timestamp;
        }

        private static readonly Dictionary<FactionHQ, Dictionary<Unit, List<AlliedBearingObservation>>> _factionBearingObs =
            new Dictionary<FactionHQ, Dictionary<Unit, List<AlliedBearingObservation>>>();

        public static float VisualIdentificationRange
        {
            get
            {
                return (RadioWarsConfig.VisualRadarIdentificationRangeMeters != null)
                    ? Mathf.Max(RadioWarsConfig.VisualRadarIdentificationRangeMeters.Value, 100.0f)
                    : 10000.0f;
            }
        }

        public static float GetTargetMemoryDuration(Unit target)
        {
            if (target is Aircraft)
            {
                return (RadioWarsConfig.AircraftMemoryDurationSeconds != null)
                    ? RadioWarsConfig.AircraftMemoryDurationSeconds.Value
                    : 60.0f;
            }
            return (RadioWarsConfig.GroundTargetMemoryDurationSeconds != null)
                ? RadioWarsConfig.GroundTargetMemoryDurationSeconds.Value
                : 180.0f;
        }

        public static void RecordAlliedBearing(FactionHQ hq, Unit emitter, Unit observer, Vector3 observerPos, Vector3 bearing, float timestamp)
        {
            if (hq == null || emitter == null || observer == null) return;
            Dictionary<Unit, List<AlliedBearingObservation>> obsDict;
            if (!_factionBearingObs.TryGetValue(hq, out obsDict))
            {
                obsDict = new Dictionary<Unit, List<AlliedBearingObservation>>();
                _factionBearingObs[hq] = obsDict;
            }

            List<AlliedBearingObservation> list;
            if (!obsDict.TryGetValue(emitter, out list))
            {
                list = new List<AlliedBearingObservation>();
                obsDict[emitter] = list;
            }

            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (timestamp - list[i].Timestamp > 6.0f || list[i].Observer == null || list[i].Observer.disabled)
                {
                    list.RemoveAt(i);
                }
            }

            bool updated = false;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Observer == observer)
                {
                    list[i] = new AlliedBearingObservation
                    {
                        Observer = observer,
                        Position = observerPos,
                        Bearing = bearing,
                        Timestamp = timestamp
                    };
                    updated = true;
                    break;
                }
            }
            if (!updated)
            {
                list.Add(new AlliedBearingObservation
                {
                    Observer = observer,
                    Position = observerPos,
                    Bearing = bearing,
                    Timestamp = timestamp
                });
            }
        }

        public static bool CheckMultiBearingTriangulation(FactionHQ hq, Unit emitter, float now)
        {
            if (hq == null || emitter == null) return false;
            Dictionary<Unit, List<AlliedBearingObservation>> obsDict;
            if (!_factionBearingObs.TryGetValue(hq, out obsDict)) return false;

            List<AlliedBearingObservation> list;
            if (!obsDict.TryGetValue(emitter, out list) || list == null || list.Count < 2) return false;

            for (int i = 0; i < list.Count; i++)
            {
                if (now - list[i].Timestamp > 6.0f || list[i].Observer == null || list[i].Observer.disabled) continue;

                for (int j = i + 1; j < list.Count; j++)
                {
                    if (now - list[j].Timestamp > 6.0f || list[j].Observer == null || list[j].Observer.disabled) continue;

                    float separation = Vector3.Distance(list[i].Position, list[j].Position);
                    if (separation >= 3000.0f)
                    {
                        float angle = Vector3.Angle(list[i].Bearing, list[j].Bearing);
                        if (angle >= 12.0f && angle <= 168.0f)
                        {
                            if (RadioWarsConfig.DebugLogging != null && RadioWarsConfig.DebugLogging.Value)
                            {
                                if (RadioWarsPlugin.Log != null)
                                {
                                    RadioWarsPlugin.Log.LogInfo(string.Format("[RadioWars ESM] Datalink cross-bearing fix on {0}: obs1={1}, obs2={2}, angle={3:F1} deg, baseline={4:F0}m",
                                        emitter.unitName, list[i].Observer != null ? list[i].Observer.unitName : "null", list[j].Observer != null ? list[j].Observer.unitName : "null", angle, separation));
                                }
                            }
                            return true;
                        }
                    }
                }
            }
            return false;
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

                bool isLocalPlayerInit = (CombatHUD.i != null && CombatHUD.i.aircraft != null && playerAircraft == CombatHUD.i.aircraft);
                if (isLocalPlayerInit)
                {
                    track.FirstObservedOwnshipPos = playerAircraft.transform.position;
                    track.FirstObservedBearing = worldBearing;
                    track.FirstObservedTime = now;
                    track.HasInitialObservation = true;
                }

                tracks[emitter] = track;
            }

            bool isLocalPlayer = (CombatHUD.i != null && CombatHUD.i.aircraft != null && playerAircraft == CombatHUD.i.aircraft);
            if (isLocalPlayer && !track.HasInitialObservation)
            {
                track.FirstObservedOwnshipPos = playerAircraft.transform.position;
                track.FirstObservedBearing = worldBearing;
                track.FirstObservedTime = now;
                track.HasInitialObservation = true;
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

            // 3. Cooperative Allied Datalink: active radar track or multi-bearing RWR intersection
            bool enableDatalink = RadioWarsConfig.EnableAlliedDatalink == null || RadioWarsConfig.EnableAlliedDatalink.Value;
            if (enableDatalink)
            {
                RecordAlliedBearing(hq, emitter, playerAircraft, playerPos, worldBearing, now);

                if (!isTriangulated)
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

                    // Multi-station RWR intersection: 2+ allied aircraft cross-bearing at >= 12 degrees
                    if (!isTriangulated && CheckMultiBearingTriangulation(hq, emitter, now))
                    {
                        isTriangulated = true;
                        if (!track.IsTriangulated)
                        {
                            TrackUncertaintyCalculator.RecordTriangulationBoost(track, isMultiStation: true);
                        }
                    }
                }
            }

            // 4. Single-ship synthetic kinematic baseline triangulation (cross-bearing flight maneuver)
            if (!isTriangulated && isLocalPlayer && track.HasInitialObservation)
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
                        if (!track.IsTriangulated)
                        {
                            TrackUncertaintyCalculator.RecordTriangulationBoost(track, isMultiStation: false);
                        }
                    }
                    else if (timeSinceFirst > 30.0f)
                    {
                        track.FirstObservedOwnshipPos = playerPos;
                        track.FirstObservedBearing = worldBearing;
                        track.FirstObservedTime = now;
                    }
                }
            }

            if (isTriangulated)
            {
                track.IsTriangulated = true;
                track.LastTriangulatedTime = now;
            }

            // Process tracking quality and memory status
            ProcessTrackQualityAndPip(track, emitter, playerAircraft, trueDist, visualRange, tier, 0.0f);

            // True coordinate representation without artificial spatial offset (weapon dispersion handles miss error)
            track.TriangulatedWorldPos = emitterPos;
            track.EstimatedCenterWorldPos = emitterPos;
            track.DynamicPipOffset = Vector3.zero;

            // Authoritative CEP calculated from team-wide intelligence, distance, and triangulation geometry
            float memDuration = GetTargetMemoryDuration(emitter);
            float memElapsed = (track.LastActiveDetectionTime > 0f) ? (Time.timeSinceLevelLoad - track.LastActiveDetectionTime) : 0f;
            float rawCep = TrackUncertaintyCalculator.CalculateDispersionRadius(
                trueDist, track.TrackingQuality, track.IsInMemoryState(memDuration), memElapsed, track.IsTriangulated);

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
        /// and maintains the target memory state for lost contacts.
        /// While active contact (visual, radar lock, or RWR pings) is maintained, Q does not decay,
        /// and the 180s (ground) or 60s (air) memory countdown is continuously reset.
        /// </summary>
        public static void ProcessTrackQualityAndPip(
            TriangulationTrack track,
            Unit emitter,
            Aircraft playerAircraft,
            float trueDist,
            float visualRange,
            RwrTier tier,
            float dt = 0.0f)
        {
            if (track == null || emitter == null) return;

            float now = Time.timeSinceLevelLoad;

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

            // Active contact verification:
            // Contact is ACTIVE if visually identified, actively illuminated by radar, OR if our RWR has received a ping within the last sweep interval (6.0s)
            bool hasVisual = (trueDist < visualRange);
            bool hasRadarLock = isDirectlyIlluminated || (timeSinceHit < 6.0f);
            bool hasRecentRwrPing = (now - track.LastUpdateTime < 6.0f);

            bool isActivelyDetected = hasVisual || hasRadarLock || hasRecentRwrPing;

            if (isActivelyDetected)
            {
                // TARGET IS IN ACTIVE CONTACT:
                // Refresh memory timer so the 180s/60s countdown is continuously reset!
                track.HadActiveContact = true;
                track.LastActiveDetectionTime = now;
                track.LastKnownGlobalPosition = GlobalPositionExtensions.GlobalPosition(emitter);
                track.LastKnownHeading = emitter.transform.forward;
                track.TargetPipPosition = emitter.transform.position;

                if (hasVisual)
                {
                    // Direct visual contact (< 10 km): establishes Q >= 0.80
                    TrackUncertaintyCalculator.RecordVisualContact(track);
                }
                else if (isDirectlyIlluminated)
                {
                    // Continuous active radar dwell: refine accuracy towards 1.0 with distance weighting
                    float dwellDt = dt > 0f ? dt : 0.016f;
                    TrackUncertaintyCalculator.RecordRadarDwell(track, dwellDt, trueDist);
                }
                // ZERO DECAY! Quality is completely preserved while active contact (including passive RWR pings) is present!
            }
            else
            {
                // CONTACT IS LOST:
                // No visual, no radar illumination, and no RWR pings for > 6.0 seconds.
                // Target enters the 180s (ground) or 60s (air) memory window.
                // Coordinates are frozen at last known position.
                if (track.HadActiveContact)
                {
                    track.TargetPipPosition = track.LastKnownGlobalPosition.ToLocalPosition();
                }
                else
                {
                    track.TargetPipPosition = emitter.transform.position;
                }

                if (dt > 0f)
                {
                    track.ContinuousDwellSeconds = Mathf.Max(0f, track.ContinuousDwellSeconds - dt);

                    // Continuous linear Q decay over the target's memory window (180s for ground, 60s for air):
                    float memDuration = GetTargetMemoryDuration(emitter);
                    float decayRate = (memDuration > 0.0f) ? (1.0f / memDuration) : (1.0f / 120.0f);

                    if (track.TrackingQuality > 0.0f)
                    {
                        track.TrackingQuality = Mathf.Max(0.0f, track.TrackingQuality - decayRate * dt);
                    }
                }

                // Enforce inactivity memory expiration (wipes track when elapsed > memDuration)
                TrackUncertaintyCalculator.CheckInactivityExpiration(track);
            }

            track.DynamicPipOffset = Vector3.zero;
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

                    float targetMem = GetTargetMemoryDuration(target);
                    if (target == null || target.disabled || track.IsExpired(targetMem))
                    {
                        _staleList.Add(target);
                        continue;
                    }

                    FactionHQ trackHq = (player != null && player.NetworkHQ != null) ? player.NetworkHQ : GetPlayerFactionHQ();
                    if (enableDatalink && CheckMultiBearingTriangulation(trackHq, target, Time.timeSinceLevelLoad))
                    {
                        if (!track.IsTriangulated)
                        {
                            TrackUncertaintyCalculator.RecordTriangulationBoost(track, isMultiStation: true);
                        }
                        track.IsTriangulated = true;
                        track.LastTriangulatedTime = Time.timeSinceLevelLoad;
                    }

                    float dist = (player != null && !player.disabled) ? Vector3.Distance(playerPos, target.transform.position) : 99999f;
                    ProcessTrackQualityAndPip(track, target, player, dist, visualRange, tier, dt);
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
                    float targetMem = GetTargetMemoryDuration(kvp.Key);
                    if (kvp.Key == null || kvp.Key.disabled || kvp.Value.IsExpired(targetMem))
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
            _factionBearingObs.Clear();
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
