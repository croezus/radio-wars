using System;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using HarmonyLib;

namespace RadioWars.Core
{
    public struct AspectRCSProfile
    {
        public float NoseRCS;    // θ = 0°, φ = 0° (m²)
        public float BeamRCS;    // θ = ±90°, φ = 0° (m²)
        public float TailRCS;    // θ = ±180°, φ = 0° (m²)
        public float DorsalRCS;  // φ = +90° (top) (m²)
        public float VentralRCS; // φ = -90° (belly) (m²)
        public bool IsHelicopter;
        public float RotorHERM_RCS; // Micro-Doppler spike RCS

        public static AspectRCSProfile CreateFighter(float baseRcs)
        {
            AspectRCSProfile p;
            p.IsHelicopter = false;
            p.RotorHERM_RCS = 0.0f;

            if (baseRcs <= 0.1f) // Stealth fighter (e.g. FS-20 Vortex, F-22E, FS-41 Eclipse, Ifrit)
            {
                p.NoseRCS = Mathf.Max(0.0002f, baseRcs * 0.2f);
                p.BeamRCS = baseRcs * 3.5f;
                p.TailRCS = baseRcs * 1.5f;
                p.DorsalRCS = baseRcs * 4.0f;
                p.VentralRCS = baseRcs * 3.5f;
            }
            else // Conventional 4th gen fighter / attack aircraft
            {
                p.NoseRCS = baseRcs * 0.7f;
                p.BeamRCS = baseRcs * 2.8f;
                p.TailRCS = baseRcs * 1.6f;
                p.DorsalRCS = baseRcs * 3.2f;
                p.VentralRCS = baseRcs * 2.5f;
            }
            return p;
        }

        public static AspectRCSProfile CreateHelicopter(float baseRcs)
        {
            AspectRCSProfile p;
            p.IsHelicopter = true;
            p.NoseRCS = baseRcs * 0.8f;
            p.BeamRCS = baseRcs * 3.0f;
            p.TailRCS = baseRcs * 1.4f;
            p.DorsalRCS = baseRcs * 3.5f;
            p.VentralRCS = baseRcs * 2.2f;
            p.RotorHERM_RCS = Mathf.Max(1.5f, baseRcs * 0.5f); // Substantial blade reflection flash
            return p;
        }

        public static AspectRCSProfile CreateMissile(float baseRcs)
        {
            AspectRCSProfile p;
            p.IsHelicopter = false;
            p.RotorHERM_RCS = 0.0f;
            p.NoseRCS = Mathf.Max(0.01f, baseRcs * 0.3f);
            p.BeamRCS = Mathf.Max(0.08f, baseRcs * 1.8f);
            p.TailRCS = Mathf.Max(0.04f, baseRcs * 1.0f);
            p.DorsalRCS = Mathf.Max(0.08f, baseRcs * 1.5f);
            p.VentralRCS = Mathf.Max(0.08f, baseRcs * 1.5f);
            return p;
        }

        public static AspectRCSProfile CreateLargeAircraft(float baseRcs)
        {
            AspectRCSProfile p;
            p.IsHelicopter = false;
            p.RotorHERM_RCS = 0.0f;
            p.NoseRCS = baseRcs * 0.75f;
            p.BeamRCS = baseRcs * 3.5f;
            p.TailRCS = baseRcs * 2.0f;
            p.DorsalRCS = baseRcs * 4.5f;
            p.VentralRCS = baseRcs * 4.0f;
            return p;
        }
    }

    public static class AspectRCS
    {
        private static Func<Weapon, Hardpoint> _weaponHardpointRef;
        private static Func<Weapon, WeaponMount> _weaponMountRef;
        private static Func<BayDoor, float> _bayDoorOpenAmountRef;
        private static bool _fieldRefsInitialized;

        private static void EnsureFieldRefs()
        {
            if (_fieldRefsInitialized) return;
            _fieldRefsInitialized = true;
            _weaponHardpointRef = FastReflection.CreateFieldGetter<Weapon, Hardpoint>("hardpoint");
            _weaponMountRef = FastReflection.CreateFieldGetter<Weapon, WeaponMount>("mount");
            _bayDoorOpenAmountRef = FastReflection.CreateFieldGetter<BayDoor, float>("openAmount");
        }
        /// <summary>
        /// Calculates the dynamic 3D aspect-dependent RCS (m²) for a target seen from a radar position.
        /// Evaluates spherical azimuth and elevation relative to the target's body orientation.
        /// </summary>
        public static float CalculateAspectRCS(
            Transform targetTransform,
            Vector3 radarPosition,
            ref AspectRCSProfile profile)
        {
            if (targetTransform == null) return profile.NoseRCS;

            Vector3 toRadar = radarPosition - targetTransform.position;
            float sqrMag = toRadar.sqrMagnitude;
            if (sqrMag < 0.001f) return profile.NoseRCS;

            // Normalized line of sight vector in world space
            Vector3 losWorld = toRadar / Mathf.Sqrt(sqrMag);

            // Convert to target local frame: +Z forward, +X right, +Y up
            Vector3 losLocal = targetTransform.InverseTransformDirection(losWorld);

            float rxzSq = losLocal.x * losLocal.x + losLocal.z * losLocal.z;
            float rxz = Mathf.Sqrt(rxzSq);
            float cosAz = (rxz > 0.0001f) ? (losLocal.z / rxz) : 1.0f;
            float sinAz = (rxz > 0.0001f) ? (losLocal.x / rxz) : 0.0f;
            float sinEl = Mathf.Clamp(losLocal.y, -1.0f, 1.0f);

            // Horizontal aspect components
            float noseWeight = Mathf.Max(0.0f, cosAz);
            noseWeight = noseWeight * noseWeight * noseWeight * noseWeight; // cos^4 (narrow forward lobe)

            float tailWeight = Mathf.Max(0.0f, -cosAz);
            tailWeight = tailWeight * tailWeight * tailWeight; // cos^3

            float beamWeight = sinAz * sinAz; // sin^2 (broad beam specular lobe)

            // Vertical elevation components
            float dorsalWeight = Mathf.Max(0.0f, sinEl);
            float ventralWeight = Mathf.Max(0.0f, -sinEl);
            float horizontalPlaneWeight = rxzSq; // cosEl^2 = 1 - sinEl^2 = rxzSq

            // Combine horizontal aspect with normalized weights
            float totalHorizWeight = noseWeight + beamWeight + tailWeight;
            float rcsHoriz = (totalHorizWeight > 0.0001f)
                ? ((profile.NoseRCS * noseWeight) + (profile.BeamRCS * beamWeight) + (profile.TailRCS * tailWeight)) / totalHorizWeight
                : profile.NoseRCS;

            // Blend with vertical aspect
            float totalRcs = (rcsHoriz * horizontalPlaneWeight) +
                             (profile.DorsalRCS * dorsalWeight * dorsalWeight) +
                             (profile.VentralRCS * ventralWeight * ventralWeight);

            return Mathf.Max(0.0002f, totalRcs);
        }

        /// <summary>
        /// Calculates additive RCS penalty from weapons, pylons, and stores.
        /// Accounts for physical shielding of internal weapon bays (FS-20 Vortex, F-22E, Darkreach, Eclipse)
        /// with dynamic cavity resonance during door opening, and physical munition scattering on external pylons.
        /// Zero heap allocation implementation.
        /// </summary>
        public static float CalculateStoresRCSPenalty(Unit unit)
        {
            if (unit == null || unit.weaponStations == null) return 0.0f;

            EnsureFieldRefs();

            float penalty = 0.0f;
            int stationCount = unit.weaponStations.Count;
            Aircraft aircraft = unit as Aircraft;

            for (int i = 0; i < stationCount; i++)
            {
                WeaponStation st = unit.weaponStations[i];
                if (st == null) continue;

                if (st.Weapons != null && st.Weapons.Count > 0)
                {
                    int weaponCount = st.Weapons.Count;
                    bool externalStationPylonCounted = false;

                    // Station-level hardpoint resolution
                    Hardpoint hp = null;
                    if (_weaponHardpointRef != null)
                    {
                        try { hp = _weaponHardpointRef(st.Weapons[0]); } catch { }
                    }

                    if (hp == null && aircraft != null && aircraft.weaponManager != null && aircraft.weaponManager.hardpointSets != null)
                    {
                        var hpSets = aircraft.weaponManager.hardpointSets;
                        for (int s = 0; s < hpSets.Length && hp == null; s++)
                        {
                            var set = hpSets[s];
                            if (set != null && set.hardpoints != null)
                            {
                                for (int h = 0; h < set.hardpoints.Count; h++)
                                {
                                    var candidHp = set.hardpoints[h];
                                    if (candidHp != null && candidHp.HardpointIndex == st.Number)
                                    {
                                        hp = candidHp;
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    // Station-level bay door state
                    float stationMaxDoorOpen = 0.0f;
                    bool stationHasBayDoors = false;
                    if (hp != null && hp.bayDoors != null && hp.bayDoors.Length > 0)
                    {
                        stationHasBayDoors = true;
                        for (int d = 0; d < hp.bayDoors.Length; d++)
                        {
                            BayDoor door = hp.bayDoors[d];
                            if (door != null && _bayDoorOpenAmountRef != null)
                            {
                                try
                                {
                                    float openAmt = _bayDoorOpenAmountRef(door);
                                    if (openAmt > stationMaxDoorOpen)
                                    {
                                        stationMaxDoorOpen = openAmt;
                                    }
                                }
                                catch { }
                            }
                        }
                    }

                    for (int w = 0; w < weaponCount; w++)
                    {
                        Weapon weapon = st.Weapons[w];
                        if (weapon == null) continue;

                        WeaponMount mount = null;
                        if (_weaponMountRef != null)
                        {
                            try { mount = _weaponMountRef(weapon); } catch { }
                        }

                        WeaponInfo info = weapon.info != null ? weapon.info : st.WeaponInfo;

                        bool isInternal = stationHasBayDoors;
                        float maxDoorOpen = stationMaxDoorOpen;

                        if (!isInternal)
                        {
                            if (mount != null && (mount.missileBay || MunitionRCSDatabase.IsInternalBayMount(mount.mountName)))
                            {
                                isInternal = true;
                            }
                        }

                        bool isStealthPod = (mount != null && MunitionRCSDatabase.IsStealthPod(mount.mountName)) ||
                                           (info != null && MunitionRCSDatabase.IsStealthPod(info.name)) ||
                                           (st.WeaponInfo != null && MunitionRCSDatabase.IsStealthPod(st.WeaponInfo.name));

                        if (isStealthPod)
                        {
                            // Conformal Stealth Weapon Pod (e.g. F-22E Strike Raptor Enclosed Stealth Pods):
                            // Faceted low-observable aerodynamic shell shields internal stores.
                            // Base pod penalty is only 0.005 m² (counted once per station).
                            if (!externalStationPylonCounted)
                            {
                                penalty += 0.005f;
                                externalStationPylonCounted = true;
                            }

                            if (maxDoorOpen > 0.05f)
                            {
                                // Dynamic cavity resonance when pod doors open for missile release
                                float cavityRcs = 0.15f * maxDoorOpen;
                                if (weapon.ammo > 0)
                                {
                                    string wName = info != null ? info.name : null;
                                    cavityRcs += MunitionRCSDatabase.GetMunitionRCS(wName) * maxDoorOpen;
                                }
                                penalty += cavityRcs;
                            }
                        }
                        else if (isInternal)
                        {
                            // Internal weapons bay:
                            // When bay doors are closed, stores are completely shielded inside the stealth airframe. Zero RCS penalty!
                            if (maxDoorOpen > 0.05f)
                            {
                                // Dynamic radar cavity resonance / corner reflector spike during bay door opening
                                float cavityRcs = 0.25f * maxDoorOpen;
                                if (weapon.ammo > 0)
                                {
                                    string wName = info != null ? info.name : null;
                                    cavityRcs += MunitionRCSDatabase.GetMunitionRCS(wName) * maxDoorOpen;
                                }
                                penalty += cavityRcs;
                            }
                        }
                        else
                        {
                            // External hardpoint / pylon:
                            // 1. Aerodynamic external pylon radar scatterer (counted once per station)
                            // Enriched with authentic MountValues.txt / WeaponMount.emptyRCS data:
                            if (!externalStationPylonCounted)
                            {
                                float pylonRcs = (mount != null && mount.emptyRCS > 0.0001f)
                                    ? mount.emptyRCS
                                    : 0.015f;
                                penalty += pylonRcs;
                                externalStationPylonCounted = true;
                            }

                            // 2. Exposed munition / fuel tank reflection
                            if (weapon.ammo > 0)
                            {
                                string wName = info != null ? info.name : null;
                                float munitionRcs = MunitionRCSDatabase.GetMunitionRCS(wName);
                                float ammoRatio = (st.FullAmmo > 0) ? ((float)weapon.ammo / st.FullAmmo) : 1.0f;
                                penalty += munitionRcs * Mathf.Clamp01(ammoRatio);
                            }
                        }
                    }
                }
                else if (st.WeaponInfo != null && st.Ammo > 0)
                {
                    // Fallback for stations without weapon references
                    if (MunitionRCSDatabase.IsStealthPod(st.WeaponInfo.name))
                    {
                        penalty += 0.005f;
                    }
                    else
                    {
                        penalty += 0.04f;
                        float munitionRcs = MunitionRCSDatabase.GetMunitionRCS(st.WeaponInfo.name);
                        float ammoRatio = (st.FullAmmo > 0) ? ((float)st.Ammo / st.FullAmmo) : 1.0f;
                        penalty += munitionRcs * Mathf.Clamp01(ammoRatio);
                    }
                }
            }

            return penalty;
        }

        /// <summary>
        /// Returns effective micro-Doppler flash RCS for helicopter rotor blades (HERM).
        /// </summary>
        public static float GetHelicopterRotorFlash(ref AspectRCSProfile profile)
        {
            if (!profile.IsHelicopter) return 0.0f;
            return profile.RotorHERM_RCS;
        }
    }
}
