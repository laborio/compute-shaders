using TMPro;
using UnityEngine;

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

    private readonly FrameTiming[] frameTimings = new FrameTiming[1];
    private CrowdController[] crowdControllers;
    private float refreshTimer;
    private float smoothedDeltaTime;

    private void OnEnable()
    {
        crowdControllers = FindObjectsByType<CrowdController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        smoothedDeltaTime = Mathf.Max(Time.unscaledDeltaTime, 1f / 60f);
        refreshTimer = 0f;
        Refresh();
    }

    private void Update()
    {
        float deltaTime = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
        smoothedDeltaTime = Mathf.Lerp(smoothedDeltaTime, deltaTime, 0.1f);

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
}
