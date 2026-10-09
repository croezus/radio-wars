using BepInEx.Configuration;
using UnityEngine;

namespace RadioWars.Config
{
    public static class RadioWarsConfig
    {
        // 00. Testing & Diagnostics
        public enum ConfigurationPreset
        {
            SidnensPreset,
            Default,
            Custom
        }

        public static ConfigEntry<ConfigurationPreset> GameplayPreset;
        public static ConfigEntry<bool> ModEnabled;
        public static ConfigEntry<KeyCode> ToggleModKey;
        public static ConfigEntry<bool> ShowDebugHUD;
        public static ConfigEntry<KeyCode> ToggleHUDKey;
        public static ConfigEntry<bool> DrawDebugGizmos;
        public static ConfigEntry<KeyCode> ToggleGizmosKey;
        public static ConfigEntry<bool> DebugLogging;

        // 01. Doppler & Clutter Filtering
        public static ConfigEntry<float> NotchVelocityThreshold;
        public static ConfigEntry<bool> EnableMissileMemoryCoasting;
        public static ConfigEntry<float> MissileMemoryCoastSeconds;

        // 02. Radar Detection & Atmospheric Loss
        public static ConfigEntry<float> MinSNR_dB;
        public static ConfigEntry<bool> EarthCurvatureEnabled;
        public static ConfigEntry<float> CurvatureMaskAltitudeThreshold;
        public static ConfigEntry<bool> AtmosphericLossEnabled;

        // 03. Radar Cross Section (RCS) & Signatures
        public static ConfigEntry<bool> ExternalStoresRCSEnabled;
        public static ConfigEntry<bool> HERMEnabled;

        // 04. Electronic Warfare (EW) & Countermeasures
        public static ConfigEntry<float> JammerBurnThroughRatio;
        public static ConfigEntry<float> ECMNotchExpansionMultiplier;
        public static ConfigEntry<bool> HOJEnabled;

        // 05. Radar Warning Receiver (RWR) Systems
        public static ConfigEntry<bool> EnableRWRTierGrading;
        public static ConfigEntry<bool> EnableRwrTierGrading { get { return EnableRWRTierGrading; } }
        public static ConfigEntry<bool> EnableRWRBlindZones;
        public static ConfigEntry<bool> EnableRwrBlindZones { get { return EnableRWRBlindZones; } }
        public static ConfigEntry<float> RWRJitterDegrees;
        public static ConfigEntry<float> RWRStrobePersistenceSeconds;
        public static ConfigEntry<bool> ScaleConeWithRWRTier;
        public static ConfigEntry<bool> EnableHUDNotchIndicator;
        public static ConfigEntry<bool> DisableVanillaNotchAids;

        // 06. Tactical Map Display (Key M)
        public static ConfigEntry<bool> EnableRealisticRWRMapStrobes;
        public static ConfigEntry<bool> MapStrobesOriginateFromOwnship;
        public static ConfigEntry<bool> EnableRWRBearingCone;
        public static ConfigEntry<float> RWRSearchSectorAngle;
        public static ConfigEntry<float> RWRTrackSectorAngle;
        public static ConfigEntry<float> RWRMissileSectorAngle;
        public static ConfigEntry<float> RWRThreatDistanceRatio;
        public static ConfigEntry<float> RWRUncertaintyMultiplier;
        public static ConfigEntry<float> RWRStrobeNarrowAngle;
        public static ConfigEntry<float> RWRUncertaintyCircleScale;
        public static ConfigEntry<float> RWRUncertaintyDistanceScaleMax;
        public static ConfigEntry<float> RWRSearchConeOpacity;
        public static ConfigEntry<float> RWRTrackConeOpacity;
        public static ConfigEntry<float> RWRMissileConeOpacity;
        public static ConfigEntry<float> RWRUncertaintyZoneOpacity;

        // 07. Cockpit Minimap Display
        public static ConfigEntry<bool> EnableMinimapRWRStrobes;
        public static ConfigEntry<float> MinimapThreatDistanceRatio;
        public static ConfigEntry<float> MinimapUncertaintyMultiplier;

        // 08. Cockpit Dashboard & TacScreen
        public enum TacScreenPreset
        {
            AdvancedCanvas,  // Custom high-contrast vector canvas with leader lines & calibrated airspeed
            NativeEnhanced   // Vanilla MFD prefabs and symbology enhanced with Radio Wars threat coloring & physical filters
        }

        public static ConfigEntry<TacScreenPreset> TacScreenDisplayPreset;
        public static ConfigEntry<bool> EnableCockpitRWRDisplay;
        public static ConfigEntry<string> CockpitRWRMode;
        public static ConfigEntry<bool> EnableTacScreenRWRStrobes;
        public static ConfigEntry<float> CockpitRWRSearchSectorAngle;
        public static ConfigEntry<float> CockpitRWRTrackSectorAngle;
        public static ConfigEntry<float> CockpitRWRMissileSectorAngle;
        public static ConfigEntry<float> CockpitRWRThreatDistanceRatio;
        public static ConfigEntry<float> CockpitRWRUncertaintyMultiplier;
        public static ConfigEntry<bool> EnableAdvancedCockpitRadarUI;
        public static ConfigEntry<string> RadarDisplayUnitSystem;
        public static ConfigEntry<float> RadarVelocityVectorScale;

        // 09. Allied Tactical Datalink
        public static ConfigEntry<bool> EnableAlliedDatalink;
        public static ConfigEntry<bool> EnableDatalinkVisualStrobes;
        public static ConfigEntry<float> RWRDatalinkConeOpacity;
        public static ConfigEntry<bool> EnableSilentARHMidcourse;
        public static ConfigEntry<bool> DrawDatalinkVisualizer;
        public static ConfigEntry<KeyCode> ToggleDatalinkVisualizerKey;
        public static ConfigEntry<bool> ShowDatalinkDiagnosticPanel;
        public static ConfigEntry<bool> ShowDatalinkMeshLines;

        // 10. Target Tracking & Memory
        public static ConfigEntry<bool> EnableRWRTriangulationSystem;
        public static ConfigEntry<bool> EnableRwrTriangulationSystem { get { return EnableRWRTriangulationSystem; } }
        public static ConfigEntry<float> RWRTriangulationErrorFactor;
        public static ConfigEntry<bool> HideUntriangulatedRadarIcons;
        public static ConfigEntry<float> VisualRadarIdentificationRangeMeters;
        public static ConfigEntry<float> TargetMemoryDurationSeconds;
        public static ConfigEntry<float> RadarTrackingRefineTimeSeconds;
        public static ConfigEntry<float> RadarTrackingDecayTimeSeconds;

        // 11. Flight HUD Missile Trajectory & ETA Tracking
        public enum MissileTrajectoryMode
        {
            TrailOnly,       // Broken polyline trail behind missile
            TrailAndLead,    // Broken trail behind missile + forward lead vector to target
            LeadOnly         // Straight vector from missile to target
        }

        public static ConfigEntry<bool> EnableMissileTrajectoryHUD;
        public static ConfigEntry<MissileTrajectoryMode> TrajectoryDisplayMode;
        public static ConfigEntry<float> MissileTrajectoryUpdateRate;
        public static ConfigEntry<int> MissileTrajectoryMaxSegments;
        public static ConfigEntry<bool> EnableMissileETADisplay;
        public static ConfigEntry<float> MissileTrajectoryLineWidth;

        // 12. Performance Optimization
        public static ConfigEntry<int> TimeSliceBuckets;

        // 13. Track Uncertainty & Seeker Dispersion
        public static ConfigEntry<bool> EnableTrackUncertaintyDispersion;
        public static ConfigEntry<bool> EnableHMDTrackQualityColoring;
        public static ConfigEntry<float> HMDReconnaissanceQualityThreshold;
        public static ConfigEntry<float> MapReconnaissanceQualityThreshold;
        public static ConfigEntry<float> VisualReconnaissanceBoost;
        public static ConfigEntry<float> VisualReconnaissanceCooldownSeconds;
        public static ConfigEntry<float> SensorIntelligenceTickIntervalSeconds;
        public static ConfigEntry<float> RadarSweepBaseContribution;
        public static ConfigEntry<float> RadarDwellContributionRate;
        public static ConfigEntry<float> RWRBaseContribution;
        public static ConfigEntry<float> TriangulationBaselineBoost;
        public static ConfigEntry<float> TrackUncertaintyMaxDispersionMeters;
        public static ConfigEntry<float> TrackUncertaintyMinDispersionMeters;
        public static ConfigEntry<float> TrackUncertaintyEdgeBiasMinFraction;
        public static ConfigEntry<float> TrackUncertaintyEdgeBiasExponent;
        public static ConfigEntry<float> ARHTerminalActivationDistanceMeters;
        public static ConfigEntry<float> ARADTerminalActivationDistanceMeters;
        public static ConfigEntry<float> MissileTerminalActivationDistanceMeters;
        public static ConfigEntry<float> TrackUncertaintyRangeWeightingMin;
        public static ConfigEntry<float> TrackUncertaintyRangeWeightingMax;
        public static ConfigEntry<float> TrackUncertaintyMemoryTimeoutSeconds;
        public static ConfigEntry<bool> ShowMissileAimpointOnMap;
        public static ConfigEntry<bool> ShowPlayerMissilesOnlyOnMap;
        public static ConfigEntry<bool> AIEvaluateMissileLaunchDoctrine;
        public static ConfigEntry<float> AIMinimumTacticalLaunchQuality;
        public static ConfigEntry<float> AIMinimumLongRangeLaunchQuality;
        public static ConfigEntry<float> AIMinimumStrategicLaunchQuality;
        public static ConfigEntry<float> AIMinimumBallisticLaunchQuality;
        public static ConfigEntry<bool> AIEnforceStandoffSurvival;
        public static ConfigEntry<float> AIMedusaSafeStandoffDistanceKm;
        public static ConfigEntry<bool> SARHEnableLoftTerrainClearance;
        public static ConfigEntry<bool> MissileEnforceSelfDestructWatchdog;
        public static ConfigEntry<float> MissileMaxBattlefieldRadiusKm;

        public static void Initialize(ConfigFile config)
        {
            // 00. Testing & Diagnostics
            GameplayPreset = config.Bind(
                "00. Testing & Diagnostics",
                "ConfigurationPreset",
                ConfigurationPreset.SidnensPreset,
                "Master configuration preset: 'SidnensPreset' (curated benchmark balance tuned by Sidnen: refined RWR scale, 5km visual ID, 100m curvature mask, telemetry HUD and map missile visualizer off), 'Default' (original baseline development defaults), or 'Custom' (manual configuration; preserves all custom values below without preset overrides)."
            );

            GameplayPreset.SettingChanged += (sender, args) =>
            {
                if (GameplayPreset.Value == ConfigurationPreset.SidnensPreset)
                {
                    ApplySidnenPreset();
                }
                else if (GameplayPreset.Value == ConfigurationPreset.Default)
                {
                    ApplyDefaultPreset();
                }
            };

            ModEnabled = config.Bind(
                "00. Testing & Diagnostics",
                "ModEnabled",
                true,
                "Global A/B toggle. When TRUE, custom physical radar, clutter notch, 3D aspect RCS, and EW models execute. When FALSE, original vanilla game methods execute."
            );

            ToggleModKey = config.Bind(
                "00. Testing & Diagnostics",
                "ToggleModKey",
                KeyCode.F10,
                "Keyboard hotkey to toggle between Mod Physics and Vanilla Radar instantly in-game."
            );

            ShowDebugHUD = config.Bind(
                "00. Testing & Diagnostics",
                "ShowDebugHUD",
                false,
                "Show on-screen real-time radar telemetry panel (Distance, Echo Power, SNR, Dynamic RCS, Notch status, Jamming, Burn-Through)."
            );

            ToggleHUDKey = config.Bind(
                "00. Testing & Diagnostics",
                "ToggleHUDKey",
                KeyCode.F9,
                "Keyboard hotkey to toggle the telemetry overlay panel."
            );

            DrawDebugGizmos = config.Bind(
                "00. Testing & Diagnostics",
                "DrawDebugGizmos",
                false,
                "Draw in-game radar line-of-sight rays and Doppler velocity vectors (Green = Clear, Yellow = Clutter-degraded, Red = Notched / Masked)."
            );

            ToggleGizmosKey = config.Bind(
                "00. Testing & Diagnostics",
                "ToggleGizmosKey",
                KeyCode.F11,
                "Keyboard hotkey to toggle in-game raycast debug lines."
            );

            DebugLogging = config.Bind(
                "00. Testing & Diagnostics",
                "DebugLogging",
                false,
                "Enable verbose physics and tracking debug logs in BepInEx console."
            );

            // 01. Doppler & Clutter Filtering
            NotchVelocityThreshold = config.Bind(
                "01. Doppler & Clutter Filtering",
                "NotchVelocityThreshold_mps",
                20.0f,
                "Pulse-Doppler clutter notch filter half-width in meters per second. Targets with relative radial ground speed below this threshold fall into the notch."
            );

            EnableMissileMemoryCoasting = config.Bind(
                "01. Doppler & Clutter Filtering",
                "EnableMissileMemoryCoasting",
                true,
                "Missile guidance computers enter Memory Coasting mode when target enters clutter notch, continuing dead-reckoning pursuit for a short duration instead of dropping lock instantly."
            );

            MissileMemoryCoastSeconds = config.Bind(
                "01. Doppler & Clutter Filtering",
                "MissileMemoryCoastSeconds",
                2.5f,
                "Duration in seconds the missile guidance computer holds memory coasting lead pursuit while target is beamed in the clutter notch."
            );

            // 02. Radar Detection & Atmospheric Loss
            MinSNR_dB = config.Bind(
                "02. Radar Detection & Atmospheric Loss",
                "MinSNR_dB",
                10.0f,
                "Minimum required Signal-to-Noise Ratio (SNR) in decibels for valid radar detection and tracking."
            );

            EarthCurvatureEnabled = config.Bind(
                "02. Radar Detection & Atmospheric Loss",
                "EarthCurvatureEnabled",
                true,
                "Simulate 4/3 effective Earth radius atmospheric refraction over the flat game map, creating realistic geometric radar horizon cutoff."
            );

            CurvatureMaskAltitudeThreshold = config.Bind(
                "02. Radar Detection & Atmospheric Loss",
                "CurvatureMaskAltitudeThreshold_m",
                100.0f,
                "Minimum altitude above terrain in meters for Earth curvature 4/3 refraction masking to apply. Targets flying above this altitude (>= 100m) are never occluded by curvature."
            );

            AtmosphericLossEnabled = config.Bind(
                "02. Radar Detection & Atmospheric Loss",
                "AtmosphericLossEnabled",
                true,
                "Apply two-way frequency-dependent atmospheric absorption loss in decibels per kilometer based on radar band."
            );

            // 03. Radar Cross Section (RCS) & Signatures
            ExternalStoresRCSEnabled = config.Bind(
                "03. Radar Cross Section (RCS) & Signatures",
                "ExternalStoresRCSEnabled",
                true,
                "Add physical RCS penalties for unshielded external weapons, pylons, and external fuel tanks."
            );

            HERMEnabled = config.Bind(
                "03. Radar Cross Section (RCS) & Signatures",
                "HelicopterRotorModulationEnabled",
                true,
                "Simulate Helicopter Rotor Modulation (HERM) micro-Doppler spikes, making hovering helicopters detectable regardless of hull beaming status."
            );

            // 04. Electronic Warfare (EW) & Countermeasures
            JammerBurnThroughRatio = config.Bind(
                "04. Electronic Warfare (EW) & Countermeasures",
                "JammerBurnThroughRatio",
                1.0f,
                "Multiplier for noise jamming burn-through sensitivity. Higher values make radars burn through jamming at greater ranges."
            );

            ECMNotchExpansionMultiplier = config.Bind(
                "04. Electronic Warfare (EW) & Countermeasures",
                "ECMNotchExpansionMultiplier",
                1.5f,
                "Multiplier for Doppler clutter notch corridor expansion when ECM is active. Simulates DRFM VGPO and Doppler noise smearing."
            );

            HOJEnabled = config.Bind(
                "04. Electronic Warfare (EW) & Countermeasures",
                "HOJEnabled",
                true,
                "Enable Home-On-Jam (HOJ) seeker guidance. Active/Semi-Active radar missiles home in on ECM noise strobes when target radiates outside look-down clutter notch."
            );

            // 05. Radar Warning Receiver (RWR) Systems
            EnableRWRTierGrading = config.Bind(
                "05. Radar Warning Receiver (RWR) Systems",
                "EnableRWRTierGrading",
                true,
                "Grade RWR quality across aircraft tiers (Tier 1 primitive analog without Ku-band sensitivity vs Tier 2 standard digital vs Tier 3 5th-gen spherical array)."
            );

            EnableRWRBlindZones = config.Bind(
                "05. Radar Warning Receiver (RWR) Systems",
                "EnableRWRBlindZones",
                true,
                "Simulate RWR antenna elevation blind cones (high-angle lofting missiles in dive) and fuselage banking masking."
            );

            RWRJitterDegrees = config.Bind(
                "05. Radar Warning Receiver (RWR) Systems",
                "RWRJitterDegrees",
                3.0f,
                "Realistic angular error cone (degrees) for RWR antenna Angle-of-Arrival (AoA) bearing measurement."
            );

            RWRStrobePersistenceSeconds = config.Bind(
                "05. Radar Warning Receiver (RWR) Systems",
                "RWRStrobePersistenceSeconds",
                3.0f,
                "Decay persistence in seconds for RWR threat strobes before fading when radar sweeps cease."
            );

            ScaleConeWithRWRTier = config.Bind(
                "05. Radar Warning Receiver (RWR) Systems",
                "ScaleConeWithRWRTier",
                true,
                "Dynamically scale threat cone aperture based on aircraft RWR Tier (Tier 1 = 1.25x wider, Tier 2 = 1.0x standard, Tier 3 = 0.7x focused)."
            );

            EnableHUDNotchIndicator = config.Bind(
                "05. Radar Warning Receiver (RWR) Systems",
                "EnableHUDNotchIndicator",
                true,
                "Render tactical EW and notch status cues ([ NOTCH ], [ ECM NOTCH ], [ HOJ RISK ], [ BURN-THROUGH ]) on the upper Flight HUD compass panel."
            );

            DisableVanillaNotchAids = config.Bind(
                "05. Radar Warning Receiver (RWR) Systems",
                "DisableVanillaNotchAids",
                true,
                "Disable the vanilla arcade notch flight director box on the HUD and dashed notch line on the map/minimap, requiring realistic instrument-based beaming."
            );

            // 06. Tactical Map Display (Key M)
            EnableRealisticRWRMapStrobes = config.Bind(
                "06. Tactical Map Display (Key M)",
                "EnableRealisticRWRMapStrobes",
                true,
                "Display realistic RWR directional bearing strobes on the fullscreen tactical map (key M) originating from ownship, without revealing enemy coordinates."
            );

            MapStrobesOriginateFromOwnship = config.Bind(
                "06. Tactical Map Display (Key M)",
                "MapStrobesOriginateFromOwnship",
                true,
                "Anchor tactical map threat strobes at ownship radiating outward along the line of bearing, rather than originating directly at the hidden emitter's position."
            );

            EnableRWRBearingCone = config.Bind(
                "06. Tactical Map Display (Key M)",
                "EnableRWRBearingCone",
                true,
                "Render RWR threat strobes as angular bearing uncertainty cones (wedges) instead of infinitely thin pencil lines, preventing artificial sniper triangulation."
            );

            RWRSearchSectorAngle = config.Bind(
                "06. Tactical Map Display (Key M)",
                "RWRSearchSectorAngle",
                20.0f,
                "Fullscreen Tactical Map: Total angular sector aperture (in degrees) for Search radar threat cones (yellow). Enter full angle directly (e.g. 20.0 gives a 20-degree sector)."
            );

            RWRTrackSectorAngle = config.Bind(
                "06. Tactical Map Display (Key M)",
                "RWRTrackSectorAngle",
                12.0f,
                "Fullscreen Tactical Map: Total angular sector aperture (in degrees) for Track / Lock radar threat cones (orange). Enter full angle directly."
            );

            RWRMissileSectorAngle = config.Bind(
                "06. Tactical Map Display (Key M)",
                "RWRMissileSectorAngle",
                7.0f,
                "Fullscreen Tactical Map: Total angular sector aperture (in degrees) for Active Terminal Pitbull Missile threat cones (red). Enter full angle directly."
            );

            RWRThreatDistanceRatio = config.Bind(
                "06. Tactical Map Display (Key M)",
                "RWRThreatDistanceRatio",
                0.85f,
                "Fullscreen Tactical Map: Proportion of physical distance from player aircraft to the radar threat emitter that the strobe cone extends (0.85 = 85% of distance to emitter, leaving a 15% distance gap before the target)."
            );

            RWRUncertaintyMultiplier = config.Bind(
                "06. Tactical Map Display (Key M)",
                "RWRUncertaintyMultiplier",
                1.0f,
                "Fullscreen Tactical Map: Angular uncertainty multiplier for threat cones. Set to 1.0 for standard realism, increase (1.5 - 2.5) for hardcore difficulty, or decrease (0.5) for arcade precision."
            );

            RWRStrobeNarrowAngle = config.Bind(
                "06. Tactical Map Display (Key M)",
                "RWRStrobeNarrowAngle",
                4.0f,
                "Full angular aperture in degrees for the narrow ESM wedge strobe connecting ownship to the threat area (default: 4.0 degrees)."
            );

            RWRUncertaintyCircleScale = config.Bind(
                "06. Tactical Map Display (Key M)",
                "RWRUncertaintyCircleScale",
                0.30f,
                "Multiplier for the diameter of untriangulated RWR uncertainty circles on the fullscreen tactical map."
            );

            RWRUncertaintyDistanceScaleMax = config.Bind(
                "06. Tactical Map Display (Key M)",
                "RWRUncertaintyDistanceScaleMax",
                0.80f,
                "Maximum distance-based expansion multiplier for the untriangulated RWR uncertainty circle at long standoff range (default: 0.8x). Scales smoothly from 1.0x at visual identification distance (~10km) up to this multiplier at long standoff distance (60km)."
            );

            RWRSearchConeOpacity = config.Bind(
                "06. Tactical Map Display (Key M)",
                "RWRSearchConeOpacity",
                0.10f,
                "Opacity / alpha transparency of yellow search (surveillance) RWR threat cones on the tactical map (0.01 to 1.0, default: 0.10 = 10%)."
            );

            RWRTrackConeOpacity = config.Bind(
                "06. Tactical Map Display (Key M)",
                "RWRTrackConeOpacity",
                0.40f,
                "Opacity / alpha transparency of orange track (target acquisition) RWR threat cones on the tactical map (0.01 to 1.0, default: 0.40 = 40%)."
            );

            RWRMissileConeOpacity = config.Bind(
                "06. Tactical Map Display (Key M)",
                "RWRMissileConeOpacity",
                0.75f,
                "Opacity / alpha transparency of red missile guidance RWR threat cones on the tactical map (0.01 to 1.0, default: 0.75 = 75%)."
            );

            RWRUncertaintyZoneOpacity = config.Bind(
                "06. Tactical Map Display (Key M)",
                "RWRUncertaintyZoneOpacity",
                0.10f,
                "Opacity / alpha transparency of yellow ambiguity zones / uncertainty circles denoting approximate radar locations on the tactical map (0.01 to 1.0, default: 0.10 = 10%)."
            );

            // 07. Cockpit Minimap Display
            EnableMinimapRWRStrobes = config.Bind(
                "07. Cockpit Minimap Display",
                "EnableMinimapRWRStrobes",
                true,
                "Display RWR directional threat strobes on the corner / cockpit minimap."
            );

            MinimapThreatDistanceRatio = config.Bind(
                "07. Cockpit Minimap Display",
                "MinimapThreatDistanceRatio",
                0.85f,
                "Corner / Cockpit Minimap: Proportion of physical distance to the threat emitter that the strobe cone extends (default 0.85 = 85%)."
            );

            MinimapUncertaintyMultiplier = config.Bind(
                "07. Cockpit Minimap Display",
                "MinimapUncertaintyMultiplier",
                1.0f,
                "Corner / Cockpit Minimap: Angular uncertainty multiplier for threat cones on the minimap display."
            );

            // 08. Cockpit Dashboard & TacScreen
            EnableCockpitRWRDisplay = config.Bind(
                "08. Cockpit Dashboard & TacScreen",
                "EnableCockpitRWRDisplay",
                true,
                "Render a dedicated tactical RWR annunciator and azimuth dial on the aircraft cockpit dashboard/MFD panels."
            );

            CockpitRWRMode = config.Bind(
                "08. Cockpit Dashboard & TacScreen",
                "CockpitRWRMode",
                "Combined",
                "Display mode for cockpit RWR widget: 'Combined' (Annunciator + Azimuth Dial), 'Annunciator' (Status text), or 'AzimuthDial' (Circular rose)."
            );

            EnableTacScreenRWRStrobes = config.Bind(
                "08. Cockpit Dashboard & TacScreen",
                "EnableTacScreenRWRStrobes",
                true,
                "Draw directional RWR threat strobes radiating from the center of the cockpit TacScreen tactical scope."
            );

            CockpitRWRSearchSectorAngle = config.Bind(
                "08. Cockpit Dashboard & TacScreen",
                "CockpitRWRSearchSectorAngle",
                20.0f,
                "Cockpit Radar Scope (TacScreen): Total angular sector aperture (in degrees) for Search radar threat cones (yellow). Enter full angle directly."
            );

            CockpitRWRTrackSectorAngle = config.Bind(
                "08. Cockpit Dashboard & TacScreen",
                "CockpitRWRTrackSectorAngle",
                12.0f,
                "Cockpit Radar Scope (TacScreen): Total angular sector aperture (in degrees) for Track / Lock radar threat cones (orange). Enter full angle directly."
            );

            CockpitRWRMissileSectorAngle = config.Bind(
                "08. Cockpit Dashboard & TacScreen",
                "CockpitRWRMissileSectorAngle",
                7.0f,
                "Cockpit Radar Scope (TacScreen): Total angular sector aperture (in degrees) for Active Terminal Pitbull Missile threat cones (red). Enter full angle directly."
            );

            CockpitRWRThreatDistanceRatio = config.Bind(
                "08. Cockpit Dashboard & TacScreen",
                "CockpitRWRThreatDistanceRatio",
                0.85f,
                "Cockpit Radar Scope (TacScreen): Proportion of physical distance to the threat emitter that the strobe cone extends (default 0.85 = 85%, clamped to screen radius)."
            );

            CockpitRWRUncertaintyMultiplier = config.Bind(
                "08. Cockpit Dashboard & TacScreen",
                "CockpitRWRUncertaintyMultiplier",
                1.0f,
                "Cockpit Radar Scope (TacScreen): Angular uncertainty multiplier for threat cones specifically on the cockpit radar display."
            );

            TacScreenDisplayPreset = config.Bind(
                "08. Cockpit Dashboard & TacScreen",
                "TacScreenDisplayPreset",
                TacScreenPreset.AdvancedCanvas,
                "Display preset for the cockpit TacScreen MFD: 'AdvancedCanvas' (High-contrast vector canvas with velocity leader lines and calibrated airspeed data blocks) or 'NativeEnhanced' (Vanilla MFD prefabs and symbology enhanced with Radio Wars physical threat coloring and clutter filtering)."
            );

            EnableAdvancedCockpitRadarUI = config.Bind(
                "08. Cockpit Dashboard & TacScreen",
                "EnableAdvancedCockpitRadarUI",
                true,
                "Render target velocity vector leader lines and calibrated airspeed data on cockpit tactical radar displays (TacScreen)."
            );

            RadarDisplayUnitSystem = config.Bind(
                "08. Cockpit Dashboard & TacScreen",
                "RadarDisplayUnitSystem",
                "Metric",
                "Unit system for radar target airspeed data blocks: 'Metric' (km/h) or 'Aviation' (Knots)."
            );

            RadarVelocityVectorScale = config.Bind(
                "08. Cockpit Dashboard & TacScreen",
                "RadarVelocityVectorScale",
                1.0f,
                "Multiplier for target velocity vector leader line length on radar displays."
            );

            // 09. Allied Tactical Datalink
            EnableAlliedDatalink = config.Bind(
                "09. Allied Tactical Datalink",
                "EnableAlliedDatalink",
                true,
                "Enable cooperative allied datalink air defense network. Friendly AWACS, SAM radars, and fighters track threats and share tracks across friendly aircraft."
            );

            EnableDatalinkVisualStrobes = config.Bind(
                "09. Allied Tactical Datalink",
                "EnableDatalinkVisualStrobes",
                false,
                "Display visual cyan datalink threat rays/strobes on the cockpit radar scope (TacScreen) and map (DynamicMap). Default: false (information exchange and HUD alerts remain fully functional without visual clutter)."
            );

            RWRDatalinkConeOpacity = config.Bind(
                "09. Allied Tactical Datalink",
                "RWRDatalinkConeOpacity",
                0.35f,
                "Opacity / alpha transparency of cyan allied datalink threat cones on tactical displays (0.01 to 1.0, default: 0.35 = 35%)."
            );

            EnableSilentARHMidcourse = config.Bind(
                "09. Allied Tactical Datalink",
                "EnableSilentARHMidcourse",
                true,
                "Model silent ARH missile midcourse inertial/datalink flight. Active seeker transmitter is OFF until terminal Pitbull range, so target RWR detects no active missile warning until terminal phase."
            );

            DrawDatalinkVisualizer = config.Bind(
                "09. Allied Tactical Datalink",
                "DrawDatalinkVisualizer",
                false,
                "Draw in-game 3D Tactical Datalink mesh lines, shared radar/visual tracks, and relayed threat alerts (Cyan = Network Mesh, Green = Radar Track Share, Yellow = Visual Recon, Red = Relayed Missile Alert)."
            );

            ToggleDatalinkVisualizerKey = config.Bind(
                "09. Allied Tactical Datalink",
                "ToggleDatalinkVisualizerKey",
                KeyCode.F8,
                "Keyboard hotkey to toggle in-game 3D Tactical Datalink gizmo visualizer and telemetry overlay."
            );

            ShowDatalinkDiagnosticPanel = config.Bind(
                "09. Allied Tactical Datalink",
                "ShowDatalinkDiagnosticPanel",
                true,
                "Show on-screen Tactical Datalink diagnostic telemetry panel when F8 visualizer is active."
            );

            ShowDatalinkMeshLines = config.Bind(
                "09. Allied Tactical Datalink",
                "ShowDatalinkMeshLines",
                true,
                "Render 3D mesh lines interconnecting friendly allied units in the datalink network."
            );

            // 10. Target Tracking & Memory
            EnableRWRTriangulationSystem = config.Bind(
                "10. Target Tracking & Memory",
                "EnableRWRTriangulationSystem",
                true,
                "Enable ESM triangulation and ambiguity zones: replaces wide sector cones with sleek narrow wedge strobes, hides exact radar icons until triangulated or visually identified, renders uncertainty circles, and offsets triangulated positions proportionally to distance."
            );

            RWRTriangulationErrorFactor = config.Bind(
                "10. Target Tracking & Memory",
                "RWRTriangulationErrorFactor",
                0.035f,
                "Ratio of spatial positioning error to physical distance for triangulated radar emitters (0.035 = 3.5% of distance: ~350m at 10km, ~1.5km at 45km, ~3.5km at 100km)."
            );

            HideUntriangulatedRadarIcons = config.Bind(
                "10. Target Tracking & Memory",
                "HideUntriangulatedRadarIcons",
                true,
                "Suppress exact red/hostile vehicle and ship icons on the map/minimap and HUD for radar emitters until triangulated or visually acquired."
            );

            VisualRadarIdentificationRangeMeters = config.Bind(
                "10. Target Tracking & Memory",
                "VisualRadarIdentificationRangeMeters",
                5000.0f,
                "Direct visual/optical identification range in meters for enemy radar emitters (default: 5000m / 5.0km). Within this range, units are visually acquired: suppression is bypassed, exact coordinates and HUD markers are displayed without ESM triangulation delay."
            );

            TargetMemoryDurationSeconds = config.Bind(
                "10. Target Tracking & Memory",
                "TargetMemoryDurationSeconds",
                120.0f,
                "Duration in seconds for which previously detected targets are remembered at their last known position as outdated contacts ('?' sprite, no velocity/heading telemetry) on the tactical map and HUD after active contact is lost."
            );

            RadarTrackingRefineTimeSeconds = config.Bind(
                "10. Target Tracking & Memory",
                "RadarTrackingRefineTimeSeconds",
                3.0f,
                "Continuous active radar illumination time in seconds required to refine tracking accuracy from coarse initial ping to 100% precision."
            );

            RadarTrackingDecayTimeSeconds = config.Bind(
                "10. Target Tracking & Memory",
                "RadarTrackingDecayTimeSeconds",
                5.0f,
                "Time in seconds for tracking accuracy to decay back to passive ESM baseline after active radar illumination ceases."
            );

            // 11. Flight HUD Missile Trajectory & ETA Tracking
            EnableMissileTrajectoryHUD = config.Bind(
                "11. Flight HUD Missile Trajectory & ETA Tracking",
                "EnableMissileTrajectoryHUD",
                true,
                "Draw Flight HUD-styled green trajectory lines following player-launched munitions to their targets until impact."
            );

            TrajectoryDisplayMode = config.Bind(
                "11. Flight HUD Missile Trajectory & ETA Tracking",
                "TrajectoryDisplayMode",
                MissileTrajectoryMode.TrailOnly,
                "Display mode: 'TrailOnly' (broken polyline trailing behind missile), 'TrailAndLead' (broken trail + forward vector to target), or 'LeadOnly' (straight vector)."
            );

            MissileTrajectoryUpdateRate = config.Bind(
                "11. Flight HUD Missile Trajectory & ETA Tracking",
                "MissileTrajectoryUpdateRate",
                8.0f,
                "Tactical datalink telemetry update frequency in Hz (2.0 to 30.0). Simulates discrete telemetry packet updates."
            );

            MissileTrajectoryMaxSegments = config.Bind(
                "11. Flight HUD Missile Trajectory & ETA Tracking",
                "MissileTrajectoryMaxSegments",
                3,
                "Number of discrete trail segments trailing behind the missile (2 to 6). Default: 3."
            );

            EnableMissileETADisplay = config.Bind(
                "11. Flight HUD Missile Trajectory & ETA Tracking",
                "EnableMissileETADisplay",
                true,
                "Display dynamic countdown of estimated time of arrival (e.g. 'ETA 4.3s') beside flying munitions."
            );

            MissileTrajectoryLineWidth = config.Bind(
                "11. Flight HUD Missile Trajectory & ETA Tracking",
                "MissileTrajectoryLineWidth",
                2.5f,
                "Screen pixel thickness multiplier for the Flight HUD missile trajectory lines (pulses dynamically between 1.5px and 4.0px)."
            );

            // 12. Performance Optimization
            TimeSliceBuckets = config.Bind(
                "12. Performance Optimization",
                "TimeSliceBuckets",
                4,
                "Number of frame buckets across which radar sweeps and heavy calculations are distributed to ensure zero frame-time spikes."
            );

            // 13. Track Uncertainty & Seeker Dispersion
            EnableTrackUncertaintyDispersion = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "EnableTrackUncertaintyDispersion",
                true,
                "When TRUE, munition launch coordinates are displaced horizontally by track uncertainty (CEP). When FALSE, munitions receive perfect coordinates."
            );

            EnableHMDTrackQualityColoring = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "EnableHMDTrackQualityColoring",
                true,
                "When TRUE, HMD unit marker reticle color smoothly grades from Pale White (low intel/high miss risk) to Amber to Crimson Red (refined fire-control lock)."
            );

            HMDReconnaissanceQualityThreshold = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "HMDReconnaissanceQualityThreshold",
                0.30f,
                "Tracking quality threshold (0.0 to 1.0) required for hostile radar threats to appear on Helmet-Mounted Display (HMD) / CombatHUD (default: 0.30 = 30%). Suppressed when Q <= 0.30."
            );

            MapReconnaissanceQualityThreshold = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "MapReconnaissanceQualityThreshold",
                0.75f,
                "Tracking quality threshold (0.0 to 1.0) required for hostile radar threats to reveal pinpoint vehicle/ship icons on the tactical map and minimap (default: 0.75 = 75%). Below this threshold, only realistic RWR bearing strobes and ESM ambiguity circles are shown."
            );

            VisualReconnaissanceBoost = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "VisualReconnaissanceBoost",
                0.35f,
                "One-time intelligence quality boost (0.05 to 1.0, default: 0.35 = 35%) granted when a target is visually identified within naked-eye range (<2.5 km). Immediately reveals contacts on HMD and Map when exceeding 0.30 threshold."
            );

            VisualReconnaissanceCooldownSeconds = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "VisualReconnaissanceCooldownSeconds",
                180.0f,
                "Cooldown in seconds (default: 180s = 3 minutes) per target before another visual intelligence boost can be applied. Target remains actively tracked while under visual observation, but intelligence does not increase further without radar/RWR feeds."
            );

            SensorIntelligenceTickIntervalSeconds = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "SensorIntelligenceTickIntervalSeconds",
                1.0f,
                "Discrete rate-limiting interval in seconds (default: 1.0s) between intelligence progress ticks for radar sweeps and passive RWR strobes."
            );

            RadarSweepBaseContribution = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "RadarSweepBaseContribution",
                0.20f,
                "Base tracking quality increment per discrete sweep tick from ownship active radar (0.01 to 0.50, default: 0.20 = 20%). Allied datalink radars contribute 75% of this value."
            );

            RadarDwellContributionRate = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "RadarDwellContributionRate",
                0.30f,
                "Continuous tracking quality accumulation rate per second during sustained active radar dwell (0.05 to 1.0, default: 0.30/s)."
            );

            RWRBaseContribution = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "RWRBaseContribution",
                0.005f,
                "Base tracking quality increment per discrete tick for ownship passive RWR reception (0.005 to 0.20, default: 0.005 = 0.5%). Modified by distance weighting and RWR hardware tier."
            );

            TriangulationBaselineBoost = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "TriangulationBaselineBoost",
                0.25f,
                "Intelligence boost granted upon single-ship kinematic baseline triangulation completion (0.05 to 0.50, default: 0.25 = 25%). Multi-station datalink triangulation grants an additional +0.05."
            );

            TrackUncertaintyMaxDispersionMeters = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "TrackUncertaintyMaxDispersionMeters",
                0.0f,
                "Optional maximum horizontal dispersion ceiling in meters. When set to 0 (default: 0.0), dispersion is uncapped and determined naturally by geometry and sensor intelligence scale (bounded only by 90% slant range to prevent inversion). Set > 0 to enforce a rigid maximum ceiling (e.g. 5000m)."
            );

            TrackUncertaintyMinDispersionMeters = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "TrackUncertaintyMinDispersionMeters",
                15.0f,
                "Residual dispersion for 100% refined direct active radar lock in meters."
            );

            TrackUncertaintyEdgeBiasMinFraction = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "TrackUncertaintyEdgeBiasMinFraction",
                0.60f,
                "Minimum fraction of CEP radius for peripheral dispersion bias (0.0 to 0.9, default: 0.60 = 60%). Munition launch offsets disperse preferentially towards the outer perimeter."
            );

            TrackUncertaintyEdgeBiasExponent = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "TrackUncertaintyEdgeBiasExponent",
                0.40f,
                "Power curve exponent for peripheral edge dispersion bias (0.1 to 2.0, default: 0.40). Lower values heavily bias displacement towards the outer edge of the CEP circle."
            );

            ARHTerminalActivationDistanceMeters = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "ARHTerminalActivationDistanceMeters",
                10000.0f,
                "Terminal active radar homing (ARH) seeker pitbull activation distance in meters (default: 10000m / 10.0km). Active radar missiles (SAAM-38, MSAAM, Scythe) remain in silent midcourse datalink flight until reaching this distance from target, after which the seeker radar powers ON and sends active RF pings, providing realistic reaction and defensive notching time for the defender."
            );

            ARADTerminalActivationDistanceMeters = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "ARADTerminalActivationDistanceMeters",
                7000.0f,
                "Terminal anti-radiation (ARAD / ARMSeeker) passive RF seeker activation distance in meters (default: 7000m / 7.0km). Anti-radiation missiles (ARAD-116, ARAD-45) navigate via midcourse datalink/inertial guidance towards target coordinates until within 7 km, after which the passive RF receiver begins detecting enemy radar emissions and homes directly onto the radiating transmitter antenna."
            );

            MissileTerminalActivationDistanceMeters = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "MissileTerminalActivationDistanceMeters",
                2800.0f,
                "Terminal optical and guided bomb/shell seeker activation distance in meters (default: 2800m / 2.8km). Optical contrast seekers (AGM-48, AGM-68, PAB bombs, guided shells) remain in midcourse inertial/datalink guidance until reaching this distance from the displaced aimpoint."
            );

            TrackUncertaintyRangeWeightingMin = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "TrackUncertaintyRangeWeightingMin",
                5000.0f,
                "Distance in meters where sensor contributions receive maximum weighting (1.0x). Below 5 km, RWR and sensor detections contribute significant intelligence (default: 5000m / 5.0km)."
            );

            TrackUncertaintyRangeWeightingMax = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "TrackUncertaintyRangeWeightingMax",
                80000.0f,
                "Distance in meters where sensor contributions drop to minimum weighting (0.15x)."
            );

            TrackUncertaintyMemoryTimeoutSeconds = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "TrackUncertaintyMemoryTimeoutSeconds",
                120.0f,
                "Inactivity memory period in seconds after which target intelligence resets to 0."
            );

            ShowMissileAimpointOnMap = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "ShowMissileAimpointOnMap",
                false,
                "Show missile fly-to-waypoint guidance line, displaced aimpoint marker, CEP offset vector, and 2.8 km terminal seeker basket on tactical map (Key M). When enabled (checked), visualizes where unguided midcourse munitions steer due to track uncertainty."
            );

            ShowPlayerMissilesOnlyOnMap = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "ShowPlayerMissilesOnlyOnMap",
                true,
                "When true, only munitions launched by the player aircraft are displayed on the tactical map aimpoint visualizer. When false, all friendly faction missiles are displayed."
            );

            AIEvaluateMissileLaunchDoctrine = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "AIEvaluateMissileLaunchDoctrine",
                true,
                "When TRUE, AI aircraft, helicopters, and ground/naval missile turrets evaluate track quality Q and seeker basket envelopes before firing missiles, refusing to waste munitions at low tracking quality and preserving safe standoff distance."
            );

            AIMinimumTacticalLaunchQuality = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "AIMinimumTacticalLaunchQuality",
                0.40f,
                "Minimum required track quality Q (0.0 to 1.0, default: 0.40 = 40%) for AI to launch short and medium-range tactical missiles (ranges < 50 km: Scythe, ARAD-45, RAM-45, MMR-S3, AGM-48, bombs)."
            );

            AIMinimumLongRangeLaunchQuality = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "AIMinimumLongRangeLaunchQuality",
                0.50f,
                "Minimum required track quality Q (0.0 to 1.0, default: 0.50 = 50%) for AI to launch long-range missiles (ranges 50-100 km: ARAD-116, Tusko-B/N, NL-98, StratoLance R9, Sabre)."
            );

            AIMinimumStrategicLaunchQuality = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "AIMinimumStrategicLaunchQuality",
                0.60f,
                "Minimum required track quality Q (0.0 to 1.0, default: 0.60 = 60%) for AI to launch strategic, standoff, and super long-range munitions (ranges >= 100 km or cruise: AAM-36 Scimitar, AGM-99, ALM-C450, ALND-4, AShM-300, Starfall, Sunfall, Zenith)."
            );

            AIMinimumBallisticLaunchQuality = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "AIMinimumBallisticLaunchQuality",
                0.90f,
                "Minimum required track quality Q (0.0 to 1.0, default: 0.90 = 90%) for AI to launch precision ballistic munitions without terminal seekers (e.g. Piledriver TBM, ballistic missiles). Since these weapons have no terminal homing seekers and fly across massive distances (up to 250 km), firing at lower quality causes severe CEP dispersion misses."
            );

            AIEnforceStandoffSurvival = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "AIEnforceStandoffSurvival",
                true,
                "When TRUE, AI aircraft armed with standoff munitions preserve safe standoff distance when tracking quality Q is insufficient, turning away / orbiting to maintain radar illumination instead of flying directly at the target in a suicidal kamikaze rush."
            );

            AIMedusaSafeStandoffDistanceKm = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "AIMedusaSafeStandoffDistanceKm",
                25.0f,
                "Safe standoff distance in kilometers for dedicated electronic warfare support aircraft (EW-25 Medusa), keeping her outside lethal SAM threat zones (default: 25.0 km)."
            );

            SARHEnableLoftTerrainClearance = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "SARHEnableLoftTerrainClearance",
                true,
                "When TRUE, long-range SARH missiles (StratoLance R9, etc.) perform smart obstacle clearance and midcourse lofting to climb over mountain ridges instead of flying directly into terrain."
            );

            MissileEnforceSelfDestructWatchdog = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "MissileEnforceSelfDestructWatchdog",
                true,
                "When TRUE, an active watchdog detonates munitions that miss their target, pass their target datum with no active radar/seeker return, or overshoot ballistic apogee without hitting."
            );

            MissileMaxBattlefieldRadiusKm = config.Bind(
                "13. Track Uncertainty & Seeker Dispersion",
                "MissileMaxBattlefieldRadiusKm",
                120.0f,
                "Maximum tactical battlefield radius in kilometers. Missiles exceeding this distance from map origin are automatically detonated to prevent infinite off-map flights."
            );

            if (GameplayPreset.Value == ConfigurationPreset.SidnensPreset)
            {
                ApplySidnenPreset();
            }
            else if (GameplayPreset.Value == ConfigurationPreset.Default)
            {
                ApplyDefaultPreset();
            }
        }

        public static void ApplySidnenPreset()
        {
            if (ShowDebugHUD != null) ShowDebugHUD.Value = false;
            if (DrawDebugGizmos != null) DrawDebugGizmos.Value = false;
            if (CurvatureMaskAltitudeThreshold != null) CurvatureMaskAltitudeThreshold.Value = 100.0f;
            if (RWRUncertaintyCircleScale != null) RWRUncertaintyCircleScale.Value = 0.30f;
            if (RWRUncertaintyDistanceScaleMax != null) RWRUncertaintyDistanceScaleMax.Value = 0.80f;
            if (VisualRadarIdentificationRangeMeters != null) VisualRadarIdentificationRangeMeters.Value = 5000.0f;
            if (MapReconnaissanceQualityThreshold != null) MapReconnaissanceQualityThreshold.Value = 0.75f;
            if (RWRBaseContribution != null) RWRBaseContribution.Value = 0.005f;
            if (ShowMissileAimpointOnMap != null) ShowMissileAimpointOnMap.Value = false;
            if (ARHTerminalActivationDistanceMeters != null) ARHTerminalActivationDistanceMeters.Value = 10000.0f;
            if (ARADTerminalActivationDistanceMeters != null) ARADTerminalActivationDistanceMeters.Value = 7000.0f;
            if (MissileTerminalActivationDistanceMeters != null) MissileTerminalActivationDistanceMeters.Value = 2800.0f;
            if (AIEvaluateMissileLaunchDoctrine != null) AIEvaluateMissileLaunchDoctrine.Value = true;
            if (AIMinimumTacticalLaunchQuality != null) AIMinimumTacticalLaunchQuality.Value = 0.40f;
            if (AIMinimumLongRangeLaunchQuality != null) AIMinimumLongRangeLaunchQuality.Value = 0.50f;
            if (AIMinimumStrategicLaunchQuality != null) AIMinimumStrategicLaunchQuality.Value = 0.60f;
            if (AIMinimumBallisticLaunchQuality != null) AIMinimumBallisticLaunchQuality.Value = 0.90f;
            if (AIEnforceStandoffSurvival != null) AIEnforceStandoffSurvival.Value = true;
            if (AIMedusaSafeStandoffDistanceKm != null) AIMedusaSafeStandoffDistanceKm.Value = 25.0f;
            if (SARHEnableLoftTerrainClearance != null) SARHEnableLoftTerrainClearance.Value = true;
            if (MissileEnforceSelfDestructWatchdog != null) MissileEnforceSelfDestructWatchdog.Value = true;
            if (MissileMaxBattlefieldRadiusKm != null) MissileMaxBattlefieldRadiusKm.Value = 120.0f;
        }

        public static void ApplyDefaultPreset()
        {
            if (ShowDebugHUD != null) ShowDebugHUD.Value = true;
            if (DrawDebugGizmos != null) DrawDebugGizmos.Value = true;
            if (CurvatureMaskAltitudeThreshold != null) CurvatureMaskAltitudeThreshold.Value = 40.0f;
            if (RWRUncertaintyCircleScale != null) RWRUncertaintyCircleScale.Value = 1.0f;
            if (RWRUncertaintyDistanceScaleMax != null) RWRUncertaintyDistanceScaleMax.Value = 2.5f;
            if (VisualRadarIdentificationRangeMeters != null) VisualRadarIdentificationRangeMeters.Value = 2500.0f;
            if (MapReconnaissanceQualityThreshold != null) MapReconnaissanceQualityThreshold.Value = 0.30f;
            if (RWRBaseContribution != null) RWRBaseContribution.Value = 0.035f;
            if (ShowMissileAimpointOnMap != null) ShowMissileAimpointOnMap.Value = false;
            if (ARHTerminalActivationDistanceMeters != null) ARHTerminalActivationDistanceMeters.Value = 10000.0f;
            if (ARADTerminalActivationDistanceMeters != null) ARADTerminalActivationDistanceMeters.Value = 7000.0f;
            if (MissileTerminalActivationDistanceMeters != null) MissileTerminalActivationDistanceMeters.Value = 2800.0f;
            if (AIEvaluateMissileLaunchDoctrine != null) AIEvaluateMissileLaunchDoctrine.Value = true;
            if (AIMinimumTacticalLaunchQuality != null) AIMinimumTacticalLaunchQuality.Value = 0.40f;
            if (AIMinimumLongRangeLaunchQuality != null) AIMinimumLongRangeLaunchQuality.Value = 0.50f;
            if (AIMinimumStrategicLaunchQuality != null) AIMinimumStrategicLaunchQuality.Value = 0.60f;
            if (AIMinimumBallisticLaunchQuality != null) AIMinimumBallisticLaunchQuality.Value = 0.90f;
            if (AIEnforceStandoffSurvival != null) AIEnforceStandoffSurvival.Value = true;
            if (AIMedusaSafeStandoffDistanceKm != null) AIMedusaSafeStandoffDistanceKm.Value = 25.0f;
            if (SARHEnableLoftTerrainClearance != null) SARHEnableLoftTerrainClearance.Value = true;
            if (MissileEnforceSelfDestructWatchdog != null) MissileEnforceSelfDestructWatchdog.Value = true;
            if (MissileMaxBattlefieldRadiusKm != null) MissileMaxBattlefieldRadiusKm.Value = 120.0f;
        }

        public static bool IsModActive
        {
            get { return ModEnabled != null && ModEnabled.Value; }
        }

        public static float EffectiveNotchThreshold
        {
            get
            {
                return NotchVelocityThreshold != null ? NotchVelocityThreshold.Value : 20.0f;
            }
        }

        public static float EffectiveMinSNR
        {
            get
            {
                return MinSNR_dB != null ? MinSNR_dB.Value : 10.0f;
            }
        }

        public static float EffectiveCurvatureMaskAltitude
        {
            get
            {
                return CurvatureMaskAltitudeThreshold != null ? CurvatureMaskAltitudeThreshold.Value : 100.0f;
            }
        }
    }
}
