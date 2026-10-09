using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using RadioWars.Config;
using RadioWars.Core;
using RadioWars.Patches;

namespace RadioWars.Components
{
    /// <summary>
    /// Tactical Map (Key M, DynamicMap) visualizer for missile midcourse track uncertainty dispersion.
    /// Provides real-time visual confirmation of unguided munition flight paths:
    /// 1. Fly-to-Waypoint Guidance Line: Line connecting missile to the displaced aimpoint (where munition steers).
    /// 2. Displaced Waypoint Marker: Crosshair / diamond marker at displaced coordinates.
    /// 3. CEP Offset Vector: Dashed line connecting true target to displaced aimpoint.
    /// 4. Terminal Seeker Pitbull Basket: 2800m ring showing seeker acquisition threshold.
    /// 5. Terminal Seeker Lock Transition: Snaps line to real target and switches to Crimson Red upon lock.
    /// Zero heap allocations per frame, pre-allocated object pool of 8 visual slots.
    /// </summary>
    public static class MissileMapDispersionVisualizer
    {
        private const int POOL_SIZE = 8;

        private static readonly Func<DynamicMap, GameObject> s_getRadarVisPrefab =
            FastReflection.CreateFieldGetter<DynamicMap, GameObject>("radarVisPrefab");

        private static readonly Func<Missile, GlobalPosition> f_missileAimPoint =
            FastReflection.CreateFieldGetter<Missile, GlobalPosition>("aimPoint");

        private class MissileMapVisualSlot
        {
            public GameObject Root;

            // 1. Guidance Line (Missile to Aimpoint or True Target)
            public Image FlyToLine;
            public RectTransform FlyToLineRt;

            // 2. Displaced Waypoint Marker
            public Image AimMarker;
            public RectTransform AimMarkerRt;

            // 3. CEP Offset Vector (True Target to Aimpoint)
            public Image OffsetLine;
            public RectTransform OffsetLineRt;

            // 4. Terminal Seeker Pitbull Basket Ring (2.8 km radius)
            public Image BasketRing;
            public RectTransform BasketRingRt;

            public void SetActive(bool active)
            {
                if (Root != null && Root.activeSelf != active)
                {
                    Root.SetActive(active);
                }
            }
        }

        private static readonly List<MissileMapVisualSlot> _slotPool = new List<MissileMapVisualSlot>(POOL_SIZE);
        private static Transform _cachedParent;

        /// <summary>
        /// Updates and renders all active missile dispersion visuals on the dynamic map.
        /// Hooked directly into DynamicMapPatches.UpdateMap_Postfix.
        /// </summary>
        public static void UpdateVisuals(DynamicMap map)
        {
            try
            {
                if (!RadioWarsConfig.IsModActive)
                {
                    HideAllSlots();
                    return;
                }

                bool enabled = (RadioWarsConfig.ShowMissileAimpointOnMap != null) && RadioWarsConfig.ShowMissileAimpointOnMap.Value;
                if (!enabled || map == null || map.iconLayer == null)
                {
                    HideAllSlots();
                    return;
                }

                List<Missile> activeMissiles = DatalinkNetwork.ActiveMissiles;
                if (activeMissiles == null || activeMissiles.Count == 0)
                {
                    HideAllSlots();
                    return;
                }

                EnsurePool(map);

                Aircraft playerAircraft = (CombatHUD.i != null) ? CombatHUD.i.aircraft : null;
                bool playerOnly = (RadioWarsConfig.ShowPlayerMissilesOnlyOnMap == null) || RadioWarsConfig.ShowPlayerMissilesOnlyOnMap.Value;

                float layerScale = (map.iconLayer.transform.lossyScale.y > 0.0001f)
                    ? map.iconLayer.transform.lossyScale.y
                    : 1.0f;

                float mapZ = (map.mapImage != null) ? map.mapImage.transform.eulerAngles.z : 0f;
                float mapFactor = map.mapDisplayFactor;

                float pitbullDist = (RadioWarsConfig.MissileTerminalActivationDistanceMeters != null)
                    ? RadioWarsConfig.MissileTerminalActivationDistanceMeters.Value
                    : 2800.0f;

                int slotIndex = 0;

                for (int i = 0; i < activeMissiles.Count; i++)
                {
                    if (slotIndex >= POOL_SIZE) break;

                    Missile missile = activeMissiles[i];
                    if (missile == null || missile.disabled) continue;

                    // Filter by owner aircraft if player-only mode is active
                    if (playerOnly)
                    {
                        if (playerAircraft == null || playerAircraft.disabled) continue;
                        if (missile.owner != playerAircraft) continue;
                    }
                    else
                    {
                        // Exclude enemy missiles from player diagnostic visualizer
                        if (playerAircraft != null && missile.NetworkHQ != null && missile.NetworkHQ != playerAircraft.NetworkHQ)
                        {
                            continue;
                        }
                    }

                    MissileMapVisualSlot slot = _slotPool[slotIndex];
                    if (slot == null || slot.Root == null) continue;

                    // 1. Resolve missile current map coordinates
                    Vector3 missileGlobal = missile.GlobalPosition().AsVector3();
                    Vector3 missileLocalPos = new Vector3(missileGlobal.x * mapFactor, missileGlobal.z * mapFactor, 0f);

                    // 2. Resolve target and dispersion
                    Unit target = SeekerDispersionHelper.GetTargetForMissile(missile);
                    Vector3 disp = Vector3.zero;
                    bool hasDisp = TrackUncertaintyCalculator.TryGetMissileDispersion(missile, out disp);

                    // 3. Resolve true target position and aimpoint
                    Vector3 targetLocalPos = Vector3.zero;
                    bool hasTarget = (target != null && !target.disabled);
                    if (hasTarget)
                    {
                        Vector3 targetGlobal = target.GlobalPosition().AsVector3();
                        targetLocalPos = new Vector3(targetGlobal.x * mapFactor, targetGlobal.z * mapFactor, 0f);
                    }

                    Vector3 aimLocalPos;
                    if (hasTarget && hasDisp && disp != Vector3.zero)
                    {
                        Vector3 aimGlobal = target.GlobalPosition().AsVector3() + new Vector3(disp.x, 0f, disp.z);
                        aimLocalPos = new Vector3(aimGlobal.x * mapFactor, aimGlobal.z * mapFactor, 0f);
                    }
                    else if (f_missileAimPoint != null)
                    {
                        Vector3 apGlobal = f_missileAimPoint(missile).AsVector3();
                        aimLocalPos = new Vector3(apGlobal.x * mapFactor, apGlobal.z * mapFactor, 0f);
                    }
                    else if (hasTarget)
                    {
                        aimLocalPos = targetLocalPos;
                    }
                    else
                    {
                        continue;
                    }

                    // 4. Check seeker terminal acquisition status
                    MissileSeeker seeker = missile.GetComponent<MissileSeeker>();
                    bool isBallistic = SeekerDispersionHelper.IsBallisticOrInertial(seeker);
                    bool isIR = seeker is IRSeeker;
                    bool isLaser = seeker is LaserSeeker;
                    bool isSARH = seeker is SARHSeeker;
                    bool isLocked = SeekerDispersionHelper.IsSeekerTerminalLocked(missile);

                    slot.SetActive(true);

                    if (isLocked)
                    {
                        // Terminal lock achieved: guidance snaps directly to real target in Crimson Red
                        Vector3 destPos = hasTarget ? targetLocalPos : aimLocalPos;
                        PositionLine(slot.FlyToLineRt, missileLocalPos, destPos, 3.0f / layerScale, mapZ);
                        slot.FlyToLine.color = new Color(1.0f, 0.15f, 0.15f, 0.95f); // Crimson Red

                        // Hide unguided CEP visual cues once locked
                        if (slot.AimMarker.gameObject.activeSelf) slot.AimMarker.gameObject.SetActive(false);
                        if (slot.OffsetLine.gameObject.activeSelf) slot.OffsetLine.gameObject.SetActive(false);
                        if (slot.BasketRing.gameObject.activeSelf) slot.BasketRing.gameObject.SetActive(false);
                    }
                    else
                    {
                        // Midcourse unguided / datalink guidance towards displaced aimpoint
                        PositionLine(slot.FlyToLineRt, missileLocalPos, aimLocalPos, 2.5f / layerScale, mapZ);
                        slot.FlyToLine.color = new Color(1.0f, 0.70f, 0.15f, 0.88f); // Amber / Gold

                        // 1. Waypoint Marker at displaced aimpoint (omitted for direct IR line-of-sight and SARH continuous beam)
                        if (!isIR && !isSARH)
                        {
                            if (!slot.AimMarker.gameObject.activeSelf) slot.AimMarker.gameObject.SetActive(true);
                            slot.AimMarkerRt.localPosition = aimLocalPos;
                            slot.AimMarkerRt.sizeDelta = new Vector2(22.0f / layerScale, 22.0f / layerScale);
                            slot.AimMarkerRt.localEulerAngles = Vector3.zero;
                            slot.AimMarker.color = new Color(1.0f, 0.75f, 0.20f, 0.95f);
                        }
                        else
                        {
                            if (slot.AimMarker.gameObject.activeSelf) slot.AimMarker.gameObject.SetActive(false);
                        }

                        // 2. Terminal Seeker Pitbull Basket Ring (10 km for ARH, 7 km for ARAD, 2.8 km for Optical)
                        // Displayed for active radar, passive anti-radiation, and optical standoff weapons
                        // Suppressed for SARH missiles (R9, RAM-45) which continuously ride carrier radar reflection without an autonomous pitbull basket
                        float weaponBasketDist = 2800.0f;
                        if (seeker is ARHSeeker)
                        {
                            weaponBasketDist = (RadioWarsConfig.ARHTerminalActivationDistanceMeters != null)
                                ? RadioWarsConfig.ARHTerminalActivationDistanceMeters.Value
                                : 10000.0f;
                        }
                        else if (seeker is ARMSeeker)
                        {
                            weaponBasketDist = (RadioWarsConfig.ARADTerminalActivationDistanceMeters != null)
                                ? RadioWarsConfig.ARADTerminalActivationDistanceMeters.Value
                                : 7000.0f;
                        }
                        else
                        {
                            weaponBasketDist = (RadioWarsConfig.MissileTerminalActivationDistanceMeters != null)
                                ? RadioWarsConfig.MissileTerminalActivationDistanceMeters.Value
                                : 2800.0f;
                        }

                        if (!isBallistic && !isIR && !isLaser && !isSARH)
                        {
                            if (!slot.BasketRing.gameObject.activeSelf) slot.BasketRing.gameObject.SetActive(true);
                            float ringDiamScreen = 2.0f * weaponBasketDist * mapFactor * layerScale;
                            float clampedDiamScreen = Mathf.Clamp(ringDiamScreen, 12.0f, 8000.0f);
                            float ringDiam = clampedDiamScreen / layerScale;
                            slot.BasketRingRt.localPosition = aimLocalPos;
                            slot.BasketRingRt.sizeDelta = new Vector2(ringDiam, ringDiam);
                            slot.BasketRingRt.localEulerAngles = Vector3.zero;

                            // Visual color cue for pitbull basket
                            float dispMag = disp.magnitude;
                            bool targetInBasket = hasTarget && (dispMag <= weaponBasketDist);
                            slot.BasketRing.color = targetInBasket
                                ? new Color(0.2f, 0.90f, 0.50f, 0.65f)  // Green: Target is inside pitbull basket
                                : new Color(0.2f, 0.80f, 1.0f, 0.55f);   // Cyan: Standard phosphor basket
                        }
                        else
                        {
                            if (slot.BasketRing.gameObject.activeSelf) slot.BasketRing.gameObject.SetActive(false);
                        }

                        // 3. CEP Offset Vector (Connecting real target to displaced waypoint)
                        if (!isIR && hasTarget && disp.sqrMagnitude > 4.0f)
                        {
                            if (!slot.OffsetLine.gameObject.activeSelf) slot.OffsetLine.gameObject.SetActive(true);
                            PositionLine(slot.OffsetLineRt, targetLocalPos, aimLocalPos, 2.0f / layerScale, mapZ);

                            // Dash color: Green if target is inside basket, Amber if outside
                            float dispMag = disp.magnitude;
                            bool targetInBasket = hasTarget && (dispMag <= weaponBasketDist);
                            slot.OffsetLine.color = targetInBasket
                                ? new Color(0.3f, 1.0f, 0.5f, 0.80f)
                                : new Color(1.0f, 0.65f, 0.15f, 0.80f);
                        }
                        else
                        {
                            if (slot.OffsetLine.gameObject.activeSelf) slot.OffsetLine.gameObject.SetActive(false);
                        }
                    }

                    slotIndex++;
                }

                // Deactivate remaining unused pool slots
                for (int j = slotIndex; j < _slotPool.Count; j++)
                {
                    _slotPool[j].SetActive(false);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioWars] MissileMapDispersionVisualizer error: " + ex);
            }
        }

        private static void PositionLine(RectTransform rt, Vector3 localStart, Vector3 localEnd, float width, float mapZ)
        {
            rt.localPosition = localStart;
            Vector3 delta = localEnd - localStart;
            float len = delta.magnitude;
            if (len < 0.001f)
            {
                rt.sizeDelta = Vector2.zero;
                return;
            }

            float baseAngle = -Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg;
            float finalAngle = baseAngle + mapZ;
            rt.eulerAngles = new Vector3(0f, 0f, finalAngle);
            rt.sizeDelta = new Vector2(width, len);
            rt.localScale = Vector3.one;
        }

        private static void EnsurePool(DynamicMap map)
        {
            Transform parent = map.iconLayer.transform;
            if (_cachedParent != parent)
            {
                ClearAll();
                _cachedParent = parent;
            }

            if (_slotPool.Count >= POOL_SIZE) return;

            GameObject prefab = (s_getRadarVisPrefab != null) ? s_getRadarVisPrefab(map) : null;

            while (_slotPool.Count < POOL_SIZE)
            {
                MissileMapVisualSlot slot = CreateSlot(map, parent, prefab, _slotPool.Count);
                _slotPool.Add(slot);
            }
        }

        private static MissileMapVisualSlot CreateSlot(DynamicMap map, Transform parent, GameObject prefab, int index)
        {
            GameObject rootGo = new GameObject("MissileMapAimpointSlot_" + index, typeof(RectTransform));
            rootGo.transform.SetParent(parent, false);
            RectTransform rootRt = rootGo.GetComponent<RectTransform>();
            rootRt.anchorMin = new Vector2(0.5f, 0.5f);
            rootRt.anchorMax = new Vector2(0.5f, 0.5f);
            rootRt.pivot = new Vector2(0.5f, 0.5f);
            rootRt.localPosition = Vector3.zero;
            rootRt.localEulerAngles = Vector3.zero;
            rootRt.localScale = Vector3.one;

            // 1. Fly-To Line
            Image flyLine = CreateImage(rootGo.transform, "FlyToLine", prefab);
            flyLine.sprite = RWRConeGraphic.GetOrCreateSolidLineSprite();
            flyLine.type = Image.Type.Simple;
            RectTransform flyRt = flyLine.rectTransform;
            flyRt.anchorMin = new Vector2(0.5f, 0.5f);
            flyRt.anchorMax = new Vector2(0.5f, 0.5f);
            flyRt.pivot = new Vector2(0.5f, 0.0f); // Bottom-center pivot

            // 2. Aimpoint Waypoint Marker
            Image aimMarker = CreateImage(rootGo.transform, "AimMarker", prefab);
            aimMarker.sprite = RWRConeGraphic.GetOrCreateCrosshairSprite();
            aimMarker.type = Image.Type.Simple;
            aimMarker.preserveAspect = true;
            RectTransform aimRt = aimMarker.rectTransform;
            aimRt.anchorMin = new Vector2(0.5f, 0.5f);
            aimRt.anchorMax = new Vector2(0.5f, 0.5f);
            aimRt.pivot = new Vector2(0.5f, 0.5f);

            // 3. CEP Offset Line (Dashed)
            Image offsetLine = CreateImage(rootGo.transform, "OffsetLine", prefab);
            offsetLine.sprite = RWRConeGraphic.GetOrCreateDashedLineSprite();
            offsetLine.type = Image.Type.Tiled;
            RectTransform offsetRt = offsetLine.rectTransform;
            offsetRt.anchorMin = new Vector2(0.5f, 0.5f);
            offsetRt.anchorMax = new Vector2(0.5f, 0.5f);
            offsetRt.pivot = new Vector2(0.5f, 0.0f);

            // 4. Pitbull Basket Ring
            Image basketRing = CreateImage(rootGo.transform, "PitbullBasketRing", prefab);
            basketRing.sprite = RWRConeGraphic.GetOrCreateUncertaintyCircleSprite();
            basketRing.type = Image.Type.Simple;
            basketRing.preserveAspect = true;
            RectTransform basketRt = basketRing.rectTransform;
            basketRt.anchorMin = new Vector2(0.5f, 0.5f);
            basketRt.anchorMax = new Vector2(0.5f, 0.5f);
            basketRt.pivot = new Vector2(0.5f, 0.5f);

            MissileMapVisualSlot slot = new MissileMapVisualSlot
            {
                Root = rootGo,
                FlyToLine = flyLine,
                FlyToLineRt = flyRt,
                AimMarker = aimMarker,
                AimMarkerRt = aimRt,
                OffsetLine = offsetLine,
                OffsetLineRt = offsetRt,
                BasketRing = basketRing,
                BasketRingRt = basketRt
            };

            slot.SetActive(false);
            return slot;
        }

        private static Image CreateImage(Transform parent, string name, GameObject prefab)
        {
            GameObject go;
            if (prefab != null)
            {
                go = UnityEngine.Object.Instantiate(prefab, parent);
                go.name = name;
            }
            else
            {
                go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(parent, false);
            }

            Image img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.transform.localPosition = Vector3.zero;
            img.transform.localEulerAngles = Vector3.zero;
            img.transform.localScale = Vector3.one;
            return img;
        }

        private static void HideAllSlots()
        {
            for (int i = 0; i < _slotPool.Count; i++)
            {
                if (_slotPool[i] != null)
                {
                    _slotPool[i].SetActive(false);
                }
            }
        }

        /// <summary>
        /// Cleans up all visual objects upon level exit, scene load, or mod toggle.
        /// </summary>
        public static void ClearAll()
        {
            for (int i = 0; i < _slotPool.Count; i++)
            {
                if (_slotPool[i] != null && _slotPool[i].Root != null)
                {
                    UnityEngine.Object.Destroy(_slotPool[i].Root);
                }
            }
            _slotPool.Clear();
            _cachedParent = null;
        }
    }
}
