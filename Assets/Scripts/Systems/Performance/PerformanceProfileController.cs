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
        // Legacy compatibility only.
        // PerformanceProfileController intentionally does not modify gameplay balance,
        // blood visual presets, or runtime framerate settings.
    }
}
