using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

public static class FirstSceneBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureManagers()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || activeScene.name != "FirstScene")
            return;

        GameFlowManager flowManager = Object.FindObjectOfType<GameFlowManager>();
        WaveManager waveManager = Object.FindObjectOfType<WaveManager>();
        GameHudController hudController = Object.FindObjectOfType<GameHudController>();

        if (flowManager == null)
        {
            GameObject flowObject = new GameObject("GameFlowManager");
            flowManager = flowObject.AddComponent<GameFlowManager>();
        }

        if (waveManager == null)
        {
            GameObject waveObject = new GameObject("WaveManager");
            waveManager = waveObject.AddComponent<WaveManager>();
        }

        if (hudController == null)
            CreateHud(flowManager, waveManager);
    }

    private static void CreateHud(GameFlowManager flowManager, WaveManager waveManager)
    {
        GameObject canvasRoot = new GameObject("GameplayHUD");
        Canvas canvas = canvasRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        canvasRoot.AddComponent<CanvasScaler>();
        canvasRoot.AddComponent<GraphicRaycaster>();

        EnsureEventSystemExists();

        GameObject hudObject = new GameObject("GameHudController");
        hudObject.transform.SetParent(canvasRoot.transform, false);

        GameHudController controller = hudObject.AddComponent<GameHudController>();

        Text waveText = CreateLabel("WaveText", canvasRoot.transform, new Vector2(24f, -24f));
        Text livesText = CreateLabel("LivesText", canvasRoot.transform, new Vector2(24f, -60f));
        Text speedText = CreateLabel("SpeedText", canvasRoot.transform, new Vector2(24f, -96f));
        Text stateText = CreateCenteredLabel("StateText", canvasRoot.transform, new Vector2(0f, 50f), 42);

        Button speedButton = CreateButton("SpeedButton", canvasRoot.transform, new Vector2(-20f, -20f), "x2");
        Button pauseButton = CreateButton("PauseButton", canvasRoot.transform, new Vector2(-20f, -64f), "Pause");

        controller.Configure(flowManager, waveManager, waveText, livesText, speedText, stateText, pauseButton, speedButton);
    }

    private static void EnsureEventSystemExists()
    {
        if (Object.FindObjectOfType<EventSystem>() != null)
            return;

        GameObject eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();

#if ENABLE_INPUT_SYSTEM
        eventSystem.AddComponent<InputSystemUIInputModule>();
#else
        eventSystem.AddComponent<StandaloneInputModule>();
#endif
    }

    private static Text CreateLabel(string name, Transform parent, Vector2 anchoredPosition)
    {
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);

        RectTransform rect = textObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(250f, 30f);

        Text text = textObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 24;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleLeft;

        return text;
    }

    private static Text CreateCenteredLabel(string name, Transform parent, Vector2 anchoredPosition, int fontSize)
    {
        Text text = CreateLabel(name, parent, anchoredPosition);
        RectTransform rect = text.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(400f, 80f);
        text.alignment = TextAnchor.MiddleCenter;
        text.fontSize = fontSize;
        return text;
    }

    private static Button CreateButton(string name, Transform parent, Vector2 anchoredPosition, string label)
    {
        GameObject buttonObject = new GameObject(name);
        buttonObject.transform.SetParent(parent, false);

        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.5f);

        Button button = buttonObject.AddComponent<Button>();

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(120f, 36f);

        Text buttonText = CreateCenteredLabel($"{name}Text", buttonObject.transform, Vector2.zero, 20);
        buttonText.text = label;

        return button;
    }
}
