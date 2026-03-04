using UnityEngine;
using UnityEngine.UI;

public class GameHudController : MonoBehaviour
{
    [SerializeField] private GameFlowManager gameFlowManager;
    [SerializeField] private WaveManager waveManager;

    [SerializeField] private Text waveText;
    [SerializeField] private Text livesText;
    [SerializeField] private Text speedText;
    [SerializeField] private Text stateText;

    [SerializeField] private Button pauseButton;
    [SerializeField] private Button speedButton;

    private bool isBound;


    public void Configure(
        GameFlowManager flow,
        WaveManager wave,
        Text waveLabel,
        Text livesLabel,
        Text speedLabel,
        Text stateLabel,
        Button pause,
        Button speed)
    {
        gameFlowManager = flow;
        waveManager = wave;
        waveText = waveLabel;
        livesText = livesLabel;
        speedText = speedLabel;
        stateText = stateLabel;
        pauseButton = pause;
        speedButton = speed;
    }
    private void Start()
    {
        if (gameFlowManager == null)
            gameFlowManager = FindObjectOfType<GameFlowManager>();

        if (waveManager == null)
            waveManager = FindObjectOfType<WaveManager>();

        Bind();
        RefreshStaticLabels();
    }

    private void Bind()
    {
        if (isBound)
            return;

        if (gameFlowManager != null)
        {
            gameFlowManager.LivesChanged += OnLivesChanged;
            gameFlowManager.TimeScaleChanged += OnTimeScaleChanged;
            gameFlowManager.GameFinished += OnGameFinished;

            if (pauseButton != null)
                pauseButton.onClick.AddListener(gameFlowManager.TogglePause);

            if (speedButton != null)
                speedButton.onClick.AddListener(gameFlowManager.ToggleSpeedMultiplier);
        }

        if (waveManager != null)
        {
            waveManager.WaveChanged += OnWaveChanged;
            waveManager.AllWavesCompleted += OnWavesCompleted;
        }

        isBound = true;
    }

    private void OnDestroy()
    {
        if (!isBound)
            return;

        if (gameFlowManager != null)
        {
            gameFlowManager.LivesChanged -= OnLivesChanged;
            gameFlowManager.TimeScaleChanged -= OnTimeScaleChanged;
            gameFlowManager.GameFinished -= OnGameFinished;

            if (pauseButton != null)
                pauseButton.onClick.RemoveListener(gameFlowManager.TogglePause);

            if (speedButton != null)
                speedButton.onClick.RemoveListener(gameFlowManager.ToggleSpeedMultiplier);
        }

        if (waveManager != null)
        {
            waveManager.WaveChanged -= OnWaveChanged;
            waveManager.AllWavesCompleted -= OnWavesCompleted;
        }
    }

    private void OnWaveChanged(int index, int total)
    {
        if (waveText != null)
            waveText.text = $"Wave: {index}/{total}";
    }

    private void OnLivesChanged(int lives)
    {
        if (livesText != null)
            livesText.text = $"Lives: {lives}";
    }

    private void OnTimeScaleChanged(float scale, bool isPaused)
    {
        if (speedText != null)
            speedText.text = isPaused ? "Speed: Paused" : $"Speed: x{scale:0}";
    }

    private void OnGameFinished(bool isVictory)
    {
        if (stateText == null)
            return;

        stateText.text = isVictory ? "Victory" : "Defeat";
        stateText.enabled = true;
    }

    private void OnWavesCompleted()
    {
        if (stateText != null && string.IsNullOrEmpty(stateText.text))
            stateText.text = "All waves cleared";

        if (gameFlowManager != null && !gameFlowManager.IsGameOver)
            gameFlowManager.CompleteLevel();
    }

    private void RefreshStaticLabels()
    {
        if (waveText != null)
            waveText.text = waveManager != null ? $"Wave: {(waveManager.CurrentWaveIndex + 1):0}/{waveManager.TotalWaves:0}" : "Wave: -";

        if (livesText != null)
            livesText.text = gameFlowManager != null ? $"Lives: {gameFlowManager.CurrentLives}" : "Lives: -";

        if (speedText != null)
            speedText.text = "Speed: x1";

        if (stateText != null)
            stateText.enabled = false;
    }
}
