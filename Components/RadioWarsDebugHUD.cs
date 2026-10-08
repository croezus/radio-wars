using System;
using System.Collections.Generic;
using UnityEngine;
using RadioWars.Config;
using RadioWars.Core;

namespace RadioWars.Components
{
    public class RadioWarsDebugHUD : MonoBehaviour
    {
        private Rect _windowRect = new Rect(20, 60, 480, 0);
        private float _toastTimer = 0.0f;
        private string _toastMessage = "";

        // UI Styles
        private GUIStyle _windowStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _badgeActiveStyle;
        private GUIStyle _badgeVanillaStyle;
        private GUIStyle _subHeaderStyle;
        private GUIStyle _sectionHeaderStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _valueStyle;
        private GUIStyle _threatHeaderStyle;
        private GUIStyle _threatSubStyle;

        // Textures
        private Texture2D _whiteTex;
        private Texture2D _windowBgTex;
        private Texture2D _dividerTex;

        private void Awake()
        {
            _whiteTex = MakeTex(1, 1, Color.white);
            _dividerTex = MakeTex(1, 1, new Color(0.20f, 0.28f, 0.38f, 0.70f));
        }

        private void OnDestroy()
        {
            if (_whiteTex != null) Destroy(_whiteTex);
            if (_windowBgTex != null) Destroy(_windowBgTex);
            if (_dividerTex != null) Destroy(_dividerTex);
        }

        private void Update()
        {
            // 1. Hotkey Handling
            if (RadioWarsConfig.ToggleModKey != null && Input.GetKeyDown(RadioWarsConfig.ToggleModKey.Value))
            {
                RadioWarsConfig.ModEnabled.Value = !RadioWarsConfig.ModEnabled.Value;
                KeyCode key = RadioWarsConfig.ToggleModKey.Value;
                _toastMessage = RadioWarsConfig.ModEnabled.Value ? 
                    string.Format("[RADIO WARS] MOD PHYSICS ENGAGED ({0})", key) : 
                    string.Format("[RADIO WARS] VANILLA RADAR RESTORED ({0})", key);
                _toastTimer = 3.5f;
            }

            if (RadioWarsConfig.ToggleHUDKey != null && Input.GetKeyDown(RadioWarsConfig.ToggleHUDKey.Value))
            {
                RadioWarsConfig.ShowDebugHUD.Value = !RadioWarsConfig.ShowDebugHUD.Value;
                KeyCode key = RadioWarsConfig.ToggleHUDKey.Value;
                _toastMessage = RadioWarsConfig.ShowDebugHUD.Value ? 
                    string.Format("[RADIO WARS] TELEMETRY HUD SHOWN ({0})", key) : 
                    string.Format("[RADIO WARS] TELEMETRY HUD HIDDEN ({0})", key);
                _toastTimer = 2.0f;
            }

            if (RadioWarsConfig.ToggleGizmosKey != null && Input.GetKeyDown(RadioWarsConfig.ToggleGizmosKey.Value))
            {
                RadioWarsConfig.DrawDebugGizmos.Value = !RadioWarsConfig.DrawDebugGizmos.Value;
                bool active = RadioWarsConfig.DrawDebugGizmos.Value;
                KeyCode key = RadioWarsConfig.ToggleGizmosKey.Value;
                _toastMessage = active ? 
                    string.Format("[RADIO WARS] DEBUG GIZMOS: ON ({0})", key) : 
                    string.Format("[RADIO WARS] DEBUG GIZMOS: OFF ({0})", key);
                _toastTimer = 2.0f;

                if (!active && RadarDebugVisualizer.Instance != null)
                {
                    RadarDebugVisualizer.Instance.HideAllLines();
                }
            }

            if (RadioWarsConfig.ToggleDatalinkVisualizerKey != null && Input.GetKeyDown(RadioWarsConfig.ToggleDatalinkVisualizerKey.Value))
            {
                RadioWarsConfig.DrawDatalinkVisualizer.Value = !RadioWarsConfig.DrawDatalinkVisualizer.Value;
                bool active = RadioWarsConfig.DrawDatalinkVisualizer.Value;
                KeyCode key = RadioWarsConfig.ToggleDatalinkVisualizerKey.Value;
                _toastMessage = active ? 
                    string.Format("[RADIO WARS] DATALINK VISUALIZER: ON ({0})", key) : 
                    string.Format("[RADIO WARS] DATALINK VISUALIZER: OFF ({0})", key);
                _toastTimer = 2.0f;

                if (!active && DatalinkDebugVisualizer.Instance != null)
                {
                    DatalinkDebugVisualizer.Instance.HideAllLines();
                }
            }

            if (_toastTimer > 0.0f)
            {
                _toastTimer -= Time.deltaTime;
            }

            // 2. In-Game Gizmos & Raycast Debug Drawing
            if (RadioWarsConfig.DrawDebugGizmos != null && RadioWarsConfig.DrawDebugGizmos.Value)
            {
                DrawDebugVisuals();
            }
        }

        private void DrawDebugVisuals()
        {
            if (!RadarTelemetry.Latest.HasTarget || (Time.time - RadarTelemetry.Latest.LastUpdateTime > 2.0f))
            {
                return;
            }

            Vector3 radarPos = RadarTelemetry.Latest.RadarPos;
            Vector3 targetPos = RadarTelemetry.Latest.TargetPos;
            Vector3 targetVel = RadarTelemetry.Latest.TargetVel;
            Color rayColor = RadarTelemetry.Latest.RayColor;

            // 1. Radar line of sight ray
            Debug.DrawLine(radarPos, targetPos, rayColor, 0.0f, false);

            // 2. Target velocity vector (Cyan)
            if (targetVel.sqrMagnitude > 1.0f)
            {
                Debug.DrawLine(targetPos, targetPos + targetVel, Color.cyan, 0.0f, false);

                // 3. Ground-relative radial projection onto radar line of sight (Magenta)
                Vector3 toTarget = (targetPos - radarPos);
                if (toTarget.sqrMagnitude > 1.0f)
                {
                    Vector3 losUnit = toTarget.normalized;
                    Vector3 radialProj = Vector3.Project(targetVel, losUnit);
                    Debug.DrawLine(targetPos, targetPos + radialProj, Color.magenta, 0.0f, false);

                    // 4. Clutter notch threshold gate marks (Yellow crossbars)
                    float notchThresh = RadioWarsConfig.EffectiveNotchThreshold;
                    Vector3 notchVec = losUnit * notchThresh;
                    Vector3 perp = Vector3.Cross(losUnit, Vector3.up).normalized * 5.0f;

                    Debug.DrawLine(targetPos + notchVec - perp, targetPos + notchVec + perp, Color.yellow, 0.0f, false);
                    Debug.DrawLine(targetPos - notchVec - perp, targetPos - notchVec + perp, Color.yellow, 0.0f, false);
                }
            }
        }

        private void InitStyles()
        {
            if (_windowStyle != null) return;

            // Window Base
            _windowStyle = new GUIStyle(GUI.skin.box);
            _windowBgTex = MakeTex(2, 2, new Color(0.05f, 0.08f, 0.12f, 0.94f));
            _windowStyle.normal.background = _windowBgTex;
            _windowStyle.padding = new RectOffset(16, 16, 12, 14);
            _windowStyle.border = new RectOffset(1, 1, 1, 1);

            // Title Style (Left aligned in custom header)
            _titleStyle = new GUIStyle(GUI.skin.label);
            _titleStyle.fontSize = 12;
            _titleStyle.fontStyle = FontStyle.Bold;
            _titleStyle.normal.textColor = new Color(0.88f, 0.93f, 0.98f);
            _titleStyle.alignment = TextAnchor.MiddleLeft;

            // Status Badge: Mod Active (Right aligned)
            _badgeActiveStyle = new GUIStyle(GUI.skin.label);
            _badgeActiveStyle.fontSize = 11;
            _badgeActiveStyle.fontStyle = FontStyle.Bold;
            _badgeActiveStyle.normal.textColor = new Color(0.20f, 0.85f, 0.45f);
            _badgeActiveStyle.alignment = TextAnchor.MiddleRight;

            // Status Badge: Vanilla Active (Right aligned)
            _badgeVanillaStyle = new GUIStyle(GUI.skin.label);
            _badgeVanillaStyle.fontSize = 11;
            _badgeVanillaStyle.fontStyle = FontStyle.Bold;
            _badgeVanillaStyle.normal.textColor = new Color(0.95f, 0.65f, 0.15f);
            _badgeVanillaStyle.alignment = TextAnchor.MiddleRight;

            // Subheader (Hotkeys bar)
            _subHeaderStyle = new GUIStyle(GUI.skin.label);
            _subHeaderStyle.fontSize = 10;
            _subHeaderStyle.normal.textColor = new Color(0.55f, 0.65f, 0.75f);
            _subHeaderStyle.alignment = TextAnchor.MiddleLeft;

            // Section Header
            _sectionHeaderStyle = new GUIStyle(GUI.skin.label);
            _sectionHeaderStyle.fontSize = 11;
            _sectionHeaderStyle.fontStyle = FontStyle.Bold;
            _sectionHeaderStyle.normal.textColor = new Color(0.35f, 0.75f, 0.90f);
            _sectionHeaderStyle.padding = new RectOffset(0, 0, 4, 2);

            // Standard Data Label (Left column)
            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.fontSize = 11;
            _labelStyle.normal.textColor = new Color(0.68f, 0.76f, 0.85f);
            _labelStyle.wordWrap = false;
            _labelStyle.alignment = TextAnchor.MiddleLeft;

            // Standard Data Value (Right column)
            _valueStyle = new GUIStyle(GUI.skin.label);
            _valueStyle.fontSize = 11;
            _valueStyle.fontStyle = FontStyle.Bold;
            _valueStyle.normal.textColor = new Color(0.92f, 0.95f, 0.98f);
            _valueStyle.wordWrap = false;
            _valueStyle.alignment = TextAnchor.MiddleRight;

            // Threat item primary header
            _threatHeaderStyle = new GUIStyle(GUI.skin.label);
            _threatHeaderStyle.fontSize = 11;
            _threatHeaderStyle.fontStyle = FontStyle.Bold;
            _threatHeaderStyle.normal.textColor = new Color(0.90f, 0.94f, 0.98f);
            _threatHeaderStyle.wordWrap = false;
            _threatHeaderStyle.alignment = TextAnchor.MiddleLeft;

            // Threat item sub details
            _threatSubStyle = new GUIStyle(GUI.skin.label);
            _threatSubStyle.fontSize = 10;
            _threatSubStyle.normal.textColor = new Color(0.60f, 0.70f, 0.80f);
            _threatSubStyle.wordWrap = false;
            _threatSubStyle.alignment = TextAnchor.MiddleLeft;

        }

        private Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++) pix[i] = col;
            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }

        private void OnGUI()
        {
            InitStyles();

            // 1. Toast Notification Banner
            if (_toastTimer > 0.0f)
            {
                float toastW = 440;
                float toastH = 34;
                Rect toastRect = new Rect((Screen.width - toastW) * 0.5f, 25, toastW, toastH);
                Color bannerCol = RadioWarsConfig.IsModActive ? 
                    new Color(0.06f, 0.28f, 0.14f, 0.92f) : 
                    new Color(0.38f, 0.22f, 0.05f, 0.92f);

                GUI.color = bannerCol;
                GUI.DrawTexture(toastRect, _whiteTex);
                GUI.color = Color.white;

                GUIStyle toastStyle = RadioWarsConfig.IsModActive ? _badgeActiveStyle : _badgeVanillaStyle;
                toastStyle.alignment = TextAnchor.MiddleCenter;
                GUI.Label(toastRect, _toastMessage, toastStyle);
                toastStyle.alignment = TextAnchor.MiddleRight;
            }

            // 2. Telemetry HUD Window: dynamically sizing without scrollbars
            if (RadioWarsConfig.ShowDebugHUD != null && RadioWarsConfig.ShowDebugHUD.Value)
            {
                if (Event.current.type == EventType.Layout)
                {
                    _windowRect.width = 0;
                    _windowRect.height = 0;
                }

                _windowRect = GUILayout.Window(98451, _windowRect, DrawTelemetryWindow, "", _windowStyle, 
                    GUILayout.MinWidth(480), GUILayout.ExpandWidth(true));

                // Clamp to screen bounds
                _windowRect.x = Mathf.Clamp(_windowRect.x, 0, Mathf.Max(0, Screen.width - _windowRect.width));
                _windowRect.y = Mathf.Clamp(_windowRect.y, 0, Mathf.Max(0, Screen.height - _windowRect.height));
            }
        }

        private void DrawTelemetryWindow(int windowID)
        {
            GUILayout.BeginVertical();

            // 1. Top Custom Title Bar (Zero Overlap)
            GUILayout.BeginHorizontal(GUILayout.Height(22));
            GUILayout.Label("RADIO WARS // AVIONICS TELEMETRY", _titleStyle);
            GUILayout.FlexibleSpace();
            bool modActive = RadioWarsConfig.IsModActive;
            GUILayout.Label(modActive ? "[MOD SIMULATION: ACTIVE]" : "[VANILLA RADAR: ACTIVE]", 
                modActive ? _badgeActiveStyle : _badgeVanillaStyle);
            GUILayout.EndHorizontal();

            // 2. Subheader Controls Bar
            GUILayout.Space(2);
            string hotkeysStr = string.Format("Hotkeys: [{0}] A/B Toggle  |  [{1}] HUD  |  [{2}] Gizmos  |  [{3}] Datalink",
                RadioWarsConfig.ToggleModKey != null ? RadioWarsConfig.ToggleModKey.Value : KeyCode.F10,
                RadioWarsConfig.ToggleHUDKey != null ? RadioWarsConfig.ToggleHUDKey.Value : KeyCode.F9,
                RadioWarsConfig.ToggleGizmosKey != null ? RadioWarsConfig.ToggleGizmosKey.Value : KeyCode.F11,
                RadioWarsConfig.ToggleDatalinkVisualizerKey != null ? RadioWarsConfig.ToggleDatalinkVisualizerKey.Value : KeyCode.F8);
            GUILayout.Label(hotkeysStr, _subHeaderStyle);

            DrawDivider();
            GUILayout.Space(4);

            // 3. Target Telemetry Block
            if (RadarTelemetry.Latest.HasTarget && (Time.time - RadarTelemetry.Latest.LastUpdateTime < 3.0f))
            {
                RadarTelemetrySnapshot data = RadarTelemetry.Latest;

                DrawSectionHeader("PRIMARY RADAR TARGET TRACK");
                DrawRow("Designation", data.TargetName);
                DrawRow("Slant Range", string.Format("{0:F1} km ({1:F0} m)", data.SlantRangeMeters * 0.001f, data.SlantRangeMeters));
                DrawRow("Elevation Angle", string.Format("{0:F1}° ({1})", data.ElevationDeg, data.ElevationDeg >= 0.0f ? "LOOK-UP" : "LOOK-DOWN"));

                DrawSectionHeader("ELECTROMAGNETICS & SNR");
                DrawRow("Received Echo (Pr)", string.Format("{0:E2} W", data.EchoPowerWatts));
                DrawRow("Thermal Noise (Pn)", string.Format("{0:E2} W", data.NoiseFloorWatts));
                if (data.ClutterPowerWatts > 0.0f)
                {
                    DrawRow("Terrain Clutter (Pc)", string.Format("{0:E2} W", data.ClutterPowerWatts));
                }
                if (data.JammerPowerWatts > 0.0f)
                {
                    DrawRow("Jammer Noise (Pj)", string.Format("{0:E2} W", data.JammerPowerWatts));
                }

                // SNR Gauge Bar
                DrawRow("Instantaneous SNR", string.Format("{0:F1} dB (Req: {1:F1} dB)", data.SNRdB, data.RequiredSNRdB));
                DrawSNRBar(data.SNRdB, data.RequiredSNRdB);

                DrawSectionHeader("RADAR CROSS SECTION (RCS)");
                string rcsStr = (data.DynamicRCS < 0.01f) 
                    ? string.Format("{0:F4} m²", data.DynamicRCS) 
                    : string.Format("{0:F2} m²", data.DynamicRCS);
                DrawRow("Dynamic 3D Aspect RCS", rcsStr);
                if (data.StoresPenaltyRCS > 0.0f)
                {
                    string storesStr = (data.StoresPenaltyRCS < 0.01f)
                        ? string.Format("+{0:F4} m²", data.StoresPenaltyRCS)
                        : string.Format("+{0:F2} m²", data.StoresPenaltyRCS);
                    DrawRow("External Stores Penalty", storesStr);
                }

                DrawSectionHeader("PULSE-DOPPLER & CLUTTER NOTCH");
                DrawRow("Closing Velocity (vr)", string.Format("{0:F1} m/s", data.RelativeRadialSpeed));
                DrawRow("Target Ground Radial", string.Format("{0:F1} m/s (Notch: ±{1:F0})", data.GroundRadialSpeed, RadioWarsConfig.EffectiveNotchThreshold));

                string notchText;
                Color notchCol;
                switch (data.NotchState)
                {
                    case TelemetryNotchState.BeamingNotched:
                        notchText = "BEAMING [CLUTTER NOTCHED - LOST]";
                        notchCol = new Color(1.0f, 0.35f, 0.35f);
                        break;
                    case TelemetryNotchState.HorizonOccluded:
                        notchText = string.Format("MASKED [4/3 CURVATURE (<{0:F0}m NOE)]", RadioWarsConfig.EffectiveCurvatureMaskAltitude);
                        notchCol = new Color(0.95f, 0.45f, 0.35f);
                        break;
                    case TelemetryNotchState.LookUpClear:
                        notchText = "IN NOTCH GATE [OPEN SKY - FILTER BYPASSED]";
                        notchCol = new Color(0.35f, 0.85f, 0.95f);
                        break;
                    case TelemetryNotchState.LookDownClutterMasked:
                        notchText = "DEGRADED [LOOK-DOWN TERRAIN CLUTTER]";
                        notchCol = new Color(1.0f, 0.80f, 0.20f);
                        break;
                    default:
                        notchText = "CLEAR TRACK [SOLID DOPPLER RETURN]";
                        notchCol = new Color(0.25f, 0.95f, 0.50f);
                        break;
                }
                DrawColoredRow("Doppler Gate Status", notchText, notchCol);

                DrawSectionHeader("ELECTRONIC WARFARE (EW)");
                DrawRow("Self-Screening Jammer", data.JammerActive ? string.Format("ACTIVE ({0:F1} kW)", data.JammerIntensity) : "INACTIVE");
                if (data.JammerActive && data.BurnThroughRangeMeters > 0.0f)
                {
                    DrawRow("Burn-Through Range", string.Format("{0:F1} km", data.BurnThroughRangeMeters * 0.001f));
                }
            }
            else
            {
                DrawSectionHeader("PRIMARY RADAR TARGET TRACK");
                GUILayout.Label("No primary radar target currently designated. (Radar Idle / Scanning)", _threatSubStyle);
            }

            // 4. Threat Warning Table: All radars currently illuminating / locking player
            List<ActiveRadarIlluminator> illuminators = RadarTelemetry.GetActiveIlluminators(3.5f);
            DrawSectionHeader(string.Format("ACTIVE EMITTERS ILLUMINATING PLAYER ({0})", illuminators.Count));

            if (illuminators.Count > 0)
            {
                for (int i = 0; i < illuminators.Count; i++)
                {
                    ActiveRadarIlluminator em = illuminators[i];
                    string stateStr;
                    Color stateCol = em.GetColor();

                    switch (em.State)
                    {
                        case RadarIlluminationState.MissileGuidance:
                            stateStr = "[CW GUIDANCE]";
                            break;
                        case RadarIlluminationState.HardLock:
                            stateStr = "[HARD LOCK]";
                            break;
                        case RadarIlluminationState.Tracking:
                            stateStr = "[TRACKING]";
                            break;
                        default:
                            stateStr = "[SEARCH SCAN]";
                            break;
                    }

                    // Render as a clean 2-row threat card: dynamically sizing, zero wrapping/overlapping
                    GUILayout.BeginVertical(GUILayout.Height(34));

                    // Row 1: Emitter source title on left, State badge on right
                    GUILayout.BeginHorizontal();
                    string sourceTitle = string.Format("■ {0} [{1}]", em.SourceName, em.PlatformType);
                    GUILayout.Label(sourceTitle, _threatHeaderStyle);
                    GUILayout.FlexibleSpace();

                    Color oldCol = _valueStyle.normal.textColor;
                    _valueStyle.normal.textColor = stateCol;
                    GUILayout.Label(stateStr, _valueStyle);
                    _valueStyle.normal.textColor = oldCol;
                    GUILayout.EndHorizontal();

                    // Row 2: Range and Signal level indented
                    GUILayout.BeginHorizontal();
                    string details = string.Format("    Slant Range: {0:F1} km   |   Signal: {1:F0} dB SNR", em.DistanceMeters * 0.001f, em.SNRdB);
                    GUILayout.Label(details, _threatSubStyle);
                    GUILayout.EndHorizontal();

                    GUILayout.EndVertical();
                    GUILayout.Space(2);
                }
            }
            else
            {
                GUILayout.Label("No enemy radars currently illuminating ownship. (RWR Clear)", _threatSubStyle);
            }

            GUILayout.EndVertical();

            // Drag window enabled across header bar
            GUI.DragWindow(new Rect(0, 0, 10000, 36));
        }

        private void DrawSectionHeader(string title)
        {
            GUILayout.Space(8);
            GUILayout.Label(title, _sectionHeaderStyle);
            DrawDivider();
            GUILayout.Space(2);
        }

        private void DrawDivider()
        {
            Rect divRect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            if (_dividerTex != null)
            {
                GUI.DrawTexture(divRect, _dividerTex);
            }
        }

        private void DrawRow(string label, string value)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(18));
            GUILayout.Label(label, _labelStyle, GUILayout.Width(200));
            GUILayout.FlexibleSpace();
            GUILayout.Label(value, _valueStyle);
            GUILayout.EndHorizontal();
        }

        private void DrawColoredRow(string label, string value, Color valColor)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(18));
            GUILayout.Label(label, _labelStyle, GUILayout.Width(200));
            GUILayout.FlexibleSpace();
            Color old = _valueStyle.normal.textColor;
            _valueStyle.normal.textColor = valColor;
            GUILayout.Label(value, _valueStyle);
            _valueStyle.normal.textColor = old;
            GUILayout.EndHorizontal();
        }

        private void DrawSNRBar(float snrDb, float reqSnrDb)
        {
            GUILayout.Space(2);
            Rect barRect = GUILayoutUtility.GetRect(0, 10, GUILayout.ExpandWidth(true));
            GUI.color = new Color(0.12f, 0.17f, 0.22f, 0.90f);
            GUI.DrawTexture(barRect, _whiteTex);

            float fillPercent = Mathf.Clamp01((snrDb + 10.0f) / 50.0f); // -10 dB to +40 dB range
            Rect fillRect = new Rect(barRect.x, barRect.y, barRect.width * fillPercent, barRect.height);

            Color barCol = snrDb >= reqSnrDb ? new Color(0.20f, 0.85f, 0.45f, 0.95f) : new Color(0.95f, 0.30f, 0.30f, 0.95f);
            GUI.color = barCol;
            GUI.DrawTexture(fillRect, _whiteTex);

            // Draw threshold mark
            float reqPercent = Mathf.Clamp01((reqSnrDb + 10.0f) / 50.0f);
            Rect markRect = new Rect(barRect.x + barRect.width * reqPercent - 1, barRect.y - 1, 2, barRect.height + 2);
            GUI.color = new Color(1.0f, 0.85f, 0.20f, 0.95f);
            GUI.DrawTexture(markRect, _whiteTex);

            GUI.color = Color.white;
            GUILayout.Space(2);
        }
    }
}
