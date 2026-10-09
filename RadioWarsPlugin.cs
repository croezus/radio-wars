using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using RadioWars.Config;

namespace RadioWars
{
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    public class RadioWarsPlugin : BaseUnityPlugin
    {
        public const string ModGuid = "com.radiowars.nuclearoption.overhaul";
        public const string ModName = "Radio Wars";
        public const string ModVersion = "1.0.3";

        public static RadioWarsPlugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            Log.LogInfo(string.Format("Initializing {0} v{1}...", ModName, ModVersion));

            // Initialize BepInEx Configuration
            RadioWarsConfig.Initialize(Config);

            // Apply Harmony Patches safely per-class
            try
            {
                _harmony = new Harmony(ModGuid);
                int successCount = 0;
                int failCount = 0;
                Type[] allTypes = System.Reflection.Assembly.GetExecutingAssembly().GetTypes();
                for (int i = 0; i < allTypes.Length; i++)
                {
                    Type patchType = allTypes[i];
                    if (patchType.IsDefined(typeof(HarmonyPatch), true))
                    {
                        try
                        {
                            _harmony.CreateClassProcessor(patchType).Patch();
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            failCount++;
                            Log.LogError(string.Format("Failed to apply patch class {0}: {1}", patchType.FullName, ex));
                        }
                    }
                }
                Log.LogInfo(string.Format("Radio Wars Harmony patches applied: {0} succeeded, {1} failed.", successCount, failCount));
            }
            catch (Exception ex)
            {
                Log.LogError(string.Format("Failed to initialize Harmony: {0}", ex));
            }

            Log.LogInfo(string.Format("Radio Wars mod active: {0}", RadioWarsConfig.IsModActive));
            Log.LogInfo(string.Format("Clutter Notch Velocity: {0} m/s", RadioWarsConfig.EffectiveNotchThreshold));
            Log.LogInfo(string.Format("Minimum Detection SNR: {0} dB", RadioWarsConfig.EffectiveMinSNR));
            Log.LogInfo(string.Format("4/3 Earth Curvature Refraction: {0}", RadioWarsConfig.EarthCurvatureEnabled.Value));

            // Attach Telemetry & Visualizer HUD
            gameObject.AddComponent<RadioWars.Components.RadioWarsDebugHUD>();
            gameObject.AddComponent<RadioWars.Components.RadarDebugVisualizer>();
            gameObject.AddComponent<RadioWars.Components.DatalinkDebugVisualizer>();
            Log.LogInfo("Radio Wars Debug Visualizer (F11 Gizmos), Datalink Visualizer (F8), & Telemetry HUD (F9) attached.");

            // Listen for scene/match lifecycle to clear tactical intelligence on mission reset
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (Log != null)
            {
                Log.LogInfo(string.Format("Scene loaded ({0}). Resetting electronic intelligence & target memory tracks.", scene.name));
            }
            RadioWars.Core.RWRTriangulationProcessor.ClearAll();
            RadioWars.Core.TrackUncertaintyCalculator.Clear();
            RadioWars.Core.DatalinkNetwork.ClearAll();
            RadioWars.Core.RadarTelemetry.ClearAll();
            RadioWars.Components.PhysRadarEmitter.ClearAll();
            RadioWars.Components.PhysRadarTarget.ClearAll();
            RadioWars.Components.PhysRWRReceiver.ClearAll();
            RadioWars.Patches.DynamicMapPatches.ClearAll();
            RadioWars.Patches.ARHSeeker_GetRadarReturn_Patch.ClearAllCoastTimers();
            RadioWars.Patches.SARHSeeker_GetTrackingStrength_Patch.ClearAllCoastTimers();
            RadioWars.Core.MunitionRCSDatabase.ClearAll();

            // Self-healing census for newly loaded mission scene
            RadioWars.Core.DatalinkNetwork.CensusSceneUnitsAndRadars(false);
        }

        private void Update()
        {
            if (!RadioWarsConfig.IsModActive) return;

            // Universal multi-agent datalink cycle (fuses bot/player visual reconnaissance and radar tracks)
            RadioWars.Core.DatalinkNetwork.ProcessDatalinkCycle();

            if (RadioWarsConfig.EnableRwrTriangulationSystem == null || RadioWarsConfig.EnableRwrTriangulationSystem.Value)
            {
                RadioWars.Core.RwrTriangulationProcessor.UpdateAllTracks(UnityEngine.Time.deltaTime);
            }
        }

        private void OnDestroy()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            RadioWars.Core.RWRTriangulationProcessor.ClearAll();
            RadioWars.Core.TrackUncertaintyCalculator.Clear();
            RadioWars.Core.DatalinkNetwork.ClearAll();
            RadioWars.Core.RadarTelemetry.ClearAll();
            RadioWars.Components.PhysRadarEmitter.ClearAll();
            RadioWars.Components.PhysRadarTarget.ClearAll();
            RadioWars.Components.PhysRWRReceiver.ClearAll();
            RadioWars.Patches.DynamicMapPatches.ClearAll();
            RadioWars.Patches.ARHSeeker_GetRadarReturn_Patch.ClearAllCoastTimers();
            RadioWars.Patches.SARHSeeker_GetTrackingStrength_Patch.ClearAllCoastTimers();
            RadioWars.Core.MunitionRCSDatabase.ClearAll();
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
            }
        }
    }
}
