using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using RadioWars.Config;
using RadioWars.Components;
using RadioWars.Core;

namespace RadioWars.Patches
{
    /// <summary>
    /// Event-driven hook for player-launched missiles to trigger Flight HUD trajectory lines and ETA tracking,
    /// and cooperative Datalink network missile tracking.
    /// </summary>
    [HarmonyPatch(typeof(Unit))]
    public static class MissilePatches
    {
        private static readonly Func<Missile, Unit> f_missileTarget = CreateFieldGetter<Missile, Unit>("target");
        private static readonly Func<MissileSeeker, Unit> f_seekerTarget = CreateFieldGetter<MissileSeeker, Unit>("targetUnit");

        private static Func<TTarget, TField> CreateFieldGetter<TTarget, TField>(string fieldName)
        {
            try
            {
                var fi = AccessTools.Field(typeof(TTarget), fieldName);
                if (fi == null) return null;
                var dm = new System.Reflection.Emit.DynamicMethod("Get_" + typeof(TTarget).Name + "_" + fieldName,
                    typeof(TField), new Type[] { typeof(TTarget) }, typeof(TTarget), true);
                var il = dm.GetILGenerator();
                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
                il.Emit(System.Reflection.Emit.OpCodes.Ldfld, fi);
                il.Emit(System.Reflection.Emit.OpCodes.Ret);
                return (Func<TTarget, TField>)dm.CreateDelegate(typeof(Func<TTarget, TField>));
            }
            catch { return null; }
        }

        [HarmonyPatch("RegisterMissile")]
        [HarmonyPostfix]
        public static void RegisterMissile_Postfix(Unit __instance, Missile missile)
        {
            if (!RadioWarsConfig.IsModActive || missile == null || __instance == null) return;

            // Track active airborne missile for allied radar surveillance & datalink early warning
            DatalinkNetwork.RegisterMissile(missile);

            // Calculate and register track uncertainty dispersion for the launched munition
            try
            {
                Unit target = (f_missileTarget != null) ? f_missileTarget(missile) : null;
                if (target == null && f_seekerTarget != null)
                {
                    MissileSeeker seeker = missile.GetComponent<MissileSeeker>();
                    if (seeker != null)
                    {
                        target = f_seekerTarget(seeker);
                    }
                }

                if (target != null && !target.disabled)
                {
                    FactionHQ hq = missile.NetworkHQ;
                    if (hq == null && missile.owner != null) hq = missile.owner.NetworkHQ;
                    if (hq == null) hq = RWRTriangulationProcessor.GetPlayerFactionHQ();

                    TriangulationTrack track = RWRTriangulationProcessor.GetTrack(hq, target);
                    float quality = (track != null) ? track.TrackingQuality : 0.15f;
                    bool inMemory = (track != null) && track.IsInMemoryState();
                    float memoryElapsed = (track != null) ? (Time.timeSinceLevelLoad - track.LastActiveDetectionTime) : 0f;

                    bool isTriangulated = (track != null) && track.IsTriangulated;
                    float dist = Vector3.Distance(missile.transform.position, target.transform.position);
                    Vector3 disp = TrackUncertaintyCalculator.GenerateHorizontalDispersion(dist, quality, inMemory, memoryElapsed, isTriangulated);
                    TrackUncertaintyCalculator.RegisterMissileDispersion(missile, disp);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] RegisterMissile TrackUncertainty error: " + ex);
            }

            if (RadioWarsConfig.EnableMissileTrajectoryHUD == null || !RadioWarsConfig.EnableMissileTrajectoryHUD.Value) return;

            try
            {
                // Only track munitions launched by the local player's aircraft
                bool isPlayerAircraft = false;
                if (CombatHUD.i != null && CombatHUD.i.aircraft != null)
                {
                    if (CombatHUD.i.aircraft == __instance || (missile.owner != null && missile.owner == CombatHUD.i.aircraft))
                    {
                        isPlayerAircraft = true;
                    }
                }

                if (isPlayerAircraft)
                {
                    if (MissileTrajectoryHUDVisualizer.Instance != null)
                    {
                        MissileTrajectoryHUDVisualizer.Instance.OnMissileLaunched(missile);
                    }
                    else if (FlightHud.i != null)
                    {
                        MissileTrajectoryHUDVisualizer visualizer = FlightHud.i.gameObject.GetComponent<MissileTrajectoryHUDVisualizer>();
                        if (visualizer == null)
                        {
                            visualizer = FlightHud.i.gameObject.AddComponent<MissileTrajectoryHUDVisualizer>();
                        }
                        visualizer.OnMissileLaunched(missile);
                    }
                }
            }
            catch (Exception ex)
            {
                if (RadioWarsPlugin.Log != null)
                {
                    RadioWarsPlugin.Log.LogError(string.Format("Error in RegisterMissile_Postfix: {0}", ex));
                }
            }
        }

        [HarmonyPatch("DeregisterMissile")]
        [HarmonyPostfix]
        public static void DeregisterMissile_Postfix(Unit __instance, Missile missile)
        {
            if (missile == null) return;

            int id = missile.GetInstanceID();
            ARHSeeker_GetRadarReturn_Patch.ClearCoastTimer(id);
            SARHSeeker_GetTrackingStrength_Patch.ClearCoastTimer(id);

            // Deregister from allied Datalink surveillance network
            DatalinkNetwork.DeregisterMissile(missile);

            // Deregister launch dispersion offset
            TrackUncertaintyCalculator.UnregisterMissile(missile);

            try
            {
                if (MissileTrajectoryHUDVisualizer.Instance != null)
                {
                    MissileTrajectoryHUDVisualizer.Instance.OnMissileDeregistered(missile);
                }
            }
            catch (Exception ex)
            {
                if (RadioWarsPlugin.Log != null)
                {
                    RadioWarsPlugin.Log.LogError(string.Format("Error in DeregisterMissile_Postfix: {0}", ex));
                }
            }
        }
    }

    /// <summary>
    /// Evaluates AI missile launch doctrine by filtering target lists based on tracking quality Q and seeker envelopes.
    /// Prevents AI combatants from wasting expensive missiles on unrefined or untriangulated tracks.
    /// </summary>
    [HarmonyPatch(typeof(CombatAI), "LookForMissileTargets")]
    public static class CombatAI_LookForMissileTargets_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Aircraft aircraft, Unit currentTarget, WeaponStation weaponStation, List<Unit> outTargetList, ref int __result)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive) return;
                if (RadioWarsConfig.AIEvaluateMissileLaunchDoctrine == null || !RadioWarsConfig.AIEvaluateMissileLaunchDoctrine.Value) return;
                if (aircraft == null || weaponStation == null || weaponStation.WeaponInfo == null || outTargetList == null || outTargetList.Count == 0) return;

                // Never restrict human player manual weapon selection/firing
                if (CombatHUD.i != null && CombatHUD.i.aircraft != null && CombatHUD.i.aircraft == aircraft) return;

                WeaponInfo winfo = weaponStation.WeaponInfo;
                float reqQ = MunitionClassification.GetRequiredLaunchQuality(winfo);
                FactionHQ hq = aircraft.NetworkHQ;

                for (int i = outTargetList.Count - 1; i >= 0; i--)
                {
                    Unit tgt = outTargetList[i];
                    if (tgt == null) continue;

                    TriangulationTrack track = RWRTriangulationProcessor.GetTrack(hq, tgt);
                    float q = (track != null) ? track.TrackingQuality : 0.0f;

                    if (q < reqQ)
                    {
                        outTargetList.RemoveAt(i);
                    }
                }

                __result = outTargetList.Count;
            }
            catch (Exception ex)
            {
                if (RadioWarsPlugin.Log != null)
                {
                    RadioWarsPlugin.Log.LogError("CombatAI_LookForMissileTargets_Patch error: " + ex);
                }
            }
        }
    }

    /// <summary>
    /// Enforces missile launch doctrine across all AI platforms (ground SAMs, ships, vehicles, and aircraft).
    /// Prevents firing missiles at low tracking quality Q (Hold Fire mode).
    /// </summary>
    [HarmonyPatch(typeof(WeaponStation), "Fire")]
    public static class WeaponStation_Fire_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(WeaponStation __instance, Unit owner, Unit target)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive) return true;
                if (RadioWarsConfig.AIEvaluateMissileLaunchDoctrine == null || !RadioWarsConfig.AIEvaluateMissileLaunchDoctrine.Value) return true;
                if (__instance == null || __instance.WeaponInfo == null || !__instance.WeaponInfo.missile || target == null || owner == null) return true;

                // Never block the local human player's manual fire commands
                if (CombatHUD.i != null && CombatHUD.i.aircraft != null && CombatHUD.i.aircraft == owner)
                {
                    return true;
                }

                float reqQ = MunitionClassification.GetRequiredLaunchQuality(__instance.WeaponInfo);
                FactionHQ hq = owner.NetworkHQ;
                TriangulationTrack track = RWRTriangulationProcessor.GetTrack(hq, target);
                float q = (track != null) ? track.TrackingQuality : 0.0f;

                if (q < reqQ)
                {
                    // Insufficient tracking quality Q for this munition: HOLD FIRE!
                    return false;
                }
            }
            catch (Exception ex)
            {
                if (RadioWarsPlugin.Log != null)
                {
                    RadioWarsPlugin.Log.LogError("WeaponStation_Fire_Patch error: " + ex);
                }
            }
            return true;
        }
    }

    /// <summary>
    /// Helper for invoking private SetCombatMode on AIPilotCombatModes without runtime reflection overhead.
    /// </summary>
    internal static class AICombatModeHelper
    {
        public static readonly Action<AIPilotCombatModes, int> SetCombatMode = CreateSetCombatModeInvoker();

        private static Action<AIPilotCombatModes, int> CreateSetCombatModeInvoker()
        {
            try
            {
                var mi = AccessTools.Method(typeof(AIPilotCombatModes), "SetCombatMode");
                if (mi == null) return null;
                var dm = new System.Reflection.Emit.DynamicMethod("Call_SetCombatMode", typeof(void),
                    new Type[] { typeof(AIPilotCombatModes), typeof(int) }, typeof(AIPilotCombatModes), true);
                var il = dm.GetILGenerator();
                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
                il.Emit(System.Reflection.Emit.OpCodes.Call, mi);
                il.Emit(System.Reflection.Emit.OpCodes.Ret);
                return (Action<AIPilotCombatModes, int>)dm.CreateDelegate(typeof(Action<AIPilotCombatModes, int>));
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Prevents AI aircraft armed with standoff munitions or electronic warfare support (EW-25 Medusa)
    /// from flying straight into enemy fire in a suicidal kamikaze rush.
    /// When tracking quality Q is insufficient and aircraft reaches the safe standoff buffer, switches to RetreatStandoff.
    /// </summary>
    [HarmonyPatch(typeof(AIPilotCombatModes), "FlyToTarget")]
    public static class AIPilotCombatModes_FlyToTarget_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(AIPilotCombatModes __instance, bool checkMode,
            ref float ___targetDist,
            ref bool ___targetAccurate,
            ref WeaponInfo ___currentWeaponInfo,
            ref Unit ___currentTarget,
            Aircraft ___aircraft)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive) return true;
                if (RadioWarsConfig.AIEvaluateMissileLaunchDoctrine == null || !RadioWarsConfig.AIEvaluateMissileLaunchDoctrine.Value) return true;
                if (RadioWarsConfig.AIEnforceStandoffSurvival == null || !RadioWarsConfig.AIEnforceStandoffSurvival.Value) return true;
                if (__instance == null || ___currentTarget == null) return true;

                Aircraft ac = ___aircraft;
                if (ac == null) return true;
                bool isMedusa = MunitionClassification.IsStandoffSupportAircraft(ac);

                float safeStandoff = 0f;
                if (isMedusa)
                {
                    safeStandoff = MunitionClassification.GetMedusaSafeStandoffDistance();
                }
                else if (___currentWeaponInfo != null && (___currentWeaponInfo.missile || ___currentWeaponInfo.glideBomb || ___currentWeaponInfo.bomb))
                {
                    safeStandoff = MunitionClassification.GetSafeStandoffDistance(___currentWeaponInfo);
                }

                if (safeStandoff > 5000.0f && (isMedusa || !___targetAccurate))
                {
                    if (___targetDist <= safeStandoff)
                    {
                        // Target position is not refined enough for launch (or Medusa) and aircraft is inside safe standoff buffer:
                        // Retreat to standoff / orbit instead of suicidal kamikaze rush into point-blank danger!
                        if (AICombatModeHelper.SetCombatMode != null)
                        {
                            AICombatModeHelper.SetCombatMode(__instance, 2); // 2 = RetreatStandoff
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (RadioWarsPlugin.Log != null)
                {
                    RadioWarsPlugin.Log.LogError("AIPilotCombatModes_FlyToTarget_Patch error: " + ex);
                }
            }
            return true;
        }
    }

    /// <summary>
    /// Enforces proper safe standoff separation and hysteresis during retreat flight.
    /// Fixes vanilla bug where aircraft immediately aborted standoff retreat if targetDist > minRange*2 (~4km),
    /// which caused rapid frame-by-frame chattering loop (FlyToTarget <-> RetreatStandoff) and kamikaze rushes into SAMs.
    /// </summary>
    [HarmonyPatch(typeof(AIPilotCombatModes), "RetreatToStandoff")]
    public static class AIPilotCombatModes_RetreatToStandoff_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            AIPilotCombatModes __instance,
            bool enterMode,
            bool checkMode,
            ref float ___targetDist,
            ref bool ___targetAccurate,
            ref WeaponInfo ___currentWeaponInfo,
            ref Unit ___currentTarget,
            Aircraft ___aircraft,
            ref GlobalPosition ___destination,
            ref bool ___followTerrain,
            ref bool ___aimVelocity,
            ref bool ___ignoreCollision,
            ref float ___aimEffort,
            ControlInputs ___controlInputs)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive) return true;
                if (RadioWarsConfig.AIEnforceStandoffSurvival == null || !RadioWarsConfig.AIEnforceStandoffSurvival.Value) return true;
                if (__instance == null) return true;

                if (enterMode) return true;
                if (!checkMode) return true;

                if (___currentTarget == null || ___currentWeaponInfo == null)
                {
                    if (AICombatModeHelper.SetCombatMode != null)
                    {
                        AICombatModeHelper.SetCombatMode(__instance, 3); // 3 = NoTarget
                        return false;
                    }
                    return true;
                }

                Aircraft ac = ___aircraft;
                if (ac == null) return true;
                bool isMedusa = MunitionClassification.IsStandoffSupportAircraft(ac);

                float safeStandoff = 0f;
                if (isMedusa)
                {
                    safeStandoff = MunitionClassification.GetMedusaSafeStandoffDistance();
                }
                else if (___currentWeaponInfo != null && (___currentWeaponInfo.missile || ___currentWeaponInfo.glideBomb || ___currentWeaponInfo.bomb))
                {
                    safeStandoff = MunitionClassification.GetSafeStandoffDistance(___currentWeaponInfo);
                }

                // If not a standoff weapon / Medusa, allow vanilla dogfight retreat logic
                if (safeStandoff <= 5000.0f)
                {
                    return true;
                }

                // 1. If target tracking is refined (Q >= Qreq) and not Medusa, switch to attack and launch!
                if (___targetAccurate && !isMedusa)
                {
                    if (AICombatModeHelper.SetCombatMode != null)
                    {
                        AICombatModeHelper.SetCombatMode(__instance, 0); // 0 = FlyingToTarget
                        return false;
                    }
                }

                // 2. If aircraft has successfully retreated beyond safe standoff buffer + 15% hysteresis,
                // allow turning back to search for launch opportunities.
                if (___targetDist >= safeStandoff * 1.15f)
                {
                    if (AICombatModeHelper.SetCombatMode != null)
                    {
                        AICombatModeHelper.SetCombatMode(__instance, 0); // 0 = FlyingToTarget
                        return false;
                    }
                }

                // 3. Still inside standoff buffer:
                // Actively steer away from target at full throttle, avoiding vanilla's premature 4km exit!
                if (___currentTarget != null)
                {
                    Vector3 toTarget = ac.GlobalPosition() - ___currentTarget.GlobalPosition();
                    toTarget.y = 0f;
                    Vector3 retreatDir = toTarget.normalized;
                    if (retreatDir == Vector3.zero)
                    {
                        retreatDir = -ac.transform.forward;
                        retreatDir.y = 0f;
                    }

                    ___destination = ac.GlobalPosition() + retreatDir * 10000.0f;
                    ___followTerrain = true;
                    ___aimVelocity = true;
                    ___ignoreCollision = true;
                    if (___controlInputs != null)
                    {
                        ___controlInputs.throttle = 1.0f;
                    }
                    ___aimEffort = 0.5f;
                }

                return false; // Skip vanilla's point-blank check!
            }
            catch (Exception ex)
            {
                if (RadioWarsPlugin.Log != null)
                {
                    RadioWarsPlugin.Log.LogError("AIPilotCombatModes_RetreatToStandoff_Patch error: " + ex);
                }
                return true;
            }
        }
    }

    /// <summary>
    /// Enforces safe standoff orbit distance (default >= 25 km) for electronic warfare support aircraft (EW-25 Medusa).
    /// Fixes vanilla bug where Medusa orbited at maxRange * 0.5 (10-15 km), flying directly into lethal SAM envelopes.
    /// </summary>
    [HarmonyPatch(typeof(AIPilotCombatModes), "UseJammer")]
    public static class AIPilotCombatModes_UseJammer_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            AIPilotCombatModes __instance,
            bool checkMode,
            Aircraft ___aircraft,
            Pilot ___pilot,
            ref float ___targetDist,
            ref Unit ___currentTarget,
            ref GlobalPosition ___destination,
            ref GlobalPosition ___targetKnownPosition,
            ref bool ___targetObscured,
            ref float ___aimEffort,
            ControlInputs ___controlInputs)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive) return true;
                if (!checkMode) return true;
                if (__instance == null) return true;

                Aircraft ac = ___aircraft;
                if (ac == null) return true;
                bool isMedusa = MunitionClassification.IsStandoffSupportAircraft(ac);
                if (!isMedusa) return true;

                if (___currentTarget != null && !___currentTarget.disabled)
                {
                    float medusaStandoff = MunitionClassification.GetMedusaSafeStandoffDistance();
                    // If target is closer than safe standoff (25 km): force retreat to standoff!
                    if (___targetDist < medusaStandoff)
                    {
                        if (AICombatModeHelper.SetCombatMode != null)
                        {
                            AICombatModeHelper.SetCombatMode(__instance, 2); // 2 = RetreatStandoff
                            return false;
                        }
                    }

                    // Standoff orbit at ~27 km (safeStandoff * 1.08)
                    float orbitTarget = medusaStandoff * 1.08f;
                    Vector3 toTarget = (___targetKnownPosition.ToLocalPosition() - ac.transform.position);
                    toTarget.y = 0f;

                    Vector3 tangent = Vector3.Cross(toTarget, Vector3.up);
                    if (Vector3.Dot(tangent, ac.transform.forward) < 0f)
                    {
                        tangent = -tangent;
                    }

                    float rangeDiff = (___targetDist - orbitTarget) / orbitTarget;
                    float bias = Mathf.Clamp(rangeDiff * 2.0f, -0.6f, 0.6f);
                    Vector3 orbitDir = Vector3.RotateTowards(tangent, toTarget, bias, 1.0f);

                    ___destination = ac.GlobalPosition() + orbitDir.normalized * 8000.0f;
                    ___aimEffort = 0.5f;
                    if (___controlInputs != null)
                    {
                        ___controlInputs.throttle = 0.85f;
                    }

                    if (___pilot != null && !___targetObscured)
                    {
                        ___pilot.Fire();
                    }

                    return false; // Handled standoff jamming orbit!
                }
            }
            catch (Exception ex)
            {
                if (RadioWarsPlugin.Log != null)
                {
                    RadioWarsPlugin.Log.LogError("AIPilotCombatModes_UseJammer_Patch error: " + ex);
                }
            }
            return true;
        }
    }

    /// <summary>
    /// Active server-side watchdog that terminates munitions that have missed their target,
    /// overshot ballistic apogee without hitting, or exceeded maximum battlefield tactical boundaries.
    /// </summary>
    [HarmonyPatch(typeof(Missile), "ServerFixedUpdate")]
    public static class Missile_ServerFixedUpdate_Watchdog_Patch
    {
        private static readonly Func<ARMSeeker, Radar> f_getArmRadar = FastReflection.CreateFieldGetter<ARMSeeker, Radar>("targetedRadar");
        private static readonly Func<ARMSeeker, GlobalPosition> f_getArmPos = FastReflection.CreateFieldGetter<ARMSeeker, GlobalPosition>("knownPos");
        private static readonly Func<BallisticMissileGuidance, GlobalPosition> f_getBallisticPos = FastReflection.CreateFieldGetter<BallisticMissileGuidance, GlobalPosition>("knownPos");

        [HarmonyPostfix]
        public static void Postfix(Missile __instance)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive || __instance == null || __instance.disabled) return;
                if (RadioWarsConfig.MissileEnforceSelfDestructWatchdog == null || !RadioWarsConfig.MissileEnforceSelfDestructWatchdog.Value) return;

                // Strict safety gate: NEVER detonate young or un-armed munitions!
                // Protects launching aircraft, pylon release, canister launch, and low-altitude cruise missiles.
                if (__instance.timeSinceSpawn < 10.0f || !__instance.IsArmed()) return;

                // Throttle checks: run every 10 frames (~0.2s) to maintain zero performance overhead
                if ((Time.frameCount + __instance.GetInstanceID()) % 10 != 0) return;

                GlobalPosition gPos = __instance.GlobalPosition();
                Vector3 vel = __instance.rb != null ? __instance.rb.velocity : Vector3.zero;

                // 1. Universal Battlefield Boundary, Deep Water & Edge-of-Atmosphere Check
                // Uses true world coordinates (GlobalPosition), completely immune to floating origin offsets!
                float maxRadiusKm = (RadioWarsConfig.MissileMaxBattlefieldRadiusKm != null) ? RadioWarsConfig.MissileMaxBattlefieldRadiusKm.Value : 120.0f;
                float maxRadiusM = maxRadiusKm * 1000.0f;
                float distSq = gPos.x * gPos.x + gPos.z * gPos.z;
                if (distSq > maxRadiusM * maxRadiusM || gPos.y < -150.0f || gPos.y > 60000.0f)
                {
                    __instance.Detonate(vel, false, false);
                    return;
                }

                // 2. Ballistic Missile Guidance Miss Watchdog (Piledriver TBM)
                BallisticMissileGuidance ballistic = __instance.GetComponent<BallisticMissileGuidance>();
                if (ballistic != null)
                {
                    // Terminal dive phase after motor burnout:
                    if (__instance.timeSinceSpawn > 35.0f && !__instance.EngineOn() && vel.y < -25.0f)
                    {
                        if (f_getBallisticPos != null)
                        {
                            GlobalPosition targetG = f_getBallisticPos(ballistic);
                            if (targetG != default(GlobalPosition))
                            {
                                // Only detonate if missile has overshot and plunged below the target datum altitude:
                                if (gPos.y < targetG.y - 50.0f)
                                {
                                    __instance.Detonate(vel, false, false);
                                    return;
                                }
                            }
                        }
                    }
                }
                // 3. ARM Anti-Radiation Seeker Miss Watchdog (ARAD-116, ARAD-45)
                else
                {
                    ARMSeeker arm = __instance.GetComponent<ARMSeeker>();
                    if (arm != null)
                    {
                        if (__instance.timeSinceSpawn > 25.0f && !__instance.EngineOn())
                        {
                            Radar targeted = (f_getArmRadar != null) ? f_getArmRadar(arm) : null;
                            if (targeted == null && f_getArmPos != null)
                            {
                                GlobalPosition targetG = f_getArmPos(arm);
                                if (targetG != default(GlobalPosition))
                                {
                                    Vector3 toTarget = targetG - gPos;
                                    // Only detonate if missile overshot the last known radar coordinate by more than 3 km:
                                    if (Vector3.Dot(toTarget, vel) < 0f && toTarget.sqrMagnitude > 9000000.0f)
                                    {
                                        __instance.Detonate(vel, false, false);
                                        return;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] Missile_ServerFixedUpdate_Watchdog_Patch error: " + ex);
            }
        }
    }
}
