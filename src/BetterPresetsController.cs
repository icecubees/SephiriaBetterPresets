using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BetterPresets;

public sealed class BetterPresetsController : MonoBehaviour
{
    private const string ModFolderName = "BetterPresets";
    private const int MaxBackupFiles = 3;
    private bool visible;
    private int selectedIndex;
    private int targetSlotIndex = -1;
    private string status = "就绪";
    private PresetStore store = new PresetStore();
    private UI_PresetPanel hookedPanel;
    private GameObject embeddedButtonObject;
    private float nextPanelProbeTime;
    private float embeddedButtonEnableTime;
    private bool storeLoaded;
    private bool pendingStoreSave;
    private float pendingStoreSaveTime;
    private int storeRevision;
    private int cachedDetailIndex = -1;
    private int cachedDetailRevision = -1;
    private DetailView cachedDetail;
    private bool cursorVisibilityCaptured;
    private bool previousCursorVisible;
    private Coroutine openOverlayCoroutine;
    private bool overlayUiBuilt;
    private GameObject overlayRoot;
    private RectTransform overlayCanvasRect;
    private RectTransform overlayPanelRect;
    private RectTransform listContentRect;
    private RectTransform detailContentRect;
    private TMP_InputField nameInputField;
    private TextMeshProUGUI countText;
    private TextMeshProUGUI slotText;
    private TextMeshProUGUI statusText;
    private RectTransform overlayTooltipRect;
    private TextMeshProUGUI overlayTooltipTitle;
    private TextMeshProUGUI overlayTooltipBody;
    private RectTransform overlayCursorRect;
    private Sprite solidSprite;
    private Sprite buttonSprite;
    private Sprite panelSprite;
    private Sprite cursorSprite;
    private Material cursorMaterial;
    private Color cursorColor = Color.white;
    private Vector2 cursorSize = new Vector2(34f, 38f);
    private TMP_FontAsset uiFont;
    private readonly List<Button> slotButtons = new List<Button>();
    private readonly Dictionary<Canvas, CanvasSortState> boostedTooltipCanvases = new Dictionary<Canvas, CanvasSortState>();
    private bool embeddedButtonStateInitialized;
    private bool embeddedButtonLastShown;
    private bool embeddedButtonLastInteractable;
    private string embeddedButtonLastLabel = "";
    private const float PanelProbeInterval = 0.5f;

    private string ModFolder => Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "AddOns", ModFolderName);
    private string PresetFile => Path.Combine(ModFolder, "presets.json");

    private void Awake()
    {
        status = "未读取外部预设。";
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextPanelProbeTime)
        {
            nextPanelProbeTime = Time.unscaledTime + PanelProbeInterval;
            EnsureEmbeddedButton();
        }

        HandleOriginalCloseClick();
        if (pendingStoreSave)
        {
            ProcessPendingStoreSave();
        }
        if (!visible && embeddedButtonObject != null && hookedPanel != null && hookedPanel.IsOpened &&
            (!embeddedButtonStateInitialized || !embeddedButtonLastShown || !embeddedButtonLastInteractable))
        {
            RefreshEmbeddedButtonState();
        }
        if (visible)
        {
            if (hookedPanel == null || !hookedPanel.IsOpened)
            {
                SetVisible(false);
            }
            else
            {
                UpdateFavoriteTooltipPosition();
                UpdateOverlayCursor();
            }
        }
    }

    private void OnDestroy()
    {
        if (embeddedButtonObject != null)
        {
            Destroy(embeddedButtonObject);
            embeddedButtonObject = null;
        }
        RestoreOriginalTooltipCanvases();
        RestoreCursorVisibility();
        DestroyOverlayUi();
    }

    private void EnsureEmbeddedButton()
    {
        UI_PresetPanel panel = TryGetPresetPanel();
        if (panel == null)
        {
            SetVisible(false);
            DestroyEmbeddedButton();
            hookedPanel = null;
            return;
        }

        if (hookedPanel != panel)
        {
            DestroyEmbeddedButton();
            hookedPanel = panel;
        }

        if (!panel.IsOpened)
        {
            SetVisible(false);
            if (embeddedButtonObject != null)
            {
                embeddedButtonObject.SetActive(false);
            }
            return;
        }

        OnPresetPanelOpenStateChanged(panel, isOpened: true);
    }

    private void OnPresetPanelOpenStateChanged(UI_PresetPanel panel, bool isOpened)
    {
        if (panel == null)
        {
            return;
        }

        if (hookedPanel != panel)
        {
            hookedPanel = panel;
        }

        if (!isOpened)
        {
            SetVisible(false);
            if (embeddedButtonObject != null)
            {
                embeddedButtonObject.SetActive(false);
                embeddedButtonLastShown = false;
                embeddedButtonLastInteractable = false;
                embeddedButtonStateInitialized = true;
            }
            return;
        }

        if (embeddedButtonObject == null)
        {
            CreateEmbeddedButton(panel);
        }

        RefreshEmbeddedButtonState();
    }

    private void CreateEmbeddedButton(UI_PresetPanel panel)
    {
        UI_HorayButton template = panel.applyPreseButton != null ? panel.applyPreseButton : panel.writePresetToClipboardButton;
        if (template == null)
        {
            return;
        }

        EnsureSolidSprite();
        Image templateImage = template.GetComponent<Image>();
        TextMeshProUGUI templateText = template.GetComponentInChildren<TextMeshProUGUI>(true);

        embeddedButtonObject = new GameObject("BetterPresetsButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(CanvasGroup));
        embeddedButtonObject.transform.SetParent(panel.transform, false);
        embeddedButtonObject.SetActive(true);
        embeddedButtonObject.transform.SetAsLastSibling();

        Image image = embeddedButtonObject.GetComponent<Image>();
        image.sprite = templateImage != null && templateImage.sprite != null ? templateImage.sprite : solidSprite;
        image.type = Image.Type.Sliced;
        image.color = templateImage != null ? templateImage.color : Color.white;
        image.raycastTarget = true;

        Button unityButton = embeddedButtonObject.GetComponent<Button>();
        unityButton.targetGraphic = image;
        unityButton.onClick.AddListener(() => SetVisible(true));
        embeddedButtonStateInitialized = false;
        embeddedButtonLastLabel = "";

        TextMeshProUGUI label = CreateText(embeddedButtonObject.GetComponent<RectTransform>(), "Label", "外部预设", templateText != null ? Mathf.RoundToInt(templateText.fontSize) : 22, new Color32(248, 245, 235, 255), TextAlignmentOptions.Center);
        if (templateText != null)
        {
            label.font = templateText.font;
            label.fontMaterial = templateText.fontMaterial;
            label.color = templateText.color;
        }
        label.textWrappingMode = TextWrappingModes.NoWrap;
        Stretch(label.rectTransform, 8f, 4f, -8f, -4f);

        RectTransform rect = embeddedButtonObject.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-78f, -12f);
            rect.sizeDelta = new Vector2(110f, 30f);
            rect.localScale = Vector3.one;
        }
    }

    private void RefreshEmbeddedButtonState()
    {
        if (embeddedButtonObject == null || hookedPanel == null || !hookedPanel.IsOpened)
        {
            embeddedButtonStateInitialized = false;
            return;
        }

        bool shouldShow = !visible;
        bool canInteract = shouldShow && Time.unscaledTime >= embeddedButtonEnableTime;
        if (!embeddedButtonStateInitialized || embeddedButtonLastShown != shouldShow)
        {
            embeddedButtonObject.SetActive(shouldShow);
            embeddedButtonLastShown = shouldShow;
        }
        SetEmbeddedButtonLabel("外部预设");
        if (!embeddedButtonStateInitialized || embeddedButtonLastInteractable != canInteract)
        {
            SetEmbeddedButtonInteractable(canInteract);
            embeddedButtonLastInteractable = canInteract;
        }
        embeddedButtonStateInitialized = true;
    }

    private void SetEmbeddedButtonLabel(string value)
    {
        if (embeddedButtonObject == null)
        {
            return;
        }

        if (embeddedButtonLastLabel == value)
        {
            return;
        }

        foreach (TextMeshProUGUI text in embeddedButtonObject.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            text.text = value;
        }
        embeddedButtonLastLabel = value;
    }

    private void SetEmbeddedButtonInteractable(bool interactable)
    {
        if (embeddedButtonObject == null)
        {
            return;
        }

        Button unityButton = embeddedButtonObject.GetComponent<Button>();
        if (unityButton != null)
        {
            unityButton.interactable = interactable;
        }

        CanvasGroup canvasGroup = embeddedButtonObject.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = embeddedButtonObject.AddComponent<CanvasGroup>();
        }
        canvasGroup.interactable = interactable;
        canvasGroup.blocksRaycasts = interactable;
    }

    private void CaptureCursorVisibility()
    {
        if (cursorVisibilityCaptured)
        {
            return;
        }

        previousCursorVisible = Cursor.visible;
        cursorVisibilityCaptured = true;
    }

    private void RestoreCursorVisibility()
    {
        if (!cursorVisibilityCaptured)
        {
            return;
        }

        Cursor.visible = previousCursorVisible;
        cursorVisibilityCaptured = false;
    }

    private void DestroyEmbeddedButton()
    {
        if (embeddedButtonObject != null)
        {
            Destroy(embeddedButtonObject);
            embeddedButtonObject = null;
        }
        embeddedButtonStateInitialized = false;
        embeddedButtonLastShown = false;
        embeddedButtonLastInteractable = false;
        embeddedButtonLastLabel = "";
    }

    private static UI_PresetPanel TryGetPresetPanel()
    {
        try
        {
            return UIManager.Instance != null ? UIManager.Instance.GetElement<UI_PresetPanel>() : null;
        }
        catch
        {
            return null;
        }
    }

    private void EnsureOverlayUiShell()
    {
        if (overlayRoot != null)
        {
            return;
        }

        CaptureUiStyle();
        EnsureSolidSprite();

        overlayRoot = new GameObject("BetterPresetsOverlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        overlayCanvasRect = overlayRoot.GetComponent<RectTransform>();
        Stretch(overlayCanvasRect);

        Canvas canvas = overlayRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;
        canvas.overrideSorting = true;

        CanvasScaler scaler = overlayRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        Image blocker = CreateImage(overlayCanvasRect, "Blocker", new Color(0f, 0f, 0f, 0.18f));
        Stretch(blocker.rectTransform);
        blocker.raycastTarget = true;

        Image panel = CreateImage(overlayCanvasRect, "Panel", new Color32(18, 14, 18, 238));
        panel.sprite = panelSprite != null ? panelSprite : solidSprite;
        panel.type = Image.Type.Sliced;
        overlayPanelRect = panel.rectTransform;
        overlayPanelRect.anchorMin = new Vector2(0.14f, 0.07f);
        overlayPanelRect.anchorMax = new Vector2(0.86f, 0.91f);
        overlayPanelRect.offsetMin = Vector2.zero;
        overlayPanelRect.offsetMax = Vector2.zero;

        AddBorder(overlayPanelRect, new Color32(180, 130, 112, 230), 4f);

        TextMeshProUGUI title = CreateText(overlayPanelRect, "Title", "外部预设", 32, new Color32(246, 232, 162, 255), TextAlignmentOptions.Center);
        SetAnchors(title.rectTransform, 0f, 0.93f, 1f, 0.995f, 0f, 0f, 0f, 0f);

        overlayRoot.SetActive(false);
    }

    private IEnumerator BuildOverlayUiDeferred()
    {
        if (overlayUiBuilt)
        {
            yield break;
        }

        EnsureOverlayUiShell();
        if (overlayRoot == null || overlayPanelRect == null)
        {
            yield break;
        }

        yield return null;

        Button closeButton = CreateButton(overlayPanelRect, "CloseButton", "关闭", 22, new Color32(44, 38, 44, 245));
        SetAnchors(closeButton.GetComponent<RectTransform>(), 0.89f, 0.938f, 0.985f, 0.988f, 0f, 0f, 0f, 0f);
        closeButton.onClick.AddListener(() => SetVisible(false));

        yield return null;

        Image leftPanel = CreateImage(overlayPanelRect, "ListPanel", new Color32(9, 8, 11, 210));
        SetAnchors(leftPanel.rectTransform, 0.02f, 0.08f, 0.255f, 0.925f, 0f, 0f, 0f, 0f);
        AddBorder(leftPanel.rectTransform, new Color32(118, 83, 75, 220), 3f);

        TextMeshProUGUI listTitle = CreateText(leftPanel.rectTransform, "ListTitle", "预设列表", 24, new Color32(236, 230, 196, 255), TextAlignmentOptions.Left);
        SetAnchors(listTitle.rectTransform, 0.04f, 0.91f, 0.96f, 0.985f, 0f, 0f, 0f, 0f);

        countText = CreateText(leftPanel.rectTransform, "Count", "", 20, new Color32(206, 202, 190, 255), TextAlignmentOptions.Left);
        SetAnchors(countText.rectTransform, 0.04f, 0.855f, 0.96f, 0.925f, 0f, 0f, 0f, 0f);

        ScrollRect listScrollRect = CreateScrollRect(leftPanel.rectTransform, "ListScroll");
        SetAnchors(listScrollRect.GetComponent<RectTransform>(), 0.035f, 0.27f, 0.965f, 0.845f, 0f, 0f, 0f, 0f);
        listContentRect = listScrollRect.content;

        yield return null;

        Button createButton = CreateButton(leftPanel.rectTransform, "CreateButton", "按当前配置创建", 20, new Color32(58, 42, 52, 245));
        SetAnchors(createButton.GetComponent<RectTransform>(), 0.035f, 0.205f, 0.965f, 0.255f, 0f, 0f, 0f, 0f);
        createButton.onClick.AddListener(() => { CaptureCurrent(); RefreshOverlayUi(); });

        Button createSlotButton = CreateButton(leftPanel.rectTransform, "CreateSlotButton", "按当前槽位创建", 20, new Color32(58, 42, 52, 245));
        SetAnchors(createSlotButton.GetComponent<RectTransform>(), 0.035f, 0.145f, 0.965f, 0.195f, 0f, 0f, 0f, 0f);
        createSlotButton.onClick.AddListener(() => { CaptureCurrentSlot(); RefreshOverlayUi(); });

        Button refreshButton = CreateButton(leftPanel.rectTransform, "RefreshButton", "刷新列表", 20, new Color32(42, 42, 48, 245));
        SetAnchors(refreshButton.GetComponent<RectTransform>(), 0.035f, 0.085f, 0.965f, 0.135f, 0f, 0f, 0f, 0f);
        refreshButton.onClick.AddListener(() => { LoadStore(); RefreshOverlayUi(); });

        Button deleteButton = CreateButton(leftPanel.rectTransform, "DeleteButton", "删除选中预设", 20, new Color32(48, 36, 42, 245));
        SetAnchors(deleteButton.GetComponent<RectTransform>(), 0.035f, 0.025f, 0.965f, 0.075f, 0f, 0f, 0f, 0f);
        deleteButton.onClick.AddListener(() => { DeleteSelectedPreset(); RefreshOverlayUi(); });

        yield return null;

        Image rightPanel = CreateImage(overlayPanelRect, "DetailPanel", new Color32(11, 10, 13, 215));
        SetAnchors(rightPanel.rectTransform, 0.275f, 0.08f, 0.98f, 0.925f, 0f, 0f, 0f, 0f);
        AddBorder(rightPanel.rectTransform, new Color32(118, 83, 75, 220), 3f);

        TextMeshProUGUI settingsTitle = CreateText(rightPanel.rectTransform, "SettingsTitle", "预设设置", 24, new Color32(236, 230, 196, 255), TextAlignmentOptions.Left);
        SetAnchors(settingsTitle.rectTransform, 0.025f, 0.91f, 0.25f, 0.985f, 0f, 0f, 0f, 0f);

        slotText = CreateText(rightPanel.rectTransform, "SlotText", "原版目标槽位", 20, new Color32(236, 230, 196, 255), TextAlignmentOptions.Right);
        SetAnchors(slotText.rectTransform, 0.57f, 0.91f, 0.72f, 0.985f, 0f, 0f, 0f, 0f);

        slotButtons.Clear();
        for (int i = 0; i < 5; i++)
        {
            int slot = i;
            Button slotButton = CreateButton(rightPanel.rectTransform, "SlotButton" + i, (i + 1).ToString(), 18, new Color32(42, 36, 44, 245));
            SetAnchors(slotButton.GetComponent<RectTransform>(), 0.73f + i * 0.05f, 0.925f, 0.775f + i * 0.05f, 0.975f, 0f, 0f, 0f, 0f);
            slotButton.onClick.AddListener(() =>
            {
                targetSlotIndex = slot;
                status = "原版目标槽位：" + (slot + 1);
                RefreshOverlayUi();
            });
            slotButtons.Add(slotButton);
        }

        yield return null;

        TextMeshProUGUI nameLabel = CreateText(rightPanel.rectTransform, "NameLabel", "名称", 22, new Color32(220, 216, 200, 255), TextAlignmentOptions.Left);
        SetAnchors(nameLabel.rectTransform, 0.025f, 0.835f, 0.085f, 0.895f, 0f, 0f, 0f, 0f);

        nameInputField = CreateInputField(rightPanel.rectTransform, "NameInput");
        SetAnchors(nameInputField.GetComponent<RectTransform>(), 0.09f, 0.835f, 0.86f, 0.895f, 0f, 0f, 0f, 0f);

        Button renameButton = CreateButton(rightPanel.rectTransform, "RenameButton", "改名", 20, new Color32(58, 42, 52, 245));
        SetAnchors(renameButton.GetComponent<RectTransform>(), 0.875f, 0.835f, 0.98f, 0.895f, 0f, 0f, 0f, 0f);
        renameButton.onClick.AddListener(RenameSelectedFromInput);

        ScrollRect detailScrollRect = CreateScrollRect(rightPanel.rectTransform, "DetailScroll");
        SetAnchors(detailScrollRect.GetComponent<RectTransform>(), 0.025f, 0.18f, 0.98f, 0.82f, 0f, 0f, 0f, 0f);
        detailContentRect = detailScrollRect.content;

        Button applyButton = CreateButton(rightPanel.rectTransform, "ApplyButton", "写入原版目标槽位", 24, new Color32(112, 48, 70, 250));
        SetAnchors(applyButton.GetComponent<RectTransform>(), 0.025f, 0.105f, 0.98f, 0.165f, 0f, 0f, 0f, 0f);
        applyButton.onClick.AddListener(() =>
        {
            int currentSlot = GetTargetSlot();
            ApplySelectedTo(currentSlot, GetPresetForwardKey(currentSlot));
            RefreshOverlayUi();
        });

        Button loadCurrentButton = CreateButton(rightPanel.rectTransform, "LoadCurrentButton", "加载到当前配置", 22, new Color32(82, 45, 64, 245));
        SetAnchors(loadCurrentButton.GetComponent<RectTransform>(), 0.70f, 0.055f, 0.98f, 0.1f, 0f, 0f, 0f, 0f);
        loadCurrentButton.onClick.AddListener(() =>
        {
            LoadSelectedToCurrentSetup();
            RefreshOverlayUi();
        });

        TextMeshProUGUI fileText = CreateText(rightPanel.rectTransform, "FileText", "文件：" + Path.GetFileName(PresetFile), 18, new Color32(188, 184, 176, 255), TextAlignmentOptions.Left);
        SetAnchors(fileText.rectTransform, 0.025f, 0.055f, 0.685f, 0.1f, 0f, 0f, 0f, 0f);

        statusText = CreateText(rightPanel.rectTransform, "StatusText", "", 20, new Color32(220, 216, 200, 255), TextAlignmentOptions.Left);
        SetAnchors(statusText.rectTransform, 0.025f, 0.015f, 0.98f, 0.055f, 0f, 0f, 0f, 0f);

        yield return null;

        CreateTooltipUi();
        CreateOverlayCursor();
        overlayUiBuilt = true;
    }

    private void DestroyOverlayUi()
    {
        RestoreOriginalTooltipCanvases();
        if (overlayRoot != null)
        {
            Destroy(overlayRoot);
            overlayRoot = null;
        }
        overlayCanvasRect = null;
        overlayPanelRect = null;
        listContentRect = null;
        detailContentRect = null;
        nameInputField = null;
        countText = null;
        slotText = null;
        statusText = null;
        overlayTooltipRect = null;
        overlayTooltipTitle = null;
        overlayTooltipBody = null;
        overlayCursorRect = null;
        overlayUiBuilt = false;
        slotButtons.Clear();
    }

    private void RefreshOverlayUi()
    {
        EnsureStoreLoaded();
        if (overlayRoot == null)
        {
            return;
        }

        RefreshOverlayHeaderUi();
        RefreshSlotButtons();
        RefreshPresetListUi();
        RefreshPresetDetailsUi();
    }

    private IEnumerator RefreshOverlayUiDeferred()
    {
        if (overlayRoot == null)
        {
            yield break;
        }

        bool wasLoaded = storeLoaded;
        string previousStatus = status;
        status = wasLoaded ? "正在刷新外部预设..." : "正在读取外部预设...";
        RefreshOverlayHeaderUi();
        RefreshSlotButtons();

        ClearGeneratedChildren(listContentRect);
        ClearGeneratedChildren(detailContentRect);
        CreateListText("正在读取外部预设...");
        CreateDetailSection("提示", "正在准备界面。");
        yield return null;

        EnsureStoreLoaded();
        if (!visible)
        {
            yield break;
        }
        if (wasLoaded)
        {
            status = previousStatus;
        }

        RefreshOverlayHeaderUi();
        RefreshSlotButtons();
        yield return null;

        RefreshPresetListUi();
        if (!visible)
        {
            yield break;
        }

        yield return null;
        RefreshPresetDetailsUi();
        RefreshOverlayHeaderUi();
    }

    private void RefreshOverlayHeaderUi()
    {
        selectedIndex = Mathf.Clamp(selectedIndex, 0, Math.Max(0, store.presets.Count - 1));
        if (countText != null)
        {
            countText.text = "共 " + store.presets.Count + " 个外部预设";
        }
        if (slotText != null)
        {
            slotText.text = "原版目标槽位";
        }
        if (statusText != null)
        {
            statusText.text = status;
        }
    }

    private void RefreshPresetListUi()
    {
        ClearGeneratedChildren(listContentRect);
        if (listContentRect == null)
        {
            return;
        }

        if (store.presets.Count == 0)
        {
            CreateListText("暂无预设。");
            CreateListText("先点击“按当前配置创建”。");
            return;
        }

        for (int i = 0; i < store.presets.Count; i++)
        {
            int index = i;
            PresetData preset = store.presets[i];
            string name = string.IsNullOrWhiteSpace(preset.name) ? "未命名预设" : preset.name;
            string label = (i + 1) + ". " + name;
            Button button = CreatePresetListButton(listContentRect, "Preset_" + i, label, selectedIndex == i);
            AddLayoutElement(button.gameObject, 78f);
            button.onClick.AddListener(() =>
            {
                selectedIndex = index;
                RefreshOverlayUi();
            });
        }
    }

    private void RefreshPresetDetailsUi()
    {
        HideFavoriteTooltip();
        ClearGeneratedChildren(detailContentRect);
        PresetData selected = GetSelectedPreset(silent: true);
        if (nameInputField != null)
        {
            nameInputField.SetTextWithoutNotify(selected == null ? "" : selected.name ?? "");
        }

        if (detailContentRect == null)
        {
            return;
        }

        if (selected == null)
        {
            CreateDetailSection("提示", "请选择一个外部预设。");
            return;
        }

        DetailView detail = GetDetailView(selected);
        CreateDetailSection("武器", detail.weapon);
        CreateDetailSection("角色", detail.costume);
        CreateCostumeStartingItemSection(selected);
        CreateDetailSection("才能", detail.passives);
        CreatePocketItemSection(selected);
        CreateDetailSection("水果串", detail.fruits);
        CreateFavoriteSection(selected);
    }

    private void RenameSelectedFromInput()
    {
        EnsureStoreLoaded();
        PresetData selected = GetSelectedPreset();
        if (selected == null || nameInputField == null)
        {
            RefreshOverlayUi();
            return;
        }

        string newName = string.IsNullOrWhiteSpace(nameInputField.text) ? "未命名预设" : nameInputField.text.Trim();
        selected.name = newName;
        SaveStore();
        storeRevision++;
        status = "已改名为：" + newName;
        RefreshOverlayUi();
    }

    private void CreateListText(string value)
    {
        TextMeshProUGUI text = CreateText(listContentRect, "ListText", value, 20, new Color32(210, 206, 190, 255), TextAlignmentOptions.Left);
        AddLayoutElement(text.gameObject, 34f);
    }

    private void CreateDetailSection(string title, string value)
    {
        Image section = CreateImage(detailContentRect, "Section_" + title, new Color32(24, 20, 25, 218));
        VerticalLayoutGroup layout = section.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 8, 10);
        layout.spacing = 5f;
        ContentSizeFitter fitter = section.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        AddLayoutElement(section.gameObject, -1f);

        TextMeshProUGUI header = CreateText(section.rectTransform, "Header", title, 22, new Color32(236, 230, 196, 255), TextAlignmentOptions.Left);
        header.fontStyle = FontStyles.Bold;
        AddLayoutElement(header.gameObject, 28f);

        TextMeshProUGUI body = CreateText(section.rectTransform, "Body", string.IsNullOrWhiteSpace(value) ? "无" : value, 20, new Color32(230, 226, 214, 255), TextAlignmentOptions.Left);
        body.textWrappingMode = TextWrappingModes.Normal;
        body.overflowMode = TextOverflowModes.Overflow;
        LayoutElement bodyLayout = body.gameObject.AddComponent<LayoutElement>();
        bodyLayout.minHeight = 30f;
    }

    private void CreateFavoriteSection(PresetData preset)
    {
        Image section = CreateImage(detailContentRect, "Section_偏好神器", new Color32(24, 20, 25, 218));
        VerticalLayoutGroup layout = section.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 8, 10);
        layout.spacing = 8f;
        ContentSizeFitter fitter = section.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        AddLayoutElement(section.gameObject, -1f);

        TextMeshProUGUI header = CreateText(section.rectTransform, "Header", "偏好神器", 22, new Color32(236, 230, 196, 255), TextAlignmentOptions.Left);
        header.fontStyle = FontStyles.Bold;
        AddLayoutElement(header.gameObject, 28f);

        if (preset.favoriteItemIds == null || preset.favoriteItemIds.Count == 0)
        {
            TextMeshProUGUI empty = CreateText(section.rectTransform, "Empty", "无", 20, new Color32(230, 226, 214, 255), TextAlignmentOptions.Left);
            AddLayoutElement(empty.gameObject, 30f);
            return;
        }

        GameObject gridObject = new GameObject("FavoriteGrid", typeof(RectTransform));
        gridObject.transform.SetParent(section.rectTransform, false);
        RectTransform gridRect = gridObject.GetComponent<RectTransform>();
        GridLayoutGroup grid = gridObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(58f, 58f);
        grid.spacing = new Vector2(8f, 8f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 10;

        int count = preset.favoriteItemIds.Distinct().Count();
        int rows = Mathf.Max(1, Mathf.CeilToInt(count / 10f));
        AddLayoutElement(gridObject, rows * 66f);

        foreach (int id in preset.favoriteItemIds.Distinct().OrderBy(ResolveItemName))
        {
            CreateItemIcon(gridRect, id, 0);
        }
    }

    private void CreatePocketItemSection(PresetData preset)
    {
        Image section = CreateImage(detailContentRect, "Section_许愿泉", new Color32(24, 20, 25, 218));
        VerticalLayoutGroup layout = section.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 8, 10);
        layout.spacing = 8f;
        ContentSizeFitter fitter = section.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        AddLayoutElement(section.gameObject, -1f);

        TextMeshProUGUI header = CreateText(section.rectTransform, "Header", "许愿泉", 22, new Color32(236, 230, 196, 255), TextAlignmentOptions.Left);
        header.fontStyle = FontStyles.Bold;
        AddLayoutElement(header.gameObject, 28f);

        if (preset.dimensionPocketItems == null || preset.dimensionPocketItems.Count == 0)
        {
            TextMeshProUGUI empty = CreateText(section.rectTransform, "Empty", "无", 20, new Color32(230, 226, 214, 255), TextAlignmentOptions.Left);
            AddLayoutElement(empty.gameObject, 30f);
            return;
        }

        GameObject gridObject = new GameObject("PocketGrid", typeof(RectTransform));
        gridObject.transform.SetParent(section.rectTransform, false);
        RectTransform gridRect = gridObject.GetComponent<RectTransform>();
        GridLayoutGroup grid = gridObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(58f, 58f);
        grid.spacing = new Vector2(8f, 8f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 10;

        int rows = Mathf.Max(1, Mathf.CeilToInt(preset.dimensionPocketItems.Count / 10f));
        AddLayoutElement(gridObject, rows * 66f);

        foreach (PocketItem item in preset.dimensionPocketItems)
        {
            CreateItemIcon(gridRect, item.entityId, item.quantity);
        }
    }

    private void CreateCostumeStartingItemSection(PresetData preset)
    {
        List<int> itemIds = GetCostumeStartingItemIds(preset.playerCostume);
        if (itemIds.Count == 0)
        {
            return;
        }

        Image section = CreateImage(detailContentRect, "Section_初始神器", new Color32(24, 20, 25, 218));
        VerticalLayoutGroup layout = section.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 8, 10);
        layout.spacing = 8f;
        ContentSizeFitter fitter = section.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        AddLayoutElement(section.gameObject, -1f);

        TextMeshProUGUI header = CreateText(section.rectTransform, "Header", "初始神器", 22, new Color32(236, 230, 196, 255), TextAlignmentOptions.Left);
        header.fontStyle = FontStyles.Bold;
        AddLayoutElement(header.gameObject, 28f);

        GameObject gridObject = new GameObject("CostumeStartingItemGrid", typeof(RectTransform));
        gridObject.transform.SetParent(section.rectTransform, false);
        RectTransform gridRect = gridObject.GetComponent<RectTransform>();
        GridLayoutGroup grid = gridObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(58f, 58f);
        grid.spacing = new Vector2(8f, 8f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 10;

        int rows = Mathf.Max(1, Mathf.CeilToInt(itemIds.Count / 10f));
        AddLayoutElement(gridObject, rows * 66f);

        foreach (int itemId in itemIds)
        {
            CreateItemIcon(gridRect, itemId, 0);
        }
    }

    private void CreateItemIcon(RectTransform parent, int itemId, int quantity)
    {
        string itemName = ResolveItemName(itemId);
        string effect = ResolveItemPrimaryEffect(itemId);
        Image background = CreateImage(parent, "Item_" + itemId, new Color32(18, 16, 22, 245));
        AddBorder(background.rectTransform, new Color32(116, 78, 62, 225), 2f);

        Sprite sprite = ResolveItemSprite(itemId);
        if (sprite != null)
        {
            Image icon = CreateImage(background.rectTransform, "Icon", Color.white);
            icon.sprite = sprite;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            Stretch(icon.rectTransform, 7f, 7f, -7f, -7f);
            CreateCharmSubIcons(background.rectTransform, itemId);
        }
        else
        {
            TextMeshProUGUI fallback = CreateText(background.rectTransform, "Fallback", ShortItemLabel(itemName), 18, new Color32(248, 245, 235, 255), TextAlignmentOptions.Center);
            fallback.textWrappingMode = TextWrappingModes.NoWrap;
            fallback.overflowMode = TextOverflowModes.Ellipsis;
            Stretch(fallback.rectTransform, 5f, 5f, -5f, -5f);
        }

        if (quantity > 1)
        {
            TextMeshProUGUI quantityText = CreateText(background.rectTransform, "Quantity", quantity.ToString(), 17, new Color32(248, 245, 235, 255), TextAlignmentOptions.Right);
            quantityText.textWrappingMode = TextWrappingModes.NoWrap;
            quantityText.fontStyle = FontStyles.Bold;
            SetAnchors(quantityText.rectTransform, 0.32f, 0f, 1f, 0.36f, 0f, 1f, -4f, 0f);
        }

        FavoriteHover hover = background.gameObject.AddComponent<FavoriteHover>();
        hover.Configure(this, itemId, itemName, string.IsNullOrWhiteSpace(effect) ? "未能读取到效果文本。" : effect);
    }

    private static void CenterIcon(RectTransform rect)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private void CreateCharmSubIcons(RectTransform parent, int itemId)
    {
        try
        {
            ItemEntity item = ItemDatabase.FindItemById(itemId);
            GameObject prefab = item != null ? item.resourcePrefab : null;
            if (prefab == null)
            {
                return;
            }

            foreach (Component component in prefab.GetComponents<Component>())
            {
                if (component == null)
                {
                    continue;
                }

                Type type = component.GetType();
                MethodInfo countMethod = type.GetMethod("GetSubIconCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                MethodInfo imageMethod = type.GetMethod("GetSubIconImage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                MethodInfo offsetMethod = type.GetMethod("GetSubIconImageOffset", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (countMethod == null || imageMethod == null || offsetMethod == null)
                {
                    continue;
                }

                int count = Convert.ToInt32(countMethod.Invoke(component, Array.Empty<object>()));
                if (count <= 0)
                {
                    continue;
                }

                for (int i = 0; i < count; i++)
                {
                    Sprite subIcon = imageMethod.Invoke(component, new object[] { new ItemPosition(0, 0), false, i }) as Sprite;
                    if (subIcon == null)
                    {
                        continue;
                    }

                    Vector2 offset = (Vector2)offsetMethod.Invoke(component, new object[] { i });
                    Image image = CreateImage(parent, "SubIcon_" + i, Color.white);
                    image.sprite = subIcon;
                    image.preserveAspect = true;
                    image.raycastTarget = false;

                    RectTransform rect = image.rectTransform;
                    CenterIcon(rect);
                    float cellSize = GetIconCellSize(parent);
                    float cellScale = cellSize / 58f;
                    rect.anchoredPosition = offset * cellScale;
                    float width = Mathf.Max(8f, image.preferredWidth);
                    float height = Mathf.Max(8f, image.preferredHeight);
                    float targetMaxSize = Mathf.Clamp(cellSize * 0.52f, 24f, 34f);
                    float scale = Mathf.Clamp(targetMaxSize / Mathf.Max(width, height), 0.5f, 4f);
                    rect.sizeDelta = new Vector2(width * scale, height * scale);
                    image.transform.SetAsLastSibling();
                }
                return;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterPresets] Failed to draw sub icons for item " + itemId + ": " + ex.Message);
        }
    }

    private static float GetIconCellSize(RectTransform parent)
    {
        if (parent != null)
        {
            float width = parent.rect.width;
            float height = parent.rect.height;
            float size = Mathf.Max(width, height);
            if (size > 20f)
            {
                return size;
            }
        }

        return 58f;
    }

    private ScrollRect CreateScrollRect(RectTransform parent, string name)
    {
        Image rootImage = CreateImage(parent, name, new Color32(5, 5, 7, 170));
        ScrollRect scrollRect = rootImage.gameObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 8f;

        Image viewportImage = CreateImage(rootImage.rectTransform, "Viewport", new Color(1f, 1f, 1f, 0.02f));
        Stretch(viewportImage.rectTransform);
        Mask mask = viewportImage.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        GameObject contentObject = new GameObject("Content", typeof(RectTransform));
        contentObject.transform.SetParent(viewportImage.transform, false);
        RectTransform content = contentObject.GetComponent<RectTransform>();
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;

        VerticalLayoutGroup layout = contentObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(6, 6, 6, 6);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = contentObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = viewportImage.rectTransform;
        scrollRect.content = content;
        return scrollRect;
    }

    private void RefreshSlotButtons()
    {
        int target = GetTargetSlot();
        for (int i = 0; i < slotButtons.Count; i++)
        {
            Image image = slotButtons[i] != null ? slotButtons[i].GetComponent<Image>() : null;
            if (image != null)
            {
                image.color = i == target ? new Color32(112, 48, 70, 250) : new Color32(42, 36, 44, 245);
            }
        }
    }

    private void CreateTooltipUi()
    {
        Image tooltip = CreateImage(overlayCanvasRect, "FavoriteTooltip", new Color32(42, 43, 58, 250));
        tooltip.raycastTarget = false;
        overlayTooltipRect = tooltip.rectTransform;
        overlayTooltipRect.pivot = new Vector2(0f, 1f);
        overlayTooltipRect.anchorMin = new Vector2(0.5f, 0.5f);
        overlayTooltipRect.anchorMax = new Vector2(0.5f, 0.5f);
        overlayTooltipRect.sizeDelta = new Vector2(560f, 350f);
        AddBorder(overlayTooltipRect, new Color32(170, 190, 210, 245), 4f);

        Image header = CreateImage(overlayTooltipRect, "Header", new Color32(50, 52, 70, 255));
        header.raycastTarget = false;
        SetAnchors(header.rectTransform, 0f, 0.78f, 1f, 1f, 8f, 0f, -8f, -8f);
        AddBorder(header.rectTransform, new Color32(118, 136, 166, 220), 2f);

        overlayTooltipTitle = CreateText(overlayTooltipRect, "Title", "", 24, new Color32(155, 255, 80, 255), TextAlignmentOptions.Left);
        overlayTooltipTitle.fontStyle = FontStyles.Bold;
        SetAnchors(overlayTooltipTitle.rectTransform, 0f, 0.82f, 1f, 0.975f, 22f, 0f, -22f, 0f);

        overlayTooltipBody = CreateText(overlayTooltipRect, "Body", "", 20, new Color32(238, 236, 228, 255), TextAlignmentOptions.Left);
        overlayTooltipBody.textWrappingMode = TextWrappingModes.Normal;
        overlayTooltipBody.overflowMode = TextOverflowModes.Overflow;
        SetAnchors(overlayTooltipBody.rectTransform, 0f, 0f, 1f, 0.76f, 22f, 18f, -22f, -12f);

        overlayTooltipRect.gameObject.SetActive(false);
    }

    private void ShowFavoriteTooltip(string title, string body)
    {
        if (overlayTooltipRect == null)
        {
            return;
        }

        if (overlayTooltipTitle != null)
        {
            overlayTooltipTitle.text = title;
        }
        if (overlayTooltipBody != null)
        {
            overlayTooltipBody.text = body;
        }

        overlayTooltipRect.gameObject.SetActive(true);
        UpdateFavoriteTooltipPosition();
        overlayTooltipRect.SetAsLastSibling();
        if (overlayCursorRect != null)
        {
            overlayCursorRect.SetAsLastSibling();
        }
    }

    private void HideFavoriteTooltip()
    {
        if (overlayTooltipRect != null)
        {
            overlayTooltipRect.gameObject.SetActive(false);
        }
        RestoreOriginalTooltipCanvases();
    }

    private void ShowOriginalFavoriteTooltip(FavoriteHover opener, int itemId, string fallbackTitle, string fallbackBody)
    {
        if (opener == null)
        {
            return;
        }

        try
        {
            ItemEntity item = ItemDatabase.FindItemById(itemId);
            if (item != null && UIManager.Instance != null)
            {
                if (item.type == EItemType.Charm)
                {
                    UI_CharmTooltip tooltip = UIManager.Instance.GetElement<UI_CharmTooltip>();
                    tooltip.Open(opener, opener.RectTransform, new Vector2(15f, 14f), item);
                    tooltip.ActivateCategorySet(expectAddition: true);
                    BringOriginalTooltipForward(tooltip);
                    return;
                }

                if (item.type == EItemType.StoneTablet)
                {
                    UI_StoneTabletTooltip tooltip = UIManager.Instance.GetElement<UI_StoneTabletTooltip>();
                    tooltip.Open(opener, opener.RectTransform, new Vector2(15f, 14f), item);
                    BringOriginalTooltipForward(tooltip);
                    return;
                }

                UI_ItemTooltip itemTooltip = UIManager.Instance.GetElement<UI_ItemTooltip>();
                itemTooltip.Open(opener, opener.RectTransform, new Vector2(15f, 14f), item);
                BringOriginalTooltipForward(itemTooltip);
                return;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterPresets] Failed to open original item tooltip: " + ex.Message);
        }

        ShowFavoriteTooltip(fallbackTitle, fallbackBody);
    }

    private void HideOriginalFavoriteTooltip(FavoriteHover opener)
    {
        if (opener == null)
        {
            RestoreOriginalTooltipCanvases();
            HideFavoriteTooltip();
            return;
        }

        if (opener.LastTooltip != null)
        {
            opener.LastTooltip.Close();
            opener.LastTooltip = null;
        }
        opener.Showing = false;
        HideFavoriteTooltip();
    }

    private void BringOriginalTooltipForward(UI_BaseTooltip tooltip)
    {
        if (tooltip == null)
        {
            return;
        }

        try
        {
            Canvas canvas = tooltip.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                if (!boostedTooltipCanvases.ContainsKey(canvas))
                {
                    boostedTooltipCanvases[canvas] = new CanvasSortState
                    {
                        overrideSorting = canvas.overrideSorting,
                        sortingOrder = canvas.sortingOrder
                    };
                }
                canvas.overrideSorting = true;
                canvas.sortingOrder = Mathf.Max(canvas.sortingOrder, 30020);
            }
            tooltip.transform.SetAsLastSibling();
        }
        catch
        {
        }
    }

    private void RestoreOriginalTooltipCanvases()
    {
        if (boostedTooltipCanvases.Count == 0)
        {
            return;
        }

        foreach (KeyValuePair<Canvas, CanvasSortState> entry in boostedTooltipCanvases.ToList())
        {
            Canvas canvas = entry.Key;
            if (canvas == null)
            {
                continue;
            }
            canvas.overrideSorting = entry.Value.overrideSorting;
            canvas.sortingOrder = entry.Value.sortingOrder;
        }
        boostedTooltipCanvases.Clear();
    }

    private void UpdateFavoriteTooltipPosition()
    {
        if (overlayCanvasRect == null || overlayTooltipRect == null || !overlayTooltipRect.gameObject.activeSelf)
        {
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        RectTransformUtility.ScreenPointToLocalPointInRectangle(overlayCanvasRect, mouse.position.ReadValue(), null, out Vector2 localPoint);
        Vector2 position = localPoint + new Vector2(26f, -20f);
        Vector2 canvasSize = overlayCanvasRect.rect.size;
        Vector2 tooltipSize = overlayTooltipRect.sizeDelta;
        float minX = -canvasSize.x * 0.5f + 16f;
        float maxX = canvasSize.x * 0.5f - tooltipSize.x - 16f;
        float minY = -canvasSize.y * 0.5f + tooltipSize.y + 16f;
        float maxY = canvasSize.y * 0.5f - 16f;
        position.x = Mathf.Clamp(position.x, minX, maxX);
        position.y = Mathf.Clamp(position.y, minY, maxY);
        overlayTooltipRect.anchoredPosition = position;
    }

    private TMP_InputField CreateInputField(RectTransform parent, string name)
    {
        Image background = CreateImage(parent, name, new Color32(28, 24, 29, 245));
        background.sprite = buttonSprite != null ? buttonSprite : solidSprite;
        background.type = Image.Type.Sliced;
        TMP_InputField input = background.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = background;
        input.characterLimit = 64;
        input.textViewport = background.rectTransform;

        TextMeshProUGUI text = CreateText(background.rectTransform, "Text", "", 22, new Color32(245, 242, 230, 255), TextAlignmentOptions.Left);
        text.textWrappingMode = TextWrappingModes.NoWrap;
        SetAnchors(text.rectTransform, 0f, 0f, 1f, 1f, 12f, 5f, -12f, -5f);

        TextMeshProUGUI placeholder = CreateText(background.rectTransform, "Placeholder", "输入预设名称", 22, new Color32(155, 150, 145, 190), TextAlignmentOptions.Left);
        placeholder.fontStyle = FontStyles.Italic;
        SetAnchors(placeholder.rectTransform, 0f, 0f, 1f, 1f, 12f, 5f, -12f, -5f);

        input.textComponent = text;
        input.placeholder = placeholder;
        input.onSubmit.AddListener(_ => RenameSelectedFromInput());
        return input;
    }

    private Button CreateButton(RectTransform parent, string name, string label, int fontSize, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
    {
        Image image = CreateImage(parent, name, color);
        image.sprite = buttonSprite != null ? buttonSprite : solidSprite;
        image.type = Image.Type.Sliced;
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.12f, 1.08f, 1.08f, 1f);
        colors.pressedColor = new Color(0.75f, 0.72f, 0.72f, 1f);
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.65f);
        button.colors = colors;

        TextMeshProUGUI text = CreateText(image.rectTransform, "Label", label, fontSize, new Color32(248, 245, 235, 255), alignment);
        text.overflowMode = TextOverflowModes.Ellipsis;
        Stretch(text.rectTransform, alignment == TextAlignmentOptions.Left ? 18f : 8f, 4f, -8f, -4f);
        return button;
    }

    private Button CreatePresetListButton(RectTransform parent, string name, string label, bool selected)
    {
        Image image = CreateImage(parent, name, selected ? new Color32(58, 31, 36, 245) : new Color32(16, 14, 18, 235));
        image.sprite = solidSprite;
        image.type = Image.Type.Sliced;
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.08f, 1.05f, 1.02f, 1f);
        colors.pressedColor = new Color(0.78f, 0.72f, 0.72f, 1f);
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.65f);
        button.colors = colors;

        AddBorder(image.rectTransform, selected ? new Color32(188, 93, 72, 235) : new Color32(82, 52, 42, 190), selected ? 3f : 2f);

        TextMeshProUGUI marker = CreateText(image.rectTransform, "Marker", selected ? "▶" : "", 21, new Color32(250, 246, 228, 255), TextAlignmentOptions.Center);
        SetAnchors(marker.rectTransform, 0f, 0f, 0f, 1f, 0f, 6f, 32f, -6f);
        marker.textWrappingMode = TextWrappingModes.NoWrap;

        TextMeshProUGUI text = CreateText(image.rectTransform, "Label", label, 20, new Color32(248, 245, 235, 255), TextAlignmentOptions.Left);
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        Stretch(text.rectTransform, 32f, 6f, -12f, -6f);
        return button;
    }

    private TextMeshProUGUI CreateText(RectTransform parent, string name, string value, int fontSize, Color color, TextAlignmentOptions alignment)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        TextMeshProUGUI text = obj.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.Normal;
        if (uiFont != null)
        {
            text.font = uiFont;
        }
        return text;
    }

    private Image CreateImage(RectTransform parent, string name, Color color)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image));
        obj.transform.SetParent(parent, false);
        Image image = obj.GetComponent<Image>();
        image.sprite = solidSprite;
        image.type = Image.Type.Sliced;
        image.color = color;
        return image;
    }

    private void CreateOverlayCursor()
    {
        if (cursorSprite != null)
        {
            Image cursor = CreateImage(overlayCanvasRect, "OverlayCursor", cursorColor);
            cursor.sprite = cursorSprite;
            cursor.material = cursorMaterial;
            cursor.preserveAspect = true;
            cursor.raycastTarget = false;
            overlayCursorRect = cursor.rectTransform;
            overlayCursorRect.pivot = new Vector2(0f, 1f);
            overlayCursorRect.sizeDelta = cursorSize;
            overlayCursorRect.SetAsLastSibling();
            return;
        }

        GameObject cursorObject = new GameObject("OverlayCursor", typeof(RectTransform));
        cursorObject.transform.SetParent(overlayCanvasRect, false);
        overlayCursorRect = cursorObject.GetComponent<RectTransform>();
        overlayCursorRect.pivot = new Vector2(0f, 1f);
        overlayCursorRect.sizeDelta = new Vector2(34f, 38f);
        overlayCursorRect.SetAsLastSibling();

        AddCursorPart(overlayCursorRect, "OutlineA", 0f, 0f, 5f, 30f, 0f, Color.black);
        AddCursorPart(overlayCursorRect, "OutlineB", 0f, 0f, 22f, 5f, 0f, Color.black);
        AddCursorPart(overlayCursorRect, "OutlineC", 13f, -13f, 5f, 26f, -38f, Color.black);
        AddCursorPart(overlayCursorRect, "OutlineD", 14f, -26f, 15f, 5f, 0f, Color.black);

        AddCursorPart(overlayCursorRect, "FillA", 1f, -1f, 3f, 24f, 0f, Color.white);
        AddCursorPart(overlayCursorRect, "FillB", 1f, -1f, 16f, 3f, 0f, Color.white);
        AddCursorPart(overlayCursorRect, "FillC", 12f, -13f, 3f, 21f, -38f, Color.white);
        AddCursorPart(overlayCursorRect, "FillD", 14f, -24f, 11f, 3f, 0f, Color.white);
    }

    private void AddCursorPart(RectTransform parent, string name, float x, float y, float width, float height, float rotation, Color color)
    {
        Image image = CreateImage(parent, name, color);
        image.raycastTarget = false;
        RectTransform rect = image.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
        rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
    }

    private void UpdateOverlayCursor()
    {
        if (overlayCanvasRect == null || overlayCursorRect == null)
        {
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            overlayCursorRect.gameObject.SetActive(false);
            return;
        }

        overlayCursorRect.gameObject.SetActive(true);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(overlayCanvasRect, mouse.position.ReadValue(), null, out Vector2 localPoint);
        overlayCursorRect.anchoredPosition = localPoint + new Vector2(2f, -2f);
        overlayCursorRect.SetAsLastSibling();
    }

    private void CaptureUiStyle()
    {
        UI_PresetPanel panel = hookedPanel != null ? hookedPanel : TryGetPresetPanel();
        if (panel == null)
        {
            return;
        }

        TextMeshProUGUI templateText = panel.GetComponentInChildren<TextMeshProUGUI>(true);
        if (templateText != null)
        {
            uiFont = templateText.font;
        }

        Image buttonImage = panel.applyPreseButton != null ? panel.applyPreseButton.GetComponent<Image>() : null;
        if (buttonImage != null)
        {
            buttonSprite = buttonImage.sprite;
        }

        Image panelImage = panel.GetComponent<Image>();
        if (panelImage == null)
        {
            panelImage = panel.GetComponentInChildren<Image>(true);
        }
        if (panelImage != null)
        {
            panelSprite = panelImage.sprite;
        }

        CaptureCursorStyle();
    }

    private void CaptureCursorStyle()
    {
        try
        {
            UI_Cursor cursor = FindFirstObjectByType<UI_Cursor>();
            if (cursor == null)
            {
                return;
            }

            Image sourceImage = GetCursorImage(cursor, "image") ?? GetCursorImage(cursor, "battleImage") ?? cursor.GetComponentInChildren<Image>(true);
            if (sourceImage == null || sourceImage.sprite == null)
            {
                return;
            }

            cursorSprite = sourceImage.sprite;
            cursorMaterial = sourceImage.material;
            cursorColor = sourceImage.color;
            RectTransform sourceRect = sourceImage.rectTransform;
            if (sourceRect != null && sourceRect.sizeDelta.x > 2f && sourceRect.sizeDelta.y > 2f)
            {
                cursorSize = Vector2.Scale(sourceRect.sizeDelta, sourceRect.lossyScale);
            }
            else if (sourceRect != null && sourceRect.rect.width > 2f && sourceRect.rect.height > 2f)
            {
                cursorSize = Vector2.Scale(sourceRect.rect.size, sourceRect.lossyScale);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterPresets] Failed to capture UI cursor style: " + ex.Message);
        }
    }

    private static Image GetCursorImage(UI_Cursor cursor, string fieldName)
    {
        FieldInfo field = typeof(UI_Cursor).GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field != null ? field.GetValue(cursor) as Image : null;
    }

    private void EnsureSolidSprite()
    {
        if (solidSprite != null)
        {
            return;
        }

        solidSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f));
    }

    private void AddBorder(RectTransform parent, Color color, float thickness)
    {
        Image top = CreateImage(parent, "BorderTop", color);
        SetAnchors(top.rectTransform, 0f, 1f, 1f, 1f, 0f, -thickness, 0f, 0f);
        Image bottom = CreateImage(parent, "BorderBottom", color);
        SetAnchors(bottom.rectTransform, 0f, 0f, 1f, 0f, 0f, 0f, 0f, thickness);
        Image left = CreateImage(parent, "BorderLeft", color);
        SetAnchors(left.rectTransform, 0f, 0f, 0f, 1f, 0f, 0f, thickness, 0f);
        Image right = CreateImage(parent, "BorderRight", color);
        SetAnchors(right.rectTransform, 1f, 0f, 1f, 1f, -thickness, 0f, 0f, 0f);
    }

    private static void AddLayoutElement(GameObject obj, float preferredHeight)
    {
        LayoutElement element = obj.GetComponent<LayoutElement>();
        if (element == null)
        {
            element = obj.AddComponent<LayoutElement>();
        }
        if (preferredHeight > 0f)
        {
            element.preferredHeight = preferredHeight;
            element.minHeight = preferredHeight;
        }
    }

    private static void ClearGeneratedChildren(RectTransform parent)
    {
        if (parent == null)
        {
            return;
        }

        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Destroy(parent.GetChild(i).gameObject);
        }
    }

    private static void Stretch(RectTransform rect)
    {
        Stretch(rect, 0f, 0f, 0f, 0f);
    }

    private static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(right, top);
    }

    private static void SetAnchors(RectTransform rect, float minX, float minY, float maxX, float maxY, float left, float bottom, float right, float top)
    {
        rect.anchorMin = new Vector2(minX, minY);
        rect.anchorMax = new Vector2(maxX, maxY);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(right, top);
    }

    private void SetVisible(bool value)
    {
        SetVisible(value, revealEmbeddedButton: true);
    }

    private void SetVisible(bool value, bool revealEmbeddedButton)
    {
        if (visible == value)
        {
            return;
        }

        visible = value;
        if (visible)
        {
            if (openOverlayCoroutine != null)
            {
                StopCoroutine(openOverlayCoroutine);
                openOverlayCoroutine = null;
            }
            CaptureCursorVisibility();
            targetSlotIndex = Mathf.Clamp(GetCurrentSlot(), 0, 4);
            embeddedButtonEnableTime = 0f;
            SetEmbeddedButtonInteractable(false);
            if (embeddedButtonObject != null)
            {
                embeddedButtonObject.SetActive(false);
                embeddedButtonLastShown = false;
                embeddedButtonLastInteractable = false;
                embeddedButtonStateInitialized = true;
            }
            EnsureOverlayUiShell();
            if (overlayRoot != null)
            {
                overlayRoot.SetActive(true);
            }
            if (!storeLoaded)
            {
                status = "正在打开外部预设...";
            }
            RefreshOverlayHeaderUi();
            openOverlayCoroutine = StartCoroutine(OpenOverlayDeferred());
        }
        else
        {
            if (openOverlayCoroutine != null)
            {
                StopCoroutine(openOverlayCoroutine);
                openOverlayCoroutine = null;
            }
            HideFavoriteTooltip();
            RestoreCursorVisibility();
            embeddedButtonEnableTime = Time.unscaledTime + 0.16f;
            if (overlayRoot != null)
            {
                overlayRoot.SetActive(false);
            }
            if (embeddedButtonObject != null)
            {
                bool shouldShow = revealEmbeddedButton && hookedPanel != null && hookedPanel.IsOpened;
                embeddedButtonObject.SetActive(shouldShow);
                SetEmbeddedButtonInteractable(false);
                embeddedButtonLastShown = shouldShow;
                embeddedButtonLastInteractable = false;
                embeddedButtonStateInitialized = true;
            }
            if (!overlayUiBuilt && overlayRoot != null)
            {
                DestroyOverlayUi();
            }
        }
    }

    private IEnumerator OpenOverlayDeferred()
    {
        yield return null;

        if (!visible || overlayRoot == null)
        {
            openOverlayCoroutine = null;
            yield break;
        }

        status = overlayUiBuilt ? status : "正在创建外部预设界面...";
        RefreshOverlayHeaderUi();
        yield return BuildOverlayUiDeferred();
        if (!visible || overlayRoot == null)
        {
            openOverlayCoroutine = null;
            yield break;
        }

        yield return RefreshOverlayUiDeferred();
        openOverlayCoroutine = null;
    }

    private void HandleOriginalCloseClick()
    {
        if (!visible)
        {
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
        {
            return;
        }

        Vector2 position = mouse.position.ReadValue();
        float closeWidth = Mathf.Clamp(Screen.width * 0.045f, 72f, 110f);
        float closeHeight = Mathf.Clamp(Screen.height * 0.105f, 88f, 132f);
        if (position.x < Screen.width - closeWidth || position.y < Screen.height - closeHeight)
        {
            return;
        }

        UI_PresetPanel panel = hookedPanel != null ? hookedPanel : TryGetPresetPanel();
        if (panel != null && panel.IsOpened)
        {
            panel.Close();
        }
        SetVisible(false, revealEmbeddedButton: false);
    }

    private DetailView GetDetailView(PresetData preset)
    {
        if (cachedDetail != null && cachedDetailIndex == selectedIndex && cachedDetailRevision == storeRevision)
        {
            return cachedDetail;
        }

        cachedDetail = new DetailView
        {
            weapon = ResolveWeaponName(preset.startingWeaponId),
            costume = ResolveCostumeSummary(preset.playerCostume, preset.playerCostumeSkin),
            passives = SummarizePassives(preset),
            favorites = SummarizeFavorites(preset),
            pocketItems = SummarizePocketItems(preset),
            fruits = SummarizeFruits(preset)
        };
        cachedDetailIndex = selectedIndex;
        cachedDetailRevision = storeRevision;
        return cachedDetail;
    }

    private void CaptureCurrent()
    {
        EnsureStoreLoaded();
        if (SaveManager.Current == null)
        {
            status = "存档尚未就绪。";
            return;
        }

        PresetData preset = CapturePreset("");
        preset.name = "预设 " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
        store.presets.Add(preset);
        selectedIndex = store.presets.Count - 1;
        SaveStore();
        storeRevision++;
        status = "已按当前配置创建外部预设。";
    }

    private void CaptureCurrentSlot()
    {
        EnsureStoreLoaded();
        if (SaveManager.Current == null)
        {
            status = "存档尚未就绪。";
            return;
        }

        int currentSlot = GetTargetSlot();
        string forwardKey = GetPresetForwardKey(currentSlot);
        PresetData preset = CapturePreset(forwardKey);
        string originalName = SaveManager.Current.GetString(forwardKey + "PresetName", "");
        preset.name = string.IsNullOrWhiteSpace(originalName) ? "槽位 " + (currentSlot + 1) : originalName.Trim();
        store.presets.Add(preset);
        selectedIndex = store.presets.Count - 1;
        SaveStore();
        storeRevision++;
        status = "已按原版目标槽位 " + (currentSlot + 1) + " 创建外部预设。";
    }

    private void ApplySelectedTo(int slotIndex, string forwardKey)
    {
        EnsureStoreLoaded();
        if (SaveManager.Current == null)
        {
            status = "存档尚未就绪。";
            return;
        }

        PresetData preset = GetSelectedPreset();
        if (preset == null)
        {
            return;
        }

        ApplyPreset(preset, forwardKey);
        if (!string.IsNullOrEmpty(forwardKey))
        {
            SaveManager.Current.SetInt(forwardKey + "PresetEnabled", 1);
            SaveManager.Current.SetString(forwardKey + "PresetName", preset.name ?? "");
        }
        SaveManager.Save(saveCurrent: true, saveCurrentRun: false);
        RefreshOriginalPresetPanel(slotIndex);
        status = string.IsNullOrEmpty(forwardKey) ? "已加载到当前配置。" : "已写入原版目标槽位 " + (slotIndex + 1) + "。";
    }

    private void LoadSelectedToCurrentSetup()
    {
        EnsureStoreLoaded();
        if (SaveManager.Current == null)
        {
            status = "存档尚未就绪。";
            return;
        }

        PresetData preset = GetSelectedPreset();
        if (preset == null)
        {
            return;
        }

        ApplyPreset(preset, "");
        SaveManager.Save(saveCurrent: true, saveCurrentRun: false);
        bool refreshed = RefreshCurrentPlayerFromOriginalPanel();
        status = refreshed ? "已加载到当前配置。" : "已写入当前配置，刷新当前角色失败。";
    }

    private bool RefreshCurrentPlayerFromOriginalPanel()
    {
        try
        {
            UI_PresetPanel panel = hookedPanel != null ? hookedPanel : TryGetPresetPanel();
            if (panel == null)
            {
                return false;
            }

            MethodInfo backupMethod = typeof(UI_PresetPanel).GetMethod("CurrentSetupBackup", BindingFlags.Instance | BindingFlags.NonPublic);
            backupMethod?.Invoke(panel, Array.Empty<object>());

            MethodInfo updateMethod = typeof(UI_PresetPanel).GetMethod("UpdateCurrentPlayer", BindingFlags.Instance | BindingFlags.NonPublic);
            if (updateMethod == null)
            {
                return false;
            }

            Action onComplete = () =>
            {
                try
                {
                    SaveManager.Save(saveCurrent: true, saveCurrentRun: false);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[BetterPresets] Failed to save after current setup refresh: " + ex.Message);
                }
            };
            updateMethod.Invoke(panel, new object[] { onComplete, true });
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterPresets] Failed to refresh current player: " + ex.Message);
            return false;
        }
    }

    private static void RefreshOriginalPresetPanel(int slotIndex)
    {
        try
        {
            UI_PresetPanel panel = TryGetPresetPanel();
            if (panel != null && panel.IsOpened)
            {
                panel.UpdatePresetInfo(Mathf.Clamp(slotIndex, 0, 4), showError: true);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterPresets] Failed to refresh UI_PresetPanel: " + ex.Message);
        }
    }

    private PresetData GetSelectedPreset(bool silent = false)
    {
        EnsureStoreLoaded();
        if (store.presets.Count == 0)
        {
            if (!silent)
            {
                status = "还没有外部预设。";
            }
            return null;
        }
        selectedIndex = Mathf.Clamp(selectedIndex, 0, store.presets.Count - 1);
        return store.presets[selectedIndex];
    }

    private void DeleteSelectedPreset()
    {
        EnsureStoreLoaded();
        if (store.presets.Count == 0)
        {
            status = "还没有可删除的预设。";
            return;
        }

        string name = store.presets[selectedIndex].name;
        store.presets.RemoveAt(selectedIndex);
        selectedIndex = Mathf.Clamp(selectedIndex, 0, Math.Max(0, store.presets.Count - 1));
        SaveStore();
        storeRevision++;
        status = "已删除预设：" + (string.IsNullOrWhiteSpace(name) ? "未命名预设" : name);
    }

    private static string ResolveWeaponName(int id)
    {
        try
        {
            WeaponEntity weapon = WeaponDatabase.FindWeaponById(id);
            if (weapon != null)
            {
                return weapon.aName.ToString();
            }
        }
        catch
        {
        }
        return "武器 ID " + id;
    }

    private static string ResolveCostumeName(string id)
    {
        try
        {
            CostumeEntity costume = CostumeDatabase.FindCostumeByID(id);
            if (costume != null)
            {
                return costume.aName.ToString();
            }
        }
        catch
        {
        }
        return string.IsNullOrWhiteSpace(id) ? "默认角色" : id;
    }

    private static string ResolveCostumeSummary(string id, string skinId)
    {
        try
        {
            CostumeEntity costume = CostumeDatabase.FindCostumeByID(id);
            if (costume == null)
            {
                return ResolveCostumeName(id) + SkinSuffix(skinId);
            }

            List<string> lines = new List<string> { costume.aName + SkinSuffix(skinId) };
            if (costume.stats != null)
            {
                foreach (string stat in costume.stats)
                {
                    string text = ResolveStatusText(stat);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        lines.Add(text);
                    }
                }
            }

            return string.Join("\n", lines);
        }
        catch
        {
            return ResolveCostumeName(id) + SkinSuffix(skinId);
        }
    }

    private static List<int> GetCostumeStartingItemIds(string costumeId)
    {
        List<int> itemIds = new List<int>();
        try
        {
            CostumeEntity costume = CostumeDatabase.FindCostumeByID(costumeId);
            if (costume == null || costume.startingItems == null)
            {
                return itemIds;
            }

            foreach (ItemEntity item in costume.startingItems)
            {
                if (item == null)
                {
                    continue;
                }

                object id = GetMemberValue(item, "id") ?? GetMemberValue(item, "ID") ?? GetMemberValue(item, "entityID");
                if (id is int intId)
                {
                    itemIds.Add(intId);
                    continue;
                }

                int resolvedId = ResolveItemId(item);
                if (resolvedId >= 0)
                {
                    itemIds.Add(resolvedId);
                }
            }
        }
        catch
        {
        }

        return itemIds.Distinct().ToList();
    }

    private static int ResolveItemId(ItemEntity target)
    {
        if (target == null)
        {
            return -1;
        }

        try
        {
            foreach (int id in SafeItemIds())
            {
                ItemEntity item = ItemDatabase.FindItemById(id);
                if (ReferenceEquals(item, target))
                {
                    return id;
                }
            }
        }
        catch
        {
        }

        return -1;
    }

    private static string SkinSuffix(string skinId)
    {
        string skinName = ResolveCostumeSkinName(skinId);
        return string.IsNullOrWhiteSpace(skinName) ? "" : " / 皮肤 " + skinName;
    }

    private static string ResolveCostumeSkinName(string skinId)
    {
        if (string.IsNullOrWhiteSpace(skinId))
        {
            return "";
        }

        try
        {
            CostumeSkinEntity skin = CostumeDatabase.GetCostumeSkinByID(skinId);
            if (skin != null && skin.aName != null)
            {
                string localizedName = skin.aName.ToString();
                if (!string.IsNullOrWhiteSpace(localizedName))
                {
                    return localizedName;
                }
            }
        }
        catch
        {
        }

        return skinId;
    }

    private static string ResolveStatusText(string stat)
    {
        if (string.IsNullOrWhiteSpace(stat))
        {
            return "";
        }

        try
        {
            StatusInstance instance = StatusDatabase.CreateStatusEntity(stat);
            if (instance != null)
            {
                return KeywordDatabase.Convert(instance.ToString(reverse: false, color: false, sprite: false), useColor: false, useSprite: false);
            }
        }
        catch
        {
        }

        return stat;
    }

    private static string SummarizePocketItems(PresetData preset)
    {
        if (preset.dimensionPocketItems.Count == 0)
        {
            return "无";
        }

        List<string> names = new List<string>();
        foreach (PocketItem item in preset.dimensionPocketItems.Take(4))
        {
            names.Add(ResolveItemName(item.entityId));
        }

        string suffix = preset.dimensionPocketItems.Count > names.Count ? " 等，共 " + preset.dimensionPocketItems.Count + " 个" : "";
        return string.Join("、", names) + suffix;
    }

    private static string ResolveItemName(int id)
    {
        try
        {
            ItemEntity item = ItemDatabase.FindItemById(id);
            if (item != null)
            {
                return item.aName.ToString();
            }
        }
        catch
        {
        }
        return "道具 ID " + id;
    }

    private static string SummarizeFavorites(PresetData preset)
    {
        if (preset.favoriteItemIds == null || preset.favoriteItemIds.Count == 0)
        {
            return "无";
        }

        List<string> lines = new List<string>();
        foreach (int id in preset.favoriteItemIds.Distinct().OrderBy(ResolveItemName))
        {
            string name = ResolveItemName(id);
            string effect = ResolveItemPrimaryEffect(id);
            lines.Add(string.IsNullOrWhiteSpace(effect) ? name : name + "\n" + effect);
        }

        return string.Join("\n\n", lines);
    }

    private static string ResolveItemPrimaryEffect(int id)
    {
        try
        {
            ItemEntity item = ItemDatabase.FindItemById(id);
            if (item == null)
            {
                return "";
            }

            string prefabEffect = CleanGameText(ResolvePrefabEffect(GetMemberValue(item, "resourcePrefab") as GameObject));
            if (!string.IsNullOrWhiteSpace(prefabEffect))
            {
                return prefabEffect;
            }

            string context = CleanGameText(MemberToString(GetMemberValue(item, "Context")));
            if (!string.IsNullOrWhiteSpace(context))
            {
                return context;
            }

            return CleanGameText(MemberToString(GetMemberValue(item, "aFlavorText")));
        }
        catch
        {
            return "";
        }
    }

    private static Sprite ResolveItemSprite(int id)
    {
        try
        {
            ItemEntity item = ItemDatabase.FindItemById(id);
            if (item != null && item.Icon != null)
            {
                return item.Icon;
            }

            GameObject prefab = GetMemberValue(item, "resourcePrefab") as GameObject;
            if (prefab == null)
            {
                return null;
            }

            Image image = prefab.GetComponentInChildren<Image>(true);
            if (image != null && image.sprite != null)
            {
                return image.sprite;
            }

            SpriteRenderer renderer = prefab.GetComponentInChildren<SpriteRenderer>(true);
            if (renderer != null && renderer.sprite != null)
            {
                return renderer.sprite;
            }

            foreach (Component component in prefab.GetComponentsInChildren<Component>(true))
            {
                Sprite sprite = FirstSpriteFromObject(component);
                if (sprite != null)
                {
                    return sprite;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static Sprite FirstSpriteFromObject(object target)
    {
        if (target == null)
        {
            return null;
        }

        Type type = target.GetType();
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (property.GetIndexParameters().Length != 0 || !typeof(Sprite).IsAssignableFrom(property.PropertyType))
            {
                continue;
            }

            try
            {
                Sprite sprite = property.GetValue(target) as Sprite;
                if (sprite != null)
                {
                    return sprite;
                }
            }
            catch
            {
            }
        }

        foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!typeof(Sprite).IsAssignableFrom(field.FieldType))
            {
                continue;
            }

            try
            {
                Sprite sprite = field.GetValue(target) as Sprite;
                if (sprite != null)
                {
                    return sprite;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static string ShortItemLabel(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            return "?";
        }

        itemName = itemName.Trim();
        return itemName.Length <= 2 ? itemName : itemName.Substring(0, 2);
    }

    private static string ResolvePrefabEffect(GameObject prefab)
    {
        if (prefab == null)
        {
            return "";
        }

        foreach (Component component in prefab.GetComponents<Component>())
        {
            if (component == null)
            {
                continue;
            }

            string builtEffect = InvokeBuildEffectString(component);
            if (!string.IsNullOrWhiteSpace(builtEffect))
            {
                return builtEffect;
            }

            string effectText = InvokeEffectStrings(component);
            if (!string.IsNullOrWhiteSpace(effectText))
            {
                return effectText;
            }

            object effectStrings = GetMemberValue(component, "effectsString");
            if (effectStrings is System.Collections.IEnumerable enumerable && !(effectStrings is string))
            {
                List<string> effects = new List<string>();
                foreach (object value in enumerable)
                {
                    string effect = MemberToString(value);
                    if (!string.IsNullOrWhiteSpace(effect))
                    {
                        effects.Add(effect);
                    }
                }
                if (effects.Count > 0)
                {
                    return string.Join("\n", effects);
                }
            }
        }

        return "";
    }

    private static string InvokeBuildEffectString(object target)
    {
        if (target == null)
        {
            return "";
        }

        try
        {
            MethodInfo method = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "BuildEffectString" && m.GetParameters().Length == 7);
            if (method == null)
            {
                return "";
            }

            ParameterInfo[] parameters = method.GetParameters();
            object[] args = new object[7];
            for (int i = 0; i < parameters.Length; i++)
            {
                Type type = parameters[i].ParameterType;
                if (type == typeof(string))
                {
                    string name = parameters[i].Name ?? "";
                    args[i] = name.IndexOf("bullet", StringComparison.OrdinalIgnoreCase) >= 0 ? "- " : "";
                }
                else if (type == typeof(int))
                {
                    string name = parameters[i].Name ?? "";
                    args[i] = name.IndexOf("offset", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1;
                }
                else if (type == typeof(bool))
                {
                    args[i] = true;
                }
                else
                {
                    args[i] = null;
                }
            }

            return MemberToString(method.Invoke(target, args));
        }
        catch
        {
            return "";
        }
    }

    private static string InvokeEffectStrings(object target)
    {
        if (target == null)
        {
            return "";
        }

        List<string> lines = new List<string>();
        int count = Mathf.Clamp(InvokeZeroArgInt(target, "GetEffectStringCount", 1), 1, 12);
        MethodInfo[] methods = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m.Name == "GetEffectString" && m.ReturnType != typeof(void))
            .OrderBy(m => m.GetParameters().Length)
            .ToArray();

        foreach (MethodInfo method in methods)
        {
            for (int i = 0; i < count; i++)
            {
                string value = InvokeEffectString(method, target, i);
                if (!string.IsNullOrWhiteSpace(value) && !lines.Contains(value))
                {
                    lines.Add(value);
                }
            }

            if (lines.Count > 0)
            {
                break;
            }
        }

        return string.Join("\n", lines);
    }

    private static string InvokeEffectString(MethodInfo method, object target, int effectIndex)
    {
        try
        {
            ParameterInfo[] parameters = method.GetParameters();
            object[] args = new object[parameters.Length];
            int intOrdinal = 0;
            for (int i = 0; i < parameters.Length; i++)
            {
                Type type = parameters[i].ParameterType;
                if (type.IsByRef)
                {
                    return "";
                }
                if (type == typeof(int))
                {
                    string parameterName = parameters[i].Name ?? "";
                    bool looksLikeIndex = parameterName.IndexOf("index", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                          parameterName.IndexOf("idx", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                          parameterName.IndexOf("count", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                          intOrdinal == 0;
                    args[i] = looksLikeIndex ? effectIndex : 1;
                    intOrdinal++;
                }
                else if (type == typeof(float))
                {
                    args[i] = 1f;
                }
                else if (type == typeof(bool))
                {
                    args[i] = false;
                }
                else if (type == typeof(string))
                {
                    args[i] = "";
                }
                else if (type.IsEnum)
                {
                    Array values = Enum.GetValues(type);
                    args[i] = values.Length > 0 ? values.GetValue(0) : Activator.CreateInstance(type);
                }
                else
                {
                    args[i] = null;
                }
            }

            return MemberToString(method.Invoke(target, args));
        }
        catch
        {
            return "";
        }
    }

    private static int InvokeZeroArgInt(object target, string methodName, int fallback)
    {
        if (target == null)
        {
            return fallback;
        }

        try
        {
            MethodInfo method = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == 0);
            if (method == null)
            {
                return fallback;
            }

            object value = method.Invoke(target, null);
            return value is int intValue ? intValue : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static object GetMemberValue(object target, string name)
    {
        if (target == null)
        {
            return null;
        }

        Type type = target.GetType();
        PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null && property.GetIndexParameters().Length == 0)
        {
            try
            {
                return property.GetValue(target);
            }
            catch
            {
            }
        }

        FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null)
        {
            try
            {
                return field.GetValue(target);
            }
            catch
            {
            }
        }

        return null;
    }

    private static string InvokeZeroArgString(object target, string methodName)
    {
        if (target == null)
        {
            return "";
        }

        try
        {
            MethodInfo method = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == 0);
            return method == null ? "" : MemberToString(method.Invoke(target, null));
        }
        catch
        {
            return "";
        }
    }

    private static string MemberToString(object value)
    {
        return value == null ? "" : value.ToString();
    }

    private static string CleanGameText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        try
        {
            value = KeywordDatabase.Convert(value, useColor: false, useSprite: false);
        }
        catch
        {
        }

        return StripRichText(value).Replace("\r\n", "\n").Replace("\r", "\n").Trim();
    }

    private static string SummarizePassives(PresetData preset)
    {
        if (preset.passivePoints == null || preset.passivePoints.Count == 0)
        {
            return "未选择天赋";
        }

        List<string> lines = new List<string>();
        foreach (KeyValuePair<string, int> pair in preset.passivePoints.OrderByDescending(p => p.Value).ThenBy(p => p.Key))
        {
            PassiveEntity passive = FindPassive(pair.Key);
            if (passive == null)
            {
                lines.Add("未知天赋 " + pair.Key + "：Lv." + pair.Value);
                continue;
            }

            string perks = SummarizeUnlockedPassivePerks(passive, pair.Value);
            if (string.IsNullOrWhiteSpace(perks))
            {
                lines.Add(passive.aName + "：Lv." + pair.Value + "\n未解锁阶段能力");
            }
            else
            {
                lines.Add(passive.aName + "：Lv." + pair.Value + "\n" + perks);
            }
        }

        return string.Join("\n\n", lines);
    }

    private static PassiveEntity FindPassive(string id)
    {
        foreach (PassiveEntity passive in SafePassives())
        {
            if (passive != null && passive.id.ToString() == id)
            {
                return passive;
            }
        }
        return null;
    }

    private static string SummarizeUnlockedPassivePerks(PassiveEntity passive, int level)
    {
        List<string> perks = new List<string>();
        AddPassivePerk(perks, 5, passive.lv5PerkPrefab, level);
        AddPassivePerk(perks, 10, passive.lv10PerkPrefab, level);
        AddPassivePerk(perks, 20, passive.lv20PerkPrefab, level);
        return string.Join("\n", perks);
    }

    private static void AddPassivePerk(List<string> perks, int threshold, GameObject prefab, int level)
    {
        if (level < threshold || prefab == null)
        {
            return;
        }

        try
        {
            PassiveObjectMetadata metadata = prefab.GetComponent<PassiveObjectMetadata>();
            if (metadata != null)
            {
                perks.Add("Lv." + threshold + "：" + StripRichText(metadata.GetEffectString()));
                return;
            }
        }
        catch
        {
        }

        perks.Add("Lv." + threshold + "：" + prefab.name);
    }

    private static string StripRichText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return System.Text.RegularExpressions.Regex.Replace(value, "<[^>]+>", "");
    }

    private static string SummarizePassiveStats(PassiveEntity passive)
    {
        if (passive.addStats == null || passive.addStats.Length == 0)
        {
            return "";
        }

        List<string> stats = new List<string>();
        foreach (string stat in passive.addStats)
        {
            if (string.IsNullOrWhiteSpace(stat))
            {
                continue;
            }

            try
            {
                StatusInstance instance = StatusDatabase.CreateStatusEntity(stat);
                if (instance != null)
                {
                    stats.Add(KeywordDatabase.Convert(instance.ToString(reverse: false, color: false, sprite: false), useColor: false, useSprite: false));
                    continue;
                }
            }
            catch
            {
            }
            stats.Add(stat);
        }

        return string.Join("，", stats);
    }

    private static string SummarizeFruits(PresetData preset)
    {
        if (preset.fruits == null || preset.fruits.Count == 0)
        {
            return preset.fruitSkewerAdaptiveItemDropBonus >= 1 ? "自适应掉落加成：开启" : "无";
        }

        Dictionary<string, int> totals = new Dictionary<string, int>();
        foreach (FruitEntry fruit in preset.fruits)
        {
            if (string.IsNullOrWhiteSpace(fruit.category))
            {
                continue;
            }

            if (!totals.ContainsKey(fruit.category))
            {
                totals[fruit.category] = 0;
            }
            totals[fruit.category] += fruit.value;
        }

        int unitPercent = GetConstSafe("adaptiveItemDropUnitPercent", 1);
        int plusValue = GetConstSafe("fruitSkewerPlusValue", 1);
        int minusValue = GetConstSafe("fruitSkewerMinusValue", 1);
        List<string> lines = new List<string>();
        if (preset.fruitSkewerAdaptiveItemDropBonus >= 1)
        {
            lines.Add("自适应掉落加成：开启");
        }

        foreach (KeyValuePair<string, int> pair in totals.OrderBy(p => ResolveFruitCategoryName(p.Key)))
        {
            int multiplier = pair.Value >= 0 ? plusValue : minusValue;
            int percent = unitPercent * pair.Value * multiplier;
            string effect = percent <= -100 ? "禁止掉落" : (percent >= 0 ? "+" : "") + percent + "%";
            lines.Add(ResolveFruitCategoryName(pair.Key) + "：" + effect + "（" + pair.Value + "）");
        }

        return string.Join("\n", lines);
    }

    private static string ResolveFruitCategoryName(string id)
    {
        try
        {
            ItemCategoryEntity category = ItemDatabase.FindItemCategory(id);
            if (category != null)
            {
                string categoryName = category.categoryName.ToString();
                if (!string.IsNullOrWhiteSpace(categoryName))
                {
                    return categoryName;
                }
                return id;
            }
        }
        catch
        {
        }

        return id;
    }

    private static int GetConstSafe(string key, int fallback)
    {
        try
        {
            return KeywordDatabase.GetConstValue(key);
        }
        catch
        {
            return fallback;
        }
    }

    private static string GetPresetForwardKey(int slotIndex)
    {
        return "Preset_" + Mathf.Max(0, slotIndex) + "_";
    }

    private static int GetOriginalCurrentSlot()
    {
        try
        {
            UI_PresetPanel panel = TryGetPresetPanel();
            if (panel != null && panel.IsOpened)
            {
                FieldInfo field = typeof(UI_PresetPanel).GetField("currentViewSlotIndex", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                {
                    int value = (int)field.GetValue(panel);
                    if (value >= 0)
                    {
                        return value;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterPresets] Failed to inspect UI_PresetPanel: " + ex.Message);
        }

        return SaveManager.Current != null ? SaveManager.Current.GetInt("Preset_SelectedSlot", 0) : 0;
    }

    private int GetCurrentSlot()
    {
        try
        {
            UI_PresetPanel panel = hookedPanel != null ? hookedPanel : TryGetPresetPanel();
            if (panel != null && panel.IsOpened)
            {
                FieldInfo field = typeof(UI_PresetPanel).GetField("currentViewSlotIndex", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                {
                    int value = (int)field.GetValue(panel);
                    if (value >= 0)
                    {
                        return value;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterPresets] Failed to inspect UI_PresetPanel: " + ex.Message);
        }

        return SaveManager.Current != null ? SaveManager.Current.GetInt("Preset_SelectedSlot", 0) : 0;
    }

    private int GetTargetSlot()
    {
        if (targetSlotIndex < 0)
        {
            targetSlotIndex = GetCurrentSlot();
        }

        targetSlotIndex = Mathf.Clamp(targetSlotIndex, 0, 4);
        return targetSlotIndex;
    }

    private static PresetData CapturePreset(string forwardKey)
    {
        SaveData save = SaveManager.Current;
        PresetData preset = new PresetData
        {
            startingWeaponId = save.GetInt(forwardKey + "StartingWeaponID", 0),
            playerCostume = save.GetString(forwardKey + "PlayerCostume", "PinkRabbit"),
            dimensionPocketCount = save.GetInt(forwardKey + "DimensionPocketCount", 0),
            fruitSkewerAdaptiveItemDropBonus = save.GetInt(forwardKey + "FruitSkewer_AdaptiveItemDropBonus", 1),
            fruitSkewerFruitCount = save.GetInt(forwardKey + "FruitSkewer_FruitCount", 0),
            uiCachedDimensionPocketStorage = save.GetInt(forwardKey + "UICachedDimensionPocketStorage", -1),
            uiCachedFruitSkewerAdditionalCount = save.GetInt(forwardKey + "UICachedFruitSkewerAdditionalCount", -1)
        };

        preset.playerCostumeSkin = save.GetString(forwardKey + "PlayerCostume_CurrentSkin_" + preset.playerCostume, "");

        foreach (int id in SafeItemIds())
        {
            if (save.GetBool(forwardKey + "Item_Favorite_" + id, false))
            {
                preset.favoriteItemIds.Add(id);
            }
        }

        foreach (PassiveEntity passive in SafePassives())
        {
            int point = save.GetInt(forwardKey + "PassivePoint_" + passive.id, 0);
            if (point > 0)
            {
                preset.passivePoints[passive.id.ToString()] = point;
            }
        }

        for (int i = 0; i < preset.dimensionPocketCount; i++)
        {
            int entityId = save.GetInt(forwardKey + $"DimensionPocket{i}_EntityID", -1);
            if (entityId >= 0)
            {
                preset.dimensionPocketItems.Add(new PocketItem
                {
                    instanceId = save.GetInt(forwardKey + $"DimensionPocket{i}_InstanceID", -1),
                    entityId = entityId,
                    quantity = save.GetInt(forwardKey + $"DimensionPocket{i}_Quantity", 1)
                });
            }
        }

        for (int i = 0; i < preset.fruitSkewerFruitCount; i++)
        {
            string category = save.GetString(forwardKey + $"FruitSkewer_Fruit{i}_Category", "");
            if (!string.IsNullOrWhiteSpace(category))
            {
                preset.fruits.Add(new FruitEntry
                {
                    category = category,
                    value = save.GetInt(forwardKey + $"FruitSkewer_Fruit{i}_Value", 0)
                });
            }
        }

        return preset;
    }

    private static void ApplyPreset(PresetData preset, string forwardKey)
    {
        SaveData save = SaveManager.Current;
        save.SetInt(forwardKey + "StartingWeaponID", preset.startingWeaponId);
        string costume = string.IsNullOrWhiteSpace(preset.playerCostume) ? "PinkRabbit" : preset.playerCostume;
        save.SetString(forwardKey + "PlayerCostume", costume);
        save.SetString(forwardKey + "PlayerCostume_CurrentSkin_" + costume, preset.playerCostumeSkin ?? "");

        HashSet<int> favorites = new HashSet<int>(preset.favoriteItemIds ?? new List<int>());
        foreach (int id in SafeItemIds())
        {
            save.SetBool(forwardKey + "Item_Favorite_" + id, favorites.Contains(id));
        }

        Dictionary<string, int> points = preset.passivePoints ?? new Dictionary<string, int>();
        foreach (PassiveEntity passive in SafePassives())
        {
            points.TryGetValue(passive.id.ToString(), out int point);
            save.SetInt(forwardKey + "PassivePoint_" + passive.id, point);
        }

        List<PocketItem> pockets = preset.dimensionPocketItems ?? new List<PocketItem>();
        save.SetInt(forwardKey + "DimensionPocketCount", pockets.Count);
        for (int i = 0; i < pockets.Count; i++)
        {
            save.SetInt(forwardKey + $"DimensionPocket{i}_InstanceID", pockets[i].instanceId);
            save.SetInt(forwardKey + $"DimensionPocket{i}_EntityID", pockets[i].entityId);
            save.SetInt(forwardKey + $"DimensionPocket{i}_Quantity", pockets[i].quantity);
        }

        List<FruitEntry> fruits = preset.fruits ?? new List<FruitEntry>();
        save.SetInt(forwardKey + "FruitSkewer_AdaptiveItemDropBonus", preset.fruitSkewerAdaptiveItemDropBonus);
        save.SetInt(forwardKey + "FruitSkewer_FruitCount", fruits.Count);
        for (int i = 0; i < fruits.Count; i++)
        {
            save.SetString(forwardKey + $"FruitSkewer_Fruit{i}_Category", fruits[i].category ?? "");
            save.SetInt(forwardKey + $"FruitSkewer_Fruit{i}_Value", fruits[i].value);
        }

        save.SetInt(forwardKey + "UICachedDimensionPocketStorage", preset.uiCachedDimensionPocketStorage);
        save.SetInt(forwardKey + "UICachedFruitSkewerAdditionalCount", preset.uiCachedFruitSkewerAdditionalCount);
    }

    private static IEnumerable<int> SafeItemIds()
    {
        try
        {
            return ItemDatabase.GetAllItemID() ?? Array.Empty<int>();
        }
        catch
        {
            return Array.Empty<int>();
        }
    }

    private static IEnumerable<PassiveEntity> SafePassives()
    {
        try
        {
            return PassiveDatabase.GetAll() ?? Enumerable.Empty<PassiveEntity>();
        }
        catch
        {
            return Enumerable.Empty<PassiveEntity>();
        }
    }

    private void EnsureStoreLoaded()
    {
        if (!storeLoaded)
        {
            LoadStore();
        }
    }

    private void LoadStore()
    {
        try
        {
            Directory.CreateDirectory(ModFolder);
            if (TryReadStoreFile(PresetFile, out PresetStore loadedStore, out string primaryError))
            {
                store = loadedStore;
                selectedIndex = Mathf.Clamp(selectedIndex, 0, Math.Max(0, store.presets.Count - 1));
                storeRevision++;
                storeLoaded = true;
                status = "已读取 " + store.presets.Count + " 个外部预设。";
                return;
            }

            string backupFile = GetBackupFilePath(0);
            if (TryReadStoreFile(backupFile, out loadedStore, out string backupError))
            {
                store = loadedStore;
                selectedIndex = Mathf.Clamp(selectedIndex, 0, Math.Max(0, store.presets.Count - 1));
                storeRevision++;
                storeLoaded = true;
                status = "主文件读取失败，已从备份恢复 " + store.presets.Count + " 个外部预设。";
                Debug.LogWarning("[BetterPresets] Failed to read presets.json (" + primaryError + "), loaded backup instead.");
                return;
            }

            if (!File.Exists(PresetFile) && !File.Exists(backupFile))
            {
                store = new PresetStore();
                SaveStore();
                selectedIndex = 0;
                storeRevision++;
                storeLoaded = true;
                status = "已创建新的外部预设文件。";
            }
            else
            {
                store = new PresetStore();
                selectedIndex = 0;
                storeRevision++;
                storeLoaded = true;
                status = "读取失败：" + primaryError + "；备份：" + backupError;
                Debug.LogError("[BetterPresets] " + status);
            }
        }
        catch (Exception ex)
        {
            store = new PresetStore();
            storeRevision++;
            storeLoaded = true;
            status = "读取失败：" + ex.Message;
            Debug.LogError("[BetterPresets] " + status);
        }
    }

    private static bool TryReadStoreFile(string path, out PresetStore loadedStore, out string error)
    {
        loadedStore = null;
        error = "";
        try
        {
            if (!File.Exists(path))
            {
                error = "文件不存在";
                return false;
            }

            loadedStore = JsonConvert.DeserializeObject<PresetStore>(File.ReadAllText(path)) ?? new PresetStore();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private void SaveStore()
    {
        string tempFile = PresetFile + ".tmp";
        string backupFile = GetBackupFilePath(0);
        try
        {
            Directory.CreateDirectory(ModFolder);
            PruneStoreSidecarFiles();
            File.WriteAllText(tempFile, JsonConvert.SerializeObject(store, Formatting.Indented));
            if (File.Exists(PresetFile))
            {
                RotateBackupFiles();
                try
                {
                    File.Replace(tempFile, PresetFile, backupFile, ignoreMetadataErrors: true);
                }
                catch
                {
                    File.Copy(PresetFile, backupFile, overwrite: true);
                    File.Copy(tempFile, PresetFile, overwrite: true);
                    File.Delete(tempFile);
                }
            }
            else
            {
                File.Move(tempFile, PresetFile);
            }
            PruneStoreSidecarFiles();
        }
        catch (Exception ex)
        {
            status = "保存失败：" + ex.Message;
            Debug.LogError("[BetterPresets] " + status);
            try
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
            catch
            {
            }
        }
    }

    private string GetBackupFilePath(int index)
    {
        return index <= 0 ? PresetFile + ".bak" : PresetFile + ".bak." + index;
    }

    private void RotateBackupFiles()
    {
        for (int i = MaxBackupFiles - 1; i >= 1; i--)
        {
            string source = GetBackupFilePath(i - 1);
            string target = GetBackupFilePath(i);
            if (File.Exists(target))
            {
                File.Delete(target);
            }
            if (File.Exists(source))
            {
                File.Move(source, target);
            }
        }
    }

    private void PruneStoreSidecarFiles()
    {
        try
        {
            if (!Directory.Exists(ModFolder))
            {
                return;
            }

            foreach (string file in Directory.GetFiles(ModFolder, "presets.json.tmp*"))
            {
                File.Delete(file);
            }
            for (int i = MaxBackupFiles; i < MaxBackupFiles + 16; i++)
            {
                string extraBackup = GetBackupFilePath(i);
                if (File.Exists(extraBackup))
                {
                    File.Delete(extraBackup);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterPresets] Failed to prune preset backup files: " + ex.Message);
        }
    }

    private void ScheduleStoreSave()
    {
        pendingStoreSave = true;
        pendingStoreSaveTime = Time.unscaledTime + 0.05f;
    }

    private void ProcessPendingStoreSave()
    {
        if (!pendingStoreSave || Time.unscaledTime < pendingStoreSaveTime)
        {
            return;
        }

        pendingStoreSave = false;
        SaveStore();
    }

    private sealed class DetailView
    {
        public string weapon;
        public string costume;
        public string passives;
        public string favorites;
        public string pocketItems;
        public string fruits;
    }

    private sealed class CanvasSortState
    {
        public bool overrideSorting;
        public int sortingOrder;
    }

    private sealed class FavoriteHover : MonoBehaviour, IUITooltipOpener, IPointerEnterHandler, IPointerExitHandler
    {
        private BetterPresetsController owner;
        private int itemId;
        private string title;
        private string body;

        public RectTransform RectTransform { get; private set; }

        public bool Showing { get; set; }

        public UI_BaseTooltip LastTooltip { get; set; }

        public void Configure(BetterPresetsController owner, int itemId, string title, string body)
        {
            this.owner = owner;
            this.itemId = itemId;
            this.title = title;
            this.body = body;
            RectTransform = GetComponent<RectTransform>();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            owner?.ShowOriginalFavoriteTooltip(this, itemId, title, body);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            owner?.HideOriginalFavoriteTooltip(this);
        }

        private void OnDisable()
        {
            owner?.HideOriginalFavoriteTooltip(this);
        }
    }
}
