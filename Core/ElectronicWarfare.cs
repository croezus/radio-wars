using System;
using UnityEngine;

namespace RadioWars.Core
{
    public enum RwrThreatState
    {
        None,
        Search,          // Wide-area low PRF search sweep
        Track,           // Dedicated Single-Target Track (STT) / Medium PRF
        MissileGuidance  // Continuous Wave (CW) or terminal active seeker illumination
    }

    public struct JammerSpecs
    {
        public float PowerWatts;          // Pjam (W) (e.g., 200W - 1000W)
        public float AntennaGainLinear;   // Gjam (~10 dBi ≈ 10.0)
        public float BandwidthHz;         // Bjam (e.g., 100 MHz barrage or 20 MHz spot)
        public float SystemLossLinear;    // Ljam (~2.0)

        public static JammerSpecs CreateStandardPod()
        {
            JammerSpecs s;
            s.PowerWatts = 450.0f;
            s.AntennaGainLinear = 8.0f;
            s.BandwidthHz = 40.0e6f; // 40 MHz spot noise
            s.SystemLossLinear = 1.8f;
            return s;
        }
    }

    public static class ElectronicWarfare
    {
        public const float FourPiSquared = 157.91367f; // (4 * pi)^2

        /// <summary>
        /// Calculates received noise jamming power Pj (Watts) at the radar receiver using 1/R² one-way path loss:
        /// Pj = (Pjam * Gjam * Gr * λ² * Br) / ((4π)² * R² * Bjam * Ljam)
        /// </summary>
        public static float CalculateJammerPowerAtReceiver(
            float rangeMeters,
            ref JammerSpecs jammer,
            ref RadarSpecs radar)
        {
            if (rangeMeters < 50.0f) rangeMeters = 50.0f;
            if (jammer.PowerWatts <= 0.0f) return 0.0f;

            float r2 = rangeMeters * rangeMeters;

            // Bandwidth ratio factor (portion of jamming noise that enters the radar receiver IF filter)
            float bwRatio = Mathf.Clamp01(radar.BandwidthHz / Mathf.Max(radar.BandwidthHz, jammer.BandwidthHz));

            float numerator = jammer.PowerWatts * jammer.AntennaGainLinear * radar.AntennaGainLinear * 
                              (radar.WavelengthMeters * radar.WavelengthMeters) * bwRatio;

            float denominator = FourPiSquared * r2 * jammer.SystemLossLinear;
            if (denominator <= 0.0f) return 0.0f;

            return numerator / denominator;
        }

        /// <summary>
        /// Calculates the physical burn-through range R_burn (meters) where target echo Pr overcomes jammer noise Pj:
        /// R_burn = sqrt((Pt * Gt * σ * Bjam) / (4π * Pjam * Gjam * Br * SNR_min))
        /// </summary>
        public static float CalculateBurnThroughRange(
            float targetRcsSqm,
            float snrThresholdLinear,
            ref JammerSpecs jammer,
            ref RadarSpecs radar)
        {
            if (jammer.PowerWatts <= 0.0f) return 100000.0f;
            if (targetRcsSqm <= 0.001f) targetRcsSqm = 0.001f;
            if (snrThresholdLinear <= 0.1f) snrThresholdLinear = 10.0f;

            float bwFactor = Mathf.Max(1.0f, jammer.BandwidthHz / Mathf.Max(1000.0f, radar.BandwidthHz));
            float procGain = Mathf.Max(1.0f, radar.ProcessingGainLinear);

            float numerator = radar.PeakPowerWatts * radar.AntennaGainLinear * procGain * targetRcsSqm * bwFactor;
            float denominator = (4.0f * Mathf.PI) * jammer.PowerWatts * jammer.AntennaGainLinear * snrThresholdLinear;

            if (denominator <= 0.0f) return 100000.0f;

            float rBurnSquared = numerator / denominator;
            if (rBurnSquared <= 0.0f) return 100.0f;

            return Mathf.Sqrt(rBurnSquared);
        }

        /// <summary>
        /// Evaluates Home-On-Jam (HOJ) seeker guidance.
        /// HOJ is strictly disabled if the target is in the notch gate!
        /// When a target radiates ECM while flying straight, the seeker tracks the jamming strobe angle.
        /// </summary>
        public static bool EvaluateHOJGuidance(
            float ecmIntensity,
            bool isInsideClutterNotch,
            float slantRange)
        {
            if (ecmIntensity < 0.15f) return false;

            // If the target is inside the notch, HOJ is strictly blocked!
            if (isInsideClutterNotch)
            {
                return false;
            }

            // At point-blank range (< 150m), radar echo dominates
            if (slantRange < 150.0f) return false;

            return true;
        }

        /// <summary>
        /// Calculates the track memory / coasting decay multiplier.
        /// When target is inside notch AND active ECM is applied, track memory collapses immediately.
        /// </summary>
        public static float CalculateTrackMemoryRetention(
            float ecmIntensity,
            bool isInsideNotch,
            bool isLookingDown)
        {
            if (isInsideNotch && isLookingDown)
            {
                if (ecmIntensity > 0.1f) return 0.05f; // Immediate memory drop
                return 0.35f; // Slower decay without ECM
            }
            return 1.0f;
        }
    }
}
