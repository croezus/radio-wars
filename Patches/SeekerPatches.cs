using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using RadioWars.Core;
using RadioWars.Config;
using RadioWars.Components;

namespace RadioWars.Patches
{
    [HarmonyPatch(typeof(ARHSeeker), "GetRadarReturn")]
    public static class ARHSeeker_GetRadarReturn_Patch
    {
        private static readonly System.Collections.Generic.Dictionary<int, float> _arhMemoryCoastTimers = new System.Collections.Generic.Dictionary<int, float>();

        public static void ClearCoastTimer(int missileId)
        {
            _arhMemoryCoastTimers.Remove(missileId);
        }

        public static void ClearAllCoastTimers()
        {
            _arhMemoryCoastTimers.Clear();
        }

        [HarmonyPrefix]
        public static bool Prefix(
            ARHSeeker __instance,
            ref float __result,
            ref float ___lastActiveTrackAttempt,
            ref float ___returnStrength,
            ref float ___targetDist,
            ref bool ___isJammed,
            bool ___homeOnJam,
            Unit ___targetUnit,
            Missile ___missile,
            ref float ___timeWithoutReturn,
            ref bool ___radarLockEstablished)
        {
            // A/B Toggle: If mod is disabled, run original vanilla calculation!
            if (!RadioWarsConfig.IsModActive)
            {
                return true;
            }

            if (__instance == null)
            {
                __result = 0.0f;
                return false;
            }

            float lastAttempt = ___lastActiveTrackAttempt;
            float now = Time.timeSinceLevelLoad;
            if (now - lastAttempt < 0.25f)
            {
                __result = ___returnStrength;
                return false;
            }
            ___lastActiveTrackAttempt = now;

            Unit target = ___targetUnit;
            Missile missile = ___missile;

            if (target == null || target.disabled || missile == null)
            {
                ___returnStrength = 0.0f;
                __result = 0.0f;
                return false;
            }

            Vector3 missilePos = missile.transform.position;
            Vector3 missileVel = missile.rb != null ? missile.rb.velocity : Vector3.zero;
            Vector3 targetPos = target.transform.position;
            Vector3 targetVel = target.rb != null ? target.rb.velocity : Vector3.zero;

            float dist = Vector3.Distance(missilePos, targetPos);
            ___targetDist = dist;

            // Terminal Pitbull Acquisition Basket:
            // Prior to active radar lock acquisition, target must be within ARH terminal basket range (default 10000m / 10km).
            // Missiles remain silent on datalink until within this range, providing realistic defensive reaction time.
            bool lockEstablished = ___radarLockEstablished;
            float pitbullDist = (RadioWarsConfig.ARHTerminalActivationDistanceMeters != null)
                ? RadioWarsConfig.ARHTerminalActivationDistanceMeters.Value
                : 10000.0f;

            if (!lockEstablished && dist > pitbullDist)
            {
                ___returnStrength = 0.0f;
                __result = 0.0f;
                return false;
            }

            // 0. Earth curvature check (early rejection before physics linecast)
            if (RadioWarsConfig.EarthCurvatureEnabled != null && RadioWarsConfig.EarthCurvatureEnabled.Value)
            {
                if (RadarPhysics.IsOccludedByEarthCurvature(missilePos, targetPos, target.radarAlt))
                {
                    ___returnStrength = 0.0f;
                    __result = 0.0f;
                    return false;
                }
            }

            // 1. Physical Topographical Terrain & Structure Occlusion Check
            Vector3 seekerLos = targetPos - missilePos;
            float seekerLosDist = seekerLos.magnitude;
            if (seekerLosDist > 5.0f)
            {
                Vector3 sDir = seekerLos / seekerLosDist;
                Vector3 sStart = missilePos + sDir * 1.5f;
                Vector3 sTarget = targetPos - sDir * 1.0f;
                RaycastHit seekerHit;
                int obstacleMask = (int)PhysicsLayers.StaticsMask | (int)PhysicsLayers.ShipsMask;

                if (Physics.Linecast(sStart, sTarget, out seekerHit, obstacleMask))
                {
                    float targetClearRadius = Mathf.Max(5.0f, target.maxRadius * 1.5f);
                    if (seekerHit.collider != null && (seekerHit.point - targetPos).sqrMagnitude > (targetClearRadius * targetClearRadius))
                    {
                        // Direct seeker line-of-sight is physically occluded by mountain or building!
                        ___returnStrength = 0.0f;
                        __result = 0.0f;
                        return false;
                    }
                }
            }

            // 2. Aspect-dependent RCS & HERM
            PhysRadarTarget physTarget = target.GetComponent<PhysRadarTarget>();
            float dynamicRcs = target.RCS;
            bool isHeliHERM = false;

            if (physTarget != null)
            {
                dynamicRcs = physTarget.GetDynamicRCS(missilePos);
                isHeliHERM = physTarget.IsHelicopterWithHERM();
            }

            // 3. Electronic Jamming status & Physical Burn-Through
            IRadarReturn radarReturn = target as IRadarReturn;
            float ecmIntensity = (radarReturn != null) ? radarReturn.GetECMIntensity() : 0.0f;

            // 4. Pulse-Doppler Notch Filter (expanded by ECM Doppler smearing / VGPO)
            float notchThreshold = RadioWarsConfig.EffectiveNotchThreshold;
            DopplerResult doppler = DopplerClutterProcessor.ProcessDopplerAndClutter(
                missilePos,
                missileVel,
                targetPos,
                targetVel,
                notchThreshold,
                isHeliHERM,
                missile.radarAlt,
                ecmIntensity,
                target != null ? target.radarAlt : 500.0f
            );

            if (doppler.TrackRejectedByNotch)
            {
                int missileId = missile.GetInstanceID();
                float nowTime = Time.time;
                float coastStart;
                if (!_arhMemoryCoastTimers.TryGetValue(missileId, out coastStart))
                {
                    coastStart = nowTime;
                    _arhMemoryCoastTimers[missileId] = coastStart;
                }

                float coastDuration = RadioWarsConfig.MissileMemoryCoastSeconds != null ? RadioWarsConfig.MissileMemoryCoastSeconds.Value : 2.5f;
                bool enableCoast = RadioWarsConfig.EnableMissileMemoryCoasting == null || RadioWarsConfig.EnableMissileMemoryCoasting.Value;

                if (enableCoast && (nowTime - coastStart < coastDuration))
                {
                    // Missile guidance computer is in Memory Coasting mode!
                    // Maintains lead pursuit on dead-reckoning extrapolation; retains lock
                    ___returnStrength = 0.4f;
                    __result = 0.4f;
                    return false;
                }

                // Target held notch continuously for full duration or decoyed! Full break-lock
                _arhMemoryCoastTimers.Remove(missileId);
                ___returnStrength = -1.0f;
                ___timeWithoutReturn = 999.0f;
                ___radarLockEstablished = false;
                ___isJammed = true;

                try
                {
                    missile.SetTarget(null);
                    // Defeat lead pursuit: direct missile aimpoint straight ahead along its current velocity vector
                    Vector3 forwardDir = (missile.rb != null && missile.rb.velocity.sqrMagnitude > 100.0f)
                        ? missile.rb.velocity.normalized
                        : missile.transform.forward;
                    GlobalPosition missilePosG = missile.GlobalPosition();
                    missile.SetAimpoint(missilePosG + forwardDir * 10000.0f, Vector3.zero);
                }
                catch { }

                __result = -1.0f;
                return false;
            }
            else
            {
                _arhMemoryCoastTimers.Remove(missile.GetInstanceID());
            }

            // 5. Ku-Band active seeker power calculation (calibrated Tier 0)
            RadarSpecs specs = RadarSpecs.CreateSeekerKuBand();

            float noiseFloor = RadarPhysics.CalculateThermalNoiseFloor(specs.BandwidthHz, specs.NoiseFigureLinear);
            float echoPower = RadarPhysics.CalculateEchoPower(dist, dynamicRcs, ref specs);

            float jammerPower = 0.0f;
            float rBurn = 0.0f;
            if (ecmIntensity > 0.01f)
            {
                JammerSpecs jammer = JammerSpecs.CreateStandardPod();
                jammer.PowerWatts = ecmIntensity * 100.0f;
                jammerPower = ElectronicWarfare.CalculateJammerPowerAtReceiver(dist, ref jammer, ref specs);
                rBurn = ElectronicWarfare.CalculateBurnThroughRange(dynamicRcs, RadioWarsConfig.EffectiveMinSNR, ref jammer, ref specs);
            }

            float snrDb = RadarPhysics.CalculateSNRdB(echoPower, noiseFloor, doppler.ClutterNoisePower, jammerPower);

            // 6. Home-On-Jam (HOJ) and Tracking Evaluation
            // HOJ is strictly enabled ONLY when target is flying straight (NOT in notch gate)
            bool allowHoj = ___homeOnJam ||
                            (RadioWarsConfig.HOJEnabled != null && RadioWarsConfig.HOJEnabled.Value);
            bool isHojHoming = allowHoj && ElectronicWarfare.EvaluateHOJGuidance(
                ecmIntensity,
                doppler.IsInsideClutterNotch,
                dist
            );

            bool skinReturnValid = snrDb >= RadioWarsConfig.EffectiveMinSNR;
            bool targetTracked = skinReturnValid || isHojHoming;

            if (targetTracked)
            {
                float strength;
                if (skinReturnValid)
                {
                    strength = Mathf.Clamp((snrDb - RadioWarsConfig.EffectiveMinSNR) * 0.5f + 1.0f, 1.0f, 15.0f);
                }
                else
                {
                    // Tracking exclusively via Home-On-Jam angle strobe
                    strength = Mathf.Clamp(ecmIntensity * 6.0f, 3.0f, 8.0f);
                    ___isJammed = false;
                }

                ___returnStrength = strength;
                __result = strength;

                // Trigger missile terminal warning on target
                Aircraft targetAircraft = target as Aircraft;
                if (targetAircraft != null)
                {
                    // Real-World ARH Midcourse vs Terminal:
                    // During midcourse inertial flight (radarLockEstablished == false),
                    // the active seeker transmitter is OFF. Target RWR does NOT detect active seeker RF!
                    // Only when radarLockEstablished == true (Terminal Pitbull phase) does the seeker emit active RF pulses!
                    bool isLockEstablished = ___radarLockEstablished;
                    bool enableSilentMidcourse = RadioWarsConfig.EnableSilentARHMidcourse == null || RadioWarsConfig.EnableSilentARHMidcourse.Value;

                    if (!enableSilentMidcourse || isLockEstablished)
                    {
                        PhysRWRReceiver rwr = targetAircraft.GetComponent<PhysRWRReceiver>();
                        if (rwr != null)
                        {
                            float normSig = Mathf.Clamp01(strength / 10.0f);
                            rwr.RegisterRadarPing(missile, null, RwrThreatState.MissileGuidance, normSig, true);
                        }

                        try
                        {
                            if (CombatHUD.i != null && targetAircraft == CombatHUD.i.aircraft)
                            {
                                targetAircraft.RpcGetRadarWarning(missile);
                            }
                        }
                        catch { }

                        if (CombatHUD.i != null && targetAircraft == CombatHUD.i.aircraft)
                        {
                            RadarTelemetry.RegisterIllumination(
                                missile != null ? missile.GetInstanceID() : __instance.GetInstanceID(),
                                missile != null ? missile.unitName : "ARH Active Missile Seeker",
                                "Missile Seeker (ARH)",
                                missilePos,
                                missile != null ? missile.transform.forward : Vector3.forward,
                                dist,
                                snrDb,
                                echoPower,
                                RadarIlluminationState.MissileGuidance
                            );
                        }
                    }
                }
            }
            else
            {
                ___returnStrength = 0.0f;
                __result = 0.0f;
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(SARHSeeker), "GetTrackingStrength")]
    public static class SARHSeeker_GetTrackingStrength_Patch
    {
        private static readonly System.Collections.Generic.Dictionary<int, float> _sarhMemoryCoastTimers = new System.Collections.Generic.Dictionary<int, float>();

        public static void ClearCoastTimer(int missileId)
        {
            _sarhMemoryCoastTimers.Remove(missileId);
        }

        public static void ClearAllCoastTimers()
        {
            _sarhMemoryCoastTimers.Clear();
        }

        [HarmonyPrefix]
        public static bool Prefix(
            SARHSeeker __instance,
            Unit targetUnit,
            ref float __result,
            Radar ___radarSource,
            Missile ___missile,
            ref float ___trackingStrength,
            ref float ___timeWithoutTrack,
            ref Transform ___targetTransform,
            ref bool ___isJammed)
        {
            // A/B Toggle: If mod is disabled, run original vanilla calculation!
            if (!RadioWarsConfig.IsModActive)
            {
                return true;
            }

            if (__instance == null || targetUnit == null || targetUnit.disabled)
            {
                __result = 0.0f;
                return false;
            }

            Radar illuminator = ___radarSource;
            Missile missile = ___missile;

            if (illuminator == null || missile == null || !illuminator.IsOperational())
            {
                ___trackingStrength = 0.0f;
                __result = 0.0f;
                return false;
            }

            Vector3 illumPos = illuminator.transform.position;
            Vector3 targetPos = targetUnit.transform.position;
            Vector3 seekerPos = missile.transform.position;

            Vector3 illumLos = targetPos - illumPos;
            float illumDist = illumLos.magnitude;

            Vector3 seekerLos = targetPos - seekerPos;
            float seekerDist = seekerLos.magnitude;

            // 0. Earth Curvature Occlusion Check (Bistatic: early exit before physical linecasts)
            if (RadioWarsConfig.EarthCurvatureEnabled != null && RadioWarsConfig.EarthCurvatureEnabled.Value)
            {
                if (RadarPhysics.IsOccludedByEarthCurvature(illumPos, targetPos, targetUnit.radarAlt) ||
                    RadarPhysics.IsOccludedByEarthCurvature(seekerPos, targetPos, targetUnit.radarAlt))
                {
                    ___trackingStrength = 0.0f;
                    __result = 0.0f;
                    return false;
                }
            }

            // 1. Physical Topographical Terrain Occlusion Checks (Bistatic: Illuminator -> Target and Seeker -> Target)
            int obstacleMask = (int)PhysicsLayers.StaticsMask | (int)PhysicsLayers.ShipsMask;
            float targetClearRadius = Mathf.Max(5.0f, targetUnit.maxRadius * 1.5f);

            // Path 1: Illuminator to Target
            if (illumDist > 5.0f)
            {
                Vector3 iDir = illumLos / illumDist;
                RaycastHit illumHit;
                if (Physics.Linecast(illumPos + iDir * 2.0f, targetPos - iDir * 1.0f, out illumHit, obstacleMask))
                {
                    if (illumHit.collider != null && (illumHit.point - targetPos).sqrMagnitude > (targetClearRadius * targetClearRadius))
                    {
                        ___trackingStrength = 0.0f;
                        __result = 0.0f;
                        return false;
                    }
                }
            }

            // Path 2: Seeker to Target
            if (seekerDist > 5.0f)
            {
                Vector3 sDir = seekerLos / seekerDist;
                RaycastHit seekerHit;
                if (Physics.Linecast(seekerPos + sDir * 1.5f, targetPos - sDir * 1.0f, out seekerHit, obstacleMask))
                {
                    if (seekerHit.collider != null && (seekerHit.point - targetPos).sqrMagnitude > (targetClearRadius * targetClearRadius))
                    {
                        ___trackingStrength = 0.0f;
                        __result = 0.0f;
                        return false;
                    }
                }
            }

            // Check look-down clutter notch relative to illuminator
            Vector3 targetVel = targetUnit.rb != null ? targetUnit.rb.velocity : Vector3.zero;
            Vector3 illumVel = illuminator.GetVelocity();
            float notchThreshold = RadioWarsConfig.EffectiveNotchThreshold;

            PhysRadarTarget physTarget = targetUnit.GetComponent<PhysRadarTarget>();
            float dynamicRcs = (physTarget != null) ? physTarget.GetDynamicRCS(illumPos) : targetUnit.RCS;
            bool isHeliHERM = physTarget != null && physTarget.IsHelicopterWithHERM();

            IRadarReturn radarReturn = targetUnit as IRadarReturn;
            float ecmIntensity = (radarReturn != null) ? radarReturn.GetECMIntensity() : 0.0f;

            DopplerResult doppler = DopplerClutterProcessor.ProcessDopplerAndClutter(
                illumPos,
                illumVel,
                targetPos,
                targetVel,
                notchThreshold,
                isHeliHERM,
                illuminator.GetAttachedUnit() != null ? illuminator.GetAttachedUnit().radarAlt : 1000.0f,
                ecmIntensity,
                targetUnit != null ? targetUnit.radarAlt : 500.0f
            );

            if (doppler.TrackRejectedByNotch)
            {
                int missileId = missile.GetInstanceID();
                float nowTime = Time.time;
                float coastStart;
                if (!_sarhMemoryCoastTimers.TryGetValue(missileId, out coastStart))
                {
                    coastStart = nowTime;
                    _sarhMemoryCoastTimers[missileId] = coastStart;
                }

                float coastDuration = RadioWarsConfig.MissileMemoryCoastSeconds != null ? RadioWarsConfig.MissileMemoryCoastSeconds.Value : 2.5f;
                bool enableCoast = RadioWarsConfig.EnableMissileMemoryCoasting == null || RadioWarsConfig.EnableMissileMemoryCoasting.Value;

                if (enableCoast && (nowTime - coastStart < coastDuration))
                {
                    // SARH Illuminator tracking is in Memory Coasting mode!
                    ___trackingStrength = 0.4f;
                    __result = 0.4f;
                    return false;
                }

                _sarhMemoryCoastTimers.Remove(missileId);
                ___trackingStrength = 0.0f;
                ___timeWithoutTrack = 999.0f;
                ___targetTransform = null;
                ___isJammed = true;

                try
                {
                    missile.SetTarget(null);
                    Vector3 forwardDir = (missile.rb != null && missile.rb.velocity.sqrMagnitude > 100.0f)
                        ? missile.rb.velocity.normalized
                        : missile.transform.forward;
                    GlobalPosition missilePosG = missile.GlobalPosition();
                    missile.SetAimpoint(missilePosG + forwardDir * 10000.0f, Vector3.zero);
                }
                catch { }

                __result = 0.0f;
                return false;
            }
            else
            {
                _sarhMemoryCoastTimers.Remove(missile.GetInstanceID());
            }

            // Physical Bistatic Radar Range Equation:
            // Pr = (Pt * Gt * Gr_seeker * λ² * σ * Gp) / ((4π)³ * r1² * r2² * Lsys)
            PhysRadarEmitter emitter = illuminator.GetComponent<PhysRadarEmitter>();
            RadarSpecs specs = (emitter != null && emitter.ThermalNoiseFloor > 0.0f)
                ? emitter.Specs
                : RadarSpecs.CreateFromVanilla(
                    illuminator.RadarParameters.maxRange,
                    illuminator.RadarParameters.maxSignal,
                    illuminator.RadarParameters.minSignal
                );

            float r1 = Mathf.Max(50.0f, illumDist); // Illuminator to target (reused)
            float r2 = Mathf.Max(50.0f, seekerDist); // Target to missile seeker (reused)
            float r1_sq = r1 * r1;
            float r2_sq = r2 * r2;
            float seekerGain = 800.0f; // Compact missile seeker antenna gain
            float procGain = Mathf.Max(1.0f, specs.ProcessingGainLinear * 0.7f);

            float numerator = specs.PeakPowerWatts * specs.AntennaGainLinear * seekerGain *
                              (specs.WavelengthMeters * specs.WavelengthMeters) * dynamicRcs * procGain;
            float denominator = RadarPhysics.FourPiCubed * r1_sq * r2_sq * specs.SystemLossLinear;
            float echoPower = (denominator > 0.0f) ? (numerator / denominator) : 0.0f;

            // Two-way atmospheric absorption along bistatic path
            if (specs.AttrCoeff_dB_per_km > 0.0f)
            {
                float totalKm = (r1 + r2) * 0.001f;
                float lossDb = specs.AttrCoeff_dB_per_km * totalKm;
                echoPower *= Mathf.Pow(10.0f, -lossDb * 0.1f);
            }

            // Seeker thermal noise floor
            float noiseFloor = RadarPhysics.CalculateThermalNoiseFloor(8.0e6f, 2.5f);

            // Target ECM jamming power against seeker receiver
            float jammerPower = 0.0f;
            float rBurn = 0.0f;
            if (ecmIntensity > 0.01f)
            {
                JammerSpecs jammer = JammerSpecs.CreateStandardPod();
                jammer.PowerWatts = ecmIntensity * 100.0f;
                RadarSpecs seekerSpecs = specs;
                seekerSpecs.AntennaGainLinear = seekerGain;
                jammerPower = ElectronicWarfare.CalculateJammerPowerAtReceiver(r2, ref jammer, ref seekerSpecs);
                rBurn = ElectronicWarfare.CalculateBurnThroughRange(dynamicRcs, RadioWarsConfig.EffectiveMinSNR, ref jammer, ref seekerSpecs);
            }

            float snrDb = RadarPhysics.CalculateSNRdB(echoPower, noiseFloor, doppler.ClutterNoisePower, jammerPower);
            float minSnr = RadioWarsConfig.EffectiveMinSNR;

            // Home-On-Jam (HOJ) check for SARH missile
            // HOJ is strictly enabled ONLY when target is flying straight (NOT in notch gate)
            bool allowHoj = (RadioWarsConfig.HOJEnabled != null && RadioWarsConfig.HOJEnabled.Value);
            bool isHojHoming = allowHoj && ElectronicWarfare.EvaluateHOJGuidance(
                ecmIntensity,
                doppler.IsInsideClutterNotch,
                r2
            );

            bool skinReturnValid = snrDb >= minSnr;
            bool targetTracked = skinReturnValid || isHojHoming;

            float strength = 0.0f;
            if (targetTracked)
            {
                if (skinReturnValid)
                {
                    strength = Mathf.Clamp((snrDb - minSnr) * 0.5f + 1.0f, 1.0f, 15.0f);
                }
                else
                {
                    strength = Mathf.Clamp(ecmIntensity * 5.0f, 2.5f, 7.5f);
                    ___isJammed = false;
                }

                ___trackingStrength = strength;
                __result = strength;
            }
            else
            {
                ___trackingStrength = 0.0f;
                __result = 0.0f;
            }

            // Trigger continuous wave (CW) missile lock warning on target
            Aircraft targetAircraft = targetUnit as Aircraft;
            if (targetAircraft != null)
            {
                Unit illumUnit = illuminator != null ? illuminator.GetAttachedUnit() : null;
                PhysRWRReceiver rwr = targetAircraft.GetComponent<PhysRWRReceiver>();
                if (rwr != null)
                {
                    float normSig = Mathf.Clamp01(strength / 10.0f);
                    rwr.RegisterRadarPing(illumUnit, illuminator, RwrThreatState.MissileGuidance, normSig, false);
                }

                try
                {
                    if (CombatHUD.i != null && targetAircraft == CombatHUD.i.aircraft && illumUnit != null)
                    {
                        targetAircraft.RpcGetRadarWarning(illumUnit);
                    }
                }
                catch { }

                if (CombatHUD.i != null && targetAircraft == CombatHUD.i.aircraft)
                {
                    RadarTelemetry.RegisterIllumination(
                        illuminator != null ? illuminator.GetInstanceID() : __instance.GetInstanceID(),
                        illumUnit != null ? illumUnit.unitName : "SARH Radar Illuminator",
                        "SARH CW Illuminator",
                        illumPos,
                        illuminator != null ? illuminator.transform.forward : Vector3.forward,
                        r1,
                        snrDb,
                        echoPower,
                        RadarIlluminationState.MissileGuidance
                    );
                }
            }

            return false;
        }
    }

    public static class SeekerDispersionHelper
    {
        private static readonly Func<Missile, Unit> getMissileTarget = FastReflection.CreateFieldGetter<Missile, Unit>("target");
        private static readonly Func<MissileSeeker, Unit> getSeekerTarget = FastReflection.CreateFieldGetter<MissileSeeker, Unit>("targetUnit");
        private static readonly Func<ARHSeeker, bool> f_arhLock = FastReflection.CreateFieldGetter<ARHSeeker, bool>("radarLockEstablished");
        private static readonly Func<OpticalSeeker, bool> f_optVisual = FastReflection.CreateFieldGetter<OpticalSeeker, bool>("hasVisual");
        private static readonly Func<OpticalSeekerCruiseMissile, bool> f_cruiseTerminal = FastReflection.CreateFieldGetter<OpticalSeekerCruiseMissile, bool>("terminalMode");
        private static readonly Func<OpticalSeekerBomb, bool> f_bombVisual = FastReflection.CreateFieldGetter<OpticalSeekerBomb, bool>("hasVisual");
        private static readonly Func<ARMSeeker, Radar> f_armRadar = FastReflection.CreateFieldGetter<ARMSeeker, Radar>("targetedRadar");
        private static readonly Func<SARHSeeker, float> f_sarhStrength = FastReflection.CreateFieldGetter<SARHSeeker, float>("trackingStrength");
        private static readonly Func<SARHSeeker, float> f_sarhTimeWithoutTrack = FastReflection.CreateFieldGetter<SARHSeeker, float>("timeWithoutTrack");

        private static readonly Func<OpticalSeekerShell, bool> f_shellVisual = FastReflection.CreateFieldGetter<OpticalSeekerShell, bool>("hasVisual");
        private static readonly Func<LaserSeeker, bool> f_laserLock = FastReflection.CreateFieldGetter<LaserSeeker, bool>("hasLock");
        private static readonly Func<IRSeeker, bool> f_irAchievedLock = FastReflection.CreateFieldGetter<IRSeeker, bool>("achievedLock");
        private static readonly Func<IRSeeker, IRSource> f_irTarget = FastReflection.CreateFieldGetter<IRSeeker, IRSource>("IRTarget");

        public static Unit GetTargetForMissile(Missile missile)
        {
            if (missile == null) return null;
            Unit target = null;
            if (getMissileTarget != null)
            {
                target = getMissileTarget(missile);
            }
            if (target == null && getSeekerTarget != null)
            {
                MissileSeeker seeker = missile.GetComponent<MissileSeeker>();
                if (seeker != null)
                {
                    target = getSeekerTarget(seeker);
                }
            }
            return target;
        }

        public static bool IsBallisticOrInertial(MissileSeeker seeker)
        {
            if (seeker == null) return false;
            return seeker is InertialSeekerShell ||
                   seeker is BallisticMissileGuidance ||
                   seeker is OpticalSeekerHighDrag;
        }

        public static bool IsSeekerTerminalLocked(Missile missile)
        {
            if (missile == null) return false;
            MissileSeeker seeker = missile.GetComponent<MissileSeeker>();
            if (seeker == null) return false;

            ARHSeeker arh = seeker as ARHSeeker;
            if (arh != null)
            {
                return f_arhLock != null && f_arhLock(arh);
            }
            OpticalSeeker opt = seeker as OpticalSeeker;
            if (opt != null)
            {
                return f_optVisual != null && f_optVisual(opt);
            }
            OpticalSeekerCruiseMissile cruise = seeker as OpticalSeekerCruiseMissile;
            if (cruise != null)
            {
                return f_cruiseTerminal != null && f_cruiseTerminal(cruise);
            }
            OpticalSeekerBomb bomb = seeker as OpticalSeekerBomb;
            if (bomb != null)
            {
                return f_bombVisual != null && f_bombVisual(bomb);
            }
            OpticalSeekerShell shell = seeker as OpticalSeekerShell;
            if (shell != null)
            {
                return f_shellVisual != null && f_shellVisual(shell);
            }
            ARMSeeker arm = seeker as ARMSeeker;
            if (arm != null)
            {
                return f_armRadar != null && f_armRadar(arm) != null;
            }
            SARHSeeker sarh = seeker as SARHSeeker;
            if (sarh != null)
            {
                return f_sarhStrength != null && f_sarhStrength(sarh) > 0.05f &&
                       f_sarhTimeWithoutTrack != null && f_sarhTimeWithoutTrack(sarh) < 0.1f;
            }
            LaserSeeker laser = seeker as LaserSeeker;
            if (laser != null)
            {
                return f_laserLock != null && f_laserLock(laser);
            }
            IRSeeker ir = seeker as IRSeeker;
            if (ir != null)
            {
                return f_irAchievedLock != null && f_irAchievedLock(ir) &&
                       f_irTarget != null && f_irTarget(ir) != null;
            }
            return false;
        }

        public static Vector3 GetDispersionForMissile(Missile missile, Unit target)
        {
            if (missile == null) return Vector3.zero;
            Vector3 offset;
            if (TrackUncertaintyCalculator.TryGetMissileDispersion(missile, out offset))
            {
                return offset;
            }

            if (target == null)
            {
                target = GetTargetForMissile(missile);
            }

            if (target == null) return Vector3.zero;

            FactionHQ hq = missile.NetworkHQ;
            if (hq == null && missile.owner != null) hq = missile.owner.NetworkHQ;
            if (hq == null) hq = RWRTriangulationProcessor.GetPlayerFactionHQ();

            TriangulationTrack track = RWRTriangulationProcessor.GetTrack(hq, target);
            float quality = (track != null) ? track.TrackingQuality : 0.15f;
            bool inMemory = (track != null) && track.IsInMemoryState();
            float memoryElapsed = (track != null) ? (Time.timeSinceLevelLoad - track.LastActiveDetectionTime) : 0f;

            bool isTriangulated = (track != null) && track.IsTriangulated;
            float dist = Vector3.Distance(missile.transform.position, target.transform.position);
            offset = TrackUncertaintyCalculator.GenerateHorizontalDispersion(dist, quality, inMemory, memoryElapsed, isTriangulated);
            TrackUncertaintyCalculator.RegisterMissileDispersion(missile, offset);
            return offset;
        }
    }

    /// <summary>
    /// Injects horizontal track uncertainty displacement (Dx, Dz, Dy=0) into ARAD anti-radiation missiles (ARMSeeker).
    /// During inertial flight, the missile flies towards the displaced aimpoint.
    /// If the enemy radar is actively transmitting within the seeker gimbal cone (maxTargetAngle), the passive RF seeker
    /// locks onto the radiation lobe and homes directly into the antenna.
    /// If the enemy radar shuts down during flight, the missile continues towards the stored aimpoint:
    /// with high team intelligence (Q->1.0), this scores a precision hit on the silent site;
    /// with low intelligence (Q=0.2), the weapon detonates harmlessly in empty terrain.
    /// </summary>
    [HarmonyPatch(typeof(ARMSeeker), "Initialize", new Type[] { typeof(Unit), typeof(GlobalPosition) })]
    public static class ARMSeeker_Initialize_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ARMSeeker __instance, Unit target, GlobalPosition aimpoint, Missile ___missile, ref GlobalPosition ___knownPos)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive) return;
                if (__instance == null) return;

                Radar r = (target != null) ? (target.radar as Radar) : null;
                if (r != null)
                {
                    ARMSeeker_TrackCurrentTarget_Patch.RegisterDesignatedRadar(__instance, r);
                }

                Vector3 disp = SeekerDispersionHelper.GetDispersionForMissile(___missile, target);

                if (disp != Vector3.zero)
                {
                    Vector3 local = ___knownPos.ToLocalPosition();
                    local.x += disp.x;
                    local.z += disp.z;
                    // Altitude (Y) is strictly preserved at target level!
                    ___knownPos = local.ToGlobalPosition();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] ARMSeeker_Initialize_Patch Postfix error: " + ex);
            }
        }
    }

    /// <summary>
    /// Delays ARM passive RF seeker radar source evaluation until within terminal activation basket (default 7000m / 7.0km).
    /// Before 7 km, munition navigates via datalink/inertial guidance towards displaced aimpoint.
    /// </summary>
    [HarmonyPatch(typeof(ARMSeeker), "EvaluateRadarSources")]
    public static class ARMSeeker_EvaluateRadarSources_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ARMSeeker __instance, ref Radar __result, Missile ___missile, GlobalPosition ___knownPos)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive) return true;
                if (__instance == null || ___missile == null) return true;

                float distToWaypoint = Vector3.Distance(___missile.transform.position, ___knownPos.ToLocalPosition());

                float activationDist = (RadioWarsConfig.ARADTerminalActivationDistanceMeters != null)
                    ? RadioWarsConfig.ARADTerminalActivationDistanceMeters.Value
                    : 7000.0f;

                if (distToWaypoint > activationDist)
                {
                    // Munition is still in midcourse datalink/inertial guidance towards displaced coordinates:
                    // Passive RF receiver does not evaluate radar sources until within 7 km basket.
                    __result = null;
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] ARMSeeker_EvaluateRadarSources_Patch error: " + ex);
            }
            return true;
        }
    }

    /// <summary>
    /// Delays ARM current target tracking until within terminal passive RF acquisition basket (default 7000m / 7.0km).
    /// Before 7 km, munition navigates via datalink towards displaced coordinates.
    /// Within 7 km, passive RF receiver detects enemy radar radiation and homes directly on transmitter antenna.
    /// Overrides vanilla 10,000m LineOfSight clamp with 50,000m terrain linecast.
    /// If radar shuts down, munition continues towards stored displaced aimpoint.
    /// </summary>
    [HarmonyPatch(typeof(ARMSeeker), "TrackCurrentTarget")]
    public static class ARMSeeker_TrackCurrentTarget_Patch
    {
        private static readonly ConditionalWeakTable<ARMSeeker, Radar> _designatedRadars = new ConditionalWeakTable<ARMSeeker, Radar>();
        private static readonly FieldInfo f_targetedRadar = AccessTools.Field(typeof(ARMSeeker), "targetedRadar");
        private static readonly FieldInfo f_lastLOSCheck = AccessTools.Field(typeof(ARMSeeker), "lastLOSCheck");

        public static void RegisterDesignatedRadar(ARMSeeker seeker, Radar radar)
        {
            if (seeker == null || radar == null) return;
            _designatedRadars.Remove(seeker);
            _designatedRadars.Add(seeker, radar);
        }

        [HarmonyPrefix]
        public static bool Prefix(ARMSeeker __instance, ref Radar __result, Missile ___missile, GlobalPosition ___knownPos)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive) return true;
                if (__instance == null || ___missile == null) return true;

                float activationDist = (RadioWarsConfig.ARADTerminalActivationDistanceMeters != null)
                    ? RadioWarsConfig.ARADTerminalActivationDistanceMeters.Value
                    : 7000.0f;

                Radar designated = null;
                _designatedRadars.TryGetValue(__instance, out designated);

                float distToWaypoint = Vector3.Distance(___missile.transform.position, ___knownPos.ToLocalPosition());
                float distToRadar = (designated != null) ? Vector3.Distance(___missile.transform.position, designated.transform.position) : float.MaxValue;
                float dist = Mathf.Min(distToWaypoint, distToRadar);

                if (dist > activationDist)
                {
                    // Beyond 7 km: midcourse datalink guidance towards displaced waypoint
                    __result = null;
                    return false;
                }

                // Within 7 km: check current targeted radar or restore designated radar
                Radar targeted = (f_targetedRadar != null) ? (Radar)f_targetedRadar.GetValue(__instance) : null;
                if (targeted == null && designated != null && designated.activated)
                {
                    targeted = designated;
                    if (f_targetedRadar != null) f_targetedRadar.SetValue(__instance, targeted);
                }

                if (targeted == null || !targeted.activated)
                {
                    __result = null;
                    return false;
                }

                // Check LineOfSight up to 50,000m (overriding vanilla 10,000m clamp)
                float lastLOS = (f_lastLOSCheck != null) ? (float)f_lastLOSCheck.GetValue(__instance) : 0f;
                if (Time.timeSinceLevelLoad - lastLOS > 0.25f)
                {
                    if (f_lastLOSCheck != null) f_lastLOSCheck.SetValue(__instance, Time.timeSinceLevelLoad);

                    Unit targetUnit = targeted.GetAttachedUnit();
                    if (targetUnit == null || !targetUnit.LineOfSight(___missile.transform.position, 50000f))
                    {
                        __result = null;
                        return false;
                    }
                }

                __result = targeted;
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] ARMSeeker_TrackCurrentTarget_Patch error: " + ex);
            }
            return true;
        }
    }

    /// <summary>
    /// Injects horizontal track uncertainty dispersion into ARH active radar missiles (ARHSeeker).
    /// Sets positionalErrorVector to pure horizontal offset (Dx, Dz, Dy=0) based on team intelligence at launch.
    /// Clamps terminalRange to MissileTerminalActivationDistanceMeters (default 2800m).
    /// </summary>
    [HarmonyPatch(typeof(ARHSeeker), "Initialize", new Type[] { typeof(Unit), typeof(GlobalPosition) })]
    public static class ARHSeeker_Initialize_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(
            ARHSeeker __instance,
            Unit target,
            GlobalPosition aimpoint,
            Missile ___missile,
            ref Vector3 ___positionalErrorVector,
            ref float ___terminalRange)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive) return;
                if (__instance == null) return;

                Vector3 disp = SeekerDispersionHelper.GetDispersionForMissile(___missile, target);

                if (disp != Vector3.zero)
                {
                    ___positionalErrorVector = new Vector3(disp.x, 0f, disp.z);
                }

                float pitbullDist = (RadioWarsConfig.ARHTerminalActivationDistanceMeters != null)
                    ? RadioWarsConfig.ARHTerminalActivationDistanceMeters.Value
                    : 10000.0f;

                ___terminalRange = pitbullDist;
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] ARHSeeker_Initialize_Patch Postfix error: " + ex);
            }
        }
    }

    /// <summary>
    /// Guides ARH missile via datalink midcourse towards target coordinates displaced by track uncertainty.
    /// If radar contact was lost and target is in 120s memory state, guides missile to LastKnownGlobalPosition + displacement.
    /// Once seeker acquires autonomous active lock at terminal pitbull range, this patch yields to seeker radar.
    /// Clamps terminalRange in Prefix so vanilla distance gate uses terminal basket range (default 10000m / 10km).
    /// </summary>
    [HarmonyPatch(typeof(ARHSeeker), "DatalinkMode")]
    public static class ARHSeeker_DatalinkMode_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(ARHSeeker __instance, ref float ___terminalRange)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive || __instance == null) return;
                float pitbullDist = (RadioWarsConfig.ARHTerminalActivationDistanceMeters != null)
                    ? RadioWarsConfig.ARHTerminalActivationDistanceMeters.Value
                    : 10000.0f;
                ___terminalRange = pitbullDist;
            }
            catch { }
        }

        [HarmonyPostfix]
        public static void Postfix(
            ARHSeeker __instance,
            Unit ___targetUnit,
            Missile ___missile,
            bool ___radarLockEstablished,
            ref GlobalPosition ___knownPos,
            ref Vector3 ___positionalErrorVector)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive) return;
                if (__instance == null) return;

                Unit target = ___targetUnit;
                if (target == null || target.disabled) return;

                if (___radarLockEstablished) return; // Seeker radar acquired physical target directly

                Missile missile = ___missile;
                FactionHQ hq = (missile != null) ? missile.NetworkHQ : null;
                TriangulationTrack track = RWRTriangulationProcessor.GetTrack(hq, target);
                if (track == null) return;

                float memDuration = (RadioWarsConfig.TargetMemoryDurationSeconds != null)
                    ? RadioWarsConfig.TargetMemoryDurationSeconds.Value
                    : 120.0f;

                Vector3 disp = SeekerDispersionHelper.GetDispersionForMissile(missile, target);

                if (track.IsInMemoryState(memDuration))
                {
                    // Target contact was lost: guide missile to frozen last known coordinates + horizontal offset
                    Vector3 memLocal = track.LastKnownGlobalPosition.ToLocalPosition();
                    memLocal.x += disp.x;
                    memLocal.z += disp.z;
                    ___knownPos = memLocal.ToGlobalPosition();
                    ___positionalErrorVector = Vector3.zero;
                }
                else
                {
                    // Active tracking: ensure error vector is purely horizontal (Y = 0)
                    if (Mathf.Abs(___positionalErrorVector.y) > 0.001f)
                    {
                        ___positionalErrorVector = new Vector3(___positionalErrorVector.x, 0f, ___positionalErrorVector.z);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] ARHSeeker_DatalinkMode_Patch Postfix error: " + ex);
            }
        }
    }

    /// <summary>
    /// If SARH illuminator radar tracking contact is lost (120s memory state) or initial tracking solution is coarse,
    /// applies horizontal track uncertainty with exponential decay as carrier illumination stabilizes.
    /// </summary>
    [HarmonyPatch(typeof(SARHSeeker), "LockedMode")]
    public static class SARHSeeker_LockedMode_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(
            SARHSeeker __instance,
            Unit ___targetUnit,
            Missile ___missile,
            ref GlobalPosition ___knownPos)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive) return;
                if (__instance == null) return;

                Unit target = ___targetUnit;
                if (target == null || target.disabled) return;

                Missile missile = ___missile;
                FactionHQ hq = (missile != null) ? missile.NetworkHQ : null;
                TriangulationTrack track = RWRTriangulationProcessor.GetTrack(hq, target);
                if (track == null) return;

                float memDuration = (RadioWarsConfig.TargetMemoryDurationSeconds != null)
                    ? RadioWarsConfig.TargetMemoryDurationSeconds.Value
                    : 120.0f;

                Vector3 disp = SeekerDispersionHelper.GetDispersionForMissile(missile, target);

                if (track.IsInMemoryState(memDuration))
                {
                    Vector3 memLocal = track.LastKnownGlobalPosition.ToLocalPosition();
                    memLocal.x += disp.x;
                    memLocal.z += disp.z;
                    ___knownPos = memLocal.ToGlobalPosition();
                }
                else if (disp != Vector3.zero && missile != null)
                {
                    // Decay initial launch uncertainty as carrier radar maintains lock
                    float age = missile.timeSinceSpawn;
                    float decay = Mathf.Exp(-age / 4.0f);
                    if (decay > 0.05f)
                    {
                        Vector3 local = ___knownPos.ToLocalPosition();
                        local.x += disp.x * decay;
                        local.z += disp.z * decay;
                        ___knownPos = local.ToGlobalPosition();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] SARHSeeker_LockedMode_Patch Postfix error: " + ex);
            }
        }
    }

    /// <summary>
    /// Injects horizontal track uncertainty displacement (Dx, Dz, Dy=0) into optical munitions (AGM-68, AGM-48, ATP-1, glide bombs).
    /// Enforces hasVisual = false at launch to prevent instant standoff lock.
    /// Directs missile autopilot towards displaced waypoint.
    /// </summary>
    [HarmonyPatch(typeof(OpticalSeeker), "Initialize", new Type[] { typeof(Unit), typeof(GlobalPosition) })]
    public static class OpticalSeeker_Initialize_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(
            OpticalSeeker __instance,
            Unit target,
            GlobalPosition aimpoint,
            Missile ___missile,
            ref GlobalPosition ___knownPos,
            ref Vector3 ___knownVel,
            ref bool ___hasVisual)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive) return;
                if (__instance == null || ___missile == null) return;

                ___hasVisual = false;

                Vector3 disp = SeekerDispersionHelper.GetDispersionForMissile(___missile, target);
                if (disp != Vector3.zero)
                {
                    Vector3 local = ___knownPos.ToLocalPosition();
                    local.x += disp.x;
                    local.z += disp.z;
                    ___knownPos = local.ToGlobalPosition();

                    ___missile.SetAimpoint(___knownPos, ___knownVel);
                    ___missile.SetTarget(null);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] OpticalSeeker_Initialize_Patch error: " + ex);
            }
        }
    }

    /// <summary>
    /// Replaces vanilla OpticalCheck to enforce terminal distance gating (MissileTerminalActivationDistanceMeters, default 2800m).
    /// In midcourse flight (> 2800m), suppresses visual lock and prevents vanilla from overwriting knownPos with true target coordinates.
    /// In terminal phase (<= 2800m), evaluates optical acquisition: target must physically reside inside pitbull basket
    /// around the displaced aimpoint, with unblocked line of sight and within gimbal angle limits.
    /// </summary>
    [HarmonyPatch(typeof(OpticalSeeker), "OpticalCheck")]
    public static class OpticalSeeker_OpticalCheck_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            OpticalSeeker __instance,
            Missile ___missile,
            Unit ___targetUnit,
            Transform ___targetTransform,
            ref GlobalPosition ___knownPos,
            ref Vector3 ___knownVel,
            ref bool ___hasVisual,
            ref bool ___targetObstructed,
            ref float ___lastOpticalCheck,
            float ___searchRadius,
            float ___searchAngle,
            bool ___useDatalink)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive) return true;
                if (__instance == null || ___missile == null) return true;

                ___lastOpticalCheck = Time.timeSinceLevelLoad;

                Unit target = ___targetUnit;
                if (target == null || target.disabled)
                {
                    ___hasVisual = false;
                    ___targetObstructed = false;
                    return false;
                }

                Vector3 missilePos = ___missile.transform.position;
                Vector3 aimpointLocal = ___knownPos.ToLocalPosition();
                float distToAimpoint = Vector3.Distance(missilePos, aimpointLocal);
                float distToTarget = Vector3.Distance(missilePos, target.transform.position);

                float pitbullDist = (RadioWarsConfig.MissileTerminalActivationDistanceMeters != null)
                    ? RadioWarsConfig.MissileTerminalActivationDistanceMeters.Value
                    : 2800.0f;

                // 1. Standoff Midcourse Range Gate: missile must be in terminal approach
                if (distToAimpoint > pitbullDist && distToTarget > pitbullDist)
                {
                    ___hasVisual = false;
                    ___targetObstructed = false;
                    return false;
                }

                // 2. Terminal Basket Gate: target must physically reside inside pitbull basket of displaced aimpoint
                float basketRadius = Mathf.Max(___searchRadius, pitbullDist);
                bool inBasket = FastMath.InRange(target.GlobalPosition(), ___knownPos, basketRadius + target.maxRadius);

                if (!inBasket)
                {
                    ___targetObstructed = false;
                    ___hasVisual = false;
                    return false;
                }

                // 3. Seeker Visual Acquisition Range: missile must be within optical resolution distance of target
                if (distToTarget > pitbullDist)
                {
                    ___hasVisual = false;
                    ___targetObstructed = false;
                    return false;
                }

                // 4. Seeker gimbal / boresight angle check (within seeker FOV cone)
                Vector3 toTarget = target.transform.position - missilePos;
                float gimbalAngle = (___searchAngle > 10.0f) ? ___searchAngle : 90.0f;
                bool inGimbal = Vector3.Angle(toTarget, ___missile.transform.forward) < gimbalAngle;

                if (!inGimbal)
                {
                    ___hasVisual = false;
                    ___targetObstructed = false;
                    return false;
                }

                // 5. Line of sight check
                bool los = target.LineOfSight(missilePos, pitbullDist);

                if (los)
                {
                    ___hasVisual = true;
                    ___targetObstructed = false;
                }
                else
                {
                    ___hasVisual = false;
                    ___targetObstructed = true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] OpticalSeeker_OpticalCheck_Patch error: " + ex);
                return true;
            }
        }
    }

    /// <summary>
    /// Governs optical munition midcourse and terminal guidance.
    /// In midcourse flight (hasVisual == false), maintains displaced aimpoint anchoring based on datalink/memory track
    /// and guides munition towards displaced waypoint without lead pursuit cheating.
    /// Once direct visual contrast lock is acquired (hasVisual == true), yields to vanilla target lead pursuit tracking.
    /// </summary>
    [HarmonyPatch(typeof(OpticalSeeker), "GetTargetParameters")]
    public static class OpticalSeeker_GetTargetParameters_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(
            OpticalSeeker __instance,
            bool ___hasVisual,
            Unit ___targetUnit,
            Missile ___missile,
            ref GlobalPosition ___knownPos,
            ref Vector3 ___knownVel)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive) return;
                if (__instance == null || ___missile == null) return;

                if (___hasVisual) return; // Optical sensor acquired direct visual line-of-sight

                Unit target = ___targetUnit;
                if (target == null || target.disabled) return;

                Vector3 disp = SeekerDispersionHelper.GetDispersionForMissile(___missile, target);
                if (disp == Vector3.zero) return;

                FactionHQ hq = ___missile.NetworkHQ;
                if (hq == null && ___missile.owner != null) hq = ___missile.owner.NetworkHQ;
                if (hq == null) hq = RWRTriangulationProcessor.GetPlayerFactionHQ();

                TriangulationTrack track = RWRTriangulationProcessor.GetTrack(hq, target);

                float memDuration = (RadioWarsConfig.TargetMemoryDurationSeconds != null)
                    ? RadioWarsConfig.TargetMemoryDurationSeconds.Value
                    : 120.0f;

                GlobalPosition baseG;
                if (track != null && track.IsInMemoryState(memDuration))
                {
                    baseG = track.LastKnownGlobalPosition;
                }
                else
                {
                    baseG = target.GlobalPosition();
                }

                Vector3 local = baseG.ToLocalPosition();
                local.x += disp.x;
                local.z += disp.z;
                ___knownPos = local.ToGlobalPosition();

                if (target.rb != null)
                {
                    ___knownVel = target.rb.velocity;
                }
                else
                {
                    ___knownVel = Vector3.zero;
                }

                ___missile.SetAimpoint(___knownPos, ___knownVel);
                ___missile.SetTarget(null);
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] OpticalSeeker_GetTargetParameters_Patch Postfix error: " + ex);
            }
        }
    }

    /// <summary>
    /// Injects horizontal track uncertainty displacement into cruise missiles (OpticalSeekerCruiseMissile).
    /// </summary>
    [HarmonyPatch(typeof(OpticalSeekerCruiseMissile), "Initialize", new Type[] { typeof(Unit), typeof(GlobalPosition) })]
    public static class OpticalSeekerCruiseMissile_Initialize_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(
            OpticalSeekerCruiseMissile __instance,
            Unit target,
            GlobalPosition aimpoint,
            Missile ___missile,
            ref GlobalPosition ___knownPos,
            ref GlobalPosition ___aimPos,
            ref bool ___terminalMode)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive) return;
                if (__instance == null || ___missile == null) return;

                ___terminalMode = false;

                Vector3 disp = SeekerDispersionHelper.GetDispersionForMissile(___missile, target);
                if (disp != Vector3.zero)
                {
                    Vector3 local = ___knownPos.ToLocalPosition();
                    local.x += disp.x;
                    local.z += disp.z;
                    ___knownPos = local.ToGlobalPosition();
                    ___aimPos = ___knownPos;

                    ___missile.SetAimpoint(___knownPos, Vector3.zero);
                    ___missile.SetTarget(null);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] OpticalSeekerCruiseMissile_Initialize_Patch error: " + ex);
            }
        }
    }

    /// <summary>
    /// Preserves horizontal dispersion on cruise missiles when datalink updates known position.
    /// </summary>
    [HarmonyPatch(typeof(OpticalSeekerCruiseMissile), "UpdateTargetParameters")]
    public static class OpticalSeekerCruiseMissile_UpdateTargetParameters_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(
            OpticalSeekerCruiseMissile __instance,
            Unit ___targetUnit,
            Missile ___missile,
            bool ___terminalMode,
            ref GlobalPosition ___knownPos,
            ref GlobalPosition ___aimPos)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive) return;
                if (__instance == null || ___missile == null) return;
                if (___terminalMode) return;

                Unit target = ___targetUnit;
                if (target == null || target.disabled) return;

                Vector3 disp = SeekerDispersionHelper.GetDispersionForMissile(___missile, target);
                if (disp == Vector3.zero) return;

                FactionHQ hq = ___missile.NetworkHQ;
                if (hq == null && ___missile.owner != null) hq = ___missile.owner.NetworkHQ;
                if (hq == null) hq = RWRTriangulationProcessor.GetPlayerFactionHQ();

                TriangulationTrack track = RWRTriangulationProcessor.GetTrack(hq, target);
                float memDuration = (RadioWarsConfig.TargetMemoryDurationSeconds != null)
                    ? RadioWarsConfig.TargetMemoryDurationSeconds.Value
                    : 120.0f;

                GlobalPosition baseG;
                if (track != null && track.IsInMemoryState(memDuration))
                {
                    baseG = track.LastKnownGlobalPosition;
                }
                else
                {
                    baseG = target.GlobalPosition();
                }

                Vector3 local = baseG.ToLocalPosition();
                local.x += disp.x;
                local.z += disp.z;
                ___knownPos = local.ToGlobalPosition();
                ___aimPos = ___knownPos;

                ___missile.SetAimpoint(___knownPos, Vector3.zero);
                ___missile.SetTarget(null);
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] OpticalSeekerCruiseMissile_UpdateTargetParameters_Patch error: " + ex);
            }
        }
    }

    /// <summary>
    /// Enforces terminal distance gating and seeker basket check on cruise missiles.
    /// Only triggers terminal mode if target resides within terminal search radius of displaced waypoint.
    /// </summary>
    [HarmonyPatch(typeof(OpticalSeekerCruiseMissile), "PreTerminalMode")]
    public static class OpticalSeekerCruiseMissile_PreTerminalMode_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            OpticalSeekerCruiseMissile __instance,
            Missile ___missile,
            Unit ___targetUnit,
            ref Transform ___targetPart,
            ref bool ___terminalMode,
            ref float ___terminalRange,
            float ___terminalSearchRadius,
            ref GlobalPosition ___knownPos,
            ref GlobalPosition ___aimPos,
            ref float ___lastTerminalCheck)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive) return true;
                if (__instance == null || ___missile == null) return true;

                float pitbullDist = (RadioWarsConfig.MissileTerminalActivationDistanceMeters != null)
                    ? RadioWarsConfig.MissileTerminalActivationDistanceMeters.Value
                    : 2800.0f;
                ___terminalRange = pitbullDist;

                if (Time.timeSinceLevelLoad - ___lastTerminalCheck < 0.5f) return false;
                ___lastTerminalCheck = Time.timeSinceLevelLoad;

                if (__instance.CheckWaypoint())
                {
                    ___aimPos = ___knownPos;
                }

                GlobalPosition terrainWp = __instance.TerrainWaypoint(___aimPos);
                ___missile.SetAimpoint(terrainWp, Vector3.zero);

                if (___missile.timeSinceSpawn <= 6.0f) return false;

                if (FastMath.InRange(___missile.GlobalPosition(), ___knownPos, pitbullDist))
                {
                    Unit target = ___targetUnit;
                    if (target != null && !target.disabled)
                    {
                        float searchRad = Mathf.Max(___terminalSearchRadius, pitbullDist);
                        if (FastMath.InRange(target.GlobalPosition(), ___knownPos, searchRad + target.maxRadius))
                        {
                            ___targetPart = target.GetRandomPart();
                            ___terminalMode = true;
                            ___missile.Arm();
                            return false;
                        }
                        else
                        {
                            // Target outside terminal basket: cruise missile continues to displaced waypoint
                            return false;
                        }
                    }
                    else
                    {
                        if (___missile.rb != null)
                        {
                            ___missile.Detonate(___missile.rb.velocity, false, false);
                        }
                        return false;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] OpticalSeekerCruiseMissile_PreTerminalMode_Patch error: " + ex);
                return true;
            }
        }
    }

    /// <summary>
    /// Injects horizontal track uncertainty displacement into free-fall guided bombs (OpticalSeekerBomb).
    /// </summary>
    [HarmonyPatch(typeof(OpticalSeekerBomb), "Initialize", new Type[] { typeof(Unit), typeof(GlobalPosition) })]
    public static class OpticalSeekerBomb_Initialize_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(
            OpticalSeekerBomb __instance,
            Unit target,
            GlobalPosition aimpoint,
            Missile ___missile,
            ref GlobalPosition ___knownPos,
            ref GlobalPosition ___aimPos,
            ref bool ___hasVisual)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive) return;
                if (__instance == null || ___missile == null) return;

                ___hasVisual = false;

                Vector3 disp = SeekerDispersionHelper.GetDispersionForMissile(___missile, target);
                if (disp != Vector3.zero)
                {
                    Vector3 local = ___knownPos.ToLocalPosition();
                    local.x += disp.x;
                    local.z += disp.z;
                    ___knownPos = local.ToGlobalPosition();
                    ___aimPos = ___knownPos;

                    ___missile.SetAimpoint(___knownPos, Vector3.zero);
                    ___missile.SetTarget(null);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] OpticalSeekerBomb_Initialize_Patch error: " + ex);
            }
        }
    }

    /// <summary>
    /// Delays optical visual acquisition for guided bombs until within terminal distance (2800m) of displaced aimpoint.
    /// </summary>
    [HarmonyPatch(typeof(OpticalSeekerBomb), "TrackVisual")]
    public static class OpticalSeekerBomb_TrackVisual_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            OpticalSeekerBomb __instance,
            Missile ___missile,
            Unit ___targetUnit,
            GlobalPosition ___knownPos,
            float ___searchRadius,
            ref float ___lastVisualCheck,
            ref bool __result)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive) return true;
                if (__instance == null || ___missile == null) return true;

                ___lastVisualCheck = Time.timeSinceLevelLoad;

                Unit target = ___targetUnit;
                if (target == null || target.disabled)
                {
                    __result = false;
                    return false;
                }

                float pitbullDist = (RadioWarsConfig.MissileTerminalActivationDistanceMeters != null)
                    ? RadioWarsConfig.MissileTerminalActivationDistanceMeters.Value
                    : 2800.0f;

                Vector3 bombPos = ___missile.transform.position;
                Vector3 aimLocal = ___knownPos.ToLocalPosition();
                float distToAimpoint = Vector3.Distance(bombPos, aimLocal);
                float distToTarget = Vector3.Distance(bombPos, target.transform.position);

                // 1. Terminal range gate
                if (distToAimpoint > pitbullDist && distToTarget > pitbullDist)
                {
                    __result = false;
                    return false;
                }

                // 2. Terminal basket gate: target must be within pitbull basket of aimpoint
                float basketRadius = Mathf.Max(___searchRadius, pitbullDist);
                if (!FastMath.InRange(target.GlobalPosition(), ___knownPos, basketRadius + target.maxRadius))
                {
                    __result = false;
                    return false;
                }

                // 3. Sensor acquisition range
                if (distToTarget > pitbullDist)
                {
                    __result = false;
                    return false;
                }

                // 4. Line of Sight
                __result = target.LineOfSight(bombPos, pitbullDist);
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] OpticalSeekerBomb_TrackVisual_Patch error: " + ex);
                return true;
            }
        }
    }

    /// <summary>
    /// Governs optical guided bomb guidance: maintains displaced aimpoint while unacquired.
    /// </summary>
    [HarmonyPatch(typeof(OpticalSeekerBomb), "GetTargetParameters")]
    public static class OpticalSeekerBomb_GetTargetParameters_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(
            OpticalSeekerBomb __instance,
            bool ___hasVisual,
            Unit ___targetUnit,
            Missile ___missile,
            ref GlobalPosition ___knownPos)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive) return;
                if (__instance == null || ___missile == null) return;

                if (___hasVisual) return;

                Unit target = ___targetUnit;
                if (target == null || target.disabled) return;

                Vector3 disp = SeekerDispersionHelper.GetDispersionForMissile(___missile, target);
                if (disp == Vector3.zero) return;

                Vector3 local = target.GlobalPosition().ToLocalPosition();
                local.x += disp.x;
                local.z += disp.z;
                ___knownPos = local.ToGlobalPosition();

                ___missile.SetTarget(null);
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] OpticalSeekerBomb_GetTargetParameters_Patch error: " + ex);
            }
        }
    }

    /// <summary>
    /// Prevents vanilla IRSeeker.Initialize from dropping thermal lock when team datalink track uncertainty
    /// exceeds 500m. Heatseekers home directly on engine thermal plumes (physical line-of-sight), not datalink coordinates.
    /// </summary>
    [HarmonyPatch(typeof(IRSeeker), "Initialize", new Type[] { typeof(Unit), typeof(GlobalPosition) })]
    public static class IRSeeker_Initialize_Patch
    {
        private static readonly MethodInfo m_onTargetFlare = AccessTools.Method(typeof(IRSeeker), "IRSeeker_OnTargetFlare");

        [HarmonyPostfix]
        public static void Postfix(
            IRSeeker __instance,
            Unit target,
            ref IRSource ___IRTarget,
            ref bool ___targetOnLaunch,
            ref bool ___achievedLock,
            Missile ___missile)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive) return;
                if (__instance == null || target == null || target.disabled || ___missile == null) return;

                // If vanilla already has a valid IRTarget, thermal lock succeeded
                if (___IRTarget != null) return;

                // Radio Wars conflict fix:
                // Check if thermal lock should have succeeded based on line-of-sight and thermal signature
                if (target.HasIRSignature() && target.LineOfSight(___missile.transform.position, 1500f))
                {
                    IRSource source = target.GetIRSource();
                    if (source != null && !source.flare)
                    {
                        ___IRTarget = source;
                        ___targetOnLaunch = true;
                        ___achievedLock = true;
                        ___missile.SetTarget(target);

                        if (m_onTargetFlare != null)
                        {
                            Action<IRSource> flareDelegate = (Action<IRSource>)Delegate.CreateDelegate(typeof(Action<IRSource>), __instance, m_onTargetFlare);
                            target.onAddIRSource += flareDelegate;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] IRSeeker_Initialize_Patch error: " + ex);
            }
        }
    }

    /// <summary>
    /// Injects team reconnaissance quality (Q) track uncertainty dispersion into guided/ballistic artillery shells.
    /// </summary>
    [HarmonyPatch(typeof(InertialSeekerShell), "Initialize", new Type[] { typeof(Unit), typeof(GlobalPosition) })]
    public static class InertialSeekerShell_Initialize_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(
            InertialSeekerShell __instance,
            Unit target,
            Missile ___missile,
            ref GlobalPosition ___knownPos)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive) return;
                if (__instance == null || ___missile == null) return;

                Unit t = (target != null && !target.disabled) ? target : SeekerDispersionHelper.GetTargetForMissile(___missile);
                if (t == null) return;

                Vector3 disp = SeekerDispersionHelper.GetDispersionForMissile(___missile, t);
                if (disp != Vector3.zero)
                {
                    Vector3 local = ___knownPos.ToLocalPosition();
                    local.x += disp.x;
                    local.z += disp.z;
                    ___knownPos = local.ToGlobalPosition();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] InertialSeekerShell_Initialize_Patch error: " + ex);
            }
        }
    }

    /// <summary>
    /// Prevents vanilla InertialSeekerShell from cheating with god-mode datalink coordinate updates (knownPos = targetUnit.GlobalPosition()).
    /// Maintains displaced aimpoint tracking based on team datalink track quality.
    /// </summary>
    [HarmonyPatch(typeof(InertialSeekerShell), "UpdateTargetPosition")]
    public static class InertialSeekerShell_UpdateTargetPosition_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            InertialSeekerShell __instance,
            bool ___useDatalink,
            Missile ___missile,
            Unit ___targetUnit,
            ref GlobalPosition ___knownPos,
            ref Vector3 ___knownVel)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive) return true;
                if (__instance == null || ___missile == null) return true;

                if (!___useDatalink) return false;
                FactionHQ hq = ___missile.NetworkHQ;
                if (hq == null || ___targetUnit == null || ___targetUnit.disabled) return false;

                if (!hq.IsTargetBeingTracked(___targetUnit)) return false;

                Vector3 disp = Vector3.zero;
                TrackUncertaintyCalculator.TryGetMissileDispersion(___missile, out disp);

                Vector3 local = ___targetUnit.GlobalPosition().ToLocalPosition();
                local.x += disp.x;
                local.z += disp.z;
                ___knownPos = local.ToGlobalPosition();

                ___knownVel = (___targetUnit.rb != null) ? ___targetUnit.rb.velocity : Vector3.zero;
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] InertialSeekerShell_UpdateTargetPosition_Patch error: " + ex);
                return true;
            }
        }
    }

    /// <summary>
    /// Scales long-range ballistic missile (LRBM) circular error offset with team launch track uncertainty (Q).
    /// Since ballistic missiles have no terminal seeker head, the munition detonates at the displaced coordinates.
    /// </summary>
    [HarmonyPatch(typeof(BallisticMissileGuidance), "Initialize", new Type[] { typeof(Unit), typeof(GlobalPosition) })]
    public static class BallisticMissileGuidance_Initialize_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(
            BallisticMissileGuidance __instance,
            Unit target,
            Missile ___missile,
            ref Vector3 ___errorOffset)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive) return;
                if (__instance == null || ___missile == null) return;

                Unit t = (target != null && !target.disabled) ? target : SeekerDispersionHelper.GetTargetForMissile(___missile);
                if (t == null) return;

                Vector3 disp = SeekerDispersionHelper.GetDispersionForMissile(___missile, t);
                if (disp != Vector3.zero)
                {
                    ___errorOffset.x += disp.x;
                    ___errorOffset.z += disp.z;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] BallisticMissileGuidance_Initialize_Patch error: " + ex);
            }
        }
    }

    /// <summary>
    /// Injects horizontal track uncertainty displacement (Dx, Dz, Dy=0) into guided cannon shells (OpticalSeekerShell).
    /// </summary>
    [HarmonyPatch(typeof(OpticalSeekerShell), "Initialize", new Type[] { typeof(Unit), typeof(GlobalPosition) })]
    public static class OpticalSeekerShell_Initialize_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(
            OpticalSeekerShell __instance,
            Unit target,
            Missile ___missile,
            ref GlobalPosition ___knownPos)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive) return;
                if (__instance == null || ___missile == null) return;

                Unit t = (target != null && !target.disabled) ? target : SeekerDispersionHelper.GetTargetForMissile(___missile);
                if (t == null) return;

                Vector3 disp = SeekerDispersionHelper.GetDispersionForMissile(___missile, t);
                if (disp == Vector3.zero) return;

                Vector3 local = ___knownPos.ToLocalPosition();
                local.x += disp.x;
                local.z += disp.z;
                ___knownPos = local.ToGlobalPosition();

                ___missile.SetTarget(null);
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] OpticalSeekerShell_Initialize_Patch error: " + ex);
            }
        }
    }

    /// <summary>
    /// Gating check for guided cannon shells: requires target to be inside the terminal pitbull basket (2800m),
    /// within optical look angle, and unobstructed before visual lock can be acquired.
    /// </summary>
    [HarmonyPatch(typeof(OpticalSeekerShell), "TrackVisual")]
    public static class OpticalSeekerShell_TrackVisual_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            OpticalSeekerShell __instance,
            ref bool __result,
            ref float ___lastVisualCheck,
            Unit ___targetUnit,
            Missile ___missile,
            GlobalPosition ___knownPos)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive) return true;
                if (__instance == null || ___missile == null)
                {
                    __result = false;
                    return false;
                }

                ___lastVisualCheck = Time.timeSinceLevelLoad;

                Unit target = ___targetUnit;
                if (target == null || target.disabled)
                {
                    __result = false;
                    return false;
                }

                Vector3 shellPos = ___missile.transform.position;
                Vector3 targetPos = target.transform.position;
                float distToTarget = Vector3.Distance(shellPos, targetPos);

                float pitbullDist = (RadioWarsConfig.MissileTerminalActivationDistanceMeters != null)
                    ? RadioWarsConfig.MissileTerminalActivationDistanceMeters.Value
                    : 2800.0f;

                // 1. Terminal Pitbull Basket check
                Vector3 disp = Vector3.zero;
                if (TrackUncertaintyCalculator.TryGetMissileDispersion(___missile, out disp))
                {
                    if (disp.magnitude > pitbullDist)
                    {
                        __result = false;
                        return false;
                    }
                }

                // 2. Optical look angle check (must be within seeker FOV)
                Vector3 toTarget = (targetPos - shellPos).normalized;
                float lookAngle = Vector3.Angle(___missile.transform.forward, toTarget);
                if (lookAngle > 45.0f)
                {
                    __result = false;
                    return false;
                }

                // 3. Sensor acquisition range
                if (distToTarget > pitbullDist)
                {
                    __result = false;
                    return false;
                }

                // 4. Line of Sight
                __result = target.LineOfSight(shellPos, pitbullDist);
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] OpticalSeekerShell_TrackVisual_Patch error: " + ex);
                return true;
            }
        }
    }

    /// <summary>
    /// Governs guided cannon shell guidance: maintains displaced aimpoint while unacquired.
    /// </summary>
    [HarmonyPatch(typeof(OpticalSeekerShell), "GetTargetParameters")]
    public static class OpticalSeekerShell_GetTargetParameters_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(
            OpticalSeekerShell __instance,
            bool ___hasVisual,
            Unit ___targetUnit,
            Missile ___missile,
            ref GlobalPosition ___knownPos)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive) return;
                if (__instance == null || ___missile == null) return;

                if (___hasVisual) return;

                Unit target = ___targetUnit;
                if (target == null || target.disabled) return;

                Vector3 disp = SeekerDispersionHelper.GetDispersionForMissile(___missile, target);
                if (disp == Vector3.zero) return;

                Vector3 local = target.GlobalPosition().ToLocalPosition();
                local.x += disp.x;
                local.z += disp.z;
                ___knownPos = local.ToGlobalPosition();

                ___missile.SetTarget(null);
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] OpticalSeekerShell_GetTargetParameters_Patch error: " + ex);
            }
        }
    }
}
