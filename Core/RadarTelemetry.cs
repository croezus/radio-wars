using System;
using System.Collections.Generic;
using UnityEngine;

namespace RadioWars.Core
{
    public enum RadarIlluminationState
    {
        Search,           // Sweeping / periodic ping
        Tracking,         // Continuous lock attempt
        HardLock,         // Single Target Track (STT) lock
        MissileGuidance   // CW / SARH illuminator or active ARH seeker
    }

    public class ActiveRadarIlluminator
    {
        public int EmitterId;
        public string SourceName;
        public string PlatformType;
        public Vector3 EmitterPosition;
        public Vector3 EmitterForward;
        public float DistanceMeters;
        public float SNRdB;
        public float ReceivedPowerWatts;
        public RadarIlluminationState State;
        public float FirstSeenTime;
        public float LastPingTime;

        public Color GetColor()
        {
            switch (State)
            {
                case RadarIlluminationState.MissileGuidance:
                    bool flash = (Time.time * 8.0f) % 1.0f > 0.35f;
                    return flash ? new Color(1.0f, 0.0f, 0.35f, 1.0f) : new Color(1.0f, 0.2f, 0.9f, 1.0f);
                case RadarIlluminationState.HardLock:
                    return new Color(1.0f, 0.25f, 0.0f, 1.0f); // Vivid Orange-Red
                case RadarIlluminationState.Tracking:
                    return new Color(1.0f, 0.88f, 0.0f, 0.95f); // Vivid Yellow
                case RadarIlluminationState.Search:
                default:
                    return new Color(0.0f, 0.95f, 1.0f, 0.85f); // Cyan
            }
        }
    }

    public enum TelemetryNotchState
    {
        Clear,
        LookUpClear,
        BeamingNotched,
        LookDownClutterMasked,
        HorizonOccluded,
        LostJamming
    }

    public struct RadarTelemetrySnapshot
    {
        public bool HasTarget;
        public string TargetName;
        public float SlantRangeMeters;
        public float EchoPowerWatts;
        public float NoiseFloorWatts;
        public float ClutterPowerWatts;
        public float JammerPowerWatts;
        public float SNRdB;
        public float RequiredSNRdB;
        public float DynamicRCS;
        public float StoresPenaltyRCS;
        public float RelativeRadialSpeed;
        public float GroundRadialSpeed;
        public float ElevationDeg;
        public TelemetryNotchState NotchState;
        public bool IsLookingDown;
        public float BurnThroughRangeMeters;
        public bool JammerActive;
        public float JammerIntensity;
        public float LastUpdateTime;

        // Gizmo / Debug Raycast Vectors
        public Vector3 RadarPos;
        public Vector3 TargetPos;
        public Vector3 TargetVel;
        public Color RayColor;
    }

    public static class RadarTelemetry
    {
        public static RadarTelemetrySnapshot Latest = new RadarTelemetrySnapshot();
        public static int CurrentPriority = 0;
        public static float LastPriorityTime = 0.0f;

        public static readonly Dictionary<int, ActiveRadarIlluminator> ActiveIlluminators = new Dictionary<int, ActiveRadarIlluminator>();
        private static readonly List<ActiveRadarIlluminator> CachedIlluminatorList = new List<ActiveRadarIlluminator>();
        private static readonly List<int> _staleKeys = new List<int>();
        private static int _lastQueryFrame = -1;

        private class IlluminatorPriorityComparer : IComparer<ActiveRadarIlluminator>
        {
            public int Compare(ActiveRadarIlluminator a, ActiveRadarIlluminator b)
            {
                if (ReferenceEquals(a, b)) return 0;
                if (a == null) return 1;
                if (b == null) return -1;
                int c = b.State.CompareTo(a.State);
                if (c != 0) return c;
                return a.DistanceMeters.CompareTo(b.DistanceMeters);
            }
        }
        private static readonly IlluminatorPriorityComparer _priorityComparer = new IlluminatorPriorityComparer();

        private static float _lastSelfPruneTime = 0.0f;

        public static void RegisterIllumination(
            int emitterId,
            string sourceName,
            string platformType,
            Vector3 emitterPos,
            Vector3 emitterFwd,
            float dist,
            float snrDb,
            float prWatts,
            RadarIlluminationState state)
        {
            float now = Time.time;

            if (now - _lastSelfPruneTime > 5.0f)
            {
                _lastSelfPruneTime = now;
                _staleKeys.Clear();
                foreach (var kvp in ActiveIlluminators)
                {
                    if (now - kvp.Value.LastPingTime > 5.0f)
                    {
                        _staleKeys.Add(kvp.Key);
                    }
                }
                for (int i = 0; i < _staleKeys.Count; i++)
                {
                    ActiveIlluminators.Remove(_staleKeys[i]);
                }
            }

            ActiveRadarIlluminator item;
            if (!ActiveIlluminators.TryGetValue(emitterId, out item))
            {
                item = new ActiveRadarIlluminator();
                item.EmitterId = emitterId;
                item.FirstSeenTime = now;
                ActiveIlluminators[emitterId] = item;
            }

            item.SourceName = !string.IsNullOrEmpty(sourceName) ? sourceName : "Radar Emitter";
            item.PlatformType = platformType;
            item.EmitterPosition = emitterPos;
            item.EmitterForward = emitterFwd;
            item.DistanceMeters = dist;
            item.SNRdB = snrDb;
            item.ReceivedPowerWatts = prWatts;
            item.State = state;
            item.LastPingTime = now;
        }

        public static List<ActiveRadarIlluminator> GetActiveIlluminators(float maxAgeSeconds = 3.0f)
        {
            // If already evaluated within this frame, return cached list with zero overhead
            if (Time.frameCount == _lastQueryFrame)
            {
                return CachedIlluminatorList;
            }
            _lastQueryFrame = Time.frameCount;

            float now = Time.time;
            CachedIlluminatorList.Clear();
            _staleKeys.Clear();

            foreach (var kvp in ActiveIlluminators)
            {
                if (now - kvp.Value.LastPingTime > maxAgeSeconds)
                {
                    _staleKeys.Add(kvp.Key);
                }
                else
                {
                    CachedIlluminatorList.Add(kvp.Value);
                }
            }

            for (int i = 0; i < _staleKeys.Count; i++)
            {
                ActiveIlluminators.Remove(_staleKeys[i]);
            }

            CachedIlluminatorList.Sort(_priorityComparer);

            return CachedIlluminatorList;
        }

        public static void UpdateTelemetry(
            Vector3 radarPos,
            Vector3 radarVel,
            Vector3 targetPos,
            Vector3 targetVel,
            string targetName,
            float dist,
            float pr,
            float pn,
            float pc,
            float pj,
            float snrDb,
            float reqSnrDb,
            float dynamicRcs,
            float storesRcs,
            ref DopplerResult doppler,
            float rBurn,
            bool jammerActive,
            float jammerIntensity,
            bool horizonOccluded,
            int priority = 0)
        {
            float now = Time.time;
            if (now - LastPriorityTime > 0.5f)
            {
                CurrentPriority = 0;
            }

            if (priority < CurrentPriority)
            {
                return;
            }

            CurrentPriority = priority;
            LastPriorityTime = now;

            Latest.HasTarget = true;
            Latest.TargetName = !string.IsNullOrEmpty(targetName) ? targetName : "Unknown Target";
            Latest.SlantRangeMeters = dist;
            Latest.EchoPowerWatts = pr;
            Latest.NoiseFloorWatts = pn;
            Latest.ClutterPowerWatts = pc;
            Latest.JammerPowerWatts = pj;
            Latest.SNRdB = snrDb;
            Latest.RequiredSNRdB = reqSnrDb;
            Latest.DynamicRCS = dynamicRcs;
            Latest.StoresPenaltyRCS = storesRcs;
            Latest.RelativeRadialSpeed = doppler.RelativeRadialVelocity;
            Latest.GroundRadialSpeed = doppler.TargetGroundRadialSpeed;
            Latest.ElevationDeg = doppler.ElevationAngleDeg;
            Latest.IsLookingDown = doppler.IsLookingDown;
            Latest.BurnThroughRangeMeters = rBurn;
            Latest.JammerActive = jammerActive;
            Latest.JammerIntensity = jammerIntensity;
            Latest.LastUpdateTime = now;

            Latest.RadarPos = radarPos;
            Latest.TargetPos = targetPos;
            Latest.TargetVel = targetVel;

            if (horizonOccluded)
            {
                Latest.NotchState = TelemetryNotchState.HorizonOccluded;
                Latest.RayColor = new Color(0.9f, 0.1f, 0.1f, 0.9f); // Red
            }
            else if (doppler.TrackRejectedByNotch)
            {
                Latest.NotchState = TelemetryNotchState.BeamingNotched;
                Latest.RayColor = new Color(1.0f, 0.2f, 0.2f, 0.9f); // Bright Red
            }
            else if (doppler.IsInsideClutterNotch && !doppler.IsLookingDown)
            {
                Latest.NotchState = TelemetryNotchState.LookUpClear;
                Latest.RayColor = new Color(0.2f, 0.9f, 0.3f, 0.9f); // Green
            }
            else if (doppler.IsLookingDown && (pc > 0.0f || doppler.IsInsideClutterNotch))
            {
                Latest.NotchState = TelemetryNotchState.LookDownClutterMasked;
                Latest.RayColor = new Color(1.0f, 0.85f, 0.1f, 0.9f); // Yellow / amber
            }
            else
            {
                Latest.NotchState = TelemetryNotchState.Clear;
                Latest.RayColor = new Color(0.1f, 1.0f, 0.3f, 0.9f); // Emerald Green
            }
        }

        public static void ClearAll()
        {
            ActiveIlluminators.Clear();
            CachedIlluminatorList.Clear();
            _staleKeys.Clear();
            CurrentPriority = 0;
            LastPriorityTime = 0.0f;
            Latest = new RadarTelemetrySnapshot();
        }
    }
}
