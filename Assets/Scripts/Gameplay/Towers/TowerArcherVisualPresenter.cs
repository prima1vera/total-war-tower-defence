using UnityEngine;

public class TowerArcherVisualPresenter : MonoBehaviour
{
    [Header("Scene Wiring")]
    [Tooltip("Owning ArcherTower used as source of aim and shot events.")]
    [SerializeField] private ArcherTower archerTower;
    [Tooltip("Sprite renderer that displays archer idle/attack frame sets.")]
    [SerializeField] private SpriteRenderer archerRenderer;
    [Tooltip("Directional sprite profile with per-level idle/pre-attack/attack frames.")]
    [SerializeField] private TowerArcherVisualProfile profile;
    [Tooltip("Shot source transform for filtering shot events to this presenter only.")]
    [SerializeField] private Transform shotSource;

    [Header("Direction")]
    [Tooltip("Y-direction threshold separating Up/Down bands from Side band.")]
    [SerializeField, Range(0f, 1f)] private float verticalBandThreshold = 0.35f;
    [Tooltip("If enabled, side animations flip by aim X sign instead of requiring dedicated left/right sets.")]
    [SerializeField] private bool flipSideByAimX = true;

    private TowerArcherLevelVisual activeLevelVisual;
    private TowerArcherAimBand currentIdleBand = TowerArcherAimBand.Side;
    private bool currentIdleFlipX;
    private TowerArcherAimBand currentShotBand = TowerArcherAimBand.Side;
    private bool currentShotFlipX;

    private PlaybackState playbackState = PlaybackState.Idle;
    private Sprite[] playbackFrames;
    private float playbackFps = 6f;
    private bool playbackLoop = true;
    private int playbackFrameIndex;
    private float playbackTimer;
    private bool isWired;

    private enum PlaybackState
    {
        Idle,
        PreAttack,
        Attack
    }

    private void Awake()
    {
        isWired = ValidateWiring();
        if (!isWired)
            enabled = false;
    }

    private void OnEnable()
    {
        if (!isWired)
            return;

        archerTower.ShotFiredFrom += HandleShotFiredFrom;
        archerTower.VisualLevelChanged += HandleVisualLevelChanged;

        ApplyLevel(archerTower.VisualLevel);
        RefreshIdleFromAim();
        BeginIdle();
    }

    private void OnDisable()
    {
        if (archerTower != null)
        {
            archerTower.ShotFiredFrom -= HandleShotFiredFrom;
            archerTower.VisualLevelChanged -= HandleVisualLevelChanged;
        }
    }

    private void Update()
    {
        if (!isWired)
            return;

        if (playbackState == PlaybackState.Idle)
            RefreshIdleFromAim();

        AdvancePlayback(Time.deltaTime);
    }

    private bool ValidateWiring()
    {
        bool valid = true;

        if (archerTower == null)
        {
            Debug.LogError("TowerArcherVisualPresenter: ArcherTower reference is missing.", this);
            valid = false;
        }

        if (archerRenderer == null)
        {
            Debug.LogError("TowerArcherVisualPresenter: Archer SpriteRenderer is missing.", this);
            valid = false;
        }

        if (profile == null)
        {
            Debug.LogError("TowerArcherVisualPresenter: Archer profile is missing.", this);
            valid = false;
        }

        if (shotSource == null)
            shotSource = transform;

        return valid;
    }

    private void HandleVisualLevelChanged(int level)
    {
        ApplyLevel(level);
        BeginIdle();
    }

    private void HandleShotFiredFrom(Vector2 origin, Vector2 direction)
    {
        if (shotSource != null)
        {
            float sourceDistanceSqr = ((Vector2)shotSource.position - origin).sqrMagnitude;
            if (sourceDistanceSqr > 0.0004f)
                return;
        }

        HandleShotFired(direction);
    }

    private void HandleShotFired(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        currentShotBand = ResolveBand(direction);
        currentShotFlipX = ResolveFlipX(currentShotBand, direction);

        if (!TryGetDirectionalSet(currentShotBand, out TowerArcherDirectionalSprites set))
            return;

        if (set.PreAttack.Length > 0)
        {
            BeginPlayback(set.PreAttack, profile.PreAttackFps, false, PlaybackState.PreAttack, currentShotFlipX);
            return;
        }

        if (set.Attack.Length > 0)
        {
            BeginPlayback(set.Attack, profile.AttackFps, false, PlaybackState.Attack, currentShotFlipX);
            return;
        }

        BeginIdle();
    }

    private void ApplyLevel(int towerLevel)
    {
        if (profile == null)
            return;

        if (!profile.TryGetLevel(towerLevel, out TowerArcherLevelVisual levelVisual))
        {
            activeLevelVisual = null;
            return;
        }

        activeLevelVisual = levelVisual;
    }

    private void RefreshIdleFromAim()
    {
        if (!archerTower.TryGetAimDirection(out Vector2 direction))
            return;

        TowerArcherAimBand band = ResolveBand(direction);
        bool flipX = ResolveFlipX(band, direction);

        bool bandChanged = band != currentIdleBand;
        bool flipChanged = flipX != currentIdleFlipX;

        currentIdleBand = band;
        currentIdleFlipX = flipX;

        if (playbackState == PlaybackState.Idle && (bandChanged || flipChanged))
            BeginIdle();
    }

    private void BeginIdle()
    {
        if (!TryGetDirectionalSet(currentIdleBand, out TowerArcherDirectionalSprites set))
            return;

        if (set.Idle.Length > 0)
            BeginPlayback(set.Idle, profile.IdleFps, true, PlaybackState.Idle, currentIdleFlipX);
    }

    private void BeginPlayback(Sprite[] frames, float fps, bool loop, PlaybackState state, bool flipX)
    {
        if (frames == null || frames.Length == 0 || archerRenderer == null)
            return;

        playbackFrames = frames;
        playbackFps = Mathf.Max(1f, fps);
        playbackLoop = loop;
        playbackState = state;
        playbackFrameIndex = 0;
        playbackTimer = 0f;

        archerRenderer.flipX = flipX;
        archerRenderer.sprite = playbackFrames[0];
    }

    private void AdvancePlayback(float deltaTime)
    {
        if (playbackFrames == null || playbackFrames.Length == 0)
            return;

        if (playbackFrames.Length == 1)
        {
            if (!playbackLoop)
                HandleNonLoopCompleted();

            return;
        }

        float frameDuration = 1f / playbackFps;
        playbackTimer += deltaTime;

        while (playbackTimer >= frameDuration)
        {
            playbackTimer -= frameDuration;

            if (playbackLoop)
            {
                playbackFrameIndex = (playbackFrameIndex + 1) % playbackFrames.Length;
                archerRenderer.sprite = playbackFrames[playbackFrameIndex];
                continue;
            }

            if (playbackFrameIndex < playbackFrames.Length - 1)
            {
                playbackFrameIndex++;
                archerRenderer.sprite = playbackFrames[playbackFrameIndex];
                continue;
            }

            HandleNonLoopCompleted();
            break;
        }
    }

    private void HandleNonLoopCompleted()
    {
        if (!TryGetDirectionalSet(currentShotBand, out TowerArcherDirectionalSprites set))
        {
            BeginIdle();
            return;
        }

        if (playbackState == PlaybackState.PreAttack && set.Attack.Length > 0)
        {
            BeginPlayback(set.Attack, profile.AttackFps, false, PlaybackState.Attack, currentShotFlipX);
            return;
        }

        BeginIdle();
    }

    private bool TryGetDirectionalSet(TowerArcherAimBand band, out TowerArcherDirectionalSprites set)
    {
        if (activeLevelVisual == null)
        {
            set = null;
            return false;
        }

        set = activeLevelVisual.GetBand(band);
        return set != null;
    }

    private TowerArcherAimBand ResolveBand(Vector2 direction)
    {
        if (direction.y >= verticalBandThreshold)
            return TowerArcherAimBand.Up;

        if (direction.y <= -verticalBandThreshold)
            return TowerArcherAimBand.Down;

        return TowerArcherAimBand.Side;
    }

    private bool ResolveFlipX(TowerArcherAimBand band, Vector2 direction)
    {
        if (!flipSideByAimX)
            return false;

        if (band != TowerArcherAimBand.Side)
            return false;

        // Source sprites are authored as "aiming to the left" by default.
        // For side band we mirror when target is on the right.
        return direction.x > 0f;
    }
}

