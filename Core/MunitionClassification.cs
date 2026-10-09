using System;
using System.Collections.Generic;
using UnityEngine;
using RadioWars.Config;

namespace RadioWars.Core
{
    /// <summary>
    /// Tactical range classification categories for guided and unguided munitions in Nuclear Option.
    /// Categorized by actual factory maximum launch ranges (TargetRequirements.maxRange) and strategic flags.
    /// </summary>
    public enum MunitionRangeCategory
    {
        /// <summary>
        /// Short-range tactical WVR and close support (< 20 km):
        /// MMR-S3 (15km), RAM-45 (15km), AGM-68 (15km), IRM-S2 (10km), AGM-48 (10km),
        /// PAB-80LR/250 (10km), GBM-500LR (10km), ATP-1 (8km), AGR-18/24 (6-8km), IRM-S1 (5km), AT-145 (4km).
        /// </summary>
        ShortRange,

        /// <summary>
        /// Medium-range tactical BVR and strike (20 km - 50 km):
        /// AAM-29 Scythe (35km), ARAD-45 (25km), MLRS (40km), MRM-S4 Broadsword (25km).
        /// </summary>
        MediumRange,

        /// <summary>
        /// Long-range interceptors, strike and heavy SAMs (50 km - 100 km):
        /// ARAD-116 (60km), Tusko-B / Tusko-N (60km), NL-98 (50km), StratoLance R9 (50km), AAM-45 Sabre (80km).
        /// </summary>
        LongRange,

        /// <summary>
        /// Strategic, standoff and super long-range munitions (>= 100 km or Cruise/Nuclear):
        /// AAM-36 Scimitar (100km), AGM-99 (100km), ALM-C450 (200km), ALND-4 (200km),
        /// AShM-300 (250km), HSM-750 Starfall, HSM-N Sunfall, R-100N Zenith.
        /// </summary>
        StrategicBallistic,

        /// <summary>
        /// Precision long-range ballistic munitions without terminal seekers (>= 100 km):
        /// Piledriver TBM (250km). Fired across massive standoff distances with no active seeker head;
        /// relies critically on launch tracking quality (Q >= 0.90) to prevent CEP dispersion misses.
        /// </summary>
        Ballistic
    }

    /// <summary>
    /// High-performance, zero-allocation munition classification module.
    /// Analyzes WeaponInfo specifications, aerodynamic properties, and guidance envelopes
    /// to determine required tracking quality thresholds and safe standoff distances for AI combatants.
    /// </summary>
    public static class MunitionClassification
    {
        private static readonly Dictionary<string, MunitionRangeCategory> s_categoryCache =
            new Dictionary<string, MunitionRangeCategory>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, float> s_standoffCache =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        public static void ClearCache()
        {
            s_categoryCache.Clear();
            s_standoffCache.Clear();
        }

        /// <summary>
        /// Determines the tactical range category for a given weapon.
        /// Zero heap allocation after initial evaluation per weapon key.
        /// </summary>
        public static MunitionRangeCategory GetCategory(WeaponInfo weaponInfo)
        {
            if (weaponInfo == null) return MunitionRangeCategory.ShortRange;

            string key = weaponInfo.shortName;
            if (string.IsNullOrEmpty(key)) key = weaponInfo.weaponName;
            if (string.IsNullOrEmpty(key)) return MunitionRangeCategory.ShortRange;

            MunitionRangeCategory cat;
            if (s_categoryCache.TryGetValue(key, out cat))
            {
                return cat;
            }

            cat = EvaluateCategory(weaponInfo, key);
            s_categoryCache[key] = cat;
            return cat;
        }

        /// <summary>
        /// Checks whether a weapon is a precision ballistic munition without a terminal seeker (e.g. Piledriver TBM).
        /// </summary>
        public static bool IsBallisticMunition(WeaponInfo weaponInfo, string key)
        {
            if (weaponInfo != null && weaponInfo.weaponPrefab != null)
            {
                if (weaponInfo.weaponPrefab.GetComponent<BallisticMissileGuidance>() != null)
                {
                    return true;
                }
            }
            if (!string.IsNullOrEmpty(key))
            {
                string lk = key.ToLowerInvariant();
                if (lk.Contains("piledriver") || lk.Contains("ballistic") || lk.Contains("tbm"))
                {
                    return true;
                }
            }
            return false;
        }

        private static MunitionRangeCategory EvaluateCategory(WeaponInfo weaponInfo, string key)
        {
            string lk = key.ToLowerInvariant();

            // 0. Precision Ballistic munitions lacking terminal seekers (Piledriver TBM)
            if (IsBallisticMunition(weaponInfo, key))
            {
                return MunitionRangeCategory.Ballistic;
            }

            // 1. Explicit Strategic / Super Long-Range standoff keyword checks
            if (weaponInfo.strategic ||
                lk.Contains("starfall") ||
                lk.Contains("sunfall") ||
                lk.Contains("cruisemissile") ||
                lk.Contains("c450") ||
                lk.Contains("alnd") ||
                lk.Contains("slnd") ||
                lk.Contains("ashm-300") ||
                lk.Contains("ashm1") ||
                lk.Contains("zenith") ||
                lk.Contains("hasam") ||
                lk.Contains("scimitar") ||
                lk.Contains("aam-36") ||
                lk.Contains("aam4") ||
                lk.Contains("agm-99") ||
                lk.Contains("ashm2"))
            {
                return MunitionRangeCategory.StrategicBallistic;
            }

            // 2. Numeric range evaluation from TargetRequirements.maxRange
            float maxR = weaponInfo.targetRequirements.maxRange;

            // Weapons with >= 100 km max range
            if (maxR >= 95000.0f)
            {
                return MunitionRangeCategory.StrategicBallistic;
            }

            // Long-Range: 50 km to 100 km (e.g. ARAD-116 at 60km, Tusko at 60km, NL-98 at 50km, StratoLance at 50km)
            if (maxR >= 48000.0f ||
                lk.Contains("arad-116") ||
                lk.Contains("arm1") ||
                lk.Contains("tusko") ||
                lk.Contains("stratolance") ||
                lk.Contains("sam_radar2") ||
                lk.Contains("nl-98") ||
                lk.Contains("arh1") ||
                lk.Contains("aam-45") ||
                lk.Contains("sabre") ||
                lk.Contains("atlatl") ||
                lk.Contains("agm-76"))
            {
                return MunitionRangeCategory.LongRange;
            }

            // Medium-Range: 20 km to 50 km (e.g. Scythe at 35km, ARAD-45 at 25km, MLRS at 40km)
            if (maxR >= 20000.0f ||
                lk.Contains("scythe") ||
                lk.Contains("aam-29") ||
                lk.Contains("aam2") ||
                lk.Contains("arad-45") ||
                lk.Contains("arm2") ||
                lk.Contains("broadsword") ||
                lk.Contains("lrirm"))
            {
                return MunitionRangeCategory.MediumRange;
            }

            // Short-Range: < 20 km
            return MunitionRangeCategory.ShortRange;
        }

        /// <summary>
        /// Returns the minimum tracking quality (Q in [0, 1]) required for an AI combatant to fire this munition.
        /// </summary>
        public static float GetRequiredLaunchQuality(WeaponInfo weaponInfo)
        {
            if (weaponInfo == null) return 0.40f;

            MunitionRangeCategory cat = GetCategory(weaponInfo);

            switch (cat)
            {
                case MunitionRangeCategory.Ballistic:
                    return (RadioWarsConfig.AIMinimumBallisticLaunchQuality != null)
                        ? RadioWarsConfig.AIMinimumBallisticLaunchQuality.Value
                        : 0.90f;

                case MunitionRangeCategory.StrategicBallistic:
                    return (RadioWarsConfig.AIMinimumStrategicLaunchQuality != null)
                        ? RadioWarsConfig.AIMinimumStrategicLaunchQuality.Value
                        : 0.60f;

                case MunitionRangeCategory.LongRange:
                    return (RadioWarsConfig.AIMinimumLongRangeLaunchQuality != null)
                        ? RadioWarsConfig.AIMinimumLongRangeLaunchQuality.Value
                        : 0.50f;

                case MunitionRangeCategory.MediumRange:
                case MunitionRangeCategory.ShortRange:
                default:
                    return (RadioWarsConfig.AIMinimumTacticalLaunchQuality != null)
                        ? RadioWarsConfig.AIMinimumTacticalLaunchQuality.Value
                        : 0.40f;
            }
        }

        /// <summary>
        /// Detects if an aircraft is a dedicated standoff electronic warfare / AWACS support airframe (e.g. EW-25 Medusa).
        /// These high-value support assets must never penetrate into hostile SAM lethal envelopes ("hot zones").
        /// </summary>
        public static bool IsStandoffSupportAircraft(Aircraft aircraft)
        {
            if (aircraft == null) return false;
            string name = (aircraft.unitName + " " + aircraft.name).ToLowerInvariant();
            return name.Contains("medusa") || name.Contains("ew-25") || name.Contains("ew25") ||
                   name.Contains("ew1") || name.Contains("awacs");
        }

        /// <summary>
        /// Returns the safe standoff distance (in meters) for dedicated electronic warfare support aircraft (EW-25 Medusa).
        /// Default: 25 km (25,000 meters).
        /// </summary>
        public static float GetMedusaSafeStandoffDistance()
        {
            float km = (RadioWarsConfig.AIMedusaSafeStandoffDistanceKm != null)
                ? RadioWarsConfig.AIMedusaSafeStandoffDistanceKm.Value
                : 25.0f;
            return km * 1000.0f;
        }

        /// <summary>
        /// Calculates the safe standoff distance (in meters) for an aircraft armed with this weapon.
        /// When within this distance and tracking quality is low, the aircraft must break off / retreat to standoff
        /// instead of flying directly at the target in a suicidal kamikaze rush.
        /// </summary>
        public static float GetSafeStandoffDistance(WeaponInfo weaponInfo)
        {
            if (weaponInfo == null) return 5000.0f;

            if (weaponInfo.jammer)
            {
                return GetMedusaSafeStandoffDistance();
            }

            string key = weaponInfo.shortName;
            if (string.IsNullOrEmpty(key)) key = weaponInfo.weaponName;
            float cachedDist;
            if (!string.IsNullOrEmpty(key) && s_standoffCache.TryGetValue(key, out cachedDist))
            {
                return cachedDist;
            }

            float maxR = weaponInfo.targetRequirements.maxRange;
            float minR = weaponInfo.targetRequirements.minRange;
            MunitionRangeCategory cat = GetCategory(weaponInfo);

            float standoff;
            switch (cat)
            {
                case MunitionRangeCategory.Ballistic:
                    // Preserve at least 50 km standoff for ballistic standoff munitions (Piledriver)
                    standoff = Mathf.Max(maxR * 0.50f, 50000.0f);
                    break;

                case MunitionRangeCategory.StrategicBallistic:
                    // Preserve at least 45 km standoff for strategic / ballistic / 100km+ weapons
                    standoff = Mathf.Max(maxR * 0.50f, 45000.0f);
                    break;

                case MunitionRangeCategory.LongRange:
                    // Preserve at least 25 km standoff for 50-100km weapons (ARAD-116, NL-98, Tusko)
                    standoff = Mathf.Max(maxR * 0.55f, 25000.0f);
                    break;

                case MunitionRangeCategory.MediumRange:
                    // Preserve at least 15 km standoff for 20-50km weapons (Scythe, ARAD-45)
                    standoff = Mathf.Max(maxR * 0.60f, 15000.0f);
                    break;

                case MunitionRangeCategory.ShortRange:
                default:
                    // WVR / Close weapons: standard turning/minimum safety margin
                    standoff = Mathf.Max(minR * 2.0f, 4000.0f);
                    break;
            }

            if (!string.IsNullOrEmpty(key))
            {
                s_standoffCache[key] = standoff;
            }

            return standoff;
        }
    }
}
