using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class OverdrawCheckerWindow : EditorWindow
{
    bool isEnabled => _monitorsGo != null && Application.isPlaying;
    GameObject _monitorsGo;
    Dictionary<CameraOverdrawChecker, CameraOverdrawStats> _stats;
    private Dictionary<string, List<float>> m_CacheOverdrawData;
    private static Vector2 m_CurvePanelSize = new Vector2(300, 600);
    private static Vector2 m_OffsetFactor = new Vector2(0, -20);
    private static Vector2 m_OffsetFactor2 = new Vector2(0, -150);

    [MenuItem("Tools/Overdraw Checker")]
    static void ShowWindow()
    {
        GetWindow<OverdrawCheckerWindow>().Show();
    }

    void Init()
    {
        if (_monitorsGo != null)
            throw new Exception("Attempt to start overdraw checker twice");

        _monitorsGo = new GameObject("OverdrawChecker");
        _monitorsGo.hideFlags = HideFlags.HideAndDontSave;
        _stats = new Dictionary<CameraOverdrawChecker, CameraOverdrawStats>();
        m_CacheOverdrawData = new Dictionary<string, List<float>>();
        m_CurvePanelSize = new Vector2(this.position.width, m_CurvePanelSize.y);
    }

    void TryShutdown()
    {
        if (_monitorsGo == null)
            return;

        DestroyImmediate(_monitorsGo);
        _stats = null;
    }

    void Update()
    {
        // Check shutdown if needed
        if (!isEnabled)
        {
            TryShutdown();
            return;
        }

        Camera[] activeCameras = Camera.allCameras;

        // Remove monitors for non-active cameras
        var monitors = GetAllCheckers();
        foreach (var monitor in monitors)
            if (!Array.Exists(activeCameras, c => monitor.targetCamera == c))
                DestroyImmediate(monitor);

        // Add new monitors
        monitors = GetAllCheckers();
        foreach (Camera activeCamera in activeCameras)
        {
            if (!Array.Exists(monitors,m => m.targetCamera == activeCamera))
            {
                var monitor = _monitorsGo.AddComponent<CameraOverdrawChecker>();
                monitor.SetTargetCamera(activeCamera);
            }
        }
    }

    CameraOverdrawChecker[] GetAllCheckers()
    {
        return _monitorsGo.GetComponentsInChildren<CameraOverdrawChecker>(true);
    }

    void OnGUI()
    {
        if (Application.isPlaying)
        {
            int startButtonHeight = 25;
            if (GUILayout.Button(isEnabled ? "Stop" : "Start", GUILayout.MaxWidth(100), GUILayout.MaxHeight(startButtonHeight)))
            {
                if (!isEnabled)
                    Init();
                else
                    TryShutdown();
            }

            if (isEnabled)
            {
                CameraOverdrawChecker[] monitors = GetAllCheckers();

                GUILayout.Space(-startButtonHeight);
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("Reset Stats", GUILayout.Width(100), GUILayout.Height(20)))
                        ResetStats();
                }

                GUILayout.Space(5);

                Vector2Int gameViewResolution = GetGameViewResolution();
                GUILayout.Label($"Screen {gameViewResolution.x}x{gameViewResolution.y}");

                GUILayout.Space(5);

                foreach (CameraOverdrawChecker checker in _stats.Keys.ToArray())
                    if (!Array.Exists(monitors, m => checker))
                        _stats.Remove(checker);

                long gameViewArea = gameViewResolution.x * gameViewResolution.y;
                float totalGlobalOverdrawRatio = 0f;
                foreach (CameraOverdrawChecker checker in monitors)
                {
                    if (!m_CacheOverdrawData.TryGetValue(checker.targetCamera.name, out var overdrawData))
                    {
                        overdrawData = new List<float>();
                        m_CacheOverdrawData[checker.targetCamera.name] = overdrawData;
                    }
                    overdrawData.Add(checker.fragmentsCount / (float)gameViewArea);
                    using (new GUILayout.HorizontalScope())
                    {
                        Camera cam = checker.targetCamera;
                        GUILayout.Label($"{cam.name} {cam.pixelWidth}x{cam.pixelHeight}");

                        GUILayout.FlexibleSpace();

                        float localOverdrawRatio = checker.overdrawRatio;
                        float globalOverdrawRatio = checker.fragmentsCount / (float)gameViewArea;
                        totalGlobalOverdrawRatio += globalOverdrawRatio;

                        if (!_stats.TryGetValue(checker, out CameraOverdrawStats monitorStats))
                        {
                            monitorStats = new CameraOverdrawStats();
                            _stats.Add(checker, monitorStats);
                        }
                        monitorStats.maxLocalOverdrawRatio = Math.Max(localOverdrawRatio, monitorStats.maxLocalOverdrawRatio);
                        monitorStats.maxGlobalOverdrawRatio = Math.Max(globalOverdrawRatio, monitorStats.maxGlobalOverdrawRatio);

                        GUILayout.Label(FormatResult("Local: {0} / {1} \t Global: {2} / {3}",
                            localOverdrawRatio, monitorStats.maxLocalOverdrawRatio, globalOverdrawRatio, monitorStats.maxGlobalOverdrawRatio));
                    }
                }

                if (!m_CacheOverdrawData.TryGetValue("Total", out var overdrawData1))
                {
                    overdrawData1 = new List<float>();
                    m_CacheOverdrawData["Total"] = overdrawData1;
                }
                overdrawData1.Add(totalGlobalOverdrawRatio);

                GUILayout.Space(5);

                float maxTotalGlobalOverdrawRatio = 0f;
                foreach (CameraOverdrawStats stat in _stats.Values)
                    maxTotalGlobalOverdrawRatio += stat.maxGlobalOverdrawRatio;
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label("Total");
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(FormatResult("Global: {0} / {1}", totalGlobalOverdrawRatio, maxTotalGlobalOverdrawRatio));
                }
            }
        }
        else
        {
            GUILayout.Label("Available only in Play mode");
        }
        DrawOverdrawCurve();
        Repaint();
    }

    void ResetStats()
    {
        _stats.Clear();
    }

    string FormatResult(string format, params float[] args)
    {
        var stringArgs = new List<string>();
        foreach (float arg in args)
            stringArgs.Add($"{arg:N3}");
        return string.Format(format, stringArgs.ToArray());
    }

    static Vector2Int GetGameViewResolution()
    {
        var resString = UnityStats.screenRes.Split('x');
        return new Vector2Int(int.Parse(resString[0]), int.Parse(resString[1]));
    }

    private void DrawOverdrawCurve()
    {
        if (m_CacheOverdrawData == null)
            return;
        GUILayout.BeginVertical();
        GUILayout.BeginArea(new Rect(new Vector2(10, (this.position.size - m_CurvePanelSize).y), m_CurvePanelSize));
        GUI.skin.label.fontSize = 12;
        GUI.skin.label.alignment = TextAnchor.UpperLeft;

        int index = 0;
        float last = 0;
        foreach(var cameraData in m_CacheOverdrawData)
        {
            GUI.Label(new Rect(new Vector2(0, m_CurvePanelSize.y + index * m_OffsetFactor2.y - 20) + m_OffsetFactor * 1f, m_CurvePanelSize), "1x—");
            GUI.Label(new Rect(new Vector2(0, m_CurvePanelSize.y + index * m_OffsetFactor2.y - 20) + m_OffsetFactor * 2f, m_CurvePanelSize), "2x—");
            GUI.Label(new Rect(new Vector2(0, m_CurvePanelSize.y + index * m_OffsetFactor2.y - 20) + m_OffsetFactor * 3f, m_CurvePanelSize), "3x—");
            GUI.Label(new Rect(new Vector2(0, m_CurvePanelSize.y + index * m_OffsetFactor2.y - 20) + m_OffsetFactor * 4f, m_CurvePanelSize), "4x—");
            GUI.Label(new Rect(new Vector2(0, m_CurvePanelSize.y + index * m_OffsetFactor2.y - 20) + m_OffsetFactor * 5f, m_CurvePanelSize), "5x—");
            GUI.Label(new Rect(new Vector2(0, m_CurvePanelSize.y + index * m_OffsetFactor2.y - 20) + m_OffsetFactor * 0f, m_CurvePanelSize), cameraData.Key);
            for(int i = 0; i < cameraData.Value.Count; i++)
            {
                var cur = cameraData.Value[i];
                DrawLine(last, cur, cameraData.Key == "Total" ? 4 : 3, i, index, 20);
                last = cur;
            }
            if (cameraData.Value.Count > this.position.width - 25)
                cameraData.Value.RemoveAt(0);
            index++;
        }
        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    private static void DrawLine(float lastCount, float curCount, int standard, int x, int y, float scale = 1)
    {
        UnityEngine.Color color = curCount > standard ? UnityEngine.Color.red : UnityEngine.Color.green;
        Vector2 offset = m_OffsetFactor2;
        DrawLine(GetLinePos(x, 0, scale) + offset * y, GetLinePos(x, curCount, scale) + offset * y, color);
    }

    private static Vector2 GetLinePos(int index, float value, float scale = 1)
    {
        return new Vector2(index + 25, m_CurvePanelSize.y - value * scale - 10);
    }

    public static void DrawLine(Vector2 from, Vector2 to, Color color)
    {
        var oriColor = Handles.color;
        Handles.color = color;
        Handles.DrawLine(from, to);
        Handles.color = oriColor;
    }
}

class CameraOverdrawStats
{
    public float maxLocalOverdrawRatio;
    public float maxGlobalOverdrawRatio;
}
