using System;
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
}
