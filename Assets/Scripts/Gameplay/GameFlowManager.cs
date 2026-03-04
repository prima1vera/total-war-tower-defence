using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class GameFlowManager : MonoBehaviour
{
    [SerializeField] private int startingLives = 20;
    public event Action<int> LivesChanged;
    public event Action<float, bool> TimeScaleChanged;
    public event Action<bool> GameFinished;

    public int CurrentLives { get; private set; }
    public bool IsPaused { get; private set; }
    public bool IsGameOver { get; private set; }
    public bool IsVictory { get; private set; }

    private float lastNonPausedTimeScale = 1f;

    private void OnEnable()
    {
        EnemyRuntimeEvents.EnemyReachedGoal += HandleEnemyReachedGoal;
    }

    private void Start()
    {
        CurrentLives = Mathf.Max(1, startingLives);
        SetTimeScaleInternal(1f);
        LivesChanged?.Invoke(CurrentLives);
    }

    private void Update()
    {
        if (IsGameOver)
            return;

        if (WasPausePressed())
            TogglePause();

        if (WasSpeedTogglePressed())
            ToggleSpeedMultiplier();
    }

    private bool WasPausePressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.spaceKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.Space);
#else
        return false;
#endif
    }

    private bool WasSpeedTogglePressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.tabKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.Tab);
#else
        return false;
#endif
    }

    private void OnDisable()
    {
        EnemyRuntimeEvents.EnemyReachedGoal -= HandleEnemyReachedGoal;
    }

    private void OnDestroy()
    {
        if (Time.timeScale == 0f)
            Time.timeScale = 1f;
    }

    public void TogglePause()
    {
        if (IsGameOver)
            return;

        if (IsPaused)
        {
            IsPaused = false;
            SetTimeScaleInternal(lastNonPausedTimeScale);
            return;
        }

        IsPaused = true;
        SetTimeScaleInternal(0f);
    }

    public void SetSpeedMultiplier(float value)
    {
        if (IsGameOver)
            return;

        float clamped = Mathf.Clamp(value, 1f, 2f);
        lastNonPausedTimeScale = clamped;

        if (!IsPaused)
            SetTimeScaleInternal(clamped);
    }

    public void ToggleSpeedMultiplier()
    {
        if (Mathf.Approximately(lastNonPausedTimeScale, 1f))
        {
            SetSpeedMultiplier(2f);
            return;
        }

        SetSpeedMultiplier(1f);
    }

    public void CompleteLevel()
    {
        if (IsGameOver)
            return;

        IsGameOver = true;
        IsVictory = true;
        SetTimeScaleInternal(1f);
        GameFinished?.Invoke(true);
    }

    private void HandleEnemyReachedGoal(UnitHealth enemy)
    {
        if (IsGameOver)
            return;

        CurrentLives = Mathf.Max(0, CurrentLives - 1);
        LivesChanged?.Invoke(CurrentLives);

        if (CurrentLives > 0)
            return;

        IsGameOver = true;
        IsVictory = false;
        SetTimeScaleInternal(0f);
        GameFinished?.Invoke(false);
    }

    private void SetTimeScaleInternal(float value)
    {
        Time.timeScale = value;
        TimeScaleChanged?.Invoke(value, IsPaused);
    }
}
