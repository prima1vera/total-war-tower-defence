using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

#pragma warning disable 0649
[DisallowMultipleComponent]
public sealed class BuildPlacePanelPresenter : MonoBehaviour
{
    [Serializable]
    private struct BuildButtonBinding
    {
        public string OptionId;
        public Button Button;
        public TMP_Text LabelText;
    }

    [Header("Dependencies")]
    [SerializeField] private TowerBuildService buildService;
    [SerializeField] private PlayerCurrencyWallet currencyWallet;

    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private string defaultTitle = "Build";
    [SerializeField] private BuildButtonBinding[] buildButtons = Array.Empty<BuildButtonBinding>();

    [Header("Labels")]
    [SerializeField] private string currencySuffix = "g";
    [SerializeField] private string unavailableLabel = "N/A";

    private CanvasGroup panelCanvasGroup;
    private bool useCanvasGroupVisibility;

    private void Awake()
    {
        InitializePanelVisibilityMode();
        WireButtons();
        SetPanelVisible(false);
    }

    private void OnEnable()
    {
        if (buildService != null)
            buildService.SelectedBuildPlaceChanged += HandleSelectedPlaceChanged;

        if (currencyWallet != null)
            currencyWallet.BalanceChanged += HandleBalanceChanged;

        RefreshPanel();
    }

    private void OnDisable()
    {
        if (buildService != null)
            buildService.SelectedBuildPlaceChanged -= HandleSelectedPlaceChanged;

        if (currencyWallet != null)
            currencyWallet.BalanceChanged -= HandleBalanceChanged;
    }

    private void InitializePanelVisibilityMode()
    {
        if (panelRoot == null)
            return;

        useCanvasGroupVisibility = panelRoot == gameObject;
        if (!useCanvasGroupVisibility)
            return;

        panelCanvasGroup = panelRoot.GetComponent<CanvasGroup>();
        if (panelCanvasGroup == null)
            panelCanvasGroup = panelRoot.AddComponent<CanvasGroup>();
    }

    private void WireButtons()
    {
        for (int i = 0; i < buildButtons.Length; i++)
        {
            BuildButtonBinding binding = buildButtons[i];
            if (binding.Button == null)
                continue;

            string optionId = binding.OptionId;
            binding.Button.onClick.AddListener(() => HandleBuildClicked(optionId));
        }
    }

    private void HandleSelectedPlaceChanged(BuildPlace _)
    {
        RefreshPanel();
    }

    private void HandleBalanceChanged(int _)
    {
        RefreshButtons();
    }

    private void HandleBuildClicked(string optionId)
    {
        if (buildService == null || string.IsNullOrWhiteSpace(optionId))
            return;

        if (buildService.TryBuildOnSelectedPlace(optionId))
            buildService.ClearBuildPlaceSelection();
    }

    private void RefreshPanel()
    {
        BuildPlace selectedPlace = buildService != null ? buildService.SelectedBuildPlace : null;
        bool hasSelection = selectedPlace != null && !selectedPlace.IsOccupied;

        SetPanelVisible(hasSelection);

        if (titleText != null)
            titleText.text = hasSelection ? $"{defaultTitle} #{selectedPlace.PlaceId}" : defaultTitle;

        RefreshButtons();
    }

    private void RefreshButtons()
    {
        BuildPlace selectedPlace = buildService != null ? buildService.SelectedBuildPlace : null;
        bool canBuild = selectedPlace != null && !selectedPlace.IsOccupied;

        for (int i = 0; i < buildButtons.Length; i++)
        {
            BuildButtonBinding binding = buildButtons[i];
            if (binding.Button == null)
                continue;

            TowerBuildOptionDefinition option = default;
            bool isAvailable = canBuild && buildService != null && buildService.TryGetBuildOption(binding.OptionId, out option);
            bool canAfford = isAvailable && (currencyWallet == null || currencyWallet.CanAfford(option.Cost));

            binding.Button.interactable = isAvailable && canAfford;

            if (binding.LabelText == null)
                continue;

            if (!isAvailable)
            {
                binding.LabelText.text = unavailableLabel;
                binding.LabelText.alpha = 0.6f;
                continue;
            }

            binding.LabelText.text = $"{option.DisplayName} ({option.Cost}{currencySuffix})";
            binding.LabelText.alpha = canAfford ? 1f : 0.6f;
        }
    }

    private void SetPanelVisible(bool isVisible)
    {
        if (panelRoot == null)
            return;

        if (useCanvasGroupVisibility && panelCanvasGroup != null)
        {
            panelCanvasGroup.alpha = isVisible ? 1f : 0f;
            panelCanvasGroup.blocksRaycasts = isVisible;
            panelCanvasGroup.interactable = isVisible;
            return;
        }

        if (panelRoot.activeSelf != isVisible)
            panelRoot.SetActive(isVisible);
    }
}
#pragma warning restore 0649
