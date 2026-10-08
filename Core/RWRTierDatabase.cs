using System;
using UnityEngine;

namespace RadioWars.Core
{
    public enum RwrTier
    {
        Tier1_Basic,      // CI-22 Cricket, SAH-46 Chicane (analog 4-quadrant, narrow elevation, poor Ku sensitivity)
        Tier2_Standard,   // FS-12 Revoker, T/A-30 Compass, EW-1 Medusa, VL-49, AB-4 (digital threat library, standard Ku, TADIL datalink)
        Tier3_Advanced    // FS-20 Vortex, KR-67 Ifrit, SFB-81 Darkreach (5th-gen spherical array, zero blind cone, fusion datalink)
    }

    public enum DatalinkTier
    {
        Basic,     // Tactical datalink: receives allied missile & threat vectors (enabled on Tier 1 and Tier 2)
        Advanced   // High-speed Sensor Fusion: instant, precision track correlation with allied network
    }

    public struct RwrCapabilities
    {
        public RwrTier Tier;
        public DatalinkTier Datalink;
        public float AzimuthJitterDegrees;
        public float ElevationMinDeg;      // Lower aperture limit (negative degrees, e.g. -25°)
        public float ElevationMaxDeg;      // Upper aperture limit (positive degrees, e.g. +35°, zenith blind cone above)
        public float BankMaskingAngleDeg;  // Attitude roll angle beyond which wings/fuselage mask opposing antennas
        public bool SupportsKuBand;        // True if full-range Ku-band active missile seeker receiver is equipped
        public float KuBandMaxDetectRange; // Max range to detect high-frequency active missile seeker (meters)
        public bool CanClassifyThreats;    // True if digital threat library identifies AIR, SAM, NAV, ACT M
        public bool HasHUDNotchGuidance;   // True if HUD computer calculates clutter notch gate
    }

    public static class RWRTierDatabase
    {
        public static RwrCapabilities GetCapabilities(string aircraftName)
        {
            RwrCapabilities caps = new RwrCapabilities();

            if (string.IsNullOrEmpty(aircraftName))
            {
                return GetTier2Capabilities();
            }

            string name = aircraftName.ToLowerInvariant();

            if (name.Contains("cricket") || name.Contains("ci-22") || 
                name.Contains("chicane") || name.Contains("sah-46") ||
                name.Contains("ibis") || name.Contains("uh-80") || name.Contains("uh-90") ||
                name.Contains("utilityhelo"))
            {
                // Tier 1: Basic / Primitive Light Platforms & Helicopters
                // User requirement: Datalink IS enabled for Cricket/Chicane so allied team helps them!
                caps.Tier = RwrTier.Tier1_Basic;
                caps.Datalink = DatalinkTier.Basic;
                caps.AzimuthJitterDegrees = 18.0f; // High measurement jitter on analog 4-quadrant receiver
                caps.ElevationMinDeg = -25.0f;     // Belly blind cone below -25°
                caps.ElevationMaxDeg = 35.0f;      // Zenith blind cone above +35° (lofting diving missiles sneak in!)
                caps.BankMaskingAngleDeg = 60.0f;  // Banking hard past 60° shadows antennas on the high wing
                caps.SupportsKuBand = false;       // Primitive receiver: deaf to Ku-band active seekers at range
                caps.KuBandMaxDetectRange = 3500.0f; // Only detects Ku active seeker at point-blank emergency range (< 3.5 km)
                caps.CanClassifyThreats = false;   // Generic warning only (no threat type discrimination)
                caps.HasHUDNotchGuidance = false;  // No HUD notch cheat cue; pilot must use compass instruments
                return caps;
            }

            if (name.Contains("vortex") || name.Contains("fs-20") || name.Contains("smallfighter") ||
                name.Contains("ifrit") || name.Contains("kr-67") || name.Contains("multirole1") ||
                name.Contains("darkreach") || name.Contains("sfb-81") || name.Contains("sfb") ||
                name.Contains("eclipse") || name.Contains("fs-41") || name.Contains("interceptor1") ||
                name.Contains("raptor") || name.Contains("f-22") || name.Contains("kingraptor") ||
                name.Contains("ufo"))
            {
                // Tier 3: Advanced 5th-Gen / Next-Gen Air Superiority & Stealth
                caps.Tier = RwrTier.Tier3_Advanced;
                caps.Datalink = DatalinkTier.Advanced;
                caps.AzimuthJitterDegrees = 1.0f;  // Phase interferometry precision
                caps.ElevationMinDeg = -85.0f;     // Full spherical coverage (conformal distributed antennas)
                caps.ElevationMaxDeg = 85.0f;
                caps.BankMaskingAngleDeg = 90.0f;  // No bank shadowing
                caps.SupportsKuBand = true;
                caps.KuBandMaxDetectRange = 40000.0f;
                caps.CanClassifyThreats = true;
                caps.HasHUDNotchGuidance = true;   // Advanced 5th-gen EW computer calculates notch status
                return caps;
            }

            // Tier 2: Standard 4th-Gen Conventional Platforms (Revoker, Compass, Vagrant, Medusa, Tarantula, Brawler, Alkyon, Shrike, Agni, Chimera, etc.)
            return GetTier2Capabilities();
        }

        public static RwrCapabilities GetTier2Capabilities()
        {
            RwrCapabilities caps = new RwrCapabilities();
            caps.Tier = RwrTier.Tier2_Standard;
            caps.Datalink = DatalinkTier.Basic;
            caps.AzimuthJitterDegrees = 5.0f;  // Standard digital RWR
            caps.ElevationMinDeg = -40.0f;     // Standard elevation coverage
            caps.ElevationMaxDeg = 52.0f;      // Lofting diving missiles above 52° fall into zenith blind cone
            caps.BankMaskingAngleDeg = 75.0f;
            caps.SupportsKuBand = true;        // Full Ku-band active missile seeker receiver
            caps.KuBandMaxDetectRange = 16000.0f;
            caps.CanClassifyThreats = true;    // Threat classification: AIR, SAM, NAV, ACT M
            caps.HasHUDNotchGuidance = false;  // Approximate beaming guidance only, no guaranteed notch cheat
            return caps;
        }
    }

    /// <summary>
    /// Backward-compatibility alias for RWRTierDatabase.
    /// </summary>
    public static class RwrTierDatabase
    {
        public static RwrCapabilities GetCapabilities(string aircraftName) { return RWRTierDatabase.GetCapabilities(aircraftName); }
        public static RwrCapabilities GetTier2Capabilities() { return RWRTierDatabase.GetTier2Capabilities(); }
    }
}
