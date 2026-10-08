using System;
using UnityEngine;
using RadioWars.Config;

namespace RadioWars.Core
{
    public enum RadarBand
    {
        SBand,  // ~3 GHz, λ ≈ 0.10 m (Search / Early Warning)
        CBand,  // ~5.5 GHz, λ ≈ 0.054 m (Long-range SAM)
        XBand,  // ~9.5 GHz, λ ≈ 0.0315 m (Fighter Fire Control & AESA)
        KuBand  // ~15 GHz, λ ≈ 0.02 m (Active Radar Missile Seekers & Mast Radars)
    }

    public enum RadarTier
    {
        Tier0_ActiveSeeker,                 // ARH missiles (AAM-29, NL-98, RAM-45)
        Tier1_LightHeloNav,                 // SAH-46 Chicane, VL-49, UH-80
        Tier2_Standard4thGen,               // FS-12 Revoker, T/A-30, A-19, VT-7, F-99, AB-4
        Tier3_TacticalAESA_Heavy4Plus,      // FS-20 Vortex, KR-67 Ifrit, EW-1 Medusa, KR-33 Agni
        Tier4_HeavyAirDominanceAESA,        // F-22E Strike Raptor, SFB-81 Darkreach, FS-41 Eclipse
        Tier5_StrategicGroundNaval          // RadarStation1, RadarSam1, RadarContainer1, Warships
    }

    public struct RadarSpecs
    {
        public float PeakPowerWatts;      // Pt (W)
        public float AntennaGainLinear;   // Gt = Gr (linear power ratio, e.g. 32 dBi ≈ 1585)
        public float FrequencyHz;         // f (Hz)
        public float WavelengthMeters;    // λ = c / f (m)
        public float BandwidthHz;         // B (Hz)
        public float NoiseFigureLinear;   // F (e.g. 3.0 dB ≈ 2.0)
        public float SystemLossLinear;    // Lsys (e.g. 4 dB ≈ 2.51)
        public float ProcessingGainLinear;// Gp (Pulse compression & Doppler integration gain ~25-35 dB = 300-3500)
        public float AttrCoeff_dB_per_km; // Atmospheric loss dB/km
        public RadarBand Band;
        public RadarTier Tier;

        // Class 1 (200 km reference range): EW-25 Medusa Standoff EW Array
        public static RadarSpecs CreateMedusaEWArray()
        {
            RadarSpecs specs;
            specs.Band = RadarBand.CBand;
            specs.PeakPowerWatts = 74400.0f;         // 74.4 kW high-power C-band array
            specs.AntennaGainLinear = 3162.0f;       // ~35.0 dBi
            specs.FrequencyHz = 5.5e9f;              // 5.5 GHz C-band
            specs.WavelengthMeters = 0.05451f;
            specs.BandwidthHz = 4.0e6f;              // 4 MHz
            specs.NoiseFigureLinear = 1.78f;         // ~2.5 dB
            specs.SystemLossLinear = 2.0f;           // ~3 dB
            specs.ProcessingGainLinear = 2500.0f;    // ~34.0 dB
            specs.AttrCoeff_dB_per_km = 0.012f;      // C-band attenuation
            specs.Tier = RadarTier.Tier4_HeavyAirDominanceAESA;
            return specs;
        }

        // Class 2 (120 km reference range): Dynamo Destroyer, Argus Frigate
        public static RadarSpecs CreateNavalAirDefenseDestroyer()
        {
            RadarSpecs specs;
            specs.Band = RadarBand.CBand;
            specs.PeakPowerWatts = 10200.0f;         // 10.2 kW naval air defense
            specs.AntennaGainLinear = 2818.0f;       // ~34.5 dBi
            specs.FrequencyHz = 5.5e9f;              // 5.5 GHz
            specs.WavelengthMeters = 0.05451f;
            specs.BandwidthHz = 3.0e6f;              // 3 MHz
            specs.NoiseFigureLinear = 2.0f;          // ~3.0 dB
            specs.SystemLossLinear = 2.51f;          // ~4 dB
            specs.ProcessingGainLinear = 2000.0f;    // ~33.0 dB
            specs.AttrCoeff_dB_per_km = 0.012f;
            specs.Tier = RadarTier.Tier5_StrategicGroundNaval;
            return specs;
        }

        // Class 3A (100 km reference range): HLT Radar Truck, Radar Station, Fleet Carriers (S-Band)
        public static RadarSpecs CreateHeavyGroundNavalSBand()
        {
            RadarSpecs specs;
            specs.Band = RadarBand.SBand;
            specs.PeakPowerWatts = 1250.0f;          // 1.25 kW calibrated S-band power (100 km on 1 m²)
            specs.AntennaGainLinear = 2500.0f;       // ~34.0 dBi
            specs.FrequencyHz = 3.0e9f;              // 3.0 GHz
            specs.WavelengthMeters = 0.09993f;       // S-band wavelength
            specs.BandwidthHz = 2.0e6f;              // 2 MHz
            specs.NoiseFigureLinear = 2.0f;          // ~3.0 dB
            specs.SystemLossLinear = 2.51f;          // ~4 dB
            specs.ProcessingGainLinear = 1500.0f;    // ~31.8 dB
            specs.AttrCoeff_dB_per_km = 0.008f;      // low S-band attenuation
            specs.Tier = RadarTier.Tier5_StrategicGroundNaval;
            return specs;
        }

        // Class 3B (100 km reference range): FS-20 Vortex, KR-67 Ifrit, SFB-81 Darkreach, AB-4 Alkyon, F-22E, FS-41 (X-Band)
        public static RadarSpecs CreateHeavy5thGenAESA()
        {
            RadarSpecs specs;
            specs.Band = RadarBand.XBand;
            specs.PeakPowerWatts = 16400.0f;        // 16.4 kW peak AESA GaN power (100 km on 1 m²)
            specs.AntennaGainLinear = 2818.0f;      // ~34.5 dBi nose array
            specs.FrequencyHz = 9.5e9f;             // 9.5 GHz
            specs.WavelengthMeters = 0.03155f;      // X-band
            specs.BandwidthHz = 5.0e6f;             // 5 MHz agile pulse
            specs.NoiseFigureLinear = 1.78f;        // ~2.5 dB ultra-low noise receiver
            specs.SystemLossLinear = 2.0f;          // ~3 dB low AESA distribution losses
            specs.ProcessingGainLinear = 2500.0f;   // ~34.0 dB advanced digital STAP
            specs.AttrCoeff_dB_per_km = 0.018f;
            specs.Tier = RadarTier.Tier3_TacticalAESA_Heavy4Plus;
            return specs;
        }

        public static RadarSpecs CreateTacticalAESA()
        {
            return CreateHeavy5thGenAESA();
        }

        // Class 4 (80 km reference range): FS-12 Revoker, F-99 Shrike, KR-33 Agni
        public static RadarSpecs CreateStandard4thGen()
        {
            RadarSpecs specs;
            specs.Band = RadarBand.XBand;
            specs.PeakPowerWatts = 20000.0f;         // 20 kW pulse-doppler (80 km on 1 m²)
            specs.AntennaGainLinear = 2000.0f;       // ~33.0 dBi
            specs.FrequencyHz = 9.5e9f;              // 9.5 GHz
            specs.WavelengthMeters = 0.03155f;       // X-band
            specs.BandwidthHz = 5.0e6f;              // 5 MHz
            specs.NoiseFigureLinear = 2.0f;          // ~3.0 dB
            specs.SystemLossLinear = 2.51f;          // ~4 dB
            specs.ProcessingGainLinear = 2000.0f;    // ~33.0 dB
            specs.AttrCoeff_dB_per_km = 0.018f;
            specs.Tier = RadarTier.Tier2_Standard4thGen;
            return specs;
        }

        // Class 5 (65 km reference range): VT-7 Vagrant, T/A-30 Compass
        public static RadarSpecs CreateLightJetRadar()
        {
            RadarSpecs specs;
            specs.Band = RadarBand.XBand;
            specs.PeakPowerWatts = 15000.0f;         // 15 kW compact nose radar (65 km on 1 m²)
            specs.AntennaGainLinear = 1600.0f;       // ~32.0 dBi
            specs.FrequencyHz = 9.5e9f;              // 9.5 GHz
            specs.WavelengthMeters = 0.03155f;       // X-band
            specs.BandwidthHz = 5.0e6f;              // 5 MHz
            specs.NoiseFigureLinear = 2.0f;          // ~3.0 dB
            specs.SystemLossLinear = 2.51f;          // ~4 dB
            specs.ProcessingGainLinear = 1600.0f;    // ~32.0 dB
            specs.AttrCoeff_dB_per_km = 0.018f;
            specs.Tier = RadarTier.Tier2_Standard4thGen;
            return specs;
        }

        // Class 6 (50 km reference range): Shard Corvette, Cursor LFD, T9K41 Boltstrike
        public static RadarSpecs CreateTacticalSAMRadar()
        {
            RadarSpecs specs;
            specs.Band = RadarBand.XBand;
            specs.PeakPowerWatts = 6500.0f;          // 6.5 kW short-range radar (50 km on 1 m²)
            specs.AntennaGainLinear = 1400.0f;       // ~31.5 dBi
            specs.FrequencyHz = 9.5e9f;              // 9.5 GHz X-band
            specs.WavelengthMeters = 0.03155f;       // X-band
            specs.BandwidthHz = 4.0e6f;              // 4 MHz
            specs.NoiseFigureLinear = 2.0f;          // ~3.0 dB
            specs.SystemLossLinear = 2.51f;          // ~4 dB
            specs.ProcessingGainLinear = 1200.0f;    // ~30.8 dB
            specs.AttrCoeff_dB_per_km = 0.018f;
            specs.Tier = RadarTier.Tier5_StrategicGroundNaval;
            return specs;
        }

        // Tier 1 (35 km reference range): SAH-46 Chicane, VL-49 Tarantula, UH-80/90 Ibis
        public static RadarSpecs CreateLightHeloRadar()
        {
            RadarSpecs specs;
            specs.Band = RadarBand.KuBand;
            specs.PeakPowerWatts = 3300.0f;          // 3.3 kW compact mast radar (35 km on 1 m²)
            specs.AntennaGainLinear = 2000.0f;       // ~33.0 dBi
            specs.FrequencyHz = 15.0e9f;             // 15 GHz
            specs.WavelengthMeters = 0.01998f;       // Ku-band
            specs.BandwidthHz = 5.0e6f;              // 5 MHz
            specs.NoiseFigureLinear = 2.0f;          // ~3.0 dB
            specs.SystemLossLinear = 2.51f;          // ~4 dB
            specs.ProcessingGainLinear = 1000.0f;    // ~30.0 dB
            specs.AttrCoeff_dB_per_km = 0.035f;
            specs.Tier = RadarTier.Tier1_LightHeloNav;
            return specs;
        }

        // Tier 0 (20 km reference range): ARH Missile Seekers (AAM-29 Scythe, NL-98, RAM-45)
        public static RadarSpecs CreateSeekerRadar()
        {
            RadarSpecs specs;
            specs.Band = RadarBand.KuBand;
            specs.PeakPowerWatts = 4900.0f;          // 4.9 kW active seeker dish (20 km on 1 m²)
            specs.AntennaGainLinear = 900.0f;        // ~29.5 dBi
            specs.FrequencyHz = 15.0e9f;             // 15 GHz
            specs.WavelengthMeters = 0.01998f;       // Ku-band
            specs.BandwidthHz = 8.0e6f;              // 8 MHz
            specs.NoiseFigureLinear = 2.24f;         // ~3.5 dB
            specs.SystemLossLinear = 3.0f;           // ~4.8 dB
            specs.ProcessingGainLinear = 600.0f;     // ~27.8 dB
            specs.AttrCoeff_dB_per_km = 0.035f;
            specs.Tier = RadarTier.Tier0_ActiveSeeker;
            return specs;
        }

        public static RadarSpecs CreateSeekerKuBand()
        {
            return CreateSeekerRadar();
        }

        public static RadarSpecs GetRadarSpecsForPlatform(Unit attachedUnit, Radar radar)
        {
            if (attachedUnit == null && radar == null)
            {
                return CreateStandard4thGen();
            }

            string unitName = (attachedUnit != null && !string.IsNullOrEmpty(attachedUnit.unitName))
                ? attachedUnit.unitName.ToLowerInvariant()
                : (attachedUnit != null ? attachedUnit.name.ToLowerInvariant() : "");
            string cleanName = unitName.Replace(" ", "").Replace("-", "").Replace("_", "");

            // 1. Active missile radar seekers (Tier 0: 20 km)
            if (attachedUnit is Missile || cleanName.Contains("missile") || cleanName.Contains("aam") || cleanName.Contains("seeker"))
            {
                return CreateSeekerRadar();
            }

            // 2. Class 1 (200 km): Dedicated EW / Airborne Early Warning (EW-25 Medusa)
            if (cleanName.Contains("medusa") || cleanName.Contains("ew1") || cleanName.Contains("ew25"))
            {
                return CreateMedusaEWArray();
            }

            // 3. Class 2 (120 km): Guided Missile Destroyers & Air Defense Frigates (Dynamo, Argus)
            if (cleanName.Contains("dynamo") || cleanName.Contains("destroyer") ||
                cleanName.Contains("argus") || cleanName.Contains("frigate"))
            {
                return CreateNavalAirDefenseDestroyer();
            }

            // 4. Class 6 (50 km): Coastal Corvettes, Littoral Flight Decks & Tactical SAMs (Shard, Cursor, Boltstrike)
            if (cleanName.Contains("shard") || cleanName.Contains("corvette") ||
                cleanName.Contains("cursor") || cleanName.Contains("lfd") ||
                cleanName.Contains("boltstrike") || cleanName.Contains("t9k41") ||
                cleanName.Contains("radarsam") || cleanName.Contains("tacticalsam"))
            {
                return CreateTacticalSAMRadar();
            }

            // 5. Class 3A (100 km S-Band): Heavy Ground & Naval Search Radars (HLT Radar Truck, Radar Station, Carriers)
            if (cleanName.Contains("radarstation") || cleanName.Contains("radartruck") ||
                cleanName.Contains("hltradartruck") || cleanName.Contains("hlt") ||
                cleanName.Contains("radarcontainer") || cleanName.Contains("groundradar") ||
                cleanName.Contains("carrier") || cleanName.Contains("hyperion") || cleanName.Contains("annex") ||
                (attachedUnit != null && !(attachedUnit is Aircraft) && !(attachedUnit is Missile)))
            {
                return CreateHeavyGroundNavalSBand();
            }

            // 6. Class 3B (100 km X-Band): 5th-Gen Stealth & Heavy Strike Aircraft (FS-20 Vortex, KR-67 Ifrit, SFB-81, AB-4, F-22E, FS-41)
            if (cleanName.Contains("vortex") || cleanName.Contains("fs20") || cleanName.Contains("smallfighter1") ||
                cleanName.Contains("ifrit") || cleanName.Contains("kr67") || cleanName.Contains("multirole1") ||
                cleanName.Contains("darkreach") || cleanName.Contains("sfb81") || cleanName.Contains("fastbomber") ||
                cleanName.Contains("alkyon") || cleanName.Contains("ab4") ||
                cleanName.Contains("raptor") || cleanName.Contains("f22") || cleanName.Contains("kingraptor") ||
                cleanName.Contains("eclipse") || cleanName.Contains("fs41") || cleanName.Contains("interceptor1") ||
                cleanName.Contains("ufo"))
            {
                return CreateHeavy5thGenAESA();
            }

            // 7. Class 5 (65 km): Light Interceptor / Jet Trainers (VT-7 Vagrant, T/A-30 Compass)
            if (cleanName.Contains("vagrant") || cleanName.Contains("vt7") || cleanName.Contains("vtoltrainer1") ||
                cleanName.Contains("compass") || cleanName.Contains("ta30") || cleanName.Contains("trainer1"))
            {
                return CreateLightJetRadar();
            }

            // 8. Tier 1 (35 km): Helicopters and Light Utility Transports (Chicane, Tarantula, Ibis)
            if (cleanName.Contains("chicane") || cleanName.Contains("sah46") || cleanName.Contains("helicopter") ||
                cleanName.Contains("tarantula") || cleanName.Contains("vl49") ||
                cleanName.Contains("ibis") || cleanName.Contains("uh80") || cleanName.Contains("uh90"))
            {
                return CreateLightHeloRadar();
            }

            // 9. Class 4 (80 km): Standard 4th-Gen Frontline Fighters (FS-12 Revoker, F-99 Shrike, KR-33 Agni)
            if (cleanName.Contains("revoker") || cleanName.Contains("fs12") ||
                cleanName.Contains("shrike") || cleanName.Contains("f99") ||
                cleanName.Contains("agni") || cleanName.Contains("kr33") || cleanName.Contains("palafighter") ||
                cleanName.Contains("brawler") || cleanName.Contains("a19"))
            {
                return CreateStandard4thGen();
            }

            // 10. Fallback: Calibrate from radar parameters if available
            if (radar != null)
            {
                return CreateFromVanilla(
                    radar.RadarParameters.maxRange,
                    radar.RadarParameters.maxSignal,
                    radar.RadarParameters.minSignal
                );
            }

            return CreateStandard4thGen();
        }

        public static RadarSpecs CreateFromVanilla(float maxRange, float maxSignal, float minSignal)
        {
            float r0 = (minSignal > 0.001f) ? (maxRange / minSignal) : (maxRange * 2.0f);
            if (r0 >= 160000.0f) return CreateMedusaEWArray();
            if (r0 >= 110000.0f) return CreateNavalAirDefenseDestroyer();
            if (r0 >= 90000.0f) return CreateHeavyGroundNavalSBand();
            if (r0 >= 72000.0f) return CreateStandard4thGen();
            if (r0 >= 58000.0f) return CreateLightJetRadar();
            if (r0 >= 40000.0f) return CreateTacticalSAMRadar();
            if (r0 >= 28000.0f) return CreateLightHeloRadar();
            return CreateSeekerRadar();
        }
    }

    public static class RadarPhysics
    {
        public static RadarSpecs GetRadarSpecsForPlatform(Unit attachedUnit, Radar radar)
        {
            return RadarSpecs.GetRadarSpecsForPlatform(attachedUnit, radar);
        }

        // Physics constants
        public const float BoltzmannConstant = 1.380649e-23f; // J/K
        public const float StandardTemperatureK = 290.0f;     // T0 (Kelvin)
        public const float SpeedOfLight = 299792458.0f;        // c (m/s)
        public const float FourPiCubed = 1984.4017f;          // (4 * pi)^3

        // 4/3 Earth refraction constants
        // Real Earth radius RE ≈ 6,371,000 m. With k = 4/3 refraction:
        // k * RE ≈ 8,494,667 m. 2 * k * RE ≈ 16,989,333 m
        public const float EarthRadiusMeters = 6371000.0f;
        public const float RefractionK = 4.0f / 3.0f;
        public const float EffectiveEarthRadiusMeters = EarthRadiusMeters * RefractionK;
        public const float TwoEffectiveRadius = 2.0f * EffectiveEarthRadiusMeters; // ~1.698933e7

        /// <summary>
        /// Receiver thermal noise floor Pn = k * T0 * B * F (Watts)
        /// </summary>
        public static float CalculateThermalNoiseFloor(float bandwidthHz, float noiseFigureLinear)
        {
            if (bandwidthHz <= 0.0f) bandwidthHz = 5.0e6f;
            if (noiseFigureLinear < 1.0f) noiseFigureLinear = 1.0f;
            return BoltzmannConstant * StandardTemperatureK * bandwidthHz * noiseFigureLinear;
        }

        /// <summary>
        /// Monostatic Radar Range Equation for received echo power Pr (Watts)
        /// Pr = (Pt * Gt * Gr * λ² * σ) / ((4π)³ * R⁴ * Lsys) * 10^(-2 * γ * R / 10000)
        /// </summary>
        public static float CalculateEchoPower(
            float rangeMeters,
            float targetRcsSqm,
            ref RadarSpecs specs)
        {
            if (rangeMeters < 10.0f) rangeMeters = 10.0f;
            if (targetRcsSqm <= 0.0001f) targetRcsSqm = 0.0001f;

            float r2 = rangeMeters * rangeMeters;
            float r4 = r2 * r2;

            float numerator = specs.PeakPowerWatts * 
                              (specs.AntennaGainLinear * specs.AntennaGainLinear) * 
                              (specs.WavelengthMeters * specs.WavelengthMeters) * 
                              targetRcsSqm;

            float denominator = FourPiCubed * r4 * specs.SystemLossLinear;
            if (denominator <= 0.0f) return 0.0f;

            float pr = (numerator / denominator) * Mathf.Max(1.0f, specs.ProcessingGainLinear);

            // Two-way atmospheric attenuation
            bool applyAtmosphere = RadioWarsConfig.AtmosphericLossEnabled == null || RadioWarsConfig.AtmosphericLossEnabled.Value;
            if (applyAtmosphere && specs.AttrCoeff_dB_per_km > 0.0f)
            {
                float rangeKm = rangeMeters * 0.001f;
                float twoWayLossDb = 2.0f * specs.AttrCoeff_dB_per_km * rangeKm;
                float lossLinear = Mathf.Pow(10.0f, -twoWayLossDb * 0.1f);
                pr *= lossLinear;
            }

            return pr;
        }

        /// <summary>
        /// Instantaneous Signal-to-Interference-plus-Noise Ratio in Decibels
        /// SNR_dB = 10 * log10(Pr / (Pn + Pc + Pj))
        /// </summary>
        public static float CalculateSNRdB(float echoPowerPr, float noiseFloorPn, float clutterPowerPc, float jammerPowerPj)
        {
            float totalInterference = noiseFloorPn + clutterPowerPc + jammerPowerPj;
            if (totalInterference <= 1e-25f) totalInterference = 1e-25f;

            if (echoPowerPr <= 1e-26f) return -50.0f;

            float snrLinear = echoPowerPr / totalInterference;
            if (snrLinear <= 1e-5f) return -50.0f;

            return 10.0f * Mathf.Log10(snrLinear);
        }

        /// <summary>
        /// Computes geometric horizon depression drop Δh_drop = d² / (2 * k * RE) in meters.
        /// Strictly evaluated in meters (RE = 6,371,000m, k = 4/3, 2*k*RE = 16,989,333.33m).
        /// At d = 27,000m (27 km), Δh_drop = (27000)² / 16989333.33 ≈ 42.91 meters.
        /// </summary>
        public static float CalculateHorizonDepressionDrop(float slantRangeMeters)
        {
            if (slantRangeMeters <= 0.0f) return 0.0f;
            return (slantRangeMeters * slantRangeMeters) / TwoEffectiveRadius;
        }

        /// <summary>
        /// Maximum line-of-sight radar horizon distance d_horiz = sqrt(2*k*RE*h1) + sqrt(2*k*RE*h2) in meters.
        /// Strictly evaluated in meters.
        /// For h1 = 3,000m and h2 = 3,000m: d1 ≈ 225.8 km, d2 ≈ 225.8 km, total horizon ≈ 451.5 km.
        /// </summary>
        public static float CalculateRadarHorizonDistance(float radarAltitudeMeters, float targetAltitudeMeters)
        {
            float h1 = Mathf.Max(0.0f, radarAltitudeMeters);
            float h2 = Mathf.Max(0.0f, targetAltitudeMeters);

            float d1 = Mathf.Sqrt(TwoEffectiveRadius * h1);
            float d2 = Mathf.Sqrt(TwoEffectiveRadius * h2);
            return d1 + d2;
        }

        /// <summary>
        /// Earth curvature horizon occlusion check using 4/3 atmospheric refraction.
        /// Curvature horizon masking only applies when target flies at ultra-low altitude (< 40m Nap-of-the-Earth).
        /// Above 40m (or configured threshold), the aircraft is in open airspace and curvature will never mask it.
        /// </summary>
        public static bool IsOccludedByEarthCurvature(
            Vector3 radarPosition,
            Vector3 targetPosition,
            float targetAltitudeAboveTerrain)
        {
            // If Earth Curvature simulation is disabled in config, never occlude
            if (RadioWarsConfig.EarthCurvatureEnabled != null && !RadioWarsConfig.EarthCurvatureEnabled.Value)
            {
                return false;
            }

            // User requirement: Curvature masking only applies at ultra-low altitude (< 40m).
            // Above 40m, aircraft has clear line of sight over curvature / horizon.
            float minAltThreshold = RadioWarsConfig.EffectiveCurvatureMaskAltitude;
            if (targetAltitudeAboveTerrain >= minAltThreshold)
            {
                return false;
            }

            // Target is at ultra-low altitude (< 40m) hugging the deck.
            // Check if it is beyond the geometric radar horizon.
            float dx = targetPosition.x - radarPosition.x;
            float dz = targetPosition.z - radarPosition.z;
            float horizDistMeters = Mathf.Sqrt(dx * dx + dz * dz);

            float hRadar = Mathf.Max(1.0f, radarPosition.y);
            // Use true absolute geometric elevation above sea level datum (Y=0)
            float hTarget = Mathf.Max(1.0f, Mathf.Max(targetPosition.y, targetAltitudeAboveTerrain));

            // Distance to radar's own tangent horizon point (meters)
            float radarHorizonDist = Mathf.Sqrt(TwoEffectiveRadius * hRadar);

            // Distance to target's horizon point (meters)
            float targetHorizonDist = Mathf.Sqrt(TwoEffectiveRadius * hTarget);

            // Total maximum LOS distance over Earth curvature bulge (meters)
            float maxLOSDistance = radarHorizonDist + targetHorizonDist;

            // Target is beyond the combined horizon line of sight
            if (horizDistMeters > maxLOSDistance)
            {
                return true;
            }

            // Intermediate check: distance beyond the radar's tangent horizon
            if (horizDistMeters > radarHorizonDist)
            {
                float distBeyondRadarHorizon = horizDistMeters - radarHorizonDist;
                float reqAltitude = (distBeyondRadarHorizon * distBeyondRadarHorizon) / TwoEffectiveRadius;

                if (hTarget < reqAltitude)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
