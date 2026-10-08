using System;
using UnityEngine;
using RadioWars.Config;

namespace RadioWars.Core
{
    public struct DopplerResult
    {
        public float RelativeRadialVelocity;  // Closing speed (+ toward radar, - away) (m/s)
        public float TargetGroundRadialSpeed; // Target speed projected onto LOS (m/s)
        public float ElevationAngleDeg;       // Angle relative to horizontal plane (+ up, - down)
        public bool IsLookingDown;            // True if radar is looking down against terrain background
        public bool HasClutterBackground;     // True if ground/terrain clutter backdrop exists behind/around target
        public bool IsInsideClutterNotch;     // True if ground-relative speed < effective notch threshold
        public bool TrackRejectedByNotch;     // True if notch filter drops the return
        public float ClutterNoisePower;       // Equivalent clutter noise power Pc (Watts)
        public float EffectiveNotchThreshold; // Threshold after applying ECM Doppler smearing (m/s)
        public bool IsECMNotchAssisted;       // True if ECM helped expand the notch gate
        public bool IsHOJVulnerable;          // True if ECM is active outside look-down clutter notch
    }

    public static class DopplerClutterProcessor
    {
        /// <summary>
        /// Evaluates radial velocity, ground clutter notch gate, and look-down conditions,
        /// taking into account ECM Doppler smearing (VGPO) and HOJ vulnerability.
        /// </summary>
        public static DopplerResult ProcessDopplerAndClutter(
            Vector3 radarPosition,
            Vector3 radarVelocity,
            Vector3 targetPosition,
            Vector3 targetVelocity,
            float notchThreshold_mps,
            bool isHelicopterWithHERM,
            float radarAltitudeAboveTerrain,
            float targetEcmIntensity = 0.0f,
            float targetAltitudeAboveTerrain = 500.0f)
        {
            DopplerResult res;
            res.ClutterNoisePower = 0.0f;
            res.IsECMNotchAssisted = false;

            Vector3 losVec = targetPosition - radarPosition;
            float dist = losVec.magnitude;
            if (dist < 1.0f) dist = 1.0f;
            Vector3 losUnit = losVec / dist;

            // Relative closing velocity: positive if closing, negative if opening
            Vector3 relVel = targetVelocity - radarVelocity;
            res.RelativeRadialVelocity = -Vector3.Dot(relVel, losUnit);

            // Ground-relative radial velocity of target:
            // When beaming (flying perpendicular to radar beam), Dot(targetVelocity, losUnit) ≈ 0
            res.TargetGroundRadialSpeed = Mathf.Abs(Vector3.Dot(targetVelocity, losUnit));

            // Elevation angle of beam
            float horizontalDist = Mathf.Sqrt(losVec.x * losVec.x + losVec.z * losVec.z);
            res.ElevationAngleDeg = Mathf.Atan2(losVec.y, Mathf.Max(1.0f, horizontalDist)) * Mathf.Rad2Deg;

            // Strict geometric look-down:
            // Radar line-of-sight points below the local horizon (negative elevation) and radar is physically above target
            bool isGeometricLookDown = (res.ElevationAngleDeg < -0.2f) && (radarPosition.y > targetPosition.y);
            res.IsLookingDown = isGeometricLookDown;

            // Clutter background condition:
            // Ground clutter behind target in the antenna main beam is present if:
            // 1) Geometric look-down against terrain surface
            // 2) Nap-Of-the-Earth (NOE) flight (< 120m AGL)
            // 3) Low elevation near horizon (< 1.5° elevation and < 300m AGL)
            bool hasClutterBackground = isGeometricLookDown ||
                                        (targetAltitudeAboveTerrain < 120.0f) ||
                                        (targetAltitudeAboveTerrain < 300.0f && res.ElevationAngleDeg < 1.5f);
            res.HasClutterBackground = hasClutterBackground;

            // Calculate effective notch threshold factoring in ECM Doppler noise smearing (VGPO)
            float expansionMultiplier = (RadioWarsConfig.ECMNotchExpansionMultiplier != null)
                ? RadioWarsConfig.ECMNotchExpansionMultiplier.Value
                : 1.5f;
            float effectiveNotch = notchThreshold_mps * (1.0f + Mathf.Clamp01(targetEcmIntensity) * expansionMultiplier);
            res.EffectiveNotchThreshold = effectiveNotch;

            // Beaming / Notch check:
            res.IsInsideClutterNotch = res.TargetGroundRadialSpeed < effectiveNotch;
            res.IsECMNotchAssisted = res.IsInsideClutterNotch && (res.TargetGroundRadialSpeed >= notchThreshold_mps);

            // Rejection logic:
            if (res.IsInsideClutterNotch)
            {
                if (isHelicopterWithHERM)
                {
                    // Spinning rotor blades generate wideband micro-Doppler sidebands (200+ m/s)
                    // Helicopter remains visible despite zero hull translational velocity
                    res.TrackRejectedByNotch = false;
                }
                else if (targetEcmIntensity > 0.05f)
                {
                    // Prime Synergy: Active ECM + Beaming completely destroys Doppler tracker!
                    // DRFM VGPO smearing causes catastrophic velocity gate rejection on any altitude
                    res.TrackRejectedByNotch = true;
                }
                else if (hasClutterBackground)
                {
                    // Clean notch with ground clutter: target return is buried inside main-lobe or sidelobe ground clutter
                    res.TrackRejectedByNotch = true;
                }
                else
                {
                    // Clear / open sky (чисте небо) without ground clutter: filter is bypassed, notch is 90% ignored
                    res.TrackRejectedByNotch = false;
                }
            }
            else
            {
                res.TrackRejectedByNotch = false;
            }

            // HOJ vulnerability: HOJ can ONLY guide if target is flying straight outside the notch gate!
            // When beaming inside the notch, the Doppler channel and angular discrimination are collapsed
            if (targetEcmIntensity > 0.15f)
            {
                res.IsHOJVulnerable = !res.IsInsideClutterNotch;
            }
            else
            {
                res.IsHOJVulnerable = false;
            }

            // Clutter noise power calculation Pc
            // Scales with negative grazing angle and target proximity to surface
            if (res.IsLookingDown)
            {
                float sinGrazing = Mathf.Abs(Mathf.Sin(res.ElevationAngleDeg * Mathf.Deg2Rad));
                float terrainRoughness = 0.03f; // Average land terrain gamma (m²/m²)
                float groundAreaFactor = Mathf.Clamp(1000.0f / Mathf.Max(100.0f, dist), 0.1f, 10.0f);
                res.ClutterNoisePower = terrainRoughness * sinGrazing * groundAreaFactor * 1.5e-14f;
            }

            return res;
        }

        /// <summary>
        /// Fast inline check for whether a target is beaming relative to an emitter.
        /// </summary>
        public static bool IsTargetBeaming(Vector3 radarPos, Vector3 targetPos, Vector3 targetVel, float notchThreshold_mps)
        {
            if (notchThreshold_mps <= 0.0f) return false;
            Vector3 dir = targetPos - radarPos;
            float sqrMag = dir.sqrMagnitude;
            if (sqrMag < 1.0f) return false;

            float dot = targetVel.x * dir.x + targetVel.y * dir.y + targetVel.z * dir.z;
            return (dot * dot) < (notchThreshold_mps * notchThreshold_mps * sqrMag);
        }
    }
}
