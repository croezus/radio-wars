using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using RadioWars.Config;
using RadioWars.Components;

namespace RadioWars.Core
{
    public class DatalinkThreatEntry
    {
        public Unit SourceAlliedUnit;
        public Unit ThreatUnit;
        public Vector3 EstimatedPosition;
        public Vector3 EstimatedVelocity;
        public Vector3 LastDirection;
        public float RelativeBearing;
        public int ClockPosition;
        public float Distance;
        public float LastPingTime;
        public bool IsMissile;
        public string SourceName;
    }

    public class DatalinkNodeSnapshot
    {
        public Unit Unit;
        public string UnitName;
        public string Role;
        public Vector3 Position;
        public bool HasRadar;
        public bool IsAirborne;
        public int SharedRadarCount;
        public int SharedVisualCount;
    }

    public class SharedRadarTrackSnapshot
    {
        public Unit SourceUnit;
        public string SourceName;
        public Unit TargetUnit;
        public string TargetName;
        public Vector3 SourcePos;
        public Vector3 TargetPos;
        public float SlantRangeMeters;
        public float Timestamp;
    }

    public class SharedVisualContactSnapshot
    {
        public Unit SpotterUnit;
        public string SpotterName;
        public Unit TargetUnit;
        public string TargetName;
        public Vector3 SpotterPos;
        public Vector3 TargetPos;
        public float DistanceMeters;
        public float Timestamp;
    }

    public class RelayedMissileAlertSnapshot
    {
        public Unit RadarUnit;
        public string RadarName;
        public Missile MissileUnit;
        public Vector3 MissilePos;
        public Vector3 MissileVel;
        public float DistToPlayer;
        public float Timestamp;
    }

    public class DatalinkNetworkTelemetry
    {
        public bool NetworkActive;
        public FactionHQ PlayerHQ;
        public string FactionName = "Unknown";
        public float LastCycleTimestamp;
        public float CycleIntervalSeconds = 0.35f;
        public int TotalCyclesProcessed;

        public readonly List<DatalinkNodeSnapshot> Nodes = new List<DatalinkNodeSnapshot>();
        public readonly List<SharedRadarTrackSnapshot> SharedRadarTracks = new List<SharedRadarTrackSnapshot>();
        public readonly List<SharedVisualContactSnapshot> SharedVisualContacts = new List<SharedVisualContactSnapshot>();
        public readonly List<RelayedMissileAlertSnapshot> RelayedMissileAlerts = new List<RelayedMissileAlertSnapshot>();
    }

    /// <summary>
    /// Cooperative Air Defense Network.
    /// Fuses radar tracking data across allied units (AWACS, fighters, SAM batteries, warships)
    /// and broadcasts Tactical Datalink tracks to friendly aircraft, warning them of quiet/stealthy ARH missiles.
    /// </summary>
    public static class DatalinkNetwork
    {
        private static readonly Func<Radar, float> f_radarCone = CreateFieldGetter<Radar, float>("radarCone");
        private static readonly Func<FactionHQ, List<Radar>> f_hqRadars = CreateFieldGetter<FactionHQ, List<Radar>>("radars");

        public static List<Radar> GetHqRadars(FactionHQ hq)
        {
            if (hq == null || f_hqRadars == null) return null;
            return f_hqRadars(hq);
        }

        private static Func<TTarget, TField> CreateFieldGetter<TTarget, TField>(string fieldName)
        {
            try
            {
                FieldInfo fi = AccessTools.Field(typeof(TTarget), fieldName);
                if (fi == null) return null;
                System.Reflection.Emit.DynamicMethod dm = new System.Reflection.Emit.DynamicMethod("Get_" + typeof(TTarget).Name + "_" + fieldName,
                    typeof(TField), new Type[] { typeof(TTarget) }, typeof(TTarget), true);
                var il = dm.GetILGenerator();
                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
                il.Emit(System.Reflection.Emit.OpCodes.Ldfld, fi);
                il.Emit(System.Reflection.Emit.OpCodes.Ret);
                return (Func<TTarget, TField>)dm.CreateDelegate(typeof(Func<TTarget, TField>));
            }
            catch { return null; }
        }

        private static readonly List<Missile> _activeMissiles = new List<Missile>();
        public static List<Missile> ActiveMissiles { get { return _activeMissiles; } }
        private static readonly List<Radar> _registeredRadars = new List<Radar>();
        public static List<Radar> RegisteredRadars { get { return _registeredRadars; } }
        private static float _lastScanTime = 0.0f;
        private static float _lastCensusTime = 0.0f;
        private const float ScanIntervalSeconds = 0.35f;

        private static readonly List<Unit> _allOperationalUnits = new List<Unit>();
        public static List<Unit> AllOperationalUnits { get { return _allOperationalUnits; } }

        public static readonly DatalinkNetworkTelemetry LatestTelemetry = new DatalinkNetworkTelemetry();

        public static Unit GetRadarAttachedUnit(Radar radar)
        {
            if (radar == null) return null;
            Unit u = radar.GetAttachedUnit();
            if (u != null) return u;
            return radar.GetComponentInParent<Unit>();
        }

        public static FactionHQ GetUnitFactionHQ(Unit unit)
        {
            if (unit == null) return null;
            if (unit.NetworkHQ != null) return unit.NetworkHQ;
            if (unit.MapHQ != null) return unit.MapHQ;
            try
            {
                var allHqs = FactionRegistry.GetAllHQs();
                if (allHqs != null)
                {
                    foreach (FactionHQ hq in allHqs)
                    {
                        if (hq == null) continue;
                        List<Radar> hqRadars = GetHqRadars(hq);
                        if (hqRadars != null && unit.radar != null && hqRadars.Contains(unit.radar as Radar)) return hq;
                    }
                }
            }
            catch { }
            return null;
        }

        public static string ClassifyNodeRole(Unit unit)
        {
            if (unit == null) return "NODE";
            string name = (unit.unitName ?? unit.name ?? "").ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");
            if (name.Contains("medusa") || name.Contains("ew1") || name.Contains("ew25") || name.Contains("awacs")) return "AWACS";
            if (name.Contains("dynamo") || name.Contains("destroyer") || name.Contains("argus") || name.Contains("frigate") || name.Contains("carrier") || name.Contains("hyperion") || name.Contains("corvette") || name.Contains("shard")) return "WARSHIP";
            if (name.Contains("radartruck") || name.Contains("radarstation") || name.Contains("hlt") || name.Contains("boltstrike") || name.Contains("sam") || name.Contains("cursor")) return "SAM/GND";
            if (name.Contains("chicane") || name.Contains("tarantula") || name.Contains("ibis") || name.Contains("helo") || name.Contains("helicopter")) return "HELO";
            if (name.Contains("darkreach") || name.Contains("sfb81") || name.Contains("brawler")) return "STRIKE";
            if (unit is Aircraft) return "FIGHTER";
            return "GROUND";
        }

        /// <summary>
        /// Determines whether a unit has operational sensors (radar, optics) or combat communications
        /// capable of participating in the Tactical Datalink network.
        /// Fixed radar stations (Building subclasses) with operational radars are fully supported.
        /// Excludes non-sensor static structures such as hangars, warehouses, fuel tanks, and runways.
        /// </summary>
        public static bool IsDatalinkCapableUnit(Unit unit)
        {
            if (unit == null || unit.disabled || unit is Missile) return false;

            // 1. All aircraft and rotary-wing platforms possess crew/optics, RWR, and tactical datalink avionics
            if (unit is Aircraft) return true;

            // 2. Units with operational radar systems (HLT Radar Truck, Radar Station 1, SAM acquisition radars)
            // Evaluated BEFORE checking Building/Scenery so ground radar stations (Building subclasses) are recognized!
            if (unit.radar != null && unit.radar.IsOperational()) return true;
            Radar childRadar = unit.GetComponentInChildren<Radar>();
            if (childRadar != null && childRadar.IsOperational()) return true;

            // 3. Classified radar emitters or custom radar definitions
            if (unit.HasRadarEmission()) return true;
            if (unit.definition != null)
            {
                if (unit.definition.code == "RDR" || unit.definition.code == "SAM") return true;
                if (unit.definition.typeIdentity.radar > 0.0f) return true;
            }

            // Exclude static non-sensor structures (hangars, warehouses, fuel tanks, runways)
            if (unit is Building || unit is Scenery || unit is Container) return false;

            // 4. Armed warships and ground combat vehicles with targeting optics / weapons
            if (unit.weaponStations != null && unit.weaponStations.Count > 0)
            {
                if (unit.definition != null && unit.definition.IsObstacle)
                {
                    return false;
                }
                return true;
            }

            return false;
        }

        public static void RegisterUnit(Unit unit)
        {
            if (!IsDatalinkCapableUnit(unit)) return;
            if (!_allOperationalUnits.Contains(unit))
            {
                _allOperationalUnits.Add(unit);
            }
        }

        public static void DeregisterUnit(Unit unit)
        {
            if (unit == null) return;
            _allOperationalUnits.Remove(unit);
        }

        public static void RegisterMissile(Missile missile)
        {
            if (missile == null) return;
            if (!_activeMissiles.Contains(missile))
            {
                _activeMissiles.Add(missile);
            }
        }

        public static void DeregisterMissile(Missile missile)
        {
            if (missile == null) return;
            _activeMissiles.Remove(missile);
        }

        public static void RegisterRadar(Radar radar)
        {
            if (radar == null) return;
            if (!_registeredRadars.Contains(radar))
            {
                _registeredRadars.Add(radar);
            }

            try
            {
                PhysRadarEmitter emitter = PhysRadarEmitter.Get(radar);
                if (emitter == null)
                {
                    emitter = radar.gameObject.GetComponent<PhysRadarEmitter>();
                    if (emitter == null)
                    {
                        emitter = radar.gameObject.AddComponent<PhysRadarEmitter>();
                    }
                }
                if (emitter != null && emitter.Specs.BandwidthHz <= 0f)
                {
                    emitter.InitializeSpecs();
                }

                Unit attachedUnit = GetRadarAttachedUnit(radar);
                if (attachedUnit != null && IsDatalinkCapableUnit(attachedUnit))
                {
                    RegisterUnit(attachedUnit);
                }
            }
            catch { }
        }

        public static void DeregisterRadar(Radar radar)
        {
            if (radar == null) return;
            _registeredRadars.Remove(radar);
        }

        /// <summary>
        /// Self-healing census that populates DatalinkNetwork from native game registries
        /// (FactionRegistry.GetAllHQs, FactionHQ.radars, UnitRegistry.allUnits, and FindObjectsOfType).
        /// Ensures 100% of ground radar stations, SAM batteries, and operational air/ground units
        /// are connected to the network regardless of scene load timing or script execution order.
        /// </summary>
        public static void CensusSceneUnitsAndRadars(bool fullReset = false)
        {
            if (fullReset)
            {
                _allOperationalUnits.Clear();
                _registeredRadars.Clear();
                _activeMissiles.Clear();
            }

            try
            {
                // 1. Ingest all radars from FactionRegistry HQs
                var allHqs = FactionRegistry.GetAllHQs();
                if (allHqs != null)
                {
                    foreach (FactionHQ hq in allHqs)
                    {
                        if (hq == null) continue;
                        List<Radar> hqRadars = GetHqRadars(hq);
                        if (hqRadars != null)
                        {
                            for (int r = 0; r < hqRadars.Count; r++)
                            {
                                Radar rad = hqRadars[r];
                                if (rad != null && rad.IsOperational())
                                {
                                    RegisterRadar(rad);
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            try
            {
                // 2. Ingest from UnitRegistry
                if (UnitRegistry.allUnits != null)
                {
                    for (int i = 0; i < UnitRegistry.allUnits.Count; i++)
                    {
                        Unit u = UnitRegistry.allUnits[i];
                        if (u != null && !u.disabled && IsDatalinkCapableUnit(u))
                        {
                            RegisterUnit(u);
                            Radar rad = u.radar as Radar;
                            if (rad != null && rad.IsOperational())
                            {
                                RegisterRadar(rad);
                            }
                        }
                    }
                }
            }
            catch { }

            // 3. Fallback scan if radars or units are still empty
            if (_registeredRadars.Count == 0)
            {
                try
                {
                    Radar[] foundRadars = UnityEngine.Object.FindObjectsOfType<Radar>();
                    if (foundRadars != null)
                    {
                        for (int i = 0; i < foundRadars.Length; i++)
                        {
                            Radar rad = foundRadars[i];
                            if (rad != null && rad.IsOperational())
                            {
                                RegisterRadar(rad);
                            }
                        }
                    }
                }
                catch { }
            }

            if (_allOperationalUnits.Count == 0)
            {
                try
                {
                    Unit[] foundUnits = UnityEngine.Object.FindObjectsOfType<Unit>();
                    if (foundUnits != null)
                    {
                        for (int i = 0; i < foundUnits.Length; i++)
                        {
                            Unit u = foundUnits[i];
                            if (u != null && !u.disabled && IsDatalinkCapableUnit(u))
                            {
                                RegisterUnit(u);
                            }
                        }
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Periodically called from Update loop to process allied sensor fusion, universal visual reconnaissance,
        /// and dispatch datalink warnings across all factions in the match.
        /// </summary>
        public static void ProcessDatalinkCycle()
        {
            if (RadioWarsConfig.EnableAlliedDatalink != null && !RadioWarsConfig.EnableAlliedDatalink.Value)
            {
                LatestTelemetry.NetworkActive = false;
                return;
            }

            float now = Time.time;
            if (now - _lastScanTime < ScanIntervalSeconds)
            {
                return;
            }
            _lastScanTime = now;

            // Automated self-healing census (runs every 2.5s or whenever census is empty)
            if (now - _lastCensusTime > 2.5f || _registeredRadars.Count == 0 || _allOperationalUnits.Count == 0)
            {
                _lastCensusTime = now;
                CensusSceneUnitsAndRadars(false);
            }

            // 1. Clean up destroyed or disabled units from active registry
            for (int i = _allOperationalUnits.Count - 1; i >= 0; i--)
            {
                if (_allOperationalUnits[i] == null || _allOperationalUnits[i].disabled)
                {
                    _allOperationalUnits.RemoveAt(i);
                }
            }

            // 2. Clean up destroyed missiles in active list
            for (int i = _activeMissiles.Count - 1; i >= 0; i--)
            {
                if (_activeMissiles[i] == null || _activeMissiles[i].disabled)
                {
                    _activeMissiles.RemoveAt(i);
                }
            }

            // 3. Clean up destroyed radars
            for (int i = _registeredRadars.Count - 1; i >= 0; i--)
            {
                if (_registeredRadars[i] == null || !_registeredRadars[i].IsOperational())
                {
                    _registeredRadars.RemoveAt(i);
                }
            }

            // Prepare telemetry snapshot buffer for player's faction
            Aircraft playerAircraft = CombatHUD.i != null ? CombatHUD.i.aircraft : null;
            FactionHQ playerHq = (playerAircraft != null) ? playerAircraft.NetworkHQ : null;

            LatestTelemetry.TotalCyclesProcessed++;
            LatestTelemetry.LastCycleTimestamp = now;
            LatestTelemetry.PlayerHQ = playerHq;
            LatestTelemetry.FactionName = (playerHq != null && !string.IsNullOrEmpty(playerHq.name)) ? playerHq.name : "Allied Forces";
            LatestTelemetry.NetworkActive = true;

            LatestTelemetry.Nodes.Clear();
            LatestTelemetry.SharedRadarTracks.Clear();
            LatestTelemetry.SharedVisualContacts.Clear();
            LatestTelemetry.RelayedMissileAlerts.Clear();

            for (int i = 0; i < _allOperationalUnits.Count; i++)
            {
                Unit u = _allOperationalUnits[i];
                if (u == null || u.disabled || !IsDatalinkCapableUnit(u)) continue;
                FactionHQ uHq = GetUnitFactionHQ(u);
                if (playerHq == null || uHq == playerHq)
                {
                    DatalinkNodeSnapshot node = new DatalinkNodeSnapshot();
                    node.Unit = u;
                    node.UnitName = !string.IsNullOrEmpty(u.unitName) ? u.unitName : u.name;
                    node.Role = ClassifyNodeRole(u);
                    node.Position = u.transform.position;
                    node.HasRadar = (u.radar != null && u.radar.IsOperational());
                    node.IsAirborne = (u is Aircraft);
                    node.SharedRadarCount = 0;
                    node.SharedVisualCount = 0;
                    LatestTelemetry.Nodes.Add(node);
                }
            }

            // 4. Universal Visual Reconnaissance Scan:
            // Evaluates whether any operational unit of faction A is within visual range (10km) of an enemy radar
            float visualRange = RWRTriangulationProcessor.VisualIdentificationRange;
            for (int t = 0; t < _allOperationalUnits.Count; t++)
            {
                Unit target = _allOperationalUnits[t];
                if (target == null || target.disabled) continue;

                // Exclude friendly missiles from visual reconnaissance
                Missile tarMissile = target as Missile;
                if (tarMissile != null)
                {
                    bool isAlliedMissile = (tarMissile.owner == playerAircraft) ||
                        (playerHq != null && ((tarMissile.owner != null && tarMissile.owner.NetworkHQ == playerHq) ||
                                              (tarMissile.NetworkHQ != null && tarMissile.NetworkHQ == playerHq)));
                    if (isAlliedMissile) continue;
                }

                // Only evaluate valid trackable radar threats
                if (!Patches.CombatHUDPatches.IsTrackableThreat(target, playerHq))
                {
                    continue;
                }

                Vector3 targetPos = target.transform.position;
                FactionHQ targetHq = GetUnitFactionHQ(target);

                for (int s = 0; s < _allOperationalUnits.Count; s++)
                {
                    Unit spotter = _allOperationalUnits[s];
                    if (spotter == null || spotter.disabled || spotter == target) continue;
                    FactionHQ spotterHq = GetUnitFactionHQ(spotter);
                    if (spotterHq == null || (targetHq != null && spotterHq == targetHq)) continue;

                    Vector3 spotterPos = spotter.transform.position;
                    float sqrDist = (spotterPos - targetPos).sqrMagnitude;
                    if (sqrDist < visualRange * visualRange)
                    {
                        float dist = Mathf.Sqrt(sqrDist);
                        // Enforce 3D physical line-of-sight terrain occlusion for visual reconnaissance
                        Vector3 spotterEye = spotterPos + Vector3.up * 2.0f;
                        Vector3 targetCenter = targetPos + Vector3.up * 1.5f;
                        int obstacleMask = (int)PhysicsLayers.StaticsMask | (int)PhysicsLayers.ShipsMask;
                        if (!Physics.Linecast(spotterEye, targetCenter, obstacleMask))
                        {
                            // Operational spotter visually identifies hostile radar: fused into spotter's faction datalink!
                            RWRTriangulationProcessor.RecordVisualContact(spotterHq, target, spotter);

                            if (playerHq != null && spotterHq == playerHq)
                            {
                                if (playerAircraft != null && !playerAircraft.disabled && spotter != playerAircraft)
                                {
                                    PhysRWRReceiver playerRwr = PhysRWRReceiver.Get(playerAircraft);
                                    if (playerRwr != null)
                                    {
                                        Vector3 targetVel = target.rb != null ? target.rb.velocity : Vector3.zero;
                                        playerRwr.RegisterDatalinkThreat(
                                            spotter,
                                            target,
                                            targetPos,
                                            targetVel,
                                            target is Missile
                                        );
                                    }
                                }

                                SharedVisualContactSnapshot vSnap = new SharedVisualContactSnapshot();
                                vSnap.SpotterUnit = spotter;
                                vSnap.SpotterName = !string.IsNullOrEmpty(spotter.unitName) ? spotter.unitName : spotter.name;
                                vSnap.TargetUnit = target;
                                vSnap.TargetName = !string.IsNullOrEmpty(target.unitName) ? target.unitName : target.name;
                                vSnap.SpotterPos = spotter.transform.position;
                                vSnap.TargetPos = targetPos;
                                vSnap.DistanceMeters = dist;
                                vSnap.Timestamp = now;
                                LatestTelemetry.SharedVisualContacts.Add(vSnap);

                                for (int n = 0; n < LatestTelemetry.Nodes.Count; n++)
                                {
                                    if (LatestTelemetry.Nodes[n].Unit == spotter)
                                    {
                                        LatestTelemetry.Nodes[n].SharedVisualCount++;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // 5. Universal Radar Illumination Fusion:
            // Operational radars automatically feed active tracks into their faction datalink
            for (int r = 0; r < _registeredRadars.Count; r++)
            {
                Radar alliedRadar = _registeredRadars[r];
                if (alliedRadar == null || !alliedRadar.IsOperational()) continue;
                Unit attachedUnit = GetRadarAttachedUnit(alliedRadar);
                if (attachedUnit == null || attachedUnit.disabled) continue;
                FactionHQ attachedHq = GetUnitFactionHQ(attachedUnit);
                if (attachedHq == null) continue;

                if (alliedRadar.detectedTargets != null && alliedRadar.detectedTargets.Count > 0)
                {
                    for (int d = 0; d < alliedRadar.detectedTargets.Count; d++)
                    {
                        Unit detected = alliedRadar.detectedTargets[d];
                        if (detected == null || detected.disabled) continue;

                        // Exclude friendly missiles from radar datalink tracks
                        Missile detMissile = detected as Missile;
                        if (detMissile != null)
                        {
                            bool isAlliedMissile = (detMissile.owner == playerAircraft) ||
                                (playerHq != null && ((detMissile.owner != null && detMissile.owner.NetworkHQ == playerHq) ||
                                                      (detMissile.NetworkHQ != null && detMissile.NetworkHQ == playerHq)));
                            if (isAlliedMissile) continue;
                        }

                        if (Patches.CombatHUDPatches.IsTrackableThreat(detected, attachedHq))
                        {
                            RWRTriangulationProcessor.RecordRadarIllumination(attachedHq, detected, alliedRadar);

                            if (playerHq != null && attachedHq == playerHq)
                            {
                                if (playerAircraft != null && !playerAircraft.disabled && attachedUnit != playerAircraft)
                                {
                                    PhysRWRReceiver playerRwr = PhysRWRReceiver.Get(playerAircraft);
                                    if (playerRwr != null)
                                    {
                                        Vector3 targetVel = detected.rb != null ? detected.rb.velocity : Vector3.zero;
                                        playerRwr.RegisterDatalinkThreat(
                                            attachedUnit,
                                            detected,
                                            detected.transform.position,
                                            targetVel,
                                            detected is Missile
                                        );
                                    }
                                }

                                SharedRadarTrackSnapshot rSnap = new SharedRadarTrackSnapshot();
                                rSnap.SourceUnit = attachedUnit;
                                rSnap.SourceName = !string.IsNullOrEmpty(attachedUnit.unitName) ? attachedUnit.unitName : attachedUnit.name;
                                rSnap.TargetUnit = detected;
                                rSnap.TargetName = !string.IsNullOrEmpty(detected.unitName) ? detected.unitName : detected.name;
                                rSnap.SourcePos = alliedRadar.GetScanPoint() != null ? alliedRadar.GetScanPoint().position : attachedUnit.transform.position;
                                rSnap.TargetPos = detected.transform.position;
                                rSnap.SlantRangeMeters = Vector3.Distance(rSnap.SourcePos, rSnap.TargetPos);
                                rSnap.Timestamp = now;
                                LatestTelemetry.SharedRadarTracks.Add(rSnap);

                                for (int n = 0; n < LatestTelemetry.Nodes.Count; n++)
                                {
                                    if (LatestTelemetry.Nodes[n].Unit == attachedUnit)
                                    {
                                        LatestTelemetry.Nodes[n].SharedRadarCount++;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // 6. Local Player RWR Inbound Missile Alerts
            if (playerAircraft == null || playerAircraft.disabled) return;

            PhysRWRReceiver rwr = playerAircraft.GetComponent<PhysRWRReceiver>();
            if (rwr == null) return;

            Vector3 playerPos = playerAircraft.transform.position;

            // Scan all active airborne missiles
            for (int i = 0; i < _activeMissiles.Count; i++)
            {
                Missile m = _activeMissiles[i];
                if (m == null || m.disabled) continue;

                // Check if missile is friendly to player (fired by player or allied units)
                bool isFriendly = (m.owner == playerAircraft) ||
                    (playerHq != null && ((m.owner != null && m.owner.NetworkHQ == playerHq) ||
                                          (m.NetworkHQ != null && m.NetworkHQ == playerHq)));

                if (isFriendly) continue;

                Vector3 missilePos = m.transform.position;
                Vector3 missileVel = m.rb != null ? m.rb.velocity : Vector3.zero;
                float distToPlayer = Vector3.Distance(missilePos, playerPos);

                // Only evaluate missiles within 35 km of player
                if (distToPlayer > 35000.0f) continue;

                // Check if missile velocity is closing in toward player (heading threat)
                Vector3 toPlayer = playerPos - missilePos;
                bool isClosing = Vector3.Dot(missileVel, toPlayer.normalized) > 50.0f;
                bool isTargetingPlayer = (m.targetID == playerAircraft.persistentID);

                if (!isClosing && !isTargetingPlayer) continue;

                // Search for ANY allied radar that has detection coverage of this missile
                Unit spottingAlliedUnit = null;
                for (int rIdx = 0; rIdx < _registeredRadars.Count; rIdx++)
                {
                    Radar alliedRadar = _registeredRadars[rIdx];
                    if (alliedRadar == null || !alliedRadar.IsOperational()) continue;

                    Unit alliedUnit = GetRadarAttachedUnit(alliedRadar);
                    if (alliedUnit == null || alliedUnit == playerAircraft) continue;

                    FactionHQ alliedHq = GetUnitFactionHQ(alliedUnit);
                    if (playerHq != null && alliedHq != null && alliedHq != playerHq)
                    {
                        continue;
                    }

                    Vector3 radarPos = alliedRadar.GetScanPoint() != null ? alliedRadar.GetScanPoint().position : alliedRadar.transform.position;
                    float distToMissile = Vector3.Distance(radarPos, missilePos);

                    // Check radar max range
                    if (distToMissile > alliedRadar.RadarParameters.maxRange) continue;

                    // Check Earth curvature occlusion
                    if (RadioWarsConfig.EarthCurvatureEnabled != null && RadioWarsConfig.EarthCurvatureEnabled.Value)
                    {
                        if (RadarPhysics.IsOccludedByEarthCurvature(radarPos, missilePos, m.radarAlt))
                        {
                            continue;
                        }
                    }

                    // Check radar forward cone if limited
                    float cone = (f_radarCone != null) ? f_radarCone(alliedRadar) : 360.0f;
                    if (cone < 350.0f)
                    {
                        Vector3 radarFwd = alliedRadar.transform.forward;
                        Vector3 dirToMissile = (missilePos - radarPos).normalized;
                        if (Vector3.Angle(radarFwd, dirToMissile) > cone * 0.5f)
                        {
                            continue;
                        }
                    }

                    // Spotting successful! Allied radar has solid track on inbound munition!
                    spottingAlliedUnit = alliedUnit;
                    break;
                }

                // If allied unit spotted the incoming missile, transmit Datalink alert to player's RWR
                if (spottingAlliedUnit != null)
                {
                    rwr.RegisterDatalinkThreat(
                        spottingAlliedUnit,
                        m,
                        missilePos,
                        missileVel,
                        true
                    );

                    RelayedMissileAlertSnapshot mSnap = new RelayedMissileAlertSnapshot();
                    mSnap.RadarUnit = spottingAlliedUnit;
                    mSnap.RadarName = !string.IsNullOrEmpty(spottingAlliedUnit.unitName) ? spottingAlliedUnit.unitName : spottingAlliedUnit.name;
                    mSnap.MissileUnit = m;
                    mSnap.MissilePos = missilePos;
                    mSnap.MissileVel = missileVel;
                    mSnap.DistToPlayer = distToPlayer;
                    mSnap.Timestamp = now;
                    LatestTelemetry.RelayedMissileAlerts.Add(mSnap);
                }
            }
        }

        public static void ClearAll()
        {
            _allOperationalUnits.Clear();
            _activeMissiles.Clear();
            _registeredRadars.Clear();
            LatestTelemetry.Nodes.Clear();
            LatestTelemetry.SharedRadarTracks.Clear();
            LatestTelemetry.SharedVisualContacts.Clear();
            LatestTelemetry.RelayedMissileAlerts.Clear();
            LatestTelemetry.NetworkActive = false;
        }

        public static void Clear()
        {
            ClearAll();
        }
    }
}
