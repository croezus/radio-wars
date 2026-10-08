using System;
using System.Collections.Generic;
using UnityEngine;
using RadioWars.Config;
using RadioWars.Core;

namespace RadioWars.Components
{
    /// <summary>
    /// Interactive 3D and 2D visualizer for the Tactical Allied Datalink system.
    /// Toggled via configurable hotkey (default: F8).
    /// Renders:
    /// 1. 3D World-Space Datalink Mesh Lines between friendly communication nodes (Cyan).
    /// 2. 3D Shared Radar Track Rays from spotting radar to target (Green/Cyan).
    /// 3. 3D Shared Visual Reconnaissance Rays from spotter to target (Yellow).
    /// 4. 3D Relayed Inbound Missile Warning Rays from detecting radar to missile to defended aircraft (Red/Pulsing).
    /// 5. 2D Screen-Space tactical reticles and badges over nodes and shared contacts.
    /// 6. Real-time on-screen Datalink Diagnostic Telemetry window for validating sensor fusion.
    /// Uses pre-allocated, object-pooled LineRenderers for zero GC allocation.
    /// </summary>
    public class DatalinkDebugVisualizer : MonoBehaviour
    {
        public static DatalinkDebugVisualizer Instance { get; private set; }

        private GameObject _linesRoot;
        private Material _lineMaterial;

        // Pooled LineRenderers for 3D world visualization
        private readonly List<LineRenderer> _meshLines = new List<LineRenderer>();
        private readonly List<LineRenderer> _radarTrackLines = new List<LineRenderer>();
        private readonly List<LineRenderer> _visualContactLines = new List<LineRenderer>();
        private readonly List<LineRenderer> _missileAlertLines = new List<LineRenderer>();

        // 2D GUI Styles & Textures
        private Rect _windowRect = new Rect(20f, 60f, 460f, 0f);
        private readonly List<Rect> _drawnBadgeRects = new List<Rect>(16);

        private struct FusedContact
        {
            public Unit Target;
            public string TargetName;
            public Vector3 TargetPos;
            public bool HasRadarTrack;
            public bool IsVisual;
            public bool IsRwr;
            public string BestSourceName;
            public float BestDistanceKm;
            public int TotalFeeds;
            public float TrackingQuality; // 0.0 to 1.0 (Q)
            public float UncertaintyRadiusKm; // CEP
            public bool IsInMemory;
            public bool IsTriangulated;
        }
        private readonly List<FusedContact> _fusedContacts = new List<FusedContact>(32);
        private float _lastContactAggregationTime;
        private bool _linesHidden;

        private static readonly string[] QPercentStrings;
        private static readonly GUIContent _calcContent = new GUIContent();

        static DatalinkDebugVisualizer()
        {
            QPercentStrings = new string[101];
            for (int i = 0; i <= 100; i++)
            {
                QPercentStrings[i] = string.Format("Q: {0}%", i);
            }
        }

        private static string GetQPercentString(float q)
        {
            int idx = Mathf.Clamp(Mathf.RoundToInt(q * 100f), 0, 100);
            return QPercentStrings[idx];
        }

        private Texture2D _whiteTex;
        private Texture2D _windowBgTex;
        private Texture2D _dividerTex;

        private GUIStyle _windowStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _badgeActiveStyle;
        private GUIStyle _badgeInactiveStyle;
        private GUIStyle _subHeaderStyle;
        private GUIStyle _sectionHeaderStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _valueStyle;
        private GUIStyle _hudTextStyle;
        private GUIStyle _hudTagStyle;

        private void Awake()
        {
            Instance = this;
            InitializeLineRenderers();
            InitializeTextures();
            _windowRect = new Rect(Mathf.Max(20f, Screen.width - 480f), 60f, 460f, 0f);
        }

        private void InitializeTextures()
        {
            _whiteTex = MakeTex(1, 1, Color.white);
            _dividerTex = MakeTex(1, 1, new Color(0.20f, 0.35f, 0.45f, 0.65f));
            _windowBgTex = MakeTex(2, 2, new Color(0.04f, 0.08f, 0.12f, 0.94f));
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

        private void InitializeLineRenderers()
        {
            if (_linesRoot != null) return;

            _linesRoot = new GameObject("RadioWars_Datalink_Visualizer_Root");
            _linesRoot.transform.SetParent(transform);

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
            if (_linesRoot != null) Destroy(_linesRoot);
            if (_lineMaterial != null) Destroy(_lineMaterial);
            if (_whiteTex != null) Destroy(_whiteTex);
            if (_dividerTex != null) Destroy(_dividerTex);
            if (_windowBgTex != null) Destroy(_windowBgTex);
        }

        private void Update()
        {
            if (RadioWarsConfig.DrawDatalinkVisualizer != null && RadioWarsConfig.DrawDatalinkVisualizer.Value)
            {
                _linesHidden = false;
                UpdateActiveVisuals();
                if (Time.time - _lastContactAggregationTime >= 0.15f)
                {
                    _lastContactAggregationTime = Time.time;
                    AggregateFusedContacts();
                }
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
            for (int i = 0; i < _meshLines.Count; i++)
            {
                if (_meshLines[i] != null && _meshLines[i].gameObject.activeSelf)
                    _meshLines[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < _radarTrackLines.Count; i++)
            {
                if (_radarTrackLines[i] != null && _radarTrackLines[i].gameObject.activeSelf)
                    _radarTrackLines[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < _visualContactLines.Count; i++)
            {
                if (_visualContactLines[i] != null && _visualContactLines[i].gameObject.activeSelf)
                    _visualContactLines[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < _missileAlertLines.Count; i++)
            {
                if (_missileAlertLines[i] != null && _missileAlertLines[i].gameObject.activeSelf)
                    _missileAlertLines[i].gameObject.SetActive(false);
            }
        }

        private void SetLine(LineRenderer lr, Vector3 start, Vector3 end, Color col, float baseWidth, Vector3 camPos)
        {
            if (lr == null) return;
            if (!lr.gameObject.activeSelf) lr.gameObject.SetActive(true);

            float distStart = Vector3.Distance(camPos, start);
            float distEnd = Vector3.Distance(camPos, end);

            float widthStart = Mathf.Clamp(baseWidth * (0.35f + distStart * 0.0006f), 0.35f, 35.0f);
            float widthEnd = Mathf.Clamp(baseWidth * (0.35f + distEnd * 0.0006f), 0.35f, 35.0f);

            lr.startWidth = widthStart;
            lr.endWidth = widthEnd;
            lr.startColor = col;
            lr.endColor = col;
            lr.SetPosition(0, start);
            lr.SetPosition(1, end);
        }

        private void UpdateActiveVisuals()
        {
            var telemetry = DatalinkNetwork.LatestTelemetry;
            if (telemetry == null || !telemetry.NetworkActive)
            {
                HideAllLines();
                return;
            }

            Camera cam = CameraStateManager.i != null && CameraStateManager.i.mainCamera != null ? CameraStateManager.i.mainCamera : Camera.main;
            Vector3 camPos = (cam != null) ? cam.transform.position : Vector3.zero;

            Aircraft playerAircraft = CombatHUD.i != null ? CombatHUD.i.aircraft : null;
            Vector3 playerPos = (playerAircraft != null) ? playerAircraft.transform.position : Vector3.zero;

            // 1. Render Datalink Network Mesh Lines between friendly nodes
            bool showMesh = RadioWarsConfig.ShowDatalinkMeshLines == null || RadioWarsConfig.ShowDatalinkMeshLines.Value;
            int meshIndex = 0;

            if (showMesh && telemetry.Nodes.Count > 1)
            {
                Color meshColor = new Color(0.15f, 0.70f, 0.95f, 0.40f);

                // Identify highest tier command node (AWACS or Warship/Carrier)
                DatalinkNodeSnapshot hubNode = null;
                for (int i = 0; i < telemetry.Nodes.Count; i++)
                {
                    if (telemetry.Nodes[i].Role == "AWACS")
                    {
                        hubNode = telemetry.Nodes[i];
                        break;
                    }
                    if (telemetry.Nodes[i].Role == "WARSHIP" && hubNode == null)
                    {
                        hubNode = telemetry.Nodes[i];
                    }
                }

                // Connect nodes to hub or player
                Vector3 hubPos = (hubNode != null && hubNode.Unit != null) ? hubNode.Unit.transform.position : playerPos;

                for (int i = 0; i < telemetry.Nodes.Count; i++)
                {
                    var node = telemetry.Nodes[i];
                    if (node.Unit == null || node.Unit.disabled) continue;

                    Vector3 nodePos = node.Unit.transform.position;
                    if (hubNode != null && node == hubNode) continue;

                    float dist = Vector3.Distance(nodePos, hubPos);
                    if (dist > 80000.0f) continue; // Beyond radio line of sight limit

                    while (_meshLines.Count <= meshIndex)
                    {
                        _meshLines.Add(CreateLine("DatalinkMesh_" + _meshLines.Count, 1.5f));
                    }

                    SetLine(_meshLines[meshIndex], hubPos, nodePos, meshColor, 1.5f, camPos);
                    Debug.DrawLine(hubPos, nodePos, meshColor, 0.05f, false);
                    meshIndex++;
                }
            }

            for (int i = meshIndex; i < _meshLines.Count; i++)
            {
                if (_meshLines[i].gameObject.activeSelf) _meshLines[i].gameObject.SetActive(false);
            }

            // 2. Render Shared Radar Track Rays (Bright Green / Cyan)
            int radarTrackIndex = 0;
            Color radarTrackColor = new Color(0.20f, 0.95f, 0.55f, 0.85f);

            for (int i = 0; i < telemetry.SharedRadarTracks.Count; i++)
            {
                var track = telemetry.SharedRadarTracks[i];
                if (track.SourceUnit == null || track.SourceUnit.disabled || track.TargetUnit == null || track.TargetUnit.disabled)
                    continue;

                Vector3 startPos = track.SourceUnit.transform.position;
                Vector3 endPos = track.TargetUnit.transform.position;

                while (_radarTrackLines.Count <= radarTrackIndex)
                {
                    _radarTrackLines.Add(CreateLine("DatalinkRadarTrack_" + _radarTrackLines.Count, 2.5f));
                }

                SetLine(_radarTrackLines[radarTrackIndex], startPos, endPos, radarTrackColor, 2.5f, camPos);
                Debug.DrawLine(startPos, endPos, radarTrackColor, 0.05f, false);
                radarTrackIndex++;
            }

            for (int i = radarTrackIndex; i < _radarTrackLines.Count; i++)
            {
                if (_radarTrackLines[i].gameObject.activeSelf) _radarTrackLines[i].gameObject.SetActive(false);
            }

            // 3. Render Shared Visual Reconnaissance Rays (Yellow / Amber)
            int visualIndex = 0;
            Color visualColor = new Color(1.0f, 0.85f, 0.15f, 0.85f);

            for (int i = 0; i < telemetry.SharedVisualContacts.Count; i++)
            {
                var v = telemetry.SharedVisualContacts[i];
                if (v.SpotterUnit == null || v.SpotterUnit.disabled || v.TargetUnit == null || v.TargetUnit.disabled)
                    continue;

                Vector3 startPos = v.SpotterUnit.transform.position;
                Vector3 endPos = v.TargetUnit.transform.position;

                while (_visualContactLines.Count <= visualIndex)
                {
                    _visualContactLines.Add(CreateLine("DatalinkVisualTrack_" + _visualContactLines.Count, 2.0f));
                }

                SetLine(_visualContactLines[visualIndex], startPos, endPos, visualColor, 2.0f, camPos);
                Debug.DrawLine(startPos, endPos, visualColor, 0.05f, false);
                visualIndex++;
            }

            for (int i = visualIndex; i < _visualContactLines.Count; i++)
            {
                if (_visualContactLines[i].gameObject.activeSelf) _visualContactLines[i].gameObject.SetActive(false);
            }

            // 4. Render Relayed Inbound Missile Warning Rays (Red / Pulsing)
            int missileIndex = 0;
            float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 8.0f);
            Color missileWarnColor = new Color(1.0f, 0.20f, 0.20f, pulse);

            for (int i = 0; i < telemetry.RelayedMissileAlerts.Count; i++)
            {
                var alert = telemetry.RelayedMissileAlerts[i];
                if (alert.RadarUnit == null || alert.RadarUnit.disabled || alert.MissileUnit == null || alert.MissileUnit.disabled)
                    continue;

                Vector3 radarPos = alert.RadarUnit.transform.position;
                Vector3 missilePos = alert.MissileUnit.transform.position;

                // Segment A: Detecting radar to missile
                while (_missileAlertLines.Count <= missileIndex)
                {
                    _missileAlertLines.Add(CreateLine("DatalinkMissileAlert_" + _missileAlertLines.Count, 3.0f));
                }
                SetLine(_missileAlertLines[missileIndex], radarPos, missilePos, missileWarnColor, 3.0f, camPos);
                Debug.DrawLine(radarPos, missilePos, missileWarnColor, 0.05f, false);
                missileIndex++;

                // Segment B: Missile to Player aircraft
                if (playerAircraft != null)
                {
                    while (_missileAlertLines.Count <= missileIndex)
                    {
                        _missileAlertLines.Add(CreateLine("DatalinkMissileAlert_" + _missileAlertLines.Count, 3.0f));
                    }
                    SetLine(_missileAlertLines[missileIndex], missilePos, playerPos, missileWarnColor, 3.0f, camPos);
                    Debug.DrawLine(missilePos, playerPos, missileWarnColor, 0.05f, false);
                    missileIndex++;
                }
            }

            for (int i = missileIndex; i < _missileAlertLines.Count; i++)
            {
                if (_missileAlertLines[i].gameObject.activeSelf) _missileAlertLines[i].gameObject.SetActive(false);
            }
        }

        private void OnGUI()
        {
            if (RadioWarsConfig.DrawDatalinkVisualizer == null || !RadioWarsConfig.DrawDatalinkVisualizer.Value)
            {
                return;
            }

            InitStyles();

            // 1. Draw 2D Screen-space reticles and badges
            DrawScreenSpaceReticles();

            // 2. Draw Tactical Datalink Diagnostic Telemetry Window
            bool showPanel = RadioWarsConfig.ShowDatalinkDiagnosticPanel == null || RadioWarsConfig.ShowDatalinkDiagnosticPanel.Value;
            if (showPanel)
            {
                // Always clamp window within screen boundaries to prevent clipping
                float maxWinX = Mathf.Max(10f, Screen.width - Mathf.Max(460f, _windowRect.width) - 10f);
                float maxWinY = Mathf.Max(10f, Screen.height - Mathf.Max(100f, _windowRect.height) - 10f);
                _windowRect.x = Mathf.Clamp(_windowRect.x, 10f, maxWinX);
                _windowRect.y = Mathf.Clamp(_windowRect.y, 10f, maxWinY);

                _windowRect = GUILayout.Window(9482, _windowRect, DrawDiagnosticWindow, "", _windowStyle);

                _windowRect.x = Mathf.Clamp(_windowRect.x, 10f, maxWinX);
                _windowRect.y = Mathf.Clamp(_windowRect.y, 10f, maxWinY);
            }
        }

        private Rect GetBadgeRect(float guiX, float guiY, float width, float height)
        {
            float startX = guiX + 16f;
            float startY = guiY - height * 0.5f;

            if (startX + width > Screen.width - 15f)
            {
                startX = guiX - width - 16f;
            }

            startX = Mathf.Clamp(startX, 10f, Screen.width - width - 10f);
            startY = Mathf.Clamp(startY, 10f, Screen.height - height - 10f);

            return new Rect(startX, startY, width, height);
        }

        private void TryDrawBadge(Rect rect, string text, Color col)
        {
            for (int i = 0; i < _drawnBadgeRects.Count; i++)
            {
                if (rect.Overlaps(_drawnBadgeRects[i]))
                {
                    return;
                }
            }
            DrawBadgeCard(rect, text, col);
            _drawnBadgeRects.Add(rect);
        }

        private void DrawBadgeCard(Rect rect, string text, Color accentCol)
        {
            Color oldCol = GUI.color;
            GUI.color = new Color(0.04f, 0.08f, 0.14f, 0.90f);
            GUI.DrawTexture(rect, _whiteTex);

            DrawBoxOutline(rect, 1.0f, new Color(accentCol.r, accentCol.g, accentCol.b, 0.65f));
            GUI.color = oldCol;

            _hudTextStyle.normal.textColor = accentCol;
            GUI.Label(rect, text, _hudTextStyle);
        }

        private void AggregateFusedContacts()
        {
            var telemetry = DatalinkNetwork.LatestTelemetry;
            if (telemetry == null || !telemetry.NetworkActive)
            {
                _fusedContacts.Clear();
                return;
            }

            _fusedContacts.Clear();

            Aircraft playerAircraft = CombatHUD.i != null ? CombatHUD.i.aircraft : null;
            FactionHQ playerHq = (telemetry != null && telemetry.PlayerHQ != null)
                ? telemetry.PlayerHQ
                : RWRTriangulationProcessor.GetPlayerFactionHQ();

            // Aggregate shared radar tracks
            for (int i = 0; i < telemetry.SharedRadarTracks.Count; i++)
            {
                var track = telemetry.SharedRadarTracks[i];
                if (track.TargetUnit == null || track.TargetUnit.disabled || !Patches.CombatHUDPatches.IsTrackableThreat(track.TargetUnit, playerHq)) continue;

                float slantDistKm = track.SlantRangeMeters * 0.001f;
                int existingIdx = -1;
                for (int j = 0; j < _fusedContacts.Count; j++)
                {
                    if (_fusedContacts[j].Target == track.TargetUnit)
                    {
                        existingIdx = j;
                        break;
                    }
                }

                if (existingIdx < 0)
                {
                    FusedContact contact = new FusedContact
                    {
                        Target = track.TargetUnit,
                        TargetName = track.TargetName,
                        TargetPos = track.TargetPos,
                        HasRadarTrack = true,
                        IsVisual = false,
                        IsRwr = false,
                        BestSourceName = track.SourceName,
                        BestDistanceKm = slantDistKm,
                        TotalFeeds = 1
                    };
                    _fusedContacts.Add(contact);
                }
                else
                {
                    FusedContact contact = _fusedContacts[existingIdx];
                    contact.TotalFeeds++;
                    // Prioritize closer radar source (strongest illuminator / minimum slant range)
                    if (!contact.HasRadarTrack || slantDistKm < contact.BestDistanceKm)
                    {
                        contact.HasRadarTrack = true;
                        contact.BestSourceName = track.SourceName;
                        contact.BestDistanceKm = slantDistKm;
                        contact.IsVisual = false;
                    }
                    _fusedContacts[existingIdx] = contact;
                }
            }

            // Aggregate shared visual reconnaissance contacts
            for (int i = 0; i < telemetry.SharedVisualContacts.Count; i++)
            {
                var v = telemetry.SharedVisualContacts[i];
                if (v.TargetUnit == null || v.TargetUnit.disabled || !Patches.CombatHUDPatches.IsTrackableThreat(v.TargetUnit, playerHq)) continue;

                float visDistKm = v.DistanceMeters * 0.001f;
                int existingIdx = -1;
                for (int j = 0; j < _fusedContacts.Count; j++)
                {
                    if (_fusedContacts[j].Target == v.TargetUnit)
                    {
                        existingIdx = j;
                        break;
                    }
                }

                if (existingIdx < 0)
                {
                    FusedContact contact = new FusedContact
                    {
                        Target = v.TargetUnit,
                        TargetName = v.TargetName,
                        TargetPos = v.TargetPos,
                        HasRadarTrack = false,
                        IsVisual = true,
                        IsRwr = false,
                        BestSourceName = v.SpotterName,
                        BestDistanceKm = visDistKm,
                        TotalFeeds = 1
                    };
                    _fusedContacts.Add(contact);
                }
                else
                {
                    FusedContact contact = _fusedContacts[existingIdx];
                    contact.TotalFeeds++;
                    // If no radar track exists and this visual spotter is closer, update
                    if (!contact.HasRadarTrack && visDistKm < contact.BestDistanceKm)
                    {
                        contact.BestSourceName = v.SpotterName;
                        contact.BestDistanceKm = visDistKm;
                    }
                    _fusedContacts[existingIdx] = contact;
                }
            }

            // Populate TrackingQuality and Uncertainty for existing radar/visual contacts
            for (int i = 0; i < _fusedContacts.Count; i++)
            {
                FusedContact fc = _fusedContacts[i];
                TriangulationTrack tr = RWRTriangulationProcessor.GetTrack(playerHq, fc.Target);
                if (tr != null)
                {
                    fc.TrackingQuality = tr.TrackingQuality;
                    fc.UncertaintyRadiusKm = tr.UncertaintyRadiusMeters * 0.001f;
                    fc.IsInMemory = tr.IsInMemoryState(120.0f);
                    fc.IsTriangulated = tr.IsTriangulated;
                }
                else
                {
                    fc.TrackingQuality = fc.HasRadarTrack ? 0.90f : 1.0f;
                    fc.UncertaintyRadiusKm = fc.HasRadarTrack ? 0.1f : 0.0f;
                }
                _fusedContacts[i] = fc;
            }

            // Ingest RWR / ESM Triangulation tracks from team intelligence
            Dictionary<Unit, TriangulationTrack> esmTracks = RWRTriangulationProcessor.GetTrackDictionary(playerHq);
            if (esmTracks != null)
            {
                foreach (var kvp in esmTracks)
                {
                    Unit emitter = kvp.Key;
                    TriangulationTrack tTrack = kvp.Value;
                    if (emitter == null || emitter.disabled || tTrack == null) continue;
                    if (tTrack.IsExpired(120.0f)) continue;
                    if (!Patches.CombatHUDPatches.IsTrackableThreat(emitter, playerHq)) continue;

                    int existingIdx = -1;
                    for (int j = 0; j < _fusedContacts.Count; j++)
                    {
                        if (_fusedContacts[j].Target == emitter)
                        {
                            existingIdx = j;
                            break;
                        }
                    }

                    if (existingIdx >= 0)
                    {
                        FusedContact contact = _fusedContacts[existingIdx];
                        contact.TrackingQuality = tTrack.TrackingQuality;
                        contact.UncertaintyRadiusKm = tTrack.UncertaintyRadiusMeters * 0.001f;
                        contact.IsInMemory = tTrack.IsInMemoryState(120.0f);
                        contact.IsTriangulated = tTrack.IsTriangulated;
                        _fusedContacts[existingIdx] = contact;
                    }
                    else
                    {
                        // Target tracked exclusively via RWR triangulation
                        float distKm = (playerAircraft != null)
                            ? Vector3.Distance(playerAircraft.transform.position, tTrack.TriangulatedWorldPos) * 0.001f
                            : 0f;

                        FusedContact contact = new FusedContact
                        {
                            Target = emitter,
                            TargetName = !string.IsNullOrEmpty(emitter.unitName) ? emitter.unitName : emitter.name,
                            TargetPos = tTrack.TriangulatedWorldPos,
                            HasRadarTrack = false,
                            IsVisual = false,
                            IsRwr = true,
                            BestSourceName = tTrack.IsTriangulated ? "ESM Triangulation" : "RWR Bearing Strobe",
                            BestDistanceKm = distKm,
                            TotalFeeds = 1,
                            TrackingQuality = tTrack.TrackingQuality,
                            UncertaintyRadiusKm = tTrack.UncertaintyRadiusMeters * 0.001f,
                            IsInMemory = tTrack.IsInMemoryState(120.0f),
                            IsTriangulated = tTrack.IsTriangulated
                        };
                        _fusedContacts.Add(contact);
                    }
                }
            }
        }

        private void DrawScreenSpaceReticles()
        {
            Camera cam = CameraStateManager.i != null && CameraStateManager.i.mainCamera != null ? CameraStateManager.i.mainCamera : Camera.main;
            if (cam == null) return;

            var telemetry = DatalinkNetwork.LatestTelemetry;
            if (telemetry == null || !telemetry.NetworkActive) return;

            _drawnBadgeRects.Clear();

            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float gazeRadius = Mathf.Min(Screen.width, Screen.height) * 0.11f;

            // 1. Badges over friendly nodes (icon always visible, badge on gaze hover)
            for (int i = 0; i < telemetry.Nodes.Count; i++)
            {
                var node = telemetry.Nodes[i];
                if (node.Unit == null || node.Unit.disabled) continue;

                Vector3 screenPos = cam.WorldToScreenPoint(node.Unit.transform.position);
                if (screenPos.z <= 1.0f) continue;

                float guiX = screenPos.x;
                float guiY = Screen.height - screenPos.y;
                if (guiX < 20 || guiX > Screen.width - 20 || guiY < 20 || guiY > Screen.height - 20) continue;

                Color nodeCol = new Color(0.20f, 0.75f, 1.0f);
                DrawBoxOutline(new Rect(guiX - 6, guiY - 6, 12, 12), 2.0f, nodeCol);

                if (Vector2.Distance(new Vector2(guiX, guiY), screenCenter) <= gazeRadius)
                {
                    string badgeText = string.Format("■ {0} [{1}]\nSharing: {2} radar / {3} visual",
                        node.UnitName, node.Role, node.SharedRadarCount, node.SharedVisualCount);

                    _calcContent.text = badgeText;
                    Vector2 textSize = _hudTextStyle.CalcSize(_calcContent);
                    float cardWidth = Mathf.Ceil(textSize.x) + 18f;
                    float cardHeight = Mathf.Ceil(textSize.y) + 8f;

                    Rect badgeRect = GetBadgeRect(guiX, guiY, cardWidth, cardHeight);
                    TryDrawBadge(badgeRect, badgeText, nodeCol);
                }
            }

            // 2. Render aggregated contacts (already fused in Update)

            // Render aggregated contacts (icon always visible, badge on gaze hover)
            for (int i = 0; i < _fusedContacts.Count; i++)
            {
                var contact = _fusedContacts[i];
                if (contact.Target == null || contact.Target.disabled) continue;

                Vector3 targetWorldPos = (contact.IsRwr && !contact.IsTriangulated) ? contact.TargetPos : contact.Target.transform.position;
                Vector3 screenPos = cam.WorldToScreenPoint(targetWorldPos);
                if (screenPos.z <= 1.0f) continue;

                float guiX = screenPos.x;
                float guiY = Screen.height - screenPos.y;
                if (guiX < 20 || guiX > Screen.width - 20 || guiY < 20 || guiY > Screen.height - 20) continue;

                bool isHovered = Vector2.Distance(new Vector2(guiX, guiY), screenCenter) <= gazeRadius;

                if (contact.HasRadarTrack)
                {
                    Color trackCol = new Color(0.20f, 0.95f, 0.55f);
                    DrawDiamondOutline(guiX, guiY, 14.0f, 2.0f, trackCol);

                    // Compact tracking quality indicator below reticle
                    DrawReticleTag(guiX, guiY + 12f, GetQPercentString(contact.TrackingQuality), trackCol);

                    if (isHovered)
                    {
                        string trackText = string.Format("◆ [DATALINK RADAR] {0}\nSource: {1} | R: {2:F1} km\nIntel: Q: {3:P0} | CEP: {4:F1} km{5}",
                            contact.TargetName, contact.BestSourceName, contact.BestDistanceKm,
                            contact.TrackingQuality, contact.UncertaintyRadiusKm,
                            contact.IsInMemory ? " [MEMORY: 120s]" : "");

                        _calcContent.text = trackText;
                        Vector2 textSize = _hudTextStyle.CalcSize(_calcContent);
                        float cardWidth = Mathf.Ceil(textSize.x) + 18f;
                        float cardHeight = Mathf.Ceil(textSize.y) + 8f;

                        Rect trackRect = GetBadgeRect(guiX, guiY, cardWidth, cardHeight);
                        TryDrawBadge(trackRect, trackText, trackCol);
                    }
                }
                else if (contact.IsVisual)
                {
                    Color visualCol = new Color(1.0f, 0.85f, 0.15f);
                    DrawBoxOutline(new Rect(guiX - 7, guiY - 7, 14, 14), 2.0f, visualCol);

                    // Compact tracking quality indicator below reticle
                    DrawReticleTag(guiX, guiY + 12f, GetQPercentString(contact.TrackingQuality), visualCol);

                    if (isHovered)
                    {
                        string visText = string.Format("▲ [VISUAL RECON] {0}\nSpotter: {1} | D: {2:F1} km\nIntel: Q: {3:P0} | High Confidence{4}",
                            contact.TargetName, contact.BestSourceName, contact.BestDistanceKm,
                            contact.TrackingQuality,
                            contact.IsInMemory ? " [MEMORY: 120s]" : "");

                        _calcContent.text = visText;
                        Vector2 textSize = _hudTextStyle.CalcSize(_calcContent);
                        float cardWidth = Mathf.Ceil(textSize.x) + 18f;
                        float cardHeight = Mathf.Ceil(textSize.y) + 8f;

                        Rect visRect = GetBadgeRect(guiX, guiY, cardWidth, cardHeight);
                        TryDrawBadge(visRect, visText, visualCol);
                    }
                }
                else
                {
                    // RWR / ESM Triangulation contact
                    Color rwrCol = contact.IsTriangulated ? new Color(1.0f, 0.60f, 0.15f) : new Color(0.95f, 0.40f, 0.85f);
                    DrawDiamondOutline(guiX, guiY, 15.0f, 1.5f, rwrCol);
                    DrawBoxOutline(new Rect(guiX - 5, guiY - 5, 10, 10), 1.5f, rwrCol);

                    // Compact tracking quality indicator below reticle
                    DrawReticleTag(guiX, guiY + 12f, GetQPercentString(contact.TrackingQuality), rwrCol);

                    if (isHovered)
                    {
                        string rwrText = string.Format("◎ [RWR TRIANGULATION] {0}\nMethod: {1} | D: {2:F1} km\nIntel: Q: {3:P0} | CEP: {4:F1} km{5}",
                            contact.TargetName, contact.BestSourceName, contact.BestDistanceKm,
                            contact.TrackingQuality, contact.UncertaintyRadiusKm,
                            contact.IsInMemory ? " [MEMORY: 120s]" : "");

                        _calcContent.text = rwrText;
                        Vector2 textSize = _hudTextStyle.CalcSize(_calcContent);
                        float cardWidth = Mathf.Ceil(textSize.x) + 18f;
                        float cardHeight = Mathf.Ceil(textSize.y) + 8f;

                        Rect rwrRect = GetBadgeRect(guiX, guiY, cardWidth, cardHeight);
                        TryDrawBadge(rwrRect, rwrText, rwrCol);
                    }
                }
            }

            // 3. Badges over relayed missile warnings (icon always visible, badge on gaze hover)
            for (int i = 0; i < telemetry.RelayedMissileAlerts.Count; i++)
            {
                var alert = telemetry.RelayedMissileAlerts[i];
                if (alert.MissileUnit == null || alert.MissileUnit.disabled) continue;

                Vector3 screenPos = cam.WorldToScreenPoint(alert.MissileUnit.transform.position);
                if (screenPos.z <= 1.0f) continue;

                float guiX = screenPos.x;
                float guiY = Screen.height - screenPos.y;
                if (guiX < 20 || guiX > Screen.width - 20 || guiY < 20 || guiY > Screen.height - 20) continue;

                Color warnCol = new Color(1.0f, 0.22f, 0.22f);
                DrawBoxOutline(new Rect(guiX - 9, guiY - 9, 18, 18), 2.5f, warnCol);

                if (Vector2.Distance(new Vector2(guiX, guiY), screenCenter) <= gazeRadius)
                {
                    string warnText = string.Format("▼ [MISSILE ALERT] {0}\nDist: {1:F1} km",
                        alert.RadarName, alert.DistToPlayer * 0.001f);

                    _calcContent.text = warnText;
                    Vector2 textSize = _hudTextStyle.CalcSize(_calcContent);
                    float cardWidth = Mathf.Ceil(textSize.x) + 18f;
                    float cardHeight = Mathf.Ceil(textSize.y) + 8f;

                    Rect warnRect = GetBadgeRect(guiX, guiY, cardWidth, cardHeight);
                    TryDrawBadge(warnRect, warnText, warnCol);
                }
            }
        }

        private void DrawDiagnosticWindow(int windowId)
        {
            var telemetry = DatalinkNetwork.LatestTelemetry;

            // Header Section
            GUILayout.BeginHorizontal();
            GUILayout.Label("TACTICAL ALLIED DATALINK", _titleStyle);
            bool online = telemetry != null && telemetry.NetworkActive;
            GUILayout.Label(online ? "[LINK ONLINE]" : "[OFFLINE]", online ? _badgeActiveStyle : _badgeInactiveStyle);
            GUILayout.EndHorizontal();

            KeyCode toggleKey = (RadioWarsConfig.ToggleDatalinkVisualizerKey != null) ? RadioWarsConfig.ToggleDatalinkVisualizerKey.Value : KeyCode.F8;
            GUILayout.Label(string.Format("Network Faction: {0} | Cycle: 0.35s | Toggle: {1}",
                telemetry != null ? telemetry.FactionName : "None", toggleKey), _subHeaderStyle);

            DrawDivider();

            // Section 1: Connected Allied Nodes
            int nodeCount = telemetry != null ? telemetry.Nodes.Count : 0;
            GUILayout.Label(string.Format("CONNECTED ALLIED NODES ({0})", nodeCount), _sectionHeaderStyle);

            if (telemetry != null && telemetry.Nodes.Count > 0)
            {
                for (int i = 0; i < Mathf.Min(6, telemetry.Nodes.Count); i++)
                {
                    var node = telemetry.Nodes[i];
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(string.Format("• {0}", node.UnitName), _labelStyle, GUILayout.Width(220));
                    GUILayout.Label(string.Format("[{0}] Tracks: {1}", node.Role, node.SharedRadarCount + node.SharedVisualCount), _valueStyle);
                    GUILayout.EndHorizontal();
                }
                if (telemetry.Nodes.Count > 6)
                {
                    GUILayout.Label(string.Format("  ... and {0} more allied units connected", telemetry.Nodes.Count - 6), _subHeaderStyle);
                }
            }
            else
            {
                GUILayout.Label("  No allied datalink nodes currently transmitting.", _labelStyle);
            }

            DrawDivider();

            // Section 2: Shared Sensor Tracks & ESM Triangulation
            int radarCount = telemetry != null ? telemetry.SharedRadarTracks.Count : 0;
            int visualCount = telemetry != null ? telemetry.SharedVisualContacts.Count : 0;
            int rwrCount = 0;
            for (int i = 0; i < _fusedContacts.Count; i++)
            {
                if (_fusedContacts[i].IsRwr) rwrCount++;
            }

            GUILayout.Label(string.Format("FUSED THREAT TRACKS (Radar: {0} | Visual: {1} | RWR/ESM: {2})",
                radarCount, visualCount, rwrCount), _sectionHeaderStyle);

            if (_fusedContacts.Count > 0)
            {
                int shown = 0;
                for (int i = 0; i < _fusedContacts.Count && shown < 8; i++)
                {
                    var fc = _fusedContacts[i];
                    GUILayout.BeginHorizontal();
                    string prefix = fc.HasRadarTrack ? "◆" : (fc.IsVisual ? "▲" : "◎");
                    string memStr = fc.IsInMemory ? " [MEM]" : "";
                    GUILayout.Label(string.Format("{0} {1}{2}", prefix, fc.TargetName, memStr), _labelStyle, GUILayout.Width(170));
                    GUILayout.Label(string.Format("[Q: {0:P0}] {1} ({2:F1} km, CEP: {3:F1} km)",
                        fc.TrackingQuality, fc.BestSourceName, fc.BestDistanceKm, fc.UncertaintyRadiusKm), _valueStyle);
                    GUILayout.EndHorizontal();
                    shown++;
                }
                if (_fusedContacts.Count > 8)
                {
                    GUILayout.Label(string.Format("  ... and {0} more tracks in database", _fusedContacts.Count - 8), _subHeaderStyle);
                }
            }
            else
            {
                GUILayout.Label("  No hostile tracks currently shared across network.", _labelStyle);
            }

            // Section 3: Relayed Threat Alerts (if any)
            if (telemetry != null && telemetry.RelayedMissileAlerts.Count > 0)
            {
                DrawDivider();
                GUILayout.Label(string.Format("RELAYED MISSILE ALERTS ({0})", telemetry.RelayedMissileAlerts.Count), _sectionHeaderStyle);
                for (int i = 0; i < telemetry.RelayedMissileAlerts.Count; i++)
                {
                    var m = telemetry.RelayedMissileAlerts[i];
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("▼ INBOUND MISSILE", _labelStyle, GUILayout.Width(180));
                    GUILayout.Label(string.Format("detected by {0} ({1:F1} km)", m.RadarName, m.DistToPlayer * 0.001f), _valueStyle);
                    GUILayout.EndHorizontal();
                }
            }

            // Section 4: Triangulation / ESM Tracking Status
            var trackDict = (telemetry != null && telemetry.PlayerHQ != null) ? RWRTriangulationProcessor.GetTrackDictionary(telemetry.PlayerHQ) : null;
            if (trackDict != null && trackDict.Count > 0)
            {
                DrawDivider();
                int triangulatedCount = 0;
                foreach (var kvp in trackDict)
                {
                    if (kvp.Value.IsTriangulated) triangulatedCount++;
                }
                GUILayout.Label(string.Format("ESM TRIANGULATION (Active: {0} | Triangulated: {1})", trackDict.Count, triangulatedCount), _sectionHeaderStyle);
            }

            GUI.DragWindow(new Rect(0, 0, 10000, 24));
        }

        private void DrawDivider()
        {
            GUILayout.Space(3);
            Rect r = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(1), GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint && _dividerTex != null)
            {
                GUI.DrawTexture(r, _dividerTex);
            }
            GUILayout.Space(3);
        }

        private void DrawBoxOutline(Rect r, float thickness, Color col)
        {
            Color oldCol = GUI.color;
            GUI.color = col;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, thickness), _whiteTex);
            GUI.DrawTexture(new Rect(r.x, r.y + r.height - thickness, r.width, thickness), _whiteTex);
            GUI.DrawTexture(new Rect(r.x, r.y, thickness, r.height), _whiteTex);
            GUI.DrawTexture(new Rect(r.x + r.width - thickness, r.y, thickness, r.height), _whiteTex);
            GUI.color = oldCol;
        }

        private void DrawDiamondOutline(float cx, float cy, float radius, float thickness, Color col)
        {
            Vector3 top = new Vector3(cx, cy - radius, 0);
            Vector3 right = new Vector3(cx + radius, cy, 0);
            Vector3 bottom = new Vector3(cx, cy + radius, 0);
            Vector3 left = new Vector3(cx - radius, cy, 0);

            DrawGuiLine(top, right, thickness, col);
            DrawGuiLine(right, bottom, thickness, col);
            DrawGuiLine(bottom, left, thickness, col);
            DrawGuiLine(left, top, thickness, col);
        }

        private void DrawGuiLine(Vector3 p1, Vector3 p2, float width, Color col)
        {
            Color old = GUI.color;
            GUI.color = col;
            Matrix4x4 matrix = GUI.matrix;

            float angle = Vector3.Angle(p2 - p1, Vector2.right);
            if (p1.y > p2.y) angle = -angle;

            GUIUtility.ScaleAroundPivot(new Vector2((p2 - p1).magnitude, width), new Vector2(p1.x, p1.y + 0.5f));
            GUIUtility.RotateAroundPivot(angle, new Vector2(p1.x, p1.y + 0.5f));
            GUI.DrawTexture(new Rect(p1.x, p1.y, 1, 1), _whiteTex);

            GUI.matrix = matrix;
            GUI.color = old;
        }

        private void InitStyles()
        {
            if (_windowStyle != null) return;

            _windowStyle = new GUIStyle(GUI.skin.box);
            _windowStyle.normal.background = _windowBgTex;
            _windowStyle.padding = new RectOffset(14, 14, 10, 12);
            _windowStyle.border = new RectOffset(1, 1, 1, 1);

            _titleStyle = new GUIStyle(GUI.skin.label);
            _titleStyle.fontSize = 12;
            _titleStyle.fontStyle = FontStyle.Bold;
            _titleStyle.normal.textColor = new Color(0.20f, 0.85f, 1.0f);
            _titleStyle.alignment = TextAnchor.MiddleLeft;

            _badgeActiveStyle = new GUIStyle(GUI.skin.label);
            _badgeActiveStyle.fontSize = 11;
            _badgeActiveStyle.fontStyle = FontStyle.Bold;
            _badgeActiveStyle.normal.textColor = new Color(0.20f, 0.95f, 0.55f);
            _badgeActiveStyle.alignment = TextAnchor.MiddleRight;

            _badgeInactiveStyle = new GUIStyle(GUI.skin.label);
            _badgeInactiveStyle.fontSize = 11;
            _badgeInactiveStyle.fontStyle = FontStyle.Bold;
            _badgeInactiveStyle.normal.textColor = new Color(0.85f, 0.45f, 0.20f);
            _badgeInactiveStyle.alignment = TextAnchor.MiddleRight;

            _subHeaderStyle = new GUIStyle(GUI.skin.label);
            _subHeaderStyle.fontSize = 10;
            _subHeaderStyle.normal.textColor = new Color(0.55f, 0.65f, 0.75f);
            _subHeaderStyle.alignment = TextAnchor.MiddleLeft;

            _sectionHeaderStyle = new GUIStyle(GUI.skin.label);
            _sectionHeaderStyle.fontSize = 11;
            _sectionHeaderStyle.fontStyle = FontStyle.Bold;
            _sectionHeaderStyle.normal.textColor = new Color(0.35f, 0.78f, 0.95f);
            _sectionHeaderStyle.padding = new RectOffset(0, 0, 4, 2);

            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.fontSize = 11;
            _labelStyle.normal.textColor = new Color(0.72f, 0.80f, 0.90f);
            _labelStyle.wordWrap = false;
            _labelStyle.alignment = TextAnchor.MiddleLeft;

            _valueStyle = new GUIStyle(GUI.skin.label);
            _valueStyle.fontSize = 11;
            _valueStyle.fontStyle = FontStyle.Bold;
            _valueStyle.normal.textColor = new Color(0.92f, 0.96f, 1.0f);

            _hudTextStyle = new GUIStyle(GUI.skin.label);
            _hudTextStyle.fontSize = 11;
            _hudTextStyle.fontStyle = FontStyle.Bold;
            _hudTextStyle.padding = new RectOffset(0, 0, 0, 0);
            _hudTextStyle.margin = new RectOffset(0, 0, 0, 0);
            _hudTextStyle.alignment = TextAnchor.UpperLeft;
            _hudTextStyle.wordWrap = false;
            _hudTextStyle.clipping = TextClipping.Clip;

            _hudTagStyle = new GUIStyle(GUI.skin.label);
            _hudTagStyle.fontSize = 10;
            _hudTagStyle.fontStyle = FontStyle.Bold;
            _hudTagStyle.padding = new RectOffset(2, 2, 0, 0);
            _hudTagStyle.margin = new RectOffset(0, 0, 0, 0);
            _hudTagStyle.alignment = TextAnchor.MiddleCenter;
            _hudTagStyle.wordWrap = false;
        }

        private void DrawReticleTag(float cx, float cy, string text, Color accentCol)
        {
            if (_hudTagStyle == null) return;
            Vector2 textSize = _hudTagStyle.CalcSize(new GUIContent(text));
            float w = Mathf.Ceil(textSize.x) + 6f;
            float h = Mathf.Ceil(textSize.y) + 2f;
            Rect tagRect = new Rect(cx - (w * 0.5f), cy, w, h);

            Color old = GUI.color;
            GUI.color = new Color(0.04f, 0.08f, 0.14f, 0.85f);
            GUI.DrawTexture(tagRect, _whiteTex);

            DrawBoxOutline(tagRect, 1.0f, new Color(accentCol.r, accentCol.g, accentCol.b, 0.65f));
            GUI.color = old;

            _hudTagStyle.normal.textColor = accentCol;
            GUI.Label(tagRect, text, _hudTagStyle);
        }
    }
}
