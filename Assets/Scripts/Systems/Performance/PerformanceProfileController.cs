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
        float ogreShare = 0.16f;
        float ogreWeight = 0.40f;
        float smallWeight = 1f;
        ProfileBloodPreset bloodPreset = ProfileBloodPreset.Cinematic;

        switch (profile)
        {
            case PerformanceProfile.MobileLow:
                targetFrameRate = 45;
                ogreShare = 0.10f;
                ogreWeight = 0.30f;
                smallWeight = 1.15f;
                bloodPreset = ProfileBloodPreset.Light;
                break;

            case PerformanceProfile.MobileMid:
                targetFrameRate = 60;
                ogreShare = 0.16f;
                ogreWeight = 0.40f;
                smallWeight = 1f;
                bloodPreset = ProfileBloodPreset.Cinematic;
                break;

            case PerformanceProfile.PC:
                targetFrameRate = 120;
                ogreShare = 0.22f;
                ogreWeight = 0.55f;
                smallWeight = 1f;
                bloodPreset = ProfileBloodPreset.Gore;
                break;
        }

        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = targetFrameRate;

        if (waveManager != null)
            waveManager.ApplySpawnCompositionTuning(weightedComposition, ogreShare, ogreWeight, smallWeight);

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

    private enum ProfileBloodPreset
    {
        Light = 0,
        Cinematic = 1,
        Gore = 2
    }
}
