using System;
using System.Collections.Generic;
using UnityEngine;
using RadioWars.Core;

namespace RadioWars.Components
{
    public class PhysRadarEmitter : MonoBehaviour
    {
        private static readonly Dictionary<Radar, PhysRadarEmitter> s_registry = new Dictionary<Radar, PhysRadarEmitter>();

        public static PhysRadarEmitter Get(Radar radar)
        {
            if (radar == null) return null;
            PhysRadarEmitter emitter;
            if (!s_registry.TryGetValue(radar, out emitter))
            {
                emitter = radar.GetComponent<PhysRadarEmitter>();
                if (emitter != null) s_registry[radar] = emitter;
            }
            return emitter;
        }

        public static void ClearAll()
        {
            s_registry.Clear();
        }

        public Radar TargetRadar;
        public TargetDetector Detector;
        public Unit AttachedUnit;
        public RadarSpecs Specs;
        public RwrThreatState ThreatState = RwrThreatState.Search;

        public int TimeSliceBucket = 0;
        public float LastScanTime = 0.0f;
        public float ThermalNoiseFloor = 0.0f;

        private void Awake()
        {
            TargetRadar = GetComponent<Radar>();
            if (TargetRadar != null) s_registry[TargetRadar] = this;
            Detector = GetComponent<TargetDetector>();
            AttachedUnit = GetComponentInParent<Unit>();

            // Assign frame bucket (0 to 3) to spread CPU load across frames
            TimeSliceBucket = UnityEngine.Random.Range(0, 4);

            InitializeSpecs();
        }

        private void OnDestroy()
        {
            if (TargetRadar != null) s_registry.Remove(TargetRadar);
        }

        public void InitializeSpecs()
        {
            if (AttachedUnit == null)
            {
                AttachedUnit = GetComponentInParent<Unit>();
            }

            Specs = RadarPhysics.GetRadarSpecsForPlatform(AttachedUnit, TargetRadar);
            ThermalNoiseFloor = RadarPhysics.CalculateThermalNoiseFloor(Specs.BandwidthHz, Specs.NoiseFigureLinear);
        }

        public void UpdateThreatState(bool hasLock)
        {
            if (hasLock)
            {
                ThreatState = RwrThreatState.Track;
            }
            else
            {
                ThreatState = RwrThreatState.Search;
            }
        }
    }
}
