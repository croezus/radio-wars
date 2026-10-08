using System;
using System.Collections.Generic;
using UnityEngine;
using RadioWars.Core;
using RadioWars.Config;

namespace RadioWars.Components
{
    public class PhysRadarTarget : MonoBehaviour
    {
        private static readonly Dictionary<Unit, PhysRadarTarget> s_registry = new Dictionary<Unit, PhysRadarTarget>();

        public static PhysRadarTarget Get(Unit unit)
        {
            if (unit == null) return null;
            PhysRadarTarget target;
            if (!s_registry.TryGetValue(unit, out target))
            {
                target = unit.GetComponent<PhysRadarTarget>();
                if (target != null) s_registry[unit] = target;
            }
            return target;
        }

        public static void ClearAll()
        {
            s_registry.Clear();
        }

        public Unit AttachedUnit;
        public Aircraft AttachedAircraft;
        public Missile AttachedMissile;
        public AspectRCSProfile Profile;

        public JammerSpecs Jammer;
        public bool HasActiveJammer = false;

        private float _lastStoreCheckTime = 0.0f;
        private float _cachedStoresPenalty = 0.0f;

        public float CachedStoresPenalty
        {
            get { return _cachedStoresPenalty; }
        }

        private void Awake()
        {
            AttachedUnit = GetComponent<Unit>();
            if (AttachedUnit != null) s_registry[AttachedUnit] = this;
            AttachedAircraft = GetComponent<Aircraft>();
            AttachedMissile = GetComponent<Missile>();

            InitializeProfile();
        }

        private void OnDestroy()
        {
            if (AttachedUnit != null) s_registry.Remove(AttachedUnit);
        }

        public void InitializeProfile()
        {
            float baseRcs = (AttachedUnit != null) ? Mathf.Max(0.0002f, AttachedUnit.RCS) : 5.0f;

            if (AttachedMissile != null)
            {
                Profile = AspectRCSProfile.CreateMissile(baseRcs);
            }
            else if (AttachedAircraft != null)
            {
                string uName = AttachedUnit != null ? AttachedUnit.unitName : null;
                bool isHeli = false;
                if (!string.IsNullOrEmpty(uName))
                {
                    isHeli = (uName.IndexOf("heli", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              uName.IndexOf("chicane", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              uName.IndexOf("rotor", StringComparison.OrdinalIgnoreCase) >= 0);
                }

                if (isHeli)
                {
                    Profile = AspectRCSProfile.CreateHelicopter(baseRcs);
                }
                else if (baseRcs > 12.0f)
                {
                    Profile = AspectRCSProfile.CreateLargeAircraft(baseRcs);
                }
                else
                {
                    Profile = AspectRCSProfile.CreateFighter(baseRcs);
                }
            }
            else
            {
                // Surface vehicles or ships
                Profile = AspectRCSProfile.CreateLargeAircraft(baseRcs);
            }

            Jammer = JammerSpecs.CreateStandardPod();
        }

        public float GetDynamicRCS(Vector3 radarPosition)
        {
            float rcs = AspectRCS.CalculateAspectRCS(transform, radarPosition, ref Profile);

            if (RadioWarsConfig.ExternalStoresRCSEnabled != null && RadioWarsConfig.ExternalStoresRCSEnabled.Value)
            {
                if (Time.time - _lastStoreCheckTime > 0.25f)
                {
                    _cachedStoresPenalty = AspectRCS.CalculateStoresRCSPenalty(AttachedUnit);
                    _lastStoreCheckTime = Time.time;
                }
                rcs += _cachedStoresPenalty;
            }

            return rcs;
        }

        public bool IsHelicopterWithHERM()
        {
            if (RadioWarsConfig.HERMEnabled != null && !RadioWarsConfig.HERMEnabled.Value)
            {
                return false;
            }
            return Profile.IsHelicopter;
        }
    }
}
