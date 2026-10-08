using System;
using System.Collections.Generic;
using UnityEngine;
using RadioWars.Config;
using RadioWars.Components;

namespace RadioWars.Core
{
    /// <summary>
    /// Core intelligence accumulation and weapon dispersion engine.
    /// Manages team-wide sensor fusion progress (Q: 0.0 to 1.0) across allied radars,
    /// visual spotters, and RWR receivers with range-dependent weighting and 120-second retention.
    /// Calculates horizontal-only (X/Z, Dy=0) Circular Error Probable (CEP) displacement
    /// for launched munitions (ARAD, ARH, SARH, and Optical).
    /// </summary>
    public static class TrackUncertaintyCalculator
    {
        private static readonly Dictionary<int, Vector3> _missileDispersionOffsets = new Dictionary<int, Vector3>();

        // Fallback bounds
        private const float DefaultMinDist = 5000.0f;  // 5 km: significant RWR/sensor contribution starts here
        private const float DefaultMaxDist = 80000.0f; // 80 km
        private const float DefaultMinDispersion = 15.0f;
        private const float DefaultMaxDispersion = 0.0f; // 0 = Uncapped
        private const float DefaultMemoryTimeout = 120.0f;

        #region Range Weighting & Distance Decay

        /// <summary>
        /// Range coefficient weighting factor: closer sensor observations provide significantly higher
        /// angular accuracy and signal-to-noise ratio than distant detections.
        /// Below MinDist (5 km), sensor contributions receive maximum weighting (1.0x).
        /// Drops non-linearly with distance to prevent unrealistic accumulation from long-range RWR strobes.
        /// </summary>
        public static float CalculateRangeWeight(float distanceMeters)
        {
            float minD = (RadioWarsConfig.TrackUncertaintyRangeWeightingMin != null)
                ? RadioWarsConfig.TrackUncertaintyRangeWeightingMin.Value
                : DefaultMinDist;
            float maxD = (RadioWarsConfig.TrackUncertaintyRangeWeightingMax != null)
                ? RadioWarsConfig.TrackUncertaintyRangeWeightingMax.Value
                : DefaultMaxDist;

            if (distanceMeters <= minD) return 1.0f;
            if (maxD <= minD) maxD = minD + 1000.0f;

            float t = Mathf.Clamp01((distanceMeters - minD) / (maxD - minD));
            // Power curve: drops swiftly from 5km to 20km, decaying to 0.03x at 80km
            float curve = Mathf.Pow(t, 0.60f);
            return Mathf.Lerp(1.0f, 0.03f, curve);
        }

        #endregion

        #region Team-Wide Intelligence Accumulation Ticks

        /// <summary>
        /// Registers an active radar sweep pulse on the target from an ownship or allied radar.
        /// Progress ticks at most once per configurable tick interval (default: 1.0s) per target.
        /// </summary>
        public static void RecordRadarSweep(TriangulationTrack track, float distanceMeters, bool isOwnship)
        {
            if (track == null) return;
            float now = Time.timeSinceLevelLoad;
            track.LastActiveDetectionTime = now;
            track.LastRadarIlluminatedTime = now;
            track.HadActiveContact = true;
            track.IsTriangulated = true;

            // Enforce discrete tick rate
            float tickInterval = (RadioWarsConfig.SensorIntelligenceTickIntervalSeconds != null)
                ? Mathf.Max(RadioWarsConfig.SensorIntelligenceTickIntervalSeconds.Value, 0.05f)
                : 1.0f;
            if (now - track.LastRadarTickTime < tickInterval) return;
            track.LastRadarTickTime = now;

            float rangeWeight = CalculateRangeWeight(distanceMeters);
            float configBase = (RadioWarsConfig.RadarSweepBaseContribution != null)
                ? Mathf.Clamp01(RadioWarsConfig.RadarSweepBaseContribution.Value)
                : 0.20f;
            float baseTick = isOwnship ? configBase : (configBase * 0.75f);
            float deltaQ = baseTick * rangeWeight;

            track.TrackingQuality = Mathf.Clamp01(track.TrackingQuality + deltaQ);
            track.ContinuousDwellSeconds = Mathf.Min(track.ContinuousDwellSeconds + (isOwnship ? 2.5f : 1.5f), 15.0f);
        }

        /// <summary>
        /// Continuously refines tracking quality during sustained radar dwell (pencil-beam track).
        /// </summary>
        public static void RecordRadarDwell(TriangulationTrack track, float dt, float distanceMeters)
        {
            if (track == null) return;
            float now = Time.timeSinceLevelLoad;
            float rangeWeight = CalculateRangeWeight(distanceMeters);
            float configRate = (RadioWarsConfig.RadarDwellContributionRate != null)
                ? Mathf.Max(RadioWarsConfig.RadarDwellContributionRate.Value, 0.0f)
                : 0.30f;
            float ratePerSecond = configRate * rangeWeight;

            track.ContinuousDwellSeconds += dt;
            track.TrackingQuality = Mathf.Clamp01(track.TrackingQuality + ratePerSecond * dt);
            track.LastActiveDetectionTime = now;
            track.LastRadarIlluminatedTime = now;
            track.HadActiveContact = true;
            track.IsTriangulated = true;
        }

        /// <summary>
        /// Registers positive visual reconnaissance from an allied spotter (aircraft, vehicle, ship).
        /// Uncertainty reduction occurs ONCE per target with a 3-minute (180s) cooldown.
        /// While within visual range, the target stays actively detected, preventing memory timeouts,
        /// but its intelligence scale does not climb further without radar or RWR sensor input.
        /// </summary>
        public static void RecordVisualContact(TriangulationTrack track)
        {
            if (track == null) return;
            float now = Time.timeSinceLevelLoad;
            track.LastActiveDetectionTime = now;
            track.HadActiveContact = true;
            track.IsTriangulated = true;

            // Check visual boost cooldown (default: 180 seconds / 3 minutes)
            float cooldown = (RadioWarsConfig.VisualReconnaissanceCooldownSeconds != null)
                ? Mathf.Max(RadioWarsConfig.VisualReconnaissanceCooldownSeconds.Value, 0.0f)
                : 180.0f;

            if (track.LastVisualBoostTime <= 0f || (now - track.LastVisualBoostTime) >= cooldown)
            {
                track.LastVisualBoostTime = now;
                float boost = (RadioWarsConfig.VisualReconnaissanceBoost != null)
                    ? Mathf.Clamp01(RadioWarsConfig.VisualReconnaissanceBoost.Value)
                    : 0.35f;

                track.TrackingQuality = Mathf.Clamp01(track.TrackingQuality + boost);
                track.ContinuousDwellSeconds = Mathf.Max(track.ContinuousDwellSeconds, 10.0f);
            }
        }

        /// <summary>
        /// Registers a passive RWR RF strobe detection from enemy radar emissions.
        /// Weighted by range coefficient (significant below 5 km) and receiver hardware tier.
        /// Progress ticks at most once per configurable tick interval (default: 1.0s) per target.
        /// </summary>
        public static void RecordRWRDetection(TriangulationTrack track, float distanceMeters, RwrTier tier, bool isOwnship)
        {
            if (track == null) return;
            float now = Time.timeSinceLevelLoad;
            track.LastActiveDetectionTime = now;
            track.HadActiveContact = true;

            // Enforce discrete tick rate
            float tickInterval = (RadioWarsConfig.SensorIntelligenceTickIntervalSeconds != null)
                ? Mathf.Max(RadioWarsConfig.SensorIntelligenceTickIntervalSeconds.Value, 0.05f)
                : 1.0f;
            if (now - track.LastRwrTickTime < tickInterval) return;
            track.LastRwrTickTime = now;

            float rangeWeight = CalculateRangeWeight(distanceMeters);

            float tierMult = 1.0f;
            if (tier == RwrTier.Tier1_Basic) tierMult = 0.75f;
            else if (tier == RwrTier.Tier3_Advanced) tierMult = 1.30f;

            float configBase = (RadioWarsConfig.RWRBaseContribution != null)
                ? Mathf.Clamp01(RadioWarsConfig.RWRBaseContribution.Value)
                : 0.035f;
            float baseTick = isOwnship ? configBase : (configBase * 0.57f);
            float deltaQ = baseTick * rangeWeight * tierMult;

            track.TrackingQuality = Mathf.Clamp01(track.TrackingQuality + deltaQ);
        }

        /// <summary>
        /// Grants an intelligence boost upon successful geometric triangulation
        /// (either kinematic cross-bearing flight or multi-station datalink intersection).
        /// </summary>
        public static void RecordTriangulationBoost(TriangulationTrack track, bool isMultiStation)
        {
            if (track == null) return;
            float now = Time.timeSinceLevelLoad;
            float baseBoost = (RadioWarsConfig.TriangulationBaselineBoost != null)
                ? Mathf.Clamp01(RadioWarsConfig.TriangulationBaselineBoost.Value)
                : 0.25f;
            float boost = isMultiStation ? (baseBoost + 0.05f) : baseBoost;

            track.TrackingQuality = Mathf.Clamp01(track.TrackingQuality + boost);
            track.LastActiveDetectionTime = now;
            track.HadActiveContact = true;
            track.IsTriangulated = true;
        }

        /// <summary>
        /// Enforces the 120-second inactivity memory rule.
        /// If no sensor on the team has detected the target for 120 seconds,
        /// accumulated tracking quality and dwell reset completely to zero.
        /// </summary>
        public static void CheckInactivityExpiration(TriangulationTrack track)
        {
            if (track == null) return;
            float timeout = (RadioWarsConfig.TrackUncertaintyMemoryTimeoutSeconds != null)
                ? RadioWarsConfig.TrackUncertaintyMemoryTimeoutSeconds.Value
                : DefaultMemoryTimeout;

            float elapsed = Time.timeSinceLevelLoad - track.LastActiveDetectionTime;
            if (track.HadActiveContact && elapsed > timeout)
            {
                // Inactivity threshold exceeded: purge accumulated intelligence
                track.TrackingQuality = 0.0f;
                track.ContinuousDwellSeconds = 0.0f;
                track.IsTriangulated = false;
            }
        }

        #endregion

        #region Dispersion & CEP Calculation

        /// <summary>
        /// Computes the horizontal Circular Error Probable (CEP) radius in meters
        /// from target distance, tracking quality (Q), triangulation status, and memory state.
        /// </summary>
        public static float CalculateDispersionRadius(float distanceMeters, float trackingQuality, bool isInMemoryState, float memoryElapsedSeconds, bool isTriangulated)
        {
            float q = Mathf.Clamp01(trackingQuality);

            // Baseline angular spread in radians (from 12.0 degrees down to 0.05 degrees)
            float thetaMaxRad = 12.0f * Mathf.Deg2Rad;
            float thetaMinRad = 0.05f * Mathf.Deg2Rad;
            float thetaEff = Mathf.Lerp(thetaMaxRad, thetaMinRad, q);

            float rawAngularError = distanceMeters * Mathf.Tan(thetaEff);

            // Range uncertainty for passive un-triangulated contacts (single RWR strobe)
            float rangeErr = isTriangulated ? 0.0f : (distanceMeters * 0.25f * (1.0f - q));
            float totalBaseError = Mathf.Sqrt(rawAngularError * rawAngularError + rangeErr * rangeErr);

            // Non-linear collapse damping as Q approaches fire-control precision
            float qDamping = 1.0f - (0.92f * q * q);
            float baseCep = totalBaseError * qDamping;

            // Stale memory expansion: target kinematic dead-reckoning drift
            if (isInMemoryState && memoryElapsedSeconds > 0f)
            {
                baseCep += 120.0f * memoryElapsedSeconds * 0.50f; // drift expansion
            }

            float minDisp = (RadioWarsConfig.TrackUncertaintyMinDispersionMeters != null)
                ? RadioWarsConfig.TrackUncertaintyMinDispersionMeters.Value
                : DefaultMinDispersion;

            // Uncapped physical ceiling (optional manual override if TrackUncertaintyMaxDispersionMeters > 0)
            float maxDisp = (RadioWarsConfig.TrackUncertaintyMaxDispersionMeters != null && RadioWarsConfig.TrackUncertaintyMaxDispersionMeters.Value > 0f)
                ? RadioWarsConfig.TrackUncertaintyMaxDispersionMeters.Value
                : (distanceMeters * 0.90f);

            return Mathf.Clamp(baseCep, minDisp, Mathf.Max(minDisp, maxDisp));
        }

        public static float CalculateDispersionRadius(float distanceMeters, float trackingQuality, bool isInMemoryState, float memoryElapsedSeconds)
        {
            return CalculateDispersionRadius(distanceMeters, trackingQuality, isInMemoryState, memoryElapsedSeconds, false);
        }

        /// <summary>
        /// Generates a random horizontal displacement vector (Dx, Dz, Dy=0) within the CEP circle.
        /// Altitude (Y) is strictly zero to preserve target height.
        /// </summary>
        public static Vector3 GenerateHorizontalDispersion(float distanceMeters, float trackingQuality, bool isInMemoryState, float memoryElapsedSeconds, bool isTriangulated)
        {
            if (!RadioWarsConfig.IsModActive) return Vector3.zero;
            if (RadioWarsConfig.EnableTrackUncertaintyDispersion != null && !RadioWarsConfig.EnableTrackUncertaintyDispersion.Value)
            {
                return Vector3.zero;
            }

            float cepRadius = CalculateDispersionRadius(distanceMeters, trackingQuality, isInMemoryState, memoryElapsedSeconds, isTriangulated);

            // Peripheral / edge-biased radial distribution:
            // Munitions disperse preferentially towards the outer perimeter of the uncertainty disk (60% to 100%),
            // preventing un-triangulated launches from scoring lucky center hits.
            float minFrac = (RadioWarsConfig.TrackUncertaintyEdgeBiasMinFraction != null)
                ? Mathf.Clamp01(RadioWarsConfig.TrackUncertaintyEdgeBiasMinFraction.Value)
                : 0.60f;
            float exp = (RadioWarsConfig.TrackUncertaintyEdgeBiasExponent != null)
                ? Mathf.Max(0.01f, RadioWarsConfig.TrackUncertaintyEdgeBiasExponent.Value)
                : 0.40f;

            float phi = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            float u = UnityEngine.Random.value;
            float rFrac = Mathf.Lerp(minFrac, 1.0f, Mathf.Pow(u, exp));
            float r = cepRadius * rFrac;

            float dx = r * Mathf.Cos(phi);
            float dz = r * Mathf.Sin(phi);

            return new Vector3(dx, 0f, dz);
        }

        public static Vector3 GenerateHorizontalDispersion(float distanceMeters, float trackingQuality, bool isInMemoryState, float memoryElapsedSeconds)
        {
            return GenerateHorizontalDispersion(distanceMeters, trackingQuality, isInMemoryState, memoryElapsedSeconds, false);
        }

        #endregion

        #region Missile Launch Dispersion Registry

        /// <summary>
        /// Records an initial launch dispersion vector for a specific munition instance.
        /// </summary>
        public static void RegisterMissileDispersion(Missile missile, Vector3 horizontalOffset)
        {
            if (missile == null) return;
            _missileDispersionOffsets[missile.GetInstanceID()] = horizontalOffset;
        }

        /// <summary>
        /// Retrieves the registered launch dispersion vector for a munition.
        /// </summary>
        public static bool TryGetMissileDispersion(Missile missile, out Vector3 offset)
        {
            if (missile == null)
            {
                offset = Vector3.zero;
                return false;
            }
            return _missileDispersionOffsets.TryGetValue(missile.GetInstanceID(), out offset);
        }

        /// <summary>
        /// Unregisters a munition upon detonation, splash, or destruction.
        /// </summary>
        public static void UnregisterMissile(Missile missile)
        {
            if (missile == null) return;
            _missileDispersionOffsets.Remove(missile.GetInstanceID());
        }

        /// <summary>
        /// Clears all stored missile dispersion offsets (e.g. level reload).
        /// </summary>
        public static void Clear()
        {
            _missileDispersionOffsets.Clear();
        }

        #endregion
    }
}
