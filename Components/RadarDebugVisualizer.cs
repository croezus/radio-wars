using System;
using System.Collections.Generic;
using UnityEngine;
using RadioWars.Config;
using RadioWars.Core;

namespace RadioWars.Components
{
    public class RadarDebugVisualizer : MonoBehaviour
    {
        public static RadarDebugVisualizer Instance { get; private set; }

        private GameObject _linesRoot;
        private Material _lineMaterial;

        // Pooled LineRenderers for standalone 3D rendering
        private LineRenderer _lrLOS;
        private LineRenderer _lrTargetVel;
        private LineRenderer _lrDopplerProj;
        private LineRenderer _lrNotchBar1;
        private LineRenderer _lrNotchBar2;
        private bool _linesHidden;

        // Pooled LineRenderers for active threats illuminating player
        private readonly List<LineRenderer> _threatLines = new List<LineRenderer>();
        private readonly List<LineRenderer> _threatBeams = new List<LineRenderer>();

        private GUIStyle _hudTextStyle;
        private Texture2D _reticleTex;

        private void Awake()
        {
            Instance = this;
            InitializeLineRenderers();
            InitializeTextures();
        }

        private void InitializeTextures()
        {
            _reticleTex = new Texture2D(1, 1);
            _reticleTex.SetPixel(0, 0, Color.white);
            _reticleTex.Apply();
        }

        private void InitializeLineRenderers()
        {
            if (_linesRoot != null) return;

            _linesRoot = new GameObject("RadioWars_Debug_Visualizer_Root");
            _linesRoot.transform.SetParent(transform);

            // Find universal unlit shader
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("UI/Default");
            if (shader == null) shader = Shader.Find("Hidden/Internal-Colored");

            if (shader != null)
            {
                _lineMaterial = new Material(shader);
            }
            else
            {
                _lineMaterial = new Material(Shader.Find("Standard"));
            }

            _lineMaterial.hideFlags = HideFlags.DontSave;

            _lrLOS = CreateLine("Line_LOS", 2.5f);
            _lrTargetVel = CreateLine("Line_TargetVel", 3.0f);
            _lrDopplerProj = CreateLine("Line_DopplerProj", 3.5f);
            _lrNotchBar1 = CreateLine("Line_NotchBar1", 2.0f);
            _lrNotchBar2 = CreateLine("Line_NotchBar2", 2.0f);
        }

        private LineRenderer CreateLine(string name, float width)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(_linesRoot.transform);

            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.material = _lineMaterial;
            lr.startWidth = width;
            lr.endWidth = width;
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.alignment = LineAlignment.View;
            go.SetActive(false);

            return lr;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_linesRoot != null)
            {
                Destroy(_linesRoot);
            }
            if (_lineMaterial != null)
            {
                Destroy(_lineMaterial);
            }
            if (_reticleTex != null)
            {
                Destroy(_reticleTex);
            }
        }

        private void Update()
        {
            // Update 3D LineRenderers only when enabled in config
            if (RadioWarsConfig.DrawDebugGizmos != null && RadioWarsConfig.DrawDebugGizmos.Value)
            {
                _linesHidden = false;
                UpdateActiveVisuals();
            }
            else
            {
                if (!_linesHidden)
                {
                    HideAllLines();
                    _linesHidden = true;
                }
            }
        }

        public void HideAllLines()
        {
            if (_lrLOS != null && _lrLOS.gameObject.activeSelf) _lrLOS.gameObject.SetActive(false);
            if (_lrTargetVel != null && _lrTargetVel.gameObject.activeSelf) _lrTargetVel.gameObject.SetActive(false);
            if (_lrDopplerProj != null && _lrDopplerProj.gameObject.activeSelf) _lrDopplerProj.gameObject.SetActive(false);
            if (_lrNotchBar1 != null && _lrNotchBar1.gameObject.activeSelf) _lrNotchBar1.gameObject.SetActive(false);
            if (_lrNotchBar2 != null && _lrNotchBar2.gameObject.activeSelf) _lrNotchBar2.gameObject.SetActive(false);

            for (int i = 0; i < _threatLines.Count; i++)
            {
                if (_threatLines[i] != null && _threatLines[i].gameObject.activeSelf)
                {
                    _threatLines[i].gameObject.SetActive(false);
                }
            }
            for (int i = 0; i < _threatBeams.Count; i++)
            {
                if (_threatBeams[i] != null && _threatBeams[i].gameObject.activeSelf)
                {
                    _threatBeams[i].gameObject.SetActive(false);
                }
            }
        }

        private void UpdateActiveVisuals()
        {
            // 1. Player Forward Radar Target Visuals
            bool hasValidTarget = RadarTelemetry.Latest.HasTarget && (Time.time - RadarTelemetry.Latest.LastUpdateTime <= 2.0f);
            if (hasValidTarget)
            {
                Vector3 radarPos = RadarTelemetry.Latest.RadarPos;
                Vector3 targetPos = RadarTelemetry.Latest.TargetPos;
                Vector3 targetVel = RadarTelemetry.Latest.TargetVel;
                Color statusColor = RadarTelemetry.Latest.RayColor;

                // Radar Line of Sight (LOS)
                SetLine(_lrLOS, radarPos, targetPos, statusColor, 2.5f);
                Debug.DrawLine(radarPos, targetPos, statusColor, 0.1f, false);

                // Target Velocity Vector (Cyan)
                if (targetVel.sqrMagnitude > 1.0f)
                {
                    Vector3 velTip = targetPos + targetVel;
                    SetLine(_lrTargetVel, targetPos, velTip, Color.cyan, 3.0f);
                    Debug.DrawLine(targetPos, velTip, Color.cyan, 0.1f, false);

                    Vector3 toTarget = targetPos - radarPos;
                    if (toTarget.sqrMagnitude > 1.0f)
                    {
                        Vector3 losUnit = toTarget.normalized;
                        Vector3 radialProj = Vector3.Project(targetVel, losUnit);
                        Vector3 projTip = targetPos + radialProj;

                        SetLine(_lrDopplerProj, targetPos, projTip, Color.magenta, 3.5f);
                        Debug.DrawLine(targetPos, projTip, Color.magenta, 0.1f, false);

                        float notchThreshold = RadioWarsConfig.EffectiveNotchThreshold;
                        Vector3 notchOffset = losUnit * notchThreshold;
                        Vector3 perp = Vector3.Cross(losUnit, Vector3.up).normalized * 8.0f;

                        SetLine(_lrNotchBar1, targetPos + notchOffset - perp, targetPos + notchOffset + perp, Color.yellow, 2.0f);
                        SetLine(_lrNotchBar2, targetPos - notchOffset - perp, targetPos - notchOffset + perp, Color.yellow, 2.0f);
                    }
                }
                else
                {
                    if (_lrTargetVel != null && _lrTargetVel.gameObject.activeSelf) _lrTargetVel.gameObject.SetActive(false);
                    if (_lrDopplerProj != null && _lrDopplerProj.gameObject.activeSelf) _lrDopplerProj.gameObject.SetActive(false);
                    if (_lrNotchBar1 != null && _lrNotchBar1.gameObject.activeSelf) _lrNotchBar1.gameObject.SetActive(false);
                    if (_lrNotchBar2 != null && _lrNotchBar2.gameObject.activeSelf) _lrNotchBar2.gameObject.SetActive(false);
                }
            }
            else
            {
                if (_lrLOS != null && _lrLOS.gameObject.activeSelf) _lrLOS.gameObject.SetActive(false);
                if (_lrTargetVel != null && _lrTargetVel.gameObject.activeSelf) _lrTargetVel.gameObject.SetActive(false);
                if (_lrDopplerProj != null && _lrDopplerProj.gameObject.activeSelf) _lrDopplerProj.gameObject.SetActive(false);
                if (_lrNotchBar1 != null && _lrNotchBar1.gameObject.activeSelf) _lrNotchBar1.gameObject.SetActive(false);
                if (_lrNotchBar2 != null && _lrNotchBar2.gameObject.activeSelf) _lrNotchBar2.gameObject.SetActive(false);
            }

            // 2. Active Threats Illuminating / Locking Player
            Vector3 playerPos = Vector3.zero;
            bool hasPlayer = false;
            if (CombatHUD.i != null && CombatHUD.i.aircraft != null)
            {
                playerPos = CombatHUD.i.aircraft.transform.position;
                hasPlayer = true;
            }
            else if (Camera.main != null)
            {
                playerPos = Camera.main.transform.position;
                hasPlayer = true;
            }

            List<ActiveRadarIlluminator> illuminators = RadarTelemetry.GetActiveIlluminators(3.5f);
            if (hasPlayer && illuminators != null && illuminators.Count > 0)
            {
                for (int i = 0; i < illuminators.Count; i++)
                {
                    ActiveRadarIlluminator em = illuminators[i];
                    Color threatCol = em.GetColor();

                    float lineWidth = 2.2f;
                    switch (em.State)
                    {
                        case RadarIlluminationState.MissileGuidance:
                            lineWidth = 5.5f;
                            break;
                        case RadarIlluminationState.HardLock:
                            lineWidth = 4.2f;
                            break;
                        case RadarIlluminationState.Tracking:
                            lineWidth = 3.2f;
                            break;
                        default:
                            lineWidth = 2.0f;
                            break;
                    }

                    // Dynamic line pool expansion
                    while (_threatLines.Count <= i)
                    {
                        _threatLines.Add(CreateLine("ThreatLine_" + _threatLines.Count, 2.5f));
                    }
                    SetLine(_threatLines[i], em.EmitterPosition, playerPos, threatCol, lineWidth);
                    Debug.DrawLine(em.EmitterPosition, playerPos, threatCol, 0.1f, false);

                    // Emitter beam vector line
                    while (_threatBeams.Count <= i)
                    {
                        _threatBeams.Add(CreateLine("ThreatBeam_" + _threatBeams.Count, 3.5f));
                    }
                    Vector3 beamDir = (playerPos - em.EmitterPosition).normalized;
                    float beamLen = Mathf.Min(1200.0f, em.DistanceMeters * 0.35f);
                    SetLine(_threatBeams[i], em.EmitterPosition, em.EmitterPosition + beamDir * beamLen, threatCol, lineWidth * 1.4f);
                }

                for (int i = illuminators.Count; i < _threatLines.Count; i++)
                {
                    if (_threatLines[i].gameObject.activeSelf) _threatLines[i].gameObject.SetActive(false);
                }
                for (int i = illuminators.Count; i < _threatBeams.Count; i++)
                {
                    if (_threatBeams[i].gameObject.activeSelf) _threatBeams[i].gameObject.SetActive(false);
                }
            }
            else
            {
                for (int i = 0; i < _threatLines.Count; i++)
                {
                    if (_threatLines[i].gameObject.activeSelf) _threatLines[i].gameObject.SetActive(false);
                }
                for (int i = 0; i < _threatBeams.Count; i++)
                {
                    if (_threatBeams[i].gameObject.activeSelf) _threatBeams[i].gameObject.SetActive(false);
                }
            }
        }

        private void SetLine(LineRenderer lr, Vector3 start, Vector3 end, Color col, float width)
        {
            if (lr == null) return;
            if (!lr.gameObject.activeSelf) lr.gameObject.SetActive(true);

            // Dynamically scale line width based on camera distance so long-range contacts (10-40km)
            // remain visible while close-range geometry doesn't obstruct cockpit canopy.
            Camera cam = Camera.main;
            Vector3 camPos = (cam != null) ? cam.transform.position : start;

            float distStart = Vector3.Distance(camPos, start);
            float distEnd = Vector3.Distance(camPos, end);

            float widthStart = Mathf.Clamp(width * (0.30f + distStart * 0.0008f), 0.35f, 40.0f);
            float widthEnd = Mathf.Clamp(width * (0.30f + distEnd * 0.0008f), 0.35f, 40.0f);

            lr.startWidth = widthStart;
            lr.endWidth = widthEnd;
            lr.startColor = col;
            lr.endColor = col;
            lr.SetPosition(0, start);
            lr.SetPosition(1, end);
        }

        private void OnGUI()
        {
            // In-Game Screen-Space 2D Reticles & Status Tags
            if (RadioWarsConfig.DrawDebugGizmos != null && RadioWarsConfig.DrawDebugGizmos.Value)
            {
                DrawScreenSpaceTargetTag();
                DrawActiveThreatReticles();
            }
        }

        private void DrawScreenSpaceTargetTag()
        {
            if (!RadarTelemetry.Latest.HasTarget || (Time.time - RadarTelemetry.Latest.LastUpdateTime > 2.0f))
            {
                return;
            }

            Camera cam = Camera.main;
            if (cam == null) return;

            Vector3 targetPos = RadarTelemetry.Latest.TargetPos;
            Vector3 screenPos = cam.WorldToScreenPoint(targetPos);

            // Target must be in front of the camera viewport
            if (screenPos.z <= 1.0f) return;

            // Unity Screen coordinates have (0,0) at bottom-left; GUI coordinates have (0,0) at top-left
            float guiX = screenPos.x;
            float guiY = Screen.height - screenPos.y;

            if (guiX < -100 || guiX > Screen.width + 100 || guiY < -100 || guiY > Screen.height + 100) return;

            Color statusColor = RadarTelemetry.Latest.RayColor;

            // Draw tactical diamond reticle around target
            float boxSize = 24.0f;
            Rect boxRect = new Rect(guiX - boxSize * 0.5f, guiY - boxSize * 0.5f, boxSize, boxSize);

            GUI.color = statusColor;
            DrawBoxOutline(boxRect, 2.0f);

            // Draw status badge and telemetry text
            if (_hudTextStyle == null)
            {
                _hudTextStyle = new GUIStyle(GUI.skin.label);
                _hudTextStyle.fontSize = 11;
                _hudTextStyle.fontStyle = FontStyle.Bold;
            }

            _hudTextStyle.normal.textColor = statusColor;

            string statusText = string.Format(
                "{0}\nR: {1:F1} km | SNR: {2:F1} dB\nVr: {3:F0} m/s [{4}]",
                RadarTelemetry.Latest.TargetName,
                RadarTelemetry.Latest.SlantRangeMeters * 0.001f,
                RadarTelemetry.Latest.SNRdB,
                RadarTelemetry.Latest.GroundRadialSpeed,
                GetStatusBadgeText(RadarTelemetry.Latest.NotchState)
            );

            Rect textRect = new Rect(guiX + boxSize * 0.7f, guiY - 14.0f, 220, 60);
            GUI.Label(textRect, statusText, _hudTextStyle);

            GUI.color = Color.white;
        }

        private void DrawActiveThreatReticles()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            List<ActiveRadarIlluminator> illuminators = RadarTelemetry.GetActiveIlluminators(3.5f);
            if (illuminators == null || illuminators.Count == 0) return;

            for (int i = 0; i < illuminators.Count; i++)
            {
                ActiveRadarIlluminator em = illuminators[i];
                Vector3 screenPos = cam.WorldToScreenPoint(em.EmitterPosition);

                Color statusColor = em.GetColor();
                string stateTag;
                switch (em.State)
                {
                    case RadarIlluminationState.MissileGuidance:
                        stateTag = "CW MISSILE GUIDANCE";
                        break;
                    case RadarIlluminationState.HardLock:
                        stateTag = "HARD LOCK";
                        break;
                    case RadarIlluminationState.Tracking:
                        stateTag = "LOCKING";
                        break;
                    default:
                        stateTag = "SEARCH SCAN";
                        break;
                }

                float boxSize = 28.0f;

                if (screenPos.z > 1.0f)
                {
                    float guiX = screenPos.x;
                    float guiY = Screen.height - screenPos.y;

                    // In-bounds viewport check
                    if (guiX >= 25 && guiX <= Screen.width - 25 && guiY >= 25 && guiY <= Screen.height - 25)
                    {
                        Rect boxRect = new Rect(guiX - boxSize * 0.5f, guiY - boxSize * 0.5f, boxSize, boxSize);
                        GUI.color = statusColor;
                        DrawBoxOutline(boxRect, 2.5f);

                        // Emitter label beside the square
                        string labelText = string.Format(
                            "■ {0} [{1}]\nR: {2:F1} km | SNR: {3:F0} dB\n[{4}]",
                            em.SourceName,
                            em.PlatformType,
                            em.DistanceMeters * 0.001f,
                            em.SNRdB,
                            stateTag
                        );

                        if (_hudTextStyle == null)
                        {
                            _hudTextStyle = new GUIStyle(GUI.skin.label);
                            _hudTextStyle.fontSize = 11;
                            _hudTextStyle.fontStyle = FontStyle.Bold;
                        }
                        _hudTextStyle.normal.textColor = statusColor;

                        Rect textRect = new Rect(guiX + boxSize * 0.65f, guiY - 14.0f, 260, 60);
                        GUI.Label(textRect, labelText, _hudTextStyle);
                        GUI.color = Color.white;
                        continue;
                    }
                }

                // Off-screen or behind camera: draw clamped edge indicator
                Vector3 center = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0);
                Vector3 delta = new Vector3(screenPos.x, Screen.height - screenPos.y, 0) - center;
                if (screenPos.z < 0)
                {
                    delta = -delta;
                }
                Vector3 screenDir = delta.sqrMagnitude > 0.001f ? delta.normalized : Vector3.up;

                float edgeMargin = 45.0f;
                float edgeX = Mathf.Clamp(Screen.width * 0.5f + screenDir.x * (Screen.width * 0.5f - edgeMargin), edgeMargin, Screen.width - edgeMargin);
                float edgeY = Mathf.Clamp(Screen.height * 0.5f + screenDir.y * (Screen.height * 0.5f - edgeMargin), edgeMargin, Screen.height - edgeMargin);

                Rect edgeRect = new Rect(edgeX - 12.0f, edgeY - 12.0f, 24.0f, 24.0f);
                GUI.color = statusColor;
                DrawBoxOutline(edgeRect, 2.0f);

                if (_hudTextStyle != null)
                {
                    _hudTextStyle.normal.textColor = statusColor;
                    string edgeLabel = string.Format("◄ {0} ({1:F0}km) [{2}]", em.SourceName, em.DistanceMeters * 0.001f, stateTag);
                    GUI.Label(new Rect(edgeX + 16.0f, edgeY - 10.0f, 240, 25), edgeLabel, _hudTextStyle);
                }
                GUI.color = Color.white;
            }
        }

        private void DrawBoxOutline(Rect r, float thickness)
        {
            // Top
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, thickness), _reticleTex);
            // Bottom
            GUI.DrawTexture(new Rect(r.x, r.y + r.height - thickness, r.width, thickness), _reticleTex);
            // Left
            GUI.DrawTexture(new Rect(r.x, r.y, thickness, r.height), _reticleTex);
            // Right
            GUI.DrawTexture(new Rect(r.x + r.width - thickness, r.y, thickness, r.height), _reticleTex);
        }

        private string GetStatusBadgeText(TelemetryNotchState state)
        {
            switch (state)
            {
                case TelemetryNotchState.BeamingNotched:
                    return "NOTCHED";
                case TelemetryNotchState.HorizonOccluded:
                    return "MASKED";
                case TelemetryNotchState.LookUpClear:
                    return "LOOK-UP";
                case TelemetryNotchState.LookDownClutterMasked:
                    return "CLUTTER";
                default:
                    return "SOLID TRACK";
            }
        }
    }
}
