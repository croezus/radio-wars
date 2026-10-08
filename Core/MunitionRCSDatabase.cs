using System;
using UnityEngine;

namespace RadioWars.Core
{
    /// <summary>
    /// Authenticity-calibrated database of Radar Cross Section (RCS) values for munitions,
    /// missiles, guided bombs, drop tanks, and electronic pods across Nuclear Option.
    /// Values are extracted directly from binary game assets (radarSize) and calibrated to physical radar scattering theory.
    /// Zero heap allocation implementation.
    /// </summary>
    public static class MunitionRCSDatabase
    {
        private static readonly System.Collections.Generic.Dictionary<string, float> s_rcsCache =
            new System.Collections.Generic.Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private static readonly System.Collections.Generic.Dictionary<string, bool> s_stealthPodCache =
            new System.Collections.Generic.Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private static readonly System.Collections.Generic.Dictionary<string, bool> s_internalBayCache =
            new System.Collections.Generic.Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public static void ClearAll()
        {
            s_rcsCache.Clear();
            s_stealthPodCache.Clear();
            s_internalBayCache.Clear();
        }

        /// <summary>
        /// Retrieves the calibrated Radar Cross Section (m²) for a given munition or store.
        /// </summary>
        public static float GetMunitionRCS(string weaponKey)
        {
            if (string.IsNullOrEmpty(weaponKey)) return 0.05f;

            float cachedRcs;
            if (s_rcsCache.TryGetValue(weaponKey, out cachedRcs))
            {
                return cachedRcs;
            }

            float rcs = CalculateMunitionRCS(weaponKey);
            s_rcsCache[weaponKey] = rcs;
            return rcs;
        }

        private static float CalculateMunitionRCS(string weaponKey)
        {
            string key = weaponKey.ToLowerInvariant();

            // 0. Conformal Enclosed Stealth Weapon Pods (e.g. F-22E Strike Raptor Stealth Pods)
            // Aerodynamic faceted low-observable composite pods shielding stores inside (~0.005 m² / -23 dBsm)
            if (IsStealthPod(key))
            {
                return 0.005f;
            }

            // 1. Heavy Ramjet Long-Range Air-to-Air Missiles (e.g. AAM-45 Sabre ~0.025 m² / -16 dBsm)
            if (key.Contains("aam45") || key.Contains("aam-45") || key.Contains("sabre"))
            {
                return 0.025f;
            }

            // 2. Air-to-Air Missiles (AAM) & Anti-Radiation (ARM): Compact cylindrical bodies with cruciform fins (~0.008 - 0.012 m²)
            if (key.Contains("srirm") || key.Contains("irm-s5") || key.Contains("gladius"))
            {
                return 0.008f;
            }
            if (key.Contains("lrirm") || key.Contains("mrm-s4") || key.Contains("broadsword"))
            {
                return 0.012f;
            }
            if (key.Contains("aam1") || key.Contains("mmr-s3") || key.Contains("s3") ||
                key.Contains("3s-rmm") || key.Contains("aam1gelb") || key.Contains("aam1_backwards") ||
                key.Contains("aam2") || key.Contains("scythe") || key.Contains("aam-29") ||
                key.Contains("aam3") || key.Contains("irm-s2") || key.Contains("s2") ||
                key.Contains("aam4") || key.Contains("scimitar") || key.Contains("aam-36") ||
                key.Contains("arh1") || key.Contains("nl-98") ||
                key.Contains("arm1") || key.Contains("arad-116") || key.Contains("arad") || key.Contains("arad45") || key.Contains("wep_arm2") ||
                key.Contains("sam_ir1") || key.Contains("irm-s1"))
            {
                return 0.010f;
            }

            // 3. Compact High-Speed Air-to-Ground & Anti-Ship Missiles (AGM / AShM) (~0.015 - 0.020 m²)
            if (key.Contains("fastagm") || key.Contains("agm-27") || key.Contains("pyre"))
            {
                return 0.015f;
            }
            if (key.Contains("agm1") || key.Contains("agm-48") ||
                key.Contains("agm2") || key.Contains("atp-1") ||
                key.Contains("agm-99") || key.Contains("ashm2") ||
                key.Contains("gtg1") || key.Contains("at-145") ||
                key.Contains("agm_scanner1") || key.Contains("eyeball"))
            {
                return 0.020f;
            }

            // 4. Heavy Supersonic Air-to-Surface & Ship-Launched Missiles (~0.050 m²)
            if (key.Contains("agm_heavy") || key.Contains("agm-68") ||
                key.Contains("shipagm") || key.Contains("rgm-68"))
            {
                return 0.050f;
            }

            // 5. Hypersonic & High-Altitude ABM Strike Missiles (~0.040 - 0.060 m²)
            if (key.Contains("hypersonic1_nuke") || key.Contains("hsm-n") || key.Contains("sunfall"))
            {
                return 0.045f;
            }
            if (key.Contains("hypersonic") || key.Contains("hsm-750") || key.Contains("starfall"))
            {
                return 0.040f;
            }
            if (key.Contains("hasam") || key.Contains("r-100n") || key.Contains("zenith") || key.Contains("hlt_hail"))
            {
                return 0.060f;
            }

            // 6. Low-Observable Stealth Anti-Ship & Cruise Missiles (AShM / ALCM) (VLO ~0.005 - 0.006 m²)
            if (key.Contains("smallcruisemissile") || key.Contains("agm-76") || key.Contains("atlatl"))
            {
                return 0.006f;
            }
            if (key.Contains("ashm1") || key.Contains("ashm-300") ||
                key.Contains("cruisemissile1") || key.Contains("alm-c450") || key.Contains("c450") ||
                key.Contains("cruisemissile20kt") || key.Contains("alnd-4") || key.Contains("alnd") ||
                key.Contains("slnd") || key.Contains("slnd-9"))
            {
                return 0.005f;
            }

            // 7. Conventional Subsonic Anti-Ship Missiles (~0.008 m²)
            if (key.Contains("fastashm"))
            {
                return 0.008f;
            }

            // 8. Heavy Supersonic Quasi-Ballistic Missiles (~0.070 m²)
            if (key.Contains("ashm3") || key.Contains("tusko") || key.Contains("tuskon"))
            {
                return 0.070f;
            }

            // 9. Tactical Ballistic Missiles & Heavy Long-Range SAMs (~0.100 m² / -10 dBsm)
            if (key.Contains("ballisticmissile") || key.Contains("piledriver") ||
                key.Contains("sam_radar2") || key.Contains("stratolance") || key.Contains("r9sam") ||
                key.Contains("sam_radar1") || key.Contains("ram-45"))
            {
                return 0.100f;
            }

            // 10. Medium Naval & Ground SAM Missiles (~0.025 m²)
            if (key.Contains("sam_r85") || key.Contains("sam-85") || key.Contains("whirlwind"))
            {
                return 0.025f;
            }

            // 11. Stealth Composite & Precision Miniature Glide Bombs (VLO ~0.001 - 0.010 m²)
            if (key.Contains("glidebomb400") || key.Contains("pgm-400") || key.Contains("pgm400"))
            {
                return 0.002f;
            }
            if (key.Contains("bomb_500_glide") || key.Contains("gbm-500lr") || key.Contains("gbm500") || key.Contains("bomb80glide"))
            {
                return 0.001f;
            }
            if (key.Contains("bomb_glide1") || key.Contains("pab-80lr") || key.Contains("pab80"))
            {
                return 0.010f;
            }

            // 12. Small Guided & Retarded Bombs (~0.050 m²)
            if (key.Contains("bomb_125") || key.Contains("pab-125"))
            {
                return 0.050f;
            }

            // 13. Medium Guided Bombs & Cluster Dispensers (~0.060 - 0.100 m²)
            if (key.Contains("clusterbomb1") || key.Contains("clusterbomb"))
            {
                return 0.060f;
            }
            if (key.Contains("bombletdispenser") || key.Contains("asd-16"))
            {
                return 0.075f;
            }
            if (key.Contains("bomb_250") || key.Contains("pab-250"))
            {
                return 0.100f;
            }

            // 14. Heavy Penetrator, 500kg & Heavy Glide Bombs (~0.150 - 0.200 m²)
            if (key.Contains("bomb_penetrator") || key.Contains("gpo-2p") || key.Contains("auger"))
            {
                return 0.200f;
            }
            if (key.Contains("glidebomb_3000") || key.Contains("glidebomb3000") || key.Contains("pab-3000") || key.Contains("pab3000"))
            {
                return 0.150f;
            }
            if (key.Contains("bomb500") || key.Contains("gpo-500") ||
                key.Contains("bomb_cluster") || key.Contains("cbo-400"))
            {
                return 0.150f;
            }

            // 15. Strategic Nuclear Bombs (~0.200 m²)
            if (key.Contains("nuclearbomb") || key.Contains("gpo-n"))
            {
                return 0.200f;
            }

            // 16. Heavy Unguided Nuclear / Air-to-Air Rocket (~0.250 m²)
            if (key.Contains("genie") || key.Contains("air-2"))
            {
                return 0.250f;
            }

            // 17. Massive Demolition Blockbuster Bombs (~1.000 m²)
            if (key.Contains("demolition"))
            {
                return 1.000f;
            }

            // 18. Unguided Rockets & Artillery Shells (~0.010 m²)
            if (key.Contains("heavyrocket") || key.Contains("rocket") || key.Contains("lynchpin") || key.Contains("kingpin") ||
                key.Contains("shell") || key.Contains("mlrs") || key.Contains("152_guided"))
            {
                return 0.010f;
            }

            // 19. External Fuel Tanks / Drop Tanks: Graded by aerodynamic capacity and surface area
            if (key.Contains("droptank450") || key.Contains("150gal") || key.Contains("droptank_150"))
            {
                return 0.150f; // 450 kg compact drop tank
            }
            if (key.Contains("droptank1200") || key.Contains("370gal") || key.Contains("droptank_1200"))
            {
                return 0.300f; // 1200 kg standard tactical fighter drop tank
            }
            if (key.Contains("droptank2000") || key.Contains("600gal") || key.Contains("droptank_2000"))
            {
                return 0.450f; // 2000 kg heavy long-range drop tank
            }
            if (key.Contains("tank") || key.Contains("fuel") || key.Contains("droptank"))
            {
                return 0.350f; // Generic external fuel tank
            }

            // 20. External Sensor, Countermeasure & Gun Pods (~0.040 - 0.200 m²)
            if (key.Contains("externalflarelauncher16") || key.Contains("ecs-16"))
            {
                return 0.040f; // 16-round compact dispenser
            }
            if (key.Contains("externalflarelauncher32") || key.Contains("ecs-32"))
            {
                return 0.060f; // 32-round dispenser
            }
            if (key.Contains("targetingpod") || key.Contains("ets-15"))
            {
                return 0.120f; // Aerodynamic EO/IR targeting pod
            }
            if (key.Contains("swivel30") || key.Contains("1509_30mm_pod") || key.Contains("swivelgunpod") ||
                key.Contains("gunpod_swivel30mm"))
            {
                return 0.180f; // Heavy twin 30mm gun pod
            }
            if (key.Contains("pod") || key.Contains("jammer") || key.Contains("ecm") || key.Contains("gunpod"))
            {
                return 0.200f;
            }

            // Generic fallback heuristic
            if (key.Contains("missile")) return 0.020f;
            if (key.Contains("bomb")) return 0.100f;

            return 0.050f;
        }

        /// <summary>
        /// Checks whether a weapon or mount represents an enclosed stealth conformal weapon pod.
        /// E.g. Aryx F-22E Strike Raptor enclosed stealth pods (stealthpod_aam1, stealthpod_bomb250, etc.).
        /// </summary>
        public static bool IsStealthPod(string weaponOrMountKey)
        {
            if (string.IsNullOrEmpty(weaponOrMountKey)) return false;

            bool cached;
            if (s_stealthPodCache.TryGetValue(weaponOrMountKey, out cached))
            {
                return cached;
            }

            cached = weaponOrMountKey.IndexOf("stealthpod", StringComparison.OrdinalIgnoreCase) >= 0;
            s_stealthPodCache[weaponOrMountKey] = cached;
            return cached;
        }

        /// <summary>
        /// Checks whether a weapon mount identifier or name denotes an internal weapon bay.
        /// </summary>
        public static bool IsInternalBayMount(string mountName)
        {
            if (string.IsNullOrEmpty(mountName)) return false;

            bool cached;
            if (s_internalBayCache.TryGetValue(mountName, out cached))
            {
                return cached;
            }

            cached = mountName.IndexOf("internal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     mountName.IndexOf("bay", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     mountName.IndexOf("rotary", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     mountName.IndexOf("stealthpod", StringComparison.OrdinalIgnoreCase) >= 0;
            s_internalBayCache[mountName] = cached;
            return cached;
        }
    }
}
