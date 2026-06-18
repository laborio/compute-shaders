using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public class CrowdPerformanceDebugUI : MonoBehaviour
{
    [SerializeField] private TMP_Text fpsText;
    [SerializeField] private TMP_Text cpuMainText;
    [SerializeField] private TMP_Text drawCallsText;
    [SerializeField] private TMP_Text trisText;
    [SerializeField] private TMP_Text setPassText;
    [SerializeField] private TMP_Text shadowCastersText;
    [SerializeField] private TMP_Text renderThreadText;
    [SerializeField] private float refreshInterval = 0.25f;
    [SerializeField] private bool showWebGLDebugConsole = true;
    [SerializeField] private KeyCode toggleConsoleKey = KeyCode.BackQuote;
    [SerializeField] private int maxLogEntries = 40;

    private readonly FrameTiming[] frameTimings = new FrameTiming[1];
    private readonly List<string> runtimeLogs = new();
    private CrowdController[] crowdControllers;
    private float refreshTimer;
    private float smoothedDeltaTime;
    private bool isConsoleOpen;
    private string cachedConsoleText = string.Empty;
    private Vector2 consoleScroll;
    private GUIStyle buttonStyle;
    private GUIStyle textAreaStyle;
    private GUIStyle labelStyle;

    private void OnEnable()
    {
        crowdControllers = FindObjectsByType<CrowdController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        smoothedDeltaTime = Mathf.Max(Time.unscaledDeltaTime, 1f / 60f);
        refreshTimer = 0f;
        Application.logMessageReceived += HandleLogMessage;
        Refresh();
    }

    private void OnDisable()
    {
        Application.logMessageReceived -= HandleLogMessage;
    }

    private void Update()
    {
        float deltaTime = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
        smoothedDeltaTime = Mathf.Lerp(smoothedDeltaTime, deltaTime, 0.1f);

        if (showWebGLDebugConsole && WasTogglePressed())
        {
            isConsoleOpen = !isConsoleOpen;
            if (isConsoleOpen)
            {
                cachedConsoleText = BuildConsoleText();
            }
        }

        refreshTimer -= deltaTime;
        if (refreshTimer > 0f)
        {
            return;
        }

        refreshTimer = refreshInterval;
        Refresh();
    }

    private void Refresh()
    {
        if (crowdControllers == null || crowdControllers.Length == 0)
        {
            crowdControllers = FindObjectsByType<CrowdController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        }

        int drawCalls = 0;
        int setPassCalls = 0;
        int shadowCasters = 0;
        long triangles = 0;

        for (int i = 0; i < crowdControllers.Length; i++)
        {
            CrowdController controller = crowdControllers[i];
            if (controller == null || !controller.isActiveAndEnabled)
            {
                continue;
            }

            drawCalls += controller.LastDrawCallCount;
            setPassCalls += controller.LastSetPassCount;
            shadowCasters += controller.LastShadowCasterCount;
            triangles += controller.LastTriangleCount;
        }

        float cpuMainMs = smoothedDeltaTime * 1000f;
        float renderThreadMs = cpuMainMs;

        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, frameTimings) > 0)
        {
            cpuMainMs = (float)frameTimings[0].cpuFrameTime;
            if (frameTimings[0].gpuFrameTime > 0.01)
            {
                renderThreadMs = (float)frameTimings[0].gpuFrameTime;
            }
        }

        float fps = cpuMainMs > 0.001f ? 1000f / cpuMainMs : 0f;

        SetText(fpsText, $"FPS {fps:0}");
        SetText(cpuMainText, $"cpu main {cpuMainMs:0.0} ms");
        SetText(drawCallsText, $"drawcalls {drawCalls}");
        SetText(trisText, $"Tris {FormatThousands(triangles)}");
        SetText(setPassText, $"set pass {setPassCalls}");
        SetText(shadowCastersText, $"shadow casters {shadowCasters}");
        SetText(renderThreadText, $"render thread {renderThreadMs:0.0} ms");

        if (isConsoleOpen)
        {
            cachedConsoleText = BuildConsoleText();
        }
    }

    private static void SetText(TMP_Text target, string value)
    {
        if (target != null)
        {
            target.text = value;
        }
    }

    private static string FormatThousands(long value)
    {
        if (value >= 1000000)
        {
            return $"{value / 1000000f:0.0}M";
        }

        if (value >= 1000)
        {
            return $"{value / 1000f:0.0}k";
        }

        return value.ToString();
    }

    private void OnGUI()
    {
        if (!showWebGLDebugConsole)
        {
            return;
        }

        EnsureGuiStyles();

        const float buttonWidth = 92f;
        const float buttonHeight = 32f;
        Rect toggleRect = new(12f, 12f, buttonWidth, buttonHeight);
        if (GUI.Button(toggleRect, isConsoleOpen ? "Hide Debug" : "Show Debug", buttonStyle))
        {
            isConsoleOpen = !isConsoleOpen;
            if (isConsoleOpen)
            {
                cachedConsoleText = BuildConsoleText();
            }
        }

        if (!isConsoleOpen)
        {
            return;
        }

        float panelWidth = Mathf.Min(Screen.width - 24f, 720f);
        float panelHeight = Mathf.Min(Screen.height - 56f, Screen.height * 0.72f);
        Rect panelRect = new(12f, 52f, panelWidth, panelHeight);
        GUI.Box(panelRect, GUIContent.none);

        Rect headerRect = new(panelRect.x + 12f, panelRect.y + 10f, 180f, 24f);
        GUI.Label(headerRect, "WebGL Debug Console", labelStyle);

        Rect copyRect = new(panelRect.xMax - 104f, panelRect.y + 8f, 92f, 28f);
        if (GUI.Button(copyRect, "Copy Text", buttonStyle))
        {
            cachedConsoleText = BuildConsoleText();
            GUIUtility.systemCopyBuffer = cachedConsoleText;
        }

        Rect textRect = new(panelRect.x + 12f, panelRect.y + 42f, panelRect.width - 24f, panelRect.height - 54f);
        float viewHeight = Mathf.Max(textRect.height, CountConsoleLines(cachedConsoleText) * 18f + 12f);
        Rect viewRect = new(0f, 0f, textRect.width - 18f, viewHeight);

        consoleScroll = GUI.BeginScrollView(textRect, consoleScroll, viewRect);
        cachedConsoleText = GUI.TextArea(new Rect(0f, 0f, viewRect.width, viewRect.height), cachedConsoleText, textAreaStyle);
        GUI.EndScrollView();
    }

    private void HandleLogMessage(string condition, string stackTrace, LogType type)
    {
        string entry = type switch
        {
            LogType.Error => $"[Error] {condition}",
            LogType.Assert => $"[Assert] {condition}",
            LogType.Warning => $"[Warning] {condition}",
            LogType.Exception => $"[Exception] {condition}\n{stackTrace}",
            _ => $"[Log] {condition}",
        };

        runtimeLogs.Add(entry);
        if (runtimeLogs.Count > maxLogEntries)
        {
            runtimeLogs.RemoveAt(0);
        }

        if (isConsoleOpen)
        {
            cachedConsoleText = BuildConsoleText();
        }
    }

    private string BuildConsoleText()
    {
        if (crowdControllers == null || crowdControllers.Length == 0)
        {
            crowdControllers = FindObjectsByType<CrowdController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        }

        StringBuilder builder = new();
        builder.AppendLine("Compute Shaders WebGL Debug");
        builder.AppendLine($"timeScale: {Time.timeScale:0.###}");
        builder.AppendLine($"platform: {Application.platform}");
        builder.AppendLine($"unityVersion: {Application.unityVersion}");
        builder.AppendLine($"graphicsDeviceType: {SystemInfo.graphicsDeviceType}");
        builder.AppendLine($"graphicsShaderLevel: {SystemInfo.graphicsShaderLevel}");
        builder.AppendLine($"supportsInstancing: {SystemInfo.supportsInstancing}");
        builder.AppendLine($"supports2DArrayTextures: {SystemInfo.supports2DArrayTextures}");
        builder.AppendLine($"supports32bitsIndexBuffer: {SystemInfo.supports32bitsIndexBuffer}");
        builder.AppendLine($"screen: {Screen.width}x{Screen.height}");
        builder.AppendLine($"crowdControllerCount: {crowdControllers?.Length ?? 0}");

        if (crowdControllers != null)
        {
            for (int i = 0; i < crowdControllers.Length; i++)
            {
                CrowdController controller = crowdControllers[i];
                if (controller == null)
                {
                    continue;
                }

                builder.AppendLine($"controller[{i}].active: {controller.isActiveAndEnabled}");
                builder.AppendLine($"controller[{i}].mode: {controller.ActiveDebugRenderModeName}");
                builder.AppendLine($"controller[{i}].webglBillboardFallback: {controller.IsWebGLBillboardFallbackActive}");
                builder.AppendLine($"controller[{i}].webglNonInstancedFallback: {controller.IsWebGLNonInstancedFallbackActive}");
                builder.AppendLine($"controller[{i}].visibleInstances: {controller.LastVisibleInstanceCount}");
                builder.AppendLine($"controller[{i}].visibleChunks: {controller.LastVisibleChunkCount}");
                builder.AppendLine($"controller[{i}].drawCalls: {controller.LastDrawCallCount}");
                builder.AppendLine($"controller[{i}].setPass: {controller.LastSetPassCount}");
                builder.AppendLine($"controller[{i}].triangles: {controller.LastTriangleCount}");
                builder.AppendLine($"controller[{i}].hasBillboardMesh: {controller.HasBillboardMesh}");
                builder.AppendLine($"controller[{i}].billboardMaterials: {controller.BillboardMaterialCount}");
                builder.AppendLine($"controller[{i}].usesDedicatedBillboardShader: {controller.UsesDedicatedBillboardShader}");
                builder.AppendLine($"controller[{i}].billboardShaderName: {controller.BillboardShaderName}");
                builder.AppendLine($"controller[{i}].debugBillboardMaterials: {controller.DebugBillboardMaterialCount}");
                builder.AppendLine($"controller[{i}].hasPoseTexture: {controller.HasPoseTexture}");
                builder.AppendLine($"controller[{i}].runtimeBoneCount: {controller.RuntimeBoneCount}");
                builder.AppendLine($"controller[{i}].runtimeMaterialInstancing: {controller.RuntimeMaterialInstancingEnabled}");
            }
        }

        if (runtimeLogs.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Recent Logs:");
            for (int i = 0; i < runtimeLogs.Count; i++)
            {
                builder.AppendLine(runtimeLogs[i]);
            }
        }

        return builder.ToString();
    }

    private void EnsureGuiStyles()
    {
        if (buttonStyle == null)
        {
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter,
            };
        }

        if (textAreaStyle == null)
        {
            textAreaStyle = new GUIStyle(GUI.skin.textArea)
            {
                fontSize = 13,
                wordWrap = false,
                richText = false,
            };
        }

        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
            };
            labelStyle.normal.textColor = Color.white;
        }
    }

    private static int CountConsoleLines(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 1;
        }

        int lines = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                lines++;
            }
        }

        return lines;
    }

    private bool WasTogglePressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return false;
        }

        return toggleConsoleKey switch
        {
            KeyCode.BackQuote => keyboard.backquoteKey.wasPressedThisFrame,
            KeyCode.F3 => keyboard.f3Key.wasPressedThisFrame,
            _ => false,
        };
#else
        return Input.GetKeyDown(toggleConsoleKey);
#endif
    }
}
