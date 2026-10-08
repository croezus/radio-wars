using System;
using UnityEngine;
using RadioWars.Config;

namespace RadioWars.Core
{
    /// <summary>
    /// Generates and manages procedural phosphor sector / bearing uncertainty cone graphics
    /// for RWR threat strobes across the dynamic map and cockpit tactical MFD displays.
    /// Replaces infinitely thin pencil lines with authentic angular sectors to prevent pinpoint triangulation.
    /// </summary>
    public static class RWRConeGraphic
    {
        private static Sprite _cachedConeSprite;
        private static Texture2D _cachedConeTexture;

        /// <summary>
        /// Retrieves or lazily creates a cached 128x256 smooth phosphor cone sprite with bottom-center pivot (0.5, 0.0).
        /// Employs a cosine angular cross-section and smooth radial tip falloff. Zero allocations during flight.
        /// </summary>
        public static Sprite GetOrCreateConeSprite()
        {
            if (_cachedConeSprite != null)
            {
                return _cachedConeSprite;
            }

            int width = 128;
            int height = 256;

            _cachedConeTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            _cachedConeTexture.name = "RWR_BearingCone_Texture";
            _cachedConeTexture.wrapMode = TextureWrapMode.Clamp;
            _cachedConeTexture.filterMode = FilterMode.Bilinear;

            Color[] pixels = new Color[width * height];
            float invW = 1.0f / (width - 1);
            float invH = 1.0f / (height - 1);

            for (int y = 0; y < height; y++)
            {
                float v = y * invH; // 0 at apex, 1 at outer range
                float halfWidthNorm = 0.5f * Mathf.Max(0.005f, v);

                // Radial distance attenuation:
                // Fast gentle apex rise near ownship, solid luminous beam body, and soft cosine dispersion at tip
                float radialFactor;
                if (v < 0.02f)
                {
                    radialFactor = v / 0.02f; // Soft, tight apex right at ownship
                }
                else if (v < 0.75f)
                {
                    // Main body: solid glowing illumination with gentle transition (1.0 -> 0.75)
                    radialFactor = Mathf.Lerp(1.0f, 0.75f, (v - 0.02f) / 0.73f);
                }
                else
                {
                    // Tip feather: soft dispersion at outer perimeter (reaching the 85% distance margin)
                    float tipT = (v - 0.75f) / 0.25f;
                    radialFactor = 0.75f * Mathf.Cos(tipT * Mathf.PI * 0.5f);
                }

                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    float u = x * invW;
                    float distFromCenter = Mathf.Abs(u - 0.5f);

                    if (distFromCenter <= halfWidthNorm)
                    {
                        float delta = distFromCenter / halfWidthNorm; // 0 at core axis, 1 at lateral edge
                        float angularCos = Mathf.Cos(delta * Mathf.PI * 0.5f);
                        float angularFactor = angularCos * angularCos; // Smooth cos^2 beam cross-section with feathered edges

                        float alpha = Mathf.Clamp01(angularFactor * radialFactor);
                        pixels[rowOffset + x] = new Color(1.0f, 1.0f, 1.0f, alpha);
                    }
                    else
                    {
                        pixels[rowOffset + x] = new Color(1.0f, 1.0f, 1.0f, 0.0f);
                    }
                }
            }

            _cachedConeTexture.SetPixels(pixels);
            _cachedConeTexture.Apply(false, true); // Upload and make non-readable to save VRAM

            _cachedConeSprite = Sprite.Create(
                _cachedConeTexture,
                new Rect(0, 0, width, height),
                new Vector2(0.5f, 0.0f), // Apex pivot at center bottom
                100.0f,
                0,
                SpriteMeshType.FullRect
            );
            _cachedConeSprite.name = "RWR_BearingCone_Sprite";

            return _cachedConeSprite;
        }

        /// <summary>
        /// Computes effective half-angle (degrees) for the threat bearing uncertainty cone
        /// on the tactical map (M key) and cockpit minimap (DynamicMap).
        /// </summary>
        public static float GetConeHalfAngle(RwrThreatState state, RwrTier tier)
        {
            return GetMapConeHalfAngle(state, tier);
        }

        /// <summary>
        /// Computes effective half-angle (degrees) for the threat bearing uncertainty cone
        /// on the tactical map (M key) and cockpit minimap (DynamicMap).
        /// </summary>
        public static float GetMapConeHalfAngle(RwrThreatState state, RwrTier tier)
        {
            float baseAngle = 10.0f; // Default 10° half-angle (20° full sector)

            if (RadioWarsConfig.RWRSearchSectorAngle != null &&
                RadioWarsConfig.RWRTrackSectorAngle != null &&
                RadioWarsConfig.RWRMissileSectorAngle != null)
            {
                switch (state)
                {
                    case RwrThreatState.MissileGuidance:
                        baseAngle = RadioWarsConfig.RWRMissileSectorAngle.Value * 0.5f;
                        break;
                    case RwrThreatState.Track:
                        baseAngle = RadioWarsConfig.RWRTrackSectorAngle.Value * 0.5f;
                        break;
                    case RwrThreatState.Search:
                    default:
                        baseAngle = RadioWarsConfig.RWRSearchSectorAngle.Value * 0.5f;
                        break;
                }
            }
            else
            {
                switch (state)
                {
                    case RwrThreatState.MissileGuidance:
                        baseAngle = 3.5f;
                        break;
                    case RwrThreatState.Track:
                        baseAngle = 6.0f;
                        break;
                    case RwrThreatState.Search:
                    default:
                        baseAngle = 10.0f;
                        break;
                }
            }

            // Apply Map/Minimap hardcore uncertainty multiplier
            float uncertMultiplier = DynamicMap.mapMaximized
                ? (RadioWarsConfig.RWRUncertaintyMultiplier != null ? RadioWarsConfig.RWRUncertaintyMultiplier.Value : 1.0f)
                : (RadioWarsConfig.MinimapUncertaintyMultiplier != null ? RadioWarsConfig.MinimapUncertaintyMultiplier.Value : 1.0f);
            baseAngle *= Mathf.Clamp(uncertMultiplier, 0.1f, 5.0f);

            bool scaleWithTier = RadioWarsConfig.ScaleConeWithRWRTier == null || RadioWarsConfig.ScaleConeWithRWRTier.Value;
            if (scaleWithTier)
            {
                switch (tier)
                {
                    case RwrTier.Tier1_Basic:
                        baseAngle *= 1.25f; // Analog 4-quadrant receiver has slightly wider azimuth uncertainty
                        break;
                    case RwrTier.Tier3_Advanced:
                        baseAngle *= 0.70f; // Digital phase interferometry yields focused bearing
                        break;
                    case RwrTier.Tier2_Standard:
                    default:
                        break;
                }
            }

            return Mathf.Clamp(baseAngle, 0.5f, 45.0f);
        }

        /// <summary>
        /// Computes effective half-angle (degrees) for the threat bearing uncertainty cone
        /// on the cockpit radar display (TacScreen) inside the aircraft.
        /// </summary>
        public static float GetCockpitConeHalfAngle(RwrThreatState state, RwrTier tier)
        {
            float baseAngle = 10.0f; // Default 10° half-angle (20° full sector)

            if (RadioWarsConfig.CockpitRWRSearchSectorAngle != null &&
                RadioWarsConfig.CockpitRWRTrackSectorAngle != null &&
                RadioWarsConfig.CockpitRWRMissileSectorAngle != null)
            {
                switch (state)
                {
                    case RwrThreatState.MissileGuidance:
                        baseAngle = RadioWarsConfig.CockpitRWRMissileSectorAngle.Value * 0.5f;
                        break;
                    case RwrThreatState.Track:
                        baseAngle = RadioWarsConfig.CockpitRWRTrackSectorAngle.Value * 0.5f;
                        break;
                    case RwrThreatState.Search:
                    default:
                        baseAngle = RadioWarsConfig.CockpitRWRSearchSectorAngle.Value * 0.5f;
                        break;
                }
            }
            else
            {
                return GetMapConeHalfAngle(state, tier);
            }

            // Apply Cockpit-specific uncertainty multiplier
            if (RadioWarsConfig.CockpitRWRUncertaintyMultiplier != null)
            {
                baseAngle *= Mathf.Clamp(RadioWarsConfig.CockpitRWRUncertaintyMultiplier.Value, 0.1f, 5.0f);
            }
            else if (RadioWarsConfig.RWRUncertaintyMultiplier != null)
            {
                baseAngle *= Mathf.Clamp(RadioWarsConfig.RWRUncertaintyMultiplier.Value, 0.1f, 5.0f);
            }

            bool scaleWithTier = RadioWarsConfig.ScaleConeWithRWRTier == null || RadioWarsConfig.ScaleConeWithRWRTier.Value;
            if (scaleWithTier)
            {
                switch (tier)
                {
                    case RwrTier.Tier1_Basic:
                        baseAngle *= 1.25f;
                        break;
                    case RwrTier.Tier3_Advanced:
                        baseAngle *= 0.70f;
                        break;
                    case RwrTier.Tier2_Standard:
                    default:
                        break;
                }
            }

            return Mathf.Clamp(baseAngle, 0.5f, 45.0f);
        }

        /// <summary>
        /// Calculates the outer base width of the uncertainty cone for a given length and half-angle.
        /// Base width = 2 * length * tan(halfAngleDeg).
        /// </summary>
        public static float GetConeBaseWidth(float length, float halfAngleDeg)
        {
            float halfRad = Mathf.Clamp(halfAngleDeg, 0.5f, 50.0f) * Mathf.Deg2Rad;
            return 2.0f * length * Mathf.Tan(halfRad);
        }

        private static Sprite _cachedCircleSprite;
        private static Texture2D _cachedCircleTexture;

        /// <summary>
        /// Retrieves or lazily creates a cached 128x128 procedural ESM uncertainty circle sprite.
        /// Renders an authentic phosphor CEP (Circular Error Probable) boundary:
        /// Glowing outer rim with soft antialiased stroke and a faint, translucent phosphor interior fill.
        /// Center pivot (0.5, 0.5) for direct placement on estimated target coordinates.
        /// </summary>
        public static Sprite GetOrCreateUncertaintyCircleSprite()
        {
            if (_cachedCircleSprite != null)
            {
                return _cachedCircleSprite;
            }

            int size = 128;
            _cachedCircleTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            _cachedCircleTexture.name = "RWR_UncertaintyCircle_Texture";
            _cachedCircleTexture.wrapMode = TextureWrapMode.Clamp;
            _cachedCircleTexture.filterMode = FilterMode.Bilinear;

            Color[] pixels = new Color[size * size];
            float center = (size - 1) * 0.5f;
            float invRadius = 1.0f / center;

            for (int y = 0; y < size; y++)
            {
                float dy = (y - center) * invRadius;
                int row = y * size;
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center) * invRadius;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    if (dist > 1.0f)
                    {
                        pixels[row + x] = new Color(1.0f, 1.0f, 1.0f, 0.0f);
                    }
                    else
                    {
                        float alpha;
                        if (dist <= 0.82f)
                        {
                            // Distinct semi-transparent tactical phosphor zone fill
                            alpha = 0.38f;
                        }
                        else if (dist <= 0.95f)
                        {
                            // Crisp glowing phosphor perimeter boundary ring
                            float strokeT = (dist - 0.82f) / 0.13f;
                            alpha = Mathf.Lerp(0.38f, 0.95f, strokeT);
                        }
                        else
                        {
                            // Antialiased outer feather
                            float featherT = (dist - 0.95f) / 0.05f;
                            alpha = 0.95f * (1.0f - featherT);
                        }
                        pixels[row + x] = new Color(1.0f, 1.0f, 1.0f, alpha);
                    }
                }
            }

            _cachedCircleTexture.SetPixels(pixels);
            _cachedCircleTexture.Apply(false, true);

            _cachedCircleSprite = Sprite.Create(
                _cachedCircleTexture,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), // Center pivot
                100.0f,
                0,
                SpriteMeshType.FullRect
            );
            _cachedCircleSprite.name = "RWR_UncertaintyCircle_Sprite";

            return _cachedCircleSprite;
        }

        /// <summary>
        /// Computes half-angle in degrees for the sleek narrow ESM wedge strobe.
        /// Defaults to RWRStrobeNarrowAngle * 0.5 (2.0° half-angle / 4.0° full sector) scaled by RWR tier.
        /// </summary>
        public static float GetNarrowWedgeHalfAngle(RwrTier tier)
        {
            float narrowFull = RadioWarsConfig.RWRStrobeNarrowAngle != null
                ? RadioWarsConfig.RWRStrobeNarrowAngle.Value
                : 4.0f;
            float halfAngle = narrowFull * 0.5f;

            bool scaleWithTier = RadioWarsConfig.ScaleConeWithRWRTier == null || RadioWarsConfig.ScaleConeWithRWRTier.Value;
            if (scaleWithTier)
            {
                switch (tier)
                {
                    case RwrTier.Tier1_Basic:
                        halfAngle *= 1.25f;
                        break;
                    case RwrTier.Tier3_Advanced:
                        halfAngle *= 0.70f;
                        break;
                    case RwrTier.Tier2_Standard:
                    default:
                        break;
                }
            }

            return Mathf.Clamp(halfAngle, 0.5f, 15.0f);
        }

        private static Sprite _cachedSolidLineSprite;
        private static Texture2D _cachedSolidLineTexture;

        public static Sprite GetOrCreateSolidLineSprite()
        {
            if (_cachedSolidLineSprite != null) return _cachedSolidLineSprite;

            int size = 4;
            _cachedSolidLineTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            _cachedSolidLineTexture.name = "RWR_SolidLine_Texture";
            _cachedSolidLineTexture.wrapMode = TextureWrapMode.Clamp;
            Color[] pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
            _cachedSolidLineTexture.SetPixels(pixels);
            _cachedSolidLineTexture.Apply(false, true);

            _cachedSolidLineSprite = Sprite.Create(
                _cachedSolidLineTexture,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.0f),
                100.0f,
                0,
                SpriteMeshType.FullRect
            );
            _cachedSolidLineSprite.name = "RWR_SolidLine_Sprite";
            return _cachedSolidLineSprite;
        }

        private static Sprite _cachedDashedLineSprite;
        private static Texture2D _cachedDashedLineTexture;

        public static Sprite GetOrCreateDashedLineSprite()
        {
            if (_cachedDashedLineSprite != null) return _cachedDashedLineSprite;

            int w = 8;
            int h = 32;
            _cachedDashedLineTexture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            _cachedDashedLineTexture.name = "RWR_DashedLine_Texture";
            _cachedDashedLineTexture.wrapMode = TextureWrapMode.Repeat;
            Color[] pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                bool solid = (y < 16);
                Color c = solid ? Color.white : new Color(1.0f, 1.0f, 1.0f, 0.0f);
                for (int x = 0; x < w; x++)
                {
                    pixels[y * w + x] = c;
                }
            }
            _cachedDashedLineTexture.SetPixels(pixels);
            _cachedDashedLineTexture.Apply(false, true);

            _cachedDashedLineSprite = Sprite.Create(
                _cachedDashedLineTexture,
                new Rect(0, 0, w, h),
                new Vector2(0.5f, 0.0f),
                100.0f,
                0,
                SpriteMeshType.FullRect
            );
            _cachedDashedLineSprite.name = "RWR_DashedLine_Sprite";
            return _cachedDashedLineSprite;
        }

        private static Sprite _cachedCrosshairSprite;
        private static Texture2D _cachedCrosshairTexture;

        public static Sprite GetOrCreateCrosshairSprite()
        {
            if (_cachedCrosshairSprite != null) return _cachedCrosshairSprite;

            int size = 32;
            _cachedCrosshairTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            _cachedCrosshairTexture.name = "RWR_Crosshair_Texture";
            _cachedCrosshairTexture.wrapMode = TextureWrapMode.Clamp;
            Color[] pixels = new Color[size * size];
            float center = (size - 1) * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Abs(x - center);
                    float dy = Mathf.Abs(y - center);

                    float manhattan = dx + dy;
                    bool isDiamond = Mathf.Abs(manhattan - 9.0f) <= 1.2f;
                    bool isCrossX = (dy <= 1.0f) && (dx >= 3.0f && dx <= 13.0f);
                    bool isCrossY = (dx <= 1.0f) && (dy >= 3.0f && dy <= 13.0f);
                    bool isCenter = (dx <= 1.5f && dy <= 1.5f);

                    float alpha = (isDiamond || isCrossX || isCrossY || isCenter) ? 1.0f : 0.0f;
                    pixels[y * size + x] = new Color(1.0f, 1.0f, 1.0f, alpha);
                }
            }
            _cachedCrosshairTexture.SetPixels(pixels);
            _cachedCrosshairTexture.Apply(false, true);

            _cachedCrosshairSprite = Sprite.Create(
                _cachedCrosshairTexture,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f),
                100.0f,
                0,
                SpriteMeshType.FullRect
            );
            _cachedCrosshairSprite.name = "RWR_Crosshair_Sprite";
            return _cachedCrosshairSprite;
        }
    }

    /// <summary>
    /// Backward-compatibility alias for RWRConeGraphic.
    /// </summary>
    public static class RwrConeGraphic
    {
        public static Sprite GetOrCreateConeSprite() { return RWRConeGraphic.GetOrCreateConeSprite(); }
        public static Sprite GetOrCreateUncertaintyCircleSprite() { return RWRConeGraphic.GetOrCreateUncertaintyCircleSprite(); }
        public static Sprite GetOrCreateSolidLineSprite() { return RWRConeGraphic.GetOrCreateSolidLineSprite(); }
        public static Sprite GetOrCreateDashedLineSprite() { return RWRConeGraphic.GetOrCreateDashedLineSprite(); }
        public static Sprite GetOrCreateCrosshairSprite() { return RWRConeGraphic.GetOrCreateCrosshairSprite(); }
        public static float GetConeHalfAngle(RwrThreatState state, RwrTier tier) { return RWRConeGraphic.GetConeHalfAngle(state, tier); }
        public static float GetMapConeHalfAngle(RwrThreatState state, RwrTier tier) { return RWRConeGraphic.GetMapConeHalfAngle(state, tier); }
        public static float GetCockpitConeHalfAngle(RwrThreatState state, RwrTier tier) { return RWRConeGraphic.GetCockpitConeHalfAngle(state, tier); }
        public static float GetConeBaseWidth(float coneLength, float halfAngleDeg) { return RWRConeGraphic.GetConeBaseWidth(coneLength, halfAngleDeg); }
        public static float GetNarrowWedgeHalfAngle(RwrTier tier) { return RWRConeGraphic.GetNarrowWedgeHalfAngle(tier); }
    }
}
