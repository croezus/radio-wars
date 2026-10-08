using System;
using HarmonyLib;
using UnityEngine;
using RadioWars.Core;
using RadioWars.Config;
using RadioWars.Components;
using NuclearOption.Jobs;

namespace RadioWars.Patches
{
    [HarmonyPatch(typeof(RadarParams), "GetSignalStrength")]
    public static class RadarParams_GetSignalStrength_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            ref RadarParams __instance,
            Vector3 direction,
            float dist,
            Rigidbody rb,
            float RCS,
            float clutter,
            float ecm,
            ref float __result)
        {
            // A/B Toggle: If mod is disabled, run original vanilla calculation!
            if (!RadioWarsConfig.IsModActive)
            {
                return true;
            }

            if (dist < 1.0f) dist = 1.0f;

            // Generate physical radar specs from the instance's maxRange / minSignal
            RadarSpecs specs = RadarSpecs.CreateFromVanilla(__instance.maxRange, __instance.maxSignal, __instance.minSignal);

            // Thermal noise floor Pn
            float noiseFloor = RadarPhysics.CalculateThermalNoiseFloor(specs.BandwidthHz, specs.NoiseFigureLinear);

            // Target physical cross-section
            float effectiveRcs = Mathf.Max(0.0002f, RCS);

            // Received echo power Pr
            float echoPower = RadarPhysics.CalculateEchoPower(dist, effectiveRcs, ref specs);

            // Jammer power Pj
            float jammerPower = 0.0f;
            if (ecm > 0.01f)
            {
                JammerSpecs jammer = JammerSpecs.CreateStandardPod();
                jammer.PowerWatts = ecm * 100.0f; // Scale with ECM intensity
                jammerPower = ElectronicWarfare.CalculateJammerPowerAtReceiver(dist, ref jammer, ref specs);
                if (RadioWarsConfig.JammerBurnThroughRatio != null)
                {
                    jammerPower /= Mathf.Max(0.1f, RadioWarsConfig.JammerBurnThroughRatio.Value);
                }
            }

            // Clutter power Pc
            float clutterPower = clutter * __instance.clutterFactor * 1e-15f;

            // Instantaneous SNR in dB
            float snrDb = RadarPhysics.CalculateSNRdB(echoPower, noiseFloor, clutterPower, jammerPower);

            float minSnrThreshold = RadioWarsConfig.EffectiveMinSNR;

            if (snrDb >= minSnrThreshold)
            {
                // Doppler factor boost for high-speed head-on closures
                float dopplerSpeed = 0.0f;
                if (rb != null)
                {
                    dopplerSpeed = Mathf.Abs(Vector3.Dot(direction, rb.velocity));
                }

                float dopplerMultiplier = 1.0f + Mathf.Min(dopplerSpeed, 200.0f) * __instance.dopplerFactor * 0.005f;
                float normalizedSignal = Mathf.Clamp((snrDb - minSnrThreshold) * dopplerMultiplier + __instance.minSignal, __instance.minSignal, __instance.maxSignal);

                __result = normalizedSignal;
            }
            else
            {
                // Sub-threshold signal: return 0.0f (no track / detection)
                __result = 0.0f;
            }

            return false; // Skip vanilla scalar math
        }
    }

    [HarmonyPatch(typeof(Radar), "CanSeeRadarReturn")]
    public static class Radar_CanSeeRadarReturn_Patch
    {
        private static readonly System.Func<CombatHUD, System.Collections.Generic.List<Unit>> s_getTargetList =
            FastReflection.CreateFieldGetter<CombatHUD, System.Collections.Generic.List<Unit>>("targetList");

        [HarmonyPrefix]
        public static bool Prefix(
            Radar __instance,
            IRadarReturn radarReturn,
            float dist,
            float clutterFactor,
            ref bool __result,
            System.Collections.Generic.List<Missile> ___guidedMissiles)
        {
            // A/B Toggle: If mod is disabled, run original vanilla calculation!
            if (!RadioWarsConfig.IsModActive)
            {
                return true;
            }

            if (__instance == null || radarReturn == null)
            {
                __result = false;
                return false;
            }

            Transform scanner = __instance.GetScanPoint();
            Vector3 radarPos = scanner != null ? scanner.position : __instance.transform.position;
            Vector3 radarVel = __instance.GetVelocity();

            // Extract target Unit or Aircraft
            Unit targetUnit = radarReturn as Unit;
            if (targetUnit == null)
            {
                Component comp = radarReturn as Component;
                if (comp != null) targetUnit = comp.GetComponentInParent<Unit>();
            }

            if (targetUnit == null || targetUnit.disabled)
            {
                __result = false;
                return false;
            }

            Vector3 targetPos = targetUnit.transform.position;
            Vector3 targetVel = targetUnit.rb != null ? targetUnit.rb.velocity : Vector3.zero;

            // 1. Earth Curvature 4/3 Refraction Check (cheap analytical check before linecast)
            bool horizonOccluded = false;
            if (RadioWarsConfig.EarthCurvatureEnabled != null && RadioWarsConfig.EarthCurvatureEnabled.Value)
            {
                if (RadarPhysics.IsOccludedByEarthCurvature(radarPos, targetPos, targetUnit.radarAlt))
                {
                    horizonOccluded = true;
                    // Option 1: short-circuit early unless Debug HUD or Gizmos are active
                    if (!RadioWarsConfig.ShowDebugHUD.Value && !RadioWarsConfig.DrawDebugGizmos.Value)
                    {
                        __result = false;
                        return false;
                    }
                }
            }

            // 2. Physical Topographical Terrain & Structure Occlusion Check
            Vector3 losVector = targetPos - radarPos;
            float losDist = losVector.magnitude;
            if (losDist > 5.0f)
            {
                Vector3 losDir = losVector / losDist;
                Vector3 rayStart = radarPos + losDir * 2.0f;
                Vector3 rayTarget = targetPos - losDir * 1.0f;
                RaycastHit terrainHit;
                int obstacleMask = (int)PhysicsLayers.StaticsMask | (int)PhysicsLayers.ShipsMask;

                if (Physics.Linecast(rayStart, rayTarget, out terrainHit, obstacleMask))
                {
                    float targetClearRadius = Mathf.Max(5.0f, targetUnit.maxRadius * 1.5f);
                    if (terrainHit.collider != null && (terrainHit.point - targetPos).sqrMagnitude > (targetClearRadius * targetClearRadius))
                    {
                        // Direct line-of-sight is physically occluded by mountain or building!
                        __result = false;
                        return false;
                    }
                }
            }

            // 3. Dynamic 3D Aspect RCS & HERM
            PhysRadarTarget physTarget = PhysRadarTarget.Get(targetUnit);
            float dynamicRcs = targetUnit.RCS;
            float storesRcs = 0.0f;
            bool isHeliHERM = false;

            if (physTarget != null)
            {
                dynamicRcs = physTarget.GetDynamicRCS(radarPos);
                storesRcs = physTarget.CachedStoresPenalty;
                isHeliHERM = physTarget.IsHelicopterWithHERM();
            }

            // 4. Electronic Jamming status
            float ecmIntensity = radarReturn.GetECMIntensity();
            bool jammerActive = ecmIntensity > 0.01f;

            // 5. Pulse-Doppler Ground Clutter Notch Filter (widened by ECM Doppler noise)
            float notchThreshold = RadioWarsConfig.EffectiveNotchThreshold;
            DopplerResult doppler = DopplerClutterProcessor.ProcessDopplerAndClutter(
                radarPos,
                radarVel,
                targetPos,
                targetVel,
                notchThreshold,
                isHeliHERM,
                __instance.GetAttachedUnit() != null ? __instance.GetAttachedUnit().radarAlt : 1000.0f,
                ecmIntensity,
                targetUnit != null ? targetUnit.radarAlt : 500.0f
            );

            // 6. Physical Radar Range Equation and SNR Check (use cached specs from emitter when available)
            PhysRadarEmitter emitter = PhysRadarEmitter.Get(__instance);
            RadarSpecs specs;
            float noiseFloor;
            if (emitter != null && emitter.ThermalNoiseFloor > 0.0f)
            {
                specs = emitter.Specs;
                noiseFloor = emitter.ThermalNoiseFloor;
            }
            else
            {
                specs = RadarSpecs.CreateFromVanilla(
                    __instance.RadarParameters.maxRange,
                    __instance.RadarParameters.maxSignal,
                    __instance.RadarParameters.minSignal
                );
                noiseFloor = RadarPhysics.CalculateThermalNoiseFloor(specs.BandwidthHz, specs.NoiseFigureLinear);
            }

            float echoPower = RadarPhysics.CalculateEchoPower(dist, dynamicRcs, ref specs);

            // Pulse-Doppler notch attenuation based on ground clutter presence
            if (doppler.IsInsideClutterNotch)
            {
                if (doppler.HasClutterBackground)
                {
                    // Target return buried inside ground clutter: -20 dB notch filter rejection
                    echoPower *= 0.01f;
                }
                else
                {
                    // Open clear sky: filter bypassed, notch 90% ignored (only 10% minor attenuation)
                    echoPower *= 0.90f;
                }
            }

            float jammerPower = 0.0f;
            float rBurn = 0.0f;

            if (jammerActive)
            {
                JammerSpecs jammer = JammerSpecs.CreateStandardPod();
                jammer.PowerWatts = ecmIntensity * 120.0f;
                jammerPower = ElectronicWarfare.CalculateJammerPowerAtReceiver(dist, ref jammer, ref specs);
                if (RadioWarsConfig.JammerBurnThroughRatio != null)
                {
                    jammerPower /= Mathf.Max(0.1f, RadioWarsConfig.JammerBurnThroughRatio.Value);
                }

                rBurn = ElectronicWarfare.CalculateBurnThroughRange(dynamicRcs, RadioWarsConfig.EffectiveMinSNR, ref jammer, ref specs);
            }

            // Factor in external radar receiver saturation if radar itself is jammed
            if (__instance.IsJammed())
            {
                float receiverJamFloor = noiseFloor * 100.0f; // +20 dB saturation noise floor
                jammerPower = Mathf.Max(jammerPower, receiverJamFloor);
                jammerActive = true;
            }

            float snrDb = RadarPhysics.CalculateSNRdB(echoPower, noiseFloor, doppler.ClutterNoisePower, jammerPower);
            bool snrPass = snrDb >= RadioWarsConfig.EffectiveMinSNR;

            bool detected = snrPass && !doppler.TrackRejectedByNotch && !horizonOccluded;

            // Compute physical illumination state (Missile Guidance, Hard Lock, Tracking, Search)
            RadarIlluminationState state = RadarIlluminationState.Search;
            if (___guidedMissiles != null && ___guidedMissiles.Count > 0)
            {
                state = RadarIlluminationState.MissileGuidance;
            }
            else if (snrDb >= RadioWarsConfig.EffectiveMinSNR + 8.0f && dist < 35000.0f)
            {
                state = RadarIlluminationState.HardLock;
            }
            else if (snrDb >= RadioWarsConfig.EffectiveMinSNR + 3.0f)
            {
                state = RadarIlluminationState.Tracking;
            }

            // Stream to telemetry HUD if this radar is connected to the player's aircraft or targeting player
            bool isPlayerInvolved = false;
            int priority = 0;
            try
            {
                if (CombatHUD.i != null && CombatHUD.i.aircraft != null)
                {
                    Unit playerUnit = CombatHUD.i.aircraft;
                    bool isPlayerRadar = (__instance.GetAttachedUnit() == playerUnit);
                    bool isTargetingPlayer = (targetUnit == playerUnit);

                    if (isPlayerRadar)
                    {
                        isPlayerInvolved = true;
                        // Prioritize player's selected/designated target on CombatHUD
                        System.Collections.Generic.List<Unit> tList = (s_getTargetList != null) ? s_getTargetList(CombatHUD.i) : null;
                        bool isDesignatedTarget = (tList != null && tList.Count > 0 && tList[0] == targetUnit);

                        if (isDesignatedTarget)
                        {
                            priority = 5000;
                        }
                        else if (detected)
                        {
                            priority = 1000 - (int)Mathf.Clamp(dist * 0.01f, 0.0f, 900.0f);
                        }
                        else
                        {
                            priority = 5; // Very low priority for occluded/undetected background targets
                        }
                    }
                    else if (isTargetingPlayer)
                    {
                        // Incoming threat targeting the player
                        isPlayerInvolved = true;
                        priority = detected ? 50 : 1;

                        if (detected)
                        {
                            Unit emitterUnit = __instance.GetAttachedUnit();
                            string platformType = "Ground SAM";
                            if (emitterUnit != null)
                            {
                                if (emitterUnit is Aircraft)
                                {
                                    platformType = "Airborne";
                                }
                                else
                                {
                                    string uName = emitterUnit.unitName;
                                    if (!string.IsNullOrEmpty(uName))
                                    {
                                        if (uName.IndexOf("ship", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            uName.IndexOf("corvette", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            uName.IndexOf("carrier", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            uName.IndexOf("cruiser", StringComparison.OrdinalIgnoreCase) >= 0)
                                        {
                                            platformType = "Naval";
                                        }
                                        else if (uName.IndexOf("sam", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                 uName.IndexOf("flak", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                 uName.IndexOf("spaa", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                 uName.IndexOf("anti-air", StringComparison.OrdinalIgnoreCase) >= 0)
                                        {
                                            platformType = "Ground SAM";
                                        }
                                        else
                                        {
                                            platformType = "Ground / Station";
                                        }
                                    }
                                    else
                                    {
                                        platformType = "Ground / Station";
                                    }
                                }
                            }

                            RadarTelemetry.RegisterIllumination(
                                __instance.GetInstanceID(),
                                (emitterUnit != null && !string.IsNullOrEmpty(emitterUnit.unitName)) ? emitterUnit.unitName : __instance.name,
                                platformType,
                                radarPos,
                                __instance.transform.forward,
                                dist,
                                snrDb,
                                echoPower,
                                state
                            );
                        }
                    }
                }
            }
            catch
            {
                isPlayerInvolved = false;
            }

            if (isPlayerInvolved)
            {
                RadarTelemetry.UpdateTelemetry(
                    radarPos,
                    radarVel,
                    targetPos,
                    targetVel,
                    targetUnit.unitName,
                    dist,
                    echoPower,
                    noiseFloor,
                    doppler.ClutterNoisePower,
                    jammerPower,
                    snrDb,
                    RadioWarsConfig.EffectiveMinSNR,
                    dynamicRcs,
                    storesRcs,
                    ref doppler,
                    rBurn,
                    jammerActive,
                    ecmIntensity,
                    horizonOccluded,
                    priority
                );
            }

            if (horizonOccluded || doppler.TrackRejectedByNotch)
            {
                __result = false;
                return false;
            }

            // Trigger warning on target if detected
            if (detected)
            {
                // Register active radar illumination for tracking quality accumulation & PIP refinement
                try
                {
                    Unit radarUnit = __instance.GetAttachedUnit();
                    if (radarUnit == null) radarUnit = __instance.GetComponentInParent<Unit>();
                    FactionHQ radarHq = (radarUnit != null) ? radarUnit.NetworkHQ : null;
                    if (CombatHUDPatches.IsTrackableThreat(targetUnit, radarHq))
                    {
                        if (radarHq != null)
                        {
                            RwrTriangulationProcessor.RecordRadarIllumination(radarHq, targetUnit, __instance);
                        }
                        else
                        {
                            RwrTriangulationProcessor.RecordRadarIllumination(targetUnit, __instance);
                        }
                    }
                }
                catch { }

                Aircraft targetAircraft = targetUnit as Aircraft;
                if (targetAircraft != null)
                {
                    PhysRWRReceiver rwr = targetAircraft.GetComponent<PhysRWRReceiver>();
                    if (rwr != null)
                    {
                        RwrThreatState rwrState = RwrThreatState.Search;
                        if (state == RadarIlluminationState.MissileGuidance)
                        {
                            rwrState = RwrThreatState.MissileGuidance;
                        }
                        else if (state == RadarIlluminationState.HardLock)
                        {
                            rwrState = RwrThreatState.Track;
                        }

                        float normSig = Mathf.Clamp01((snrDb - RadioWarsConfig.EffectiveMinSNR) / 20.0f);
                        rwr.RegisterRadarPing(__instance.GetAttachedUnit(), __instance, rwrState, normSig, false);
                    }

                    // For the local player, ensure OnRadarWarning fires so map and TacScreen display pings
                    try
                    {
                        if (CombatHUD.i != null && targetAircraft == CombatHUD.i.aircraft)
                        {
                            targetAircraft.RpcGetRadarWarning(__instance.GetAttachedUnit());
                        }
                    }
                    catch { }
                }
            }

            __result = detected;
            return false; // Skip vanilla CanSeeRadarReturn
        }
    }
}
