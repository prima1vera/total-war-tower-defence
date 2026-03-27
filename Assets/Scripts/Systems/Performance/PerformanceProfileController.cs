using UnityEngine;

[DisallowMultipleComponent]
public sealed class PerformanceProfileController : MonoBehaviour
{
    public enum PerformanceProfile
    {
        MobileLow = 0,
        MobileMid = 1,
        PC = 2
    }

    [Header("Profile")]
    [SerializeField] private PerformanceProfile startupProfile = PerformanceProfile.MobileMid;
    [SerializeField] private bool applyOnAwake = true;
    [SerializeField, Tooltip("If enabled, mobile profiles may raise FPS cap to match high-refresh displays (90/120).")]
    private bool preferHighRefreshOnMobile = false;
    [SerializeField, Tooltip("Max FPS cap for mobile when high-refresh mode is enabled.")]
    private int mobileHighRefreshCap = 120;

    [Header("Scene Wiring")]
    [SerializeField] private WaveManager waveManager;
    [SerializeField] private EnemyDeathVisualManager enemyDeathVisualManager;

    private void Awake()
    {
        if (applyOnAwake)
            ApplyProfile(startupProfile);
    }

    [ContextMenu("Profile/Apply Mobile Low")]
    private void ApplyMobileLowFromContextMenu()
    {
        ApplyProfile(PerformanceProfile.MobileLow);
    }

    [ContextMenu("Profile/Apply Mobile Mid")]
    private void ApplyMobileMidFromContextMenu()
    {
        ApplyProfile(PerformanceProfile.MobileMid);
    }

    [ContextMenu("Profile/Apply PC")]
    private void ApplyPcFromContextMenu()
    {
        ApplyProfile(PerformanceProfile.PC);
    }

    public void ApplyProfile(PerformanceProfile profile)
    {
        startupProfile = profile;

        int targetFrameRate = 60;
        bool weightedComposition = true;
        float ogreShare = 0.14f;
        float deathKnightShare = 0.05f;
        float ogreWeight = 0.40f;
        float deathKnightWeight = 0.25f;
        float smallWeight = 1f;
        ProfileBloodPreset bloodPreset = ProfileBloodPreset.Cinematic;

        switch (profile)
        {
            case PerformanceProfile.MobileLow:
                targetFrameRate = 45;
                ogreShare = 0.10f;
                deathKnightShare = 0.03f;
                ogreWeight = 0.30f;
                deathKnightWeight = 0.18f;
                smallWeight = 1.15f;
                bloodPreset = ProfileBloodPreset.Light;
                break;

            case PerformanceProfile.MobileMid:
                targetFrameRate = 60;
                ogreShare = 0.14f;
                deathKnightShare = 0.05f;
                ogreWeight = 0.40f;
                deathKnightWeight = 0.25f;
                smallWeight = 1f;
                bloodPreset = ProfileBloodPreset.Cinematic;
                break;

            case PerformanceProfile.PC:
                targetFrameRate = 120;
                ogreShare = 0.20f;
                deathKnightShare = 0.09f;
                ogreWeight = 0.55f;
                deathKnightWeight = 0.38f;
                smallWeight = 1f;
                bloodPreset = ProfileBloodPreset.Gore;
                break;
        }

        if (preferHighRefreshOnMobile && Application.isMobilePlatform)
            targetFrameRate = ResolveMobileFrameRate(targetFrameRate, mobileHighRefreshCap);

        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = targetFrameRate;

        if (waveManager != null)
            waveManager.ApplySpawnCompositionTuning(
                weightedComposition,
                ogreShare,
                deathKnightShare,
                ogreWeight,
                deathKnightWeight,
                smallWeight);

        if (enemyDeathVisualManager != null)
            ApplyBloodPreset(enemyDeathVisualManager, bloodPreset);
    }

    private static void ApplyBloodPreset(EnemyDeathVisualManager manager, ProfileBloodPreset preset)
    {
        switch (preset)
        {
            case ProfileBloodPreset.Light:
                manager.ApplyLightPreset();
                break;
            case ProfileBloodPreset.Cinematic:
                manager.ApplyCinematicPreset();
                break;
            case ProfileBloodPreset.Gore:
                manager.ApplyGorePreset();
                break;
        }
    }

    private static int ResolveMobileFrameRate(int defaultFrameRate, int highRefreshCap)
    {
        int refreshRate = GetCurrentDisplayRefreshRate();
        if (refreshRate <= defaultFrameRate)
            return defaultFrameRate;

        int clampedCap = Mathf.Max(defaultFrameRate, highRefreshCap);

        // Keep requested cap stable on common mobile refresh tiers.
        if (refreshRate >= 120)
            return Mathf.Min(120, clampedCap);

        if (refreshRate >= 90)
            return Mathf.Min(90, clampedCap);

        return Mathf.Min(refreshRate, clampedCap);
    }

    private static int GetCurrentDisplayRefreshRate()
    {
#if UNITY_2022_2_OR_NEWER
        return Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value);
#else
        return Screen.currentResolution.refreshRate;
#endif
    }

    private enum ProfileBloodPreset
    {
        Light = 0,
        Cinematic = 1,
        Gore = 2
    }
}
