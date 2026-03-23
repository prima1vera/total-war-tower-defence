using System;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PerformanceFpsCounter : MonoBehaviour
{
    private const float SampleIntervalSeconds = 0.25f;

    [SerializeField] private TextMeshProUGUI targetText;

    private float sampleTimer;
    private int frameCount;
    private int lastGc0;
    private int lastGc1;
    private int lastGc2;

    private void Awake()
    {
        if (targetText == null)
            targetText = GetComponent<TextMeshProUGUI>();

        if (targetText == null)
        {
            Debug.LogError($"{name}: PerformanceFpsCounter requires a TextMeshProUGUI reference.", this);
            enabled = false;
            return;
        }
    }

    private void OnEnable()
    {
        sampleTimer = 0f;
        frameCount = 0;
        lastGc0 = GC.CollectionCount(0);
        lastGc1 = GC.CollectionCount(1);
        lastGc2 = GC.CollectionCount(2);
    }

    private void Update()
    {
        frameCount++;
        sampleTimer += Time.unscaledDeltaTime;

        if (sampleTimer < SampleIntervalSeconds)
            return;

        float fps = frameCount / Mathf.Max(0.0001f, sampleTimer);
        float frameMs = 1000f / Mathf.Max(1f, fps);
        float heapMb = GC.GetTotalMemory(false) / (1024f * 1024f);

        int gc0 = GC.CollectionCount(0);
        int gc1 = GC.CollectionCount(1);
        int gc2 = GC.CollectionCount(2);

        int d0 = gc0 - lastGc0;
        int d1 = gc1 - lastGc1;
        int d2 = gc2 - lastGc2;

        lastGc0 = gc0;
        lastGc1 = gc1;
        lastGc2 = gc2;

        targetText.SetText(
            "FPS {0:0} | {1:0.0} ms | Heap {2:0.0} MB | GC {3}/{4}/{5}",
            fps,
            frameMs,
            heapMb,
            d0,
            d1,
            d2);

        frameCount = 0;
        sampleTimer = 0f;
    }
}
