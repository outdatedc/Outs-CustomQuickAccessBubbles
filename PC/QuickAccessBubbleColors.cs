using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Alta.QuickAccessActions;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;
using TabletUILib;

[assembly: MelonInfo(typeof(QuickAccessBubbleColorsMod), "CustomQuickAccessBubbles", "1.1.6", "Copilot", "")]
[assembly: MelonOptionalDependencies("TabletUILib")]

public sealed class QuickAccessBubbleColorsMod : MelonMod
{
    private const int WindowId = 18473126;
    private const int EntriesWindowId = 18473127;
    private const int MaxProfiles = 6;
    private const int ScanFrameBudgetMilliseconds = 2;
    private const string TabletScanLabel = "Scan for Bubbles";
    private const string ShareAlphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const int ShareCodeLength = 7;
    private const string TabletOpenedStatus = "VR bubble settings opened on the Social Tablet.";
    private const string TabletClosedStatus = "VR bubble settings closed.";
    private const string BubbleSelectedStatus = "Choose separate outer, inner, and icon colors, then Apply.";
    private static readonly Color TabletScanColor = new Color(0.25f, 0.8f, 0.3f, 1f);
    private static readonly Color TabletResetColor = new Color(0.9f, 0.2f, 0.2f, 1f);
    private static QuickAccessBubbleColorsMod _instance;
    private static readonly Color UiPanelColor = new Color(0.12f, 0.11f, 0.10f, 0.98f);
    private static readonly Color UiBoxColor = new Color(0.18f, 0.16f, 0.13f, 0.98f);
    private static readonly Color UiButtonColor = new Color(0.34f, 0.25f, 0.13f, 1f);
    private static readonly Color UiButtonHoverColor = new Color(0.47f, 0.35f, 0.17f, 1f);
    private static readonly Color UiButtonActiveColor = new Color(0.56f, 0.42f, 0.21f, 1f);
    private static readonly Color UiAccentColor = new Color(0.91f, 0.71f, 0.34f, 1f);
    private static readonly Color UiTextColor = new Color(0.94f, 0.91f, 0.83f, 1f);
    private static readonly Color UiMutedTextColor = new Color(0.72f, 0.66f, 0.53f, 1f);

    private readonly Dictionary<string, BubbleSettings> _settings =
        new Dictionary<string, BubbleSettings>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, NativeBubble> _nativeBubbles = new Dictionary<int, NativeBubble>();
    private Dictionary<string, ExternalBubble> _externalBubbles =
        new Dictionary<string, ExternalBubble>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, Material> _cloneSources = new Dictionary<int, Material>();
    private readonly HashSet<int> _unsupportedMaterials = new HashSet<int>();
    private readonly Dictionary<string, Material> _tintedMaterials =
        new Dictionary<string, Material>(StringComparer.Ordinal);
    private readonly Dictionary<int, Vector3> _externalBaseScales =
        new Dictionary<int, Vector3>();
    private readonly List<QuickAccessMenuController> _pendingMenuControllers =
        new List<QuickAccessMenuController>();
    private readonly Dictionary<int, QuickAccessBubbleUiAction> _bubbleUiActions =
        new Dictionary<int, QuickAccessBubbleUiAction>();
    private readonly Dictionary<string, Func<Page>> _tabletPageFactories =
        new Dictionary<string, Func<Page>>(StringComparer.OrdinalIgnoreCase);
    private IEnumerator _bubbleScan;
    private Type _bubbleType;
    private FieldInfo _bubbleRendererField;
    private FieldInfo _activePrefabField;
    private FieldInfo _contentField;
    private FieldInfo _isBubbleSelectedField;
    private FieldInfo _selectedScaleField;
    private FieldInfo _unselectedScaleField;
    private FieldInfo _scaleAnimationSpeedField;
    private MethodInfo _scaleLerpMethod;
    private FieldInfo _quickAccessMenusField;
    private FieldInfo _actionDisplayNameField;
    private FieldInfo _actionPrefabField;
    private MethodInfo _tabletToggleMethod;
    private FieldInfo _tabletViewField;
    private FieldInfo _tabletNextField;
    private FieldInfo _tabletPrevField;
    private FieldInfo _tabletRowButtonsField;
    private FieldInfo _tabletPageField;
    private FieldInfo _tabletPageIndexField;
    private FieldInfo _tabletRowLabelsField;
    private FieldInfo _tabletCurrentField;
    private global::TouchScreenKeyboard _shareKeyboard;
    private ShareKeyboardUser _shareKeyboardUser;
    private bool _shareKeyboardOpen;
    private string _shareInput = string.Empty;
    private readonly Dictionary<int, Color> _tabletTextColors = new Dictionary<int, Color>();
    private float _nextTabletDecoration;
    private bool _tabletDecorationFailed;
    private readonly HashSet<int> _flippedTabletArrows = new HashSet<int>();
    private GameObject _modBubbleIconPrefab;
    private Rect _windowRect = new Rect(30f, 60f, 460f, 760f);
    private Rect _entriesWindowRect = new Rect(510f, 60f, 420f, 450f);
    private Vector2 _windowScroll;
    private Vector2 _selectionScroll;
    private string _selectedId;
    private string _bubbleSearchText = string.Empty;
    private string _outerHex = "#FFFFFF";
    private string _innerHex = "#FFFFFF";
    private string _iconHex = "#FFFFFF";
    private bool _applyOuterToAll;
    private bool _applyInnerToAll;
    private bool _applyIconToAll;
    private bool _applySizeToAll;
    private float _sizeDraft = 1f;
    private Color _outerDraft = Color.white;
    private Color _innerDraft = Color.white;
    private Color _iconDraft = Color.white;
    private string _status = "Choose a bubble to customize.";
    private bool _windowOpen;
    private bool _entriesWindowOpen;
    private bool _vrUiOpen;
    private bool _profileDropdownOpen;
    private int _selectedProfile = 1;
    private GUIStyle _windowStyle;
    private GUIStyle _buttonStyle;
    private GUIStyle _toggleStyle;
    private GUIStyle _textFieldStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _boxStyle;
    private GUIStyle _sliderStyle;
    private GUIStyle _sliderThumbStyle;

    public override void OnInitializeMelon()
    {
        _instance = this;
        InitializeProfiles();
        _quickAccessMenusField = typeof(QuickAccessMenuController).GetField(
            "quickAccessMenus",
            BindingFlags.Instance | BindingFlags.NonPublic);
        _actionDisplayNameField = typeof(QuickAccessMenuAction).GetField(
            "displayName",
            BindingFlags.Instance | BindingFlags.NonPublic);
        _actionPrefabField = typeof(QuickAccessMenuAction).GetField(
            "prefab",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (_quickAccessMenusField == null || _actionDisplayNameField == null ||
            _actionPrefabField == null)
            throw new MissingMemberException(
                "Could not find the quick-access members needed for the VR bubble UI.");
        _modBubbleIconPrefab = null;

        _bubbleType = AccessTools.TypeByName("Alta.QuickAccessActions.QuickAccessMenuBubble");
        if (_bubbleType == null)
            throw new TypeLoadException("Could not find Alta.QuickAccessActions.QuickAccessMenuBubble.");
        _bubbleRendererField = _bubbleType.GetField("bubbleRenderer", BindingFlags.Instance | BindingFlags.NonPublic);
        _activePrefabField = _bubbleType.GetField("activePrefab", BindingFlags.Instance | BindingFlags.NonPublic);
        _contentField = _bubbleType.GetField("content", BindingFlags.Instance | BindingFlags.NonPublic);
        _isBubbleSelectedField = _bubbleType.GetField("isBubbleSelected", BindingFlags.Instance | BindingFlags.NonPublic);
        _selectedScaleField = _bubbleType.GetField("selectedScale", BindingFlags.Instance | BindingFlags.NonPublic);
        _unselectedScaleField = _bubbleType.GetField("unselectedScale", BindingFlags.Instance | BindingFlags.NonPublic);
        _scaleAnimationSpeedField = _bubbleType.GetField("scaleAnimationSpeed", BindingFlags.Instance | BindingFlags.NonPublic);
        if (_bubbleRendererField == null || _activePrefabField == null || _contentField == null ||
            _isBubbleSelectedField == null || _selectedScaleField == null ||
            _unselectedScaleField == null || _scaleAnimationSpeedField == null)
            throw new MissingFieldException("QuickAccessMenuBubble is missing an expected renderer, prefab, content, selection, or scale field.");

        Type scaleType = AccessTools.TypeByName("ScaleLerpPooledObject");
        _scaleLerpMethod = scaleType == null ? null : AccessTools.Method(scaleType, "Lerp");
        if (_scaleLerpMethod == null)
            throw new MissingMethodException("Could not find ScaleLerpPooledObject.Lerp.");

        MethodInfo updateContent = AccessTools.Method(_bubbleType, "UpdateContent");
        MethodInfo updateActive = AccessTools.Method(_bubbleType, "UpdateActive");
        MethodInfo markSelected = AccessTools.Method(_bubbleType, "MarkSelected");
        MethodInfo unmarkSelected = AccessTools.Method(_bubbleType, "UnmarkSelected");
        MethodInfo contentPostfix = typeof(BubbleHooks).GetMethod(
            "UpdateContentPostfix",
            BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo activePostfix = typeof(BubbleHooks).GetMethod(
            "UpdateActivePostfix",
            BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo controllerInitialized = AccessTools.Method(
            typeof(QuickAccessMenuController),
            "InitializeControllerExtension");
        MethodInfo controllerPostfix = typeof(BubbleHooks).GetMethod(
            "ControllerInitializedPostfix",
            BindingFlags.Static | BindingFlags.NonPublic);
        if (updateContent == null || updateActive == null || markSelected == null ||
            unmarkSelected == null || contentPostfix == null || activePostfix == null ||
            controllerInitialized == null || controllerPostfix == null)
            throw new MissingMethodException("Could not install quick-access bubble color hooks.");

        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("com.township.quickaccessbubblecolors");
        harmony.Patch(updateContent, null, new HarmonyMethod(contentPostfix), null, null, null);
        harmony.Patch(updateActive, null, new HarmonyMethod(activePostfix), null, null, null);
        harmony.Patch(markSelected, null, new HarmonyMethod(activePostfix), null, null, null);
        harmony.Patch(unmarkSelected, null, new HarmonyMethod(activePostfix), null, null, null);
        harmony.Patch(
            controllerInitialized,
            null,
            new HarmonyMethod(controllerPostfix),
            null,
            null,
            null);

        Type tavernFunColorPatch = AccessTools.TypeByName("QuickAccessMenuBubble_ColorPatch");
        MethodInfo tavernFunApplyColors = tavernFunColorPatch == null
            ? null
            : AccessTools.Method(tavernFunColorPatch, "ApplyColors");
        MethodInfo tavernFunPostfix = typeof(BubbleHooks).GetMethod(
            "TavernFunApplyColorsPostfix",
            BindingFlags.Static | BindingFlags.NonPublic);
        if (tavernFunApplyColors != null && tavernFunPostfix != null)
        {
            harmony.Patch(
                tavernFunApplyColors,
                null,
                new HarmonyMethod(tavernFunPostfix),
                null,
                null,
                null);
            LoggerInstance.Msg("TavernFun bubble colors detected; custom colors will be reapplied after TavernFun.");
        }
        LoggerInstance.Msg(
            "CustomQuickAccessBubbles initialized. Press F6 to open desktop settings, F8 to scan, " +
            "or use the Customize Bubbles entry to open settings on the Social Tablet.");
    }

    public override void OnUpdate()
    {
        ProcessPendingMenuControllers();
        ProcessBubbleScan();
        if (IsShowingTabletPage())
            _vrUiOpen = true;
        else if (_vrUiOpen && !TabletUI.IsOpen)
            _vrUiOpen = false;
        if (_shareKeyboardOpen && !IsShowingSharePage())
            CloseShareKeyboard();
        if (_vrUiOpen && !_tabletDecorationFailed && Time.unscaledTime >= _nextTabletDecoration)
        {
            _nextTabletDecoration = Time.unscaledTime + 0.1f;
            try
            {
                UpdateTabletDecorations();
            }
            catch (Exception exception)
            {
                _tabletDecorationFailed = true;
                LoggerInstance.Warning("Could not adjust the Social Tablet buttons: " + exception.Message);
            }
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.f6Key.wasPressedThisFrame)
            _windowOpen = !_windowOpen;
        if (keyboard.f8Key.wasPressedThisFrame)
            CheckForBubbles();
    }

    private GameObject GetCustomizeIconPrefab()
    {
        try
        {
            byte[] imageBytes;
            using (Stream stream = typeof(QuickAccessBubbleColorsMod).Assembly
                .GetManifestResourceStream("CustomQuickAccessBubbles.CustomizeB.png"))
            {
                if (stream == null)
                    throw new InvalidOperationException("The embedded CustomizeB.png resource is missing.");
                imageBytes = new byte[stream.Length];
                int read = 0;
                while (read < imageBytes.Length)
                {
                    int count = stream.Read(imageBytes, read, imageBytes.Length - read);
                    if (count <= 0)
                        throw new EndOfStreamException("The embedded CustomizeB.png resource is incomplete.");
                    read += count;
                }
            }

            Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, true);
            texture.name = "CustomizeB";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            if (!ImageConversion.LoadImage(texture, imageBytes))
                throw new InvalidOperationException("The embedded CustomizeB.png image could not be decoded.");
            UnityEngine.Object.DontDestroyOnLoad(texture);

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                throw new InvalidOperationException("The Sprites/Default shader was not found.");
            Material material = new Material(shader);
            material.mainTexture = texture;
            UnityEngine.Object.DontDestroyOnLoad(material);

            // Mirrors ModBubbles' icon template: a hidden object with a front and a back quad.
            GameObject icon = new GameObject("CustomizeBubbleIcon");
            icon.transform.position = new Vector3(0f, -9999f, 0f);
            AddCustomizeIconQuad(icon, material, Quaternion.identity);
            AddCustomizeIconQuad(icon, material, Quaternion.Euler(0f, 180f, 0f));
            UnityEngine.Object.DontDestroyOnLoad(icon);
            return icon;
        }
        catch (Exception exception)
        {
            LoggerInstance.Warning(
                "Could not build the Customize bubble icon; the settings bubble may have no icon: " +
                exception.Message);
            return null;
        }
    }

    private static void AddCustomizeIconQuad(GameObject parent, Material material, Quaternion rotation)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "face";
        Collider collider = quad.GetComponent<Collider>();
        if (collider != null)
            UnityEngine.Object.Destroy(collider);
        quad.transform.SetParent(parent.transform, false);
        quad.transform.localRotation = rotation;
        quad.transform.localScale = new Vector3(0.12f, 0.12f, 0.12f);
        quad.GetComponent<Renderer>().sharedMaterial = material;
    }

    private void QueueMenuController(object controllerObject)
    {
        QuickAccessMenuController controller = controllerObject as QuickAccessMenuController;
        if (controller != null && !_pendingMenuControllers.Contains(controller))
            _pendingMenuControllers.Add(controller);
    }

    private void ProcessPendingMenuControllers()
    {
        for (int i = _pendingMenuControllers.Count - 1; i >= 0; i--)
        {
            QuickAccessMenuController menu = _pendingMenuControllers[i];
            if (menu == null)
            {
                _pendingMenuControllers.RemoveAt(i);
                continue;
            }

            ControllerExtension extension = menu as ControllerExtension;
            Controller controller = extension == null ? null : extension.Controller;
            if (controller == null || controller.PlayerController == null)
                continue;
            if (!controller.PlayerController.IsLocalPlayer)
            {
                _pendingMenuControllers.RemoveAt(i);
                continue;
            }

            Player player = controller.PlayerController.NetworkPlayer;
            if (player == null)
                continue;

            int controllerId = menu.GetInstanceID();
            QuickAccessBubbleUiAction action;
            if (!_bubbleUiActions.TryGetValue(controllerId, out action))
            {
                action = ScriptableObject.CreateInstance<QuickAccessBubbleUiAction>();
                action.name = "Custom Quick Access Bubbles";
                _actionDisplayNameField.SetValue(action, "Customize Bubbles");
                if (_modBubbleIconPrefab == null)
                    _modBubbleIconPrefab = GetCustomizeIconPrefab();
                if (_modBubbleIconPrefab != null)
                    _actionPrefabField.SetValue(action, _modBubbleIconPrefab);
                action.InitializeForPlayer(player, false);
                _bubbleUiActions.Add(controllerId, action);
            }

            QuickAccessMenuAction[] actions =
                _quickAccessMenusField.GetValue(menu) as QuickAccessMenuAction[];
            if (actions == null)
                actions = new QuickAccessMenuAction[0];
            if (Array.IndexOf(actions, action) < 0)
            {
                QuickAccessMenuAction[] updated =
                    new QuickAccessMenuAction[actions.Length + 1];
                Array.Copy(actions, updated, actions.Length);
                updated[actions.Length] = action;
                _quickAccessMenusField.SetValue(menu, updated);
            }

            _pendingMenuControllers.RemoveAt(i);
        }
    }

    internal static void RunVrUiAction(Controller controller)
    {
        if (_instance != null)
            _instance.ToggleVrUi(controller);
    }

    internal static bool VrUiOpen
    {
        get { return _instance != null && _instance._vrUiOpen; }
    }

    private void ToggleVrUi(Controller controller)
    {
        if (controller == null)
        {
            LoggerInstance.Warning("The VR settings bubble ran without a controller.");
            return;
        }

        if (_vrUiOpen && IsShowingTabletPage())
        {
            CloseVrUi();
            return;
        }

        try
        {
            Func<Page> homePage = GetTabletPageFactory("home", BuildTabletHomePage);
            if (_tabletToggleMethod == null)
                _tabletToggleMethod = AccessTools.Method(typeof(TabletUI), "Toggle");
            if (_tabletToggleMethod == null)
                throw new MissingMethodException("TabletUILib.TabletUI.Toggle was not found.");

            if (string.IsNullOrEmpty(_selectedId))
            {
                List<string> ids = GetSortedIds();
                if (ids.Count > 0)
                    SelectBubble(ids[0]);
            }
            _tabletToggleMethod.Invoke(null, new object[] { controller, homePage });
            _vrUiOpen = true;
            _status = TabletOpenedStatus;
            LoggerInstance.Msg("Opened CustomQuickAccessBubbles on the native Social Tablet.");
        }
        catch (Exception exception)
        {
            _vrUiOpen = false;
            _status = "Could not open Social Tablet settings: " + exception.Message;
            LoggerInstance.Error(_status + Environment.NewLine + exception);
        }
    }

    private Func<Page> GetTabletPageFactory(string key, Func<Page> createPage)
    {
        Func<Page> pageFactory;
        if (!_tabletPageFactories.TryGetValue(key, out pageFactory))
        {
            pageFactory = createPage;
            _tabletPageFactories.Add(key, pageFactory);
        }
        return pageFactory;
    }

    private void CloseVrUi()
    {
        if (_vrUiOpen && IsShowingTabletPage())
            TabletUI.Close();
        _vrUiOpen = false;
        _status = TabletClosedStatus;
    }

    private bool IsShowingTabletPage()
    {
        Func<Page> homePage;
        return _tabletPageFactories.TryGetValue("home", out homePage) && TabletUI.IsShowing(homePage);
    }

    private Page BuildTabletHomePage()
    {
        Page page = new Page("Quick Access Bubbles", TabletStatus());
        page.Link("Customizable bubbles", GetTabletPageFactory("bubbles", BuildTabletBubblesPage));
        page.Link("Profiles", GetTabletPageFactory("profiles", BuildTabletProfilesPage));
        page.Link("Reset options", GetTabletPageFactory("reset", BuildTabletResetPage));
        page.Button("Save settings", delegate
        {
            _status = SaveSettings() ? "Saved bubble settings." : "Could not save settings. Check the MelonLoader log.";
            TabletUI.Refresh();
        });
        return page;
    }

    private Page BuildTabletBubblesPage()
    {
        Page page = new Page("Customizable Bubbles", TabletStatus());
        page.Footer = "Select a bubble to customize";
        page.Button(TabletScanLabel, delegate
        {
            CheckForBubbles();
            TabletUI.Refresh();
        });
        List<string> ids = GetSortedIds();
        if (ids.Count == 0)
        {
            page.Text("No bubbles found", "Open the quick-access wheel or scan for bubbles.");
        }
        else
        {
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                BubbleSettings settings = GetSettings(id);
                page.Link(
                    settings.Label,
                    GetTabletPageFactory("bubble:" + id, delegate
                    {
                        return BuildTabletBubblePage(id);
                    }));
            }
        }

        page.Back = delegate { TabletUI.Back(); };
        return page;
    }

    private string TabletStatus()
    {
        return _status == TabletOpenedStatus || _status == BubbleSelectedStatus || _status == TabletClosedStatus
            ? string.Empty
            : _status;
    }

    private object GetTabletView()
    {
        if (_tabletViewField == null)
            _tabletViewField = AccessTools.Field(typeof(TabletUI), "view");
        object view = _tabletViewField == null ? null : _tabletViewField.GetValue(null);
        if (view == null)
            return null;

        if (_tabletNextField == null)
        {
            Type viewType = view.GetType();
            _tabletNextField = AccessTools.Field(viewType, "next");
            _tabletPrevField = AccessTools.Field(viewType, "prev");
            _tabletRowButtonsField = AccessTools.Field(viewType, "rowButtons");
            _tabletPageField = AccessTools.Field(viewType, "page");
            _tabletPageIndexField = AccessTools.Field(viewType, "pageIndex");
            _tabletRowLabelsField = AccessTools.Field(viewType, "rowLabels");
        }
        if (_tabletNextField == null || _tabletPrevField == null || _tabletRowButtonsField == null ||
            _tabletPageField == null || _tabletPageIndexField == null)
            return null;
        return view;
    }

    private void UpdateTabletDecorations()
    {
        object view = GetTabletView();
        if (view == null)
            return;

        // The library copies the game's forward arrow, which comes out pointing the wrong way.
        // Roll it half a turn around the screen's own normal, derived from where the buttons sit.
        Component next = _tabletNextField.GetValue(view) as Component;
        Component previous = _tabletPrevField == null ? null : _tabletPrevField.GetValue(view) as Component;
        Component[] buttons = _tabletRowButtonsField.GetValue(view) as Component[];
        if (next != null && previous != null && buttons != null && buttons.Length > 1 &&
            buttons[0] != null && buttons[buttons.Length - 1] != null &&
            !_flippedTabletArrows.Contains(next.gameObject.GetInstanceID()))
        {
            Vector3 across = next.transform.position - previous.transform.position;
            Vector3 down = buttons[buttons.Length - 1].transform.position - buttons[0].transform.position;
            Vector3 normal = Vector3.Cross(across, down);
            if (normal.sqrMagnitude > 1e-10f)
            {
                _flippedTabletArrows.Add(next.gameObject.GetInstanceID());
                next.transform.rotation =
                    Quaternion.AngleAxis(180f, normal.normalized) * next.transform.rotation;
            }
        }

        Page page = _tabletPageField.GetValue(view) as Page;
        Component[] rowButtons = _tabletRowButtonsField.GetValue(view) as Component[];
        if (page == null || rowButtons == null)
            return;

        TextRenderer[] rowLabels = _tabletRowLabelsField == null
            ? null
            : _tabletRowLabelsField.GetValue(view) as TextRenderer[];
        int firstRow = (int)_tabletPageIndexField.GetValue(view) * rowButtons.Length;
        for (int i = 0; i < rowButtons.Length; i++)
        {
            if (rowButtons[i] == null)
                continue;

            int rowIndex = firstRow + i;
            string label = rowIndex < page.Rows.Count ? page.Rows[rowIndex].Label : null;
            Color? color = null;
            if (label == TabletScanLabel)
                color = TabletScanColor;
            else if (label != null && label.StartsWith("Reset", StringComparison.Ordinal))
                color = TabletResetColor;

            if (rowLabels != null && i < rowLabels.Length)
                SetTabletTextColor(rowLabels[i], color);
            TextRenderer[] buttonTexts = rowButtons[i].GetComponentsInChildren<TextRenderer>(true);
            for (int t = 0; t < buttonTexts.Length; t++)
                SetTabletTextColor(buttonTexts[t], color);
        }
    }

    private void SetTabletTextColor(TextRenderer text, Color? color)
    {
        if (text == null)
            return;

        int id = text.GetInstanceID();
        Color original;
        if (!_tabletTextColors.TryGetValue(id, out original))
        {
            original = text.Color;
            _tabletTextColors.Add(id, original);
        }

        Color wanted = color ?? original;
        if (text.Color != wanted)
            text.Color = wanted;
    }

    private Page BuildTabletSharePage(string id)
    {
        BubbleSettings settings = GetSettings(id);
        Page page = new Page("Share Colors", TabletStatus());
        page.Footer = settings.Label;
        page.Text("Your code", GenerateShareCode(settings));
        page.Button("Entered code", _shareInput.Length == 0 ? "-" : _shareInput, delegate
        {
            OpenShareKeyboard();
            TabletUI.Refresh();
        });
        page.Button("Apply code to this bubble", delegate
        {
            ApplyShareCode(id, false);
            TabletUI.Refresh();
        });
        page.Button("Apply code to all bubbles", delegate
        {
            ApplyShareCode(id, true);
            TabletUI.Refresh();
        });
        page.Back = delegate { TabletUI.Back(); };
        return page;
    }

    // Seven base-36 characters hold 36 bits: the outer, inner and icon colors at 4 bits per channel.
    // A part that is not customized is stored as white and comes back switched off.
    private static string GenerateShareCode(BubbleSettings settings)
    {
        ulong value = PackShareColor(settings.OuterEnabled ? settings.OuterColor : Color.white);
        value = (value << 12) | PackShareColor(settings.InnerEnabled ? settings.InnerColor : Color.white);
        value = (value << 12) | PackShareColor(settings.IconEnabled ? settings.IconColor : Color.white);

        char[] code = new char[ShareCodeLength];
        for (int i = ShareCodeLength - 1; i >= 0; i--)
        {
            code[i] = ShareAlphabet[(int)(value % 36UL)];
            value /= 36UL;
        }
        return new string(code);
    }

    private static ulong PackShareColor(Color color)
    {
        int red = Mathf.RoundToInt(Mathf.Clamp01(color.r) * 15f);
        int green = Mathf.RoundToInt(Mathf.Clamp01(color.g) * 15f);
        int blue = Mathf.RoundToInt(Mathf.Clamp01(color.b) * 15f);
        return (ulong)((red << 8) | (green << 4) | blue);
    }

    private static bool TryDecodeShareCode(string code, out Color[] colors, out bool[] enabled)
    {
        colors = new Color[3];
        enabled = new bool[3];
        if (code == null || code.Length != ShareCodeLength)
            return false;

        ulong value = 0;
        string upper = code.ToUpperInvariant();
        for (int i = 0; i < upper.Length; i++)
        {
            int digit = ShareAlphabet.IndexOf(upper[i]);
            if (digit < 0)
                return false;
            value = value * 36UL + (ulong)digit;
        }
        if (value >= (1UL << 36))
            return false;

        for (int part = 2; part >= 0; part--)
        {
            int packed = (int)(value & 0xFFFUL);
            value >>= 12;
            colors[part] = new Color(
                ((packed >> 8) & 0xF) / 15f,
                ((packed >> 4) & 0xF) / 15f,
                (packed & 0xF) / 15f,
                1f);
            enabled[part] = packed != 0xFFF;
        }
        return true;
    }

    private static string CleanShareCode(string input)
    {
        StringBuilder clean = new StringBuilder(ShareCodeLength);
        if (input != null)
        {
            for (int i = 0; i < input.Length && clean.Length < ShareCodeLength; i++)
            {
                char c = char.ToUpperInvariant(input[i]);
                if (ShareAlphabet.IndexOf(c) >= 0)
                    clean.Append(c);
            }
        }
        return clean.ToString();
    }

    private void ApplyShareCode(string id, bool applyToAll)
    {
        Color[] colors;
        bool[] enabled;
        if (!TryDecodeShareCode(_shareInput, out colors, out enabled))
        {
            _status = "That isn't a valid code. Enter all " + ShareCodeLength + " letters and numbers.";
            return;
        }

        List<BubbleSettings> targets = applyToAll
            ? new List<BubbleSettings>(_settings.Values)
            : new List<BubbleSettings> { GetSettings(id) };
        BubbleColorPart[] parts = { BubbleColorPart.Outer, BubbleColorPart.Inner, BubbleColorPart.Icon };
        for (int i = 0; i < targets.Count; i++)
        {
            for (int part = 0; part < parts.Length; part++)
            {
                SetEnabled(targets[i], parts[part], enabled[part]);
                if (enabled[part])
                    SetColor(targets[i], parts[part], colors[part]);
            }
            ApplyProfile(targets[i].Id);
        }

        SelectBubble(id);
        _status = SaveSettings()
            ? "Applied code " + _shareInput + (applyToAll ? " to all bubbles." : " to this bubble.")
            : "Code applied, but settings could not be saved. Check the MelonLoader log.";
    }

    private Func<Page> GetCurrentTabletPage()
    {
        if (_tabletCurrentField == null)
            _tabletCurrentField = AccessTools.Field(typeof(TabletUI), "current");
        return _tabletCurrentField == null ? null : _tabletCurrentField.GetValue(null) as Func<Page>;
    }

    private bool IsShowingSharePage()
    {
        if (!TabletUI.IsOpen)
            return false;

        Func<Page> current = GetCurrentTabletPage();
        if (current == null)
            return false;
        foreach (KeyValuePair<string, Func<Page>> entry in _tabletPageFactories)
        {
            if (entry.Key.StartsWith("share:", StringComparison.Ordinal) && entry.Value == current)
                return true;
        }
        return false;
    }

    private static Camera GetViewCamera()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera != null && mainCamera.isActiveAndEnabled)
            return mainCamera;

        Camera[] cameras = Camera.allCameras;
        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] != null && cameras[i].isActiveAndEnabled)
                return cameras[i];
        }
        return null;
    }

    private bool TryGetKeyboardPose(out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        object view = GetTabletView();
        Camera camera = GetViewCamera();
        if (view == null || camera == null)
            return false;

        Component[] rows = _tabletRowButtonsField.GetValue(view) as Component[];
        Component next = _tabletNextField.GetValue(view) as Component;
        Component previous = _tabletPrevField.GetValue(view) as Component;
        if (rows == null || rows.Length < 2 || rows[0] == null || rows[rows.Length - 1] == null ||
            next == null || previous == null)
            return false;

        // Work out the screen's axes from where its buttons sit, then park the keyboard below it.
        Vector3 top = rows[0].transform.position;
        Vector3 bottom = rows[rows.Length - 1].transform.position;
        Vector3 down = bottom - top;
        float spacing = down.magnitude / (rows.Length - 1);
        Vector3 across = next.transform.position - previous.transform.position;
        Vector3 normal = Vector3.Cross(across, down);
        if (spacing < 1e-5f || normal.sqrMagnitude < 1e-10f)
            return false;

        down.Normalize();
        normal.Normalize();
        if (Vector3.Dot(normal, camera.transform.position - bottom) < 0f)
            normal = -normal;
        position = bottom + down * (spacing * 2.5f + 0.15f) + normal * 0.05f;
        rotation = Quaternion.LookRotation(-normal, Vector3.up) * Quaternion.Euler(30f, 0f, 0f);
        return true;
    }

    private static global::TouchScreenKeyboard FindKeyboardPrefab()
    {
        // The server search field holds the keyboard prefab the game spawns on demand.
        FieldInfo prefabField = AccessTools.Field(typeof(Alta.Meta.UI.InputFieldWithKeyboard), "keyboardPrefab");
        Alta.Meta.UI.InputFieldWithKeyboard[] fields =
            Resources.FindObjectsOfTypeAll<Alta.Meta.UI.InputFieldWithKeyboard>();
        for (int i = 0; prefabField != null && i < fields.Length; i++)
        {
            global::TouchScreenKeyboard prefab = prefabField.GetValue(fields[i]) as global::TouchScreenKeyboard;
            if (prefab != null)
                return prefab;
        }

        global::TouchScreenKeyboard[] keyboards = Resources.FindObjectsOfTypeAll<global::TouchScreenKeyboard>();
        for (int i = 0; i < keyboards.Length; i++)
        {
            if (keyboards[i] != null && !keyboards[i].gameObject.scene.IsValid())
                return keyboards[i];
        }
        return null;
    }

    private void OpenShareKeyboard()
    {
        try
        {
            global::TouchScreenKeyboard keyboard = global::TouchScreenKeyboard.Instance;
            if (keyboard == null)
            {
                global::TouchScreenKeyboard prefab = FindKeyboardPrefab();
                if (prefab == null)
                {
                    _status = "The game's keyboard isn't loaded yet. Open the server search once, then try again.";
                    LoggerInstance.Warning(_status);
                    return;
                }
                UnityEngine.Object.Instantiate(prefab);
                keyboard = global::TouchScreenKeyboard.Instance;
                if (keyboard == null)
                {
                    _status = "Could not start the game's keyboard.";
                    LoggerInstance.Warning(_status);
                    return;
                }
                LoggerInstance.Msg("Spawned the game's keyboard from '" + prefab.name + "'.");
            }

            Vector3 position;
            Quaternion rotation;
            if (!TryGetKeyboardPose(out position, out rotation))
            {
                Camera camera = GetViewCamera();
                if (camera == null)
                {
                    _status = "Could not place the keyboard: no camera was found.";
                    LoggerInstance.Warning(_status);
                    return;
                }

                LoggerInstance.Warning("Could not read the tablet's layout; placing the keyboard in front of you.");
                Vector3 flatForward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
                if (flatForward.sqrMagnitude < 1e-4f)
                    flatForward = camera.transform.forward;
                flatForward.Normalize();
                position = camera.transform.position + flatForward * 0.55f + Vector3.down * 0.35f;
                rotation = Quaternion.LookRotation(flatForward, Vector3.up) * Quaternion.Euler(30f, 0f, 0f);
            }

            if (_shareKeyboardUser == null)
                _shareKeyboardUser = new ShareKeyboardUser();
            keyboard.setupSecretKey = false;
            keyboard = global::TouchScreenKeyboard.Get(position, rotation, _shareKeyboardUser);
            if (keyboard == null)
            {
                _status = "The game's keyboard did not open.";
                LoggerInstance.Warning(_status);
                return;
            }
            if (!keyboard.gameObject.activeSelf)
                keyboard.gameObject.SetActive(true);
            keyboard.EventSetUpNewKeyboard(0);
            keyboard.Input = _shareInput;

            Renderer[] renderers = keyboard.gameObject.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    bounds.Encapsulate(renderers[i].bounds);
                keyboard.transform.position = position + (position - bounds.center);
            }

            Camera viewCamera = GetViewCamera();
            LoggerInstance.Msg(
                "Opened the game's keyboard at " + keyboard.transform.position + " (" + renderers.Length +
                " renderers, scale " + keyboard.transform.lossyScale + ", " +
                (viewCamera == null
                    ? "no camera"
                    : Vector3.Distance(viewCamera.transform.position, keyboard.transform.position).ToString("0.00") +
                        " m from the camera") + ").");
            _shareKeyboard = keyboard;
            _shareKeyboardOpen = true;
            _status = "Type the " + ShareCodeLength + "-character code on the keyboard.";
        }
        catch (Exception exception)
        {
            _shareKeyboardOpen = false;
            _status = "Could not open the keyboard: " + exception.Message;
            LoggerInstance.Error(_status + Environment.NewLine + exception);
        }
    }

    private void CloseShareKeyboard()
    {
        _shareKeyboardOpen = false;
        if (_shareKeyboard != null && _shareKeyboardUser != null &&
            _shareKeyboard.CurrentUser == _shareKeyboardUser)
            _shareKeyboard.Hide(_shareKeyboardUser);
    }

    internal static void ShareKeyboardInput(string input)
    {
        QuickAccessBubbleColorsMod owner = _instance;
        if (owner == null)
            return;

        string clean = CleanShareCode(input);
        if (owner._shareKeyboard != null && clean != input)
            owner._shareKeyboard.Input = clean;
        if (clean == owner._shareInput)
            return;
        owner._shareInput = clean;
        TabletUI.Refresh();
    }

    internal static void ShareKeyboardSubmit()
    {
        QuickAccessBubbleColorsMod owner = _instance;
        if (owner == null)
            return;

        owner.CloseShareKeyboard();
        owner._status = "Code entered. Choose where to apply it.";
        TabletUI.Refresh();
    }

    internal static void ShareKeyboardLost()
    {
        if (_instance != null)
            _instance._shareKeyboardOpen = false;
    }

    private Page BuildTabletBubblePage(string id)
    {
        SelectBubble(id);
        BubbleSettings settings = GetSettings(id);
        Page page = new Page(settings.Label, TabletStatus());
        page.Footer = "Choose a setting";
        page.Link(
            "Outer bubble",
            GetTabletPageFactory("outer:" + id, delegate
            {
                return BuildTabletColorPage(id, BubbleColorPart.Outer);
            }));
        page.Link(
            "Inner bubble",
            GetTabletPageFactory("inner:" + id, delegate
            {
                return BuildTabletColorPage(id, BubbleColorPart.Inner);
            }));
        page.Link(
            "Icon color",
            GetTabletPageFactory("icon:" + id, delegate
            {
                return BuildTabletColorPage(id, BubbleColorPart.Icon);
            }));
        page.Link(
            "Bubble size",
            GetTabletPageFactory("size:" + id, delegate
            {
                return BuildTabletSizePage(id);
            }));
        page.Link(
            "Share colors",
            GetTabletPageFactory("share:" + id, delegate
            {
                return BuildTabletSharePage(id);
            }));
        page.Button("Reset this bubble", delegate
        {
            ResetBubbleSettings(settings);
            TabletUI.Refresh();
        });
        page.Back = delegate { TabletUI.Back(); };
        return page;
    }

    private Page BuildTabletColorPage(string id, BubbleColorPart part)
    {
        BubbleSettings settings = GetSettings(id);
        string title = GetPartName(part);
        Page page = new Page(title, TabletStatus());
        page.Footer = settings.Label;
        page.Toggle(
            "Customize " + title,
            delegate { return GetEnabled(settings, part); },
            delegate(bool enabled)
            {
                SetEnabled(settings, part, enabled);
                ApplyProfile(id);
                if (!SaveSettings())
                    _status = "Color updated, but settings could not be saved. Check the MelonLoader log.";
                TabletUI.Refresh();
            });
        page.Slider("Red", delegate { return GetColor(settings, part).r * 255f; },
            delegate(float value) { SetTabletColorChannel(id, part, 0, value); }, 0f, 255f, 10f);
        page.Slider("Green", delegate { return GetColor(settings, part).g * 255f; },
            delegate(float value) { SetTabletColorChannel(id, part, 1, value); }, 0f, 255f, 10f);
        page.Slider("Blue", delegate { return GetColor(settings, part).b * 255f; },
            delegate(float value) { SetTabletColorChannel(id, part, 2, value); }, 0f, 255f, 10f);
        page.Toggle(
            "Apply color to all",
            delegate { return GetApplyToAll(part); },
            delegate(bool enabled) { SetApplyToAll(part, enabled); TabletUI.Refresh(); });
        page.Button("Apply color", delegate
        {
            if (GetApplyToAll(part))
                ApplyColorToAll(settings, part, GetColor(settings, part), GetEnabled(settings, part));
            else
            {
                ApplyProfile(id);
                _status = SaveSettings()
                    ? title + " color applied."
                    : "Color applied, but settings could not be saved. Check the MelonLoader log.";
            }
            TabletUI.Refresh();
        });
        page.Back = delegate { TabletUI.Back(); };
        return page;
    }

    private void SetTabletColorChannel(string id, BubbleColorPart part, int channel, float value)
    {
        BubbleSettings settings = GetSettings(id);
        Color color = GetColor(settings, part);
        float normalized = Mathf.RoundToInt(value) / 255f;
        if (channel == 0)
            color.r = normalized;
        else if (channel == 1)
            color.g = normalized;
        else
            color.b = normalized;
        color.a = 1f;
        SetColor(settings, part, color);
        SetDraftColor(part, color);
        SetDraftHex(part, "#" + ColorUtility.ToHtmlStringRGB(color));
        ApplyProfile(id);
        if (!SaveSettings())
            _status = "Color updated, but settings could not be saved. Check the MelonLoader log.";
    }

    private Page BuildTabletSizePage(string id)
    {
        BubbleSettings settings = GetSettings(id);
        Page page = new Page("Bubble Size", TabletStatus());
        page.Footer = settings.Label;
        page.Slider(
            "Size (%)",
            delegate { return settings.SizeMultiplier * 100f; },
            delegate(float value)
            {
                settings.SizeMultiplier = value / 100f;
                ApplyProfile(id);
            },
            1f,
            300f,
            10f);
        page.Toggle(
            "Apply size to all",
            delegate { return _applySizeToAll; },
            delegate(bool enabled) { _applySizeToAll = enabled; TabletUI.Refresh(); });
        page.Button("Apply size", delegate
        {
            if (_applySizeToAll)
            {
                foreach (BubbleSettings target in _settings.Values)
                {
                    target.SizeMultiplier = settings.SizeMultiplier;
                    ApplyProfile(target.Id);
                }
                _status = "Bubble size applied to all bubble entries.";
            }
            else
            {
                ApplyProfile(id);
                _status = "Bubble size applied.";
            }
            bool saved = SaveSettings();
            if (!saved)
                _status = "Size applied, but settings could not be saved. Check the MelonLoader log.";
            TabletUI.Refresh();
        });
        page.Back = delegate { TabletUI.Back(); };
        return page;
    }

    private Page BuildTabletProfilesPage()
    {
        Page page = new Page("Profiles", TabletStatus());
        page.Choice(
            "Profile",
            Enumerable.Range(1, MaxProfiles).Select(value => "Profile " + value).ToArray(),
            delegate { return _selectedProfile - 1; },
            delegate(int value)
            {
                if (value >= 0 && value < MaxProfiles)
                    _selectedProfile = value + 1;
                TabletUI.Refresh();
            });
        page.Button("Save profile", delegate
        {
            _status = SaveSettings(GetProfilePath(_selectedProfile))
                ? "Saved Profile " + _selectedProfile + "."
                : "Could not save the selected profile. Check the MelonLoader log.";
            TabletUI.Refresh();
        });
        page.Button("Apply profile", delegate { ApplySelectedProfile(); TabletUI.Refresh(); });
        page.Back = delegate { TabletUI.Back(); };
        return page;
    }

    private Page BuildTabletResetPage()
    {
        Page page = new Page("Reset", TabletStatus());
        page.Button("Reset all colors", delegate { ResetAllSettings(); TabletUI.Refresh(); });
        page.Button("Reset all sizes", delegate { ResetAllSizes(); TabletUI.Refresh(); });
        page.Back = delegate { TabletUI.Back(); };
        return page;
    }

    private static Color GetColor(BubbleSettings settings, BubbleColorPart part)
    {
        switch (part)
        {
            case BubbleColorPart.Outer:
                return settings.OuterColor;
            case BubbleColorPart.Inner:
                return settings.InnerColor;
            default:
                return settings.IconColor;
        }
    }

    public override void OnGUI()
    {
        if (_windowOpen)
        {
            EnsureUiStyles();
            GUISkin skin = GUI.skin;
            GUIStyle originalWindow = skin.window;
            GUIStyle originalButton = skin.button;
            GUIStyle originalToggle = skin.toggle;
            GUIStyle originalTextField = skin.textField;
            GUIStyle originalLabel = skin.label;
            GUIStyle originalBox = skin.box;
            GUIStyle originalSlider = skin.horizontalSlider;
            GUIStyle originalSliderThumb = skin.horizontalSliderThumb;
            try
            {
                skin.window = _windowStyle;
                skin.button = _buttonStyle;
                skin.toggle = _toggleStyle;
                skin.textField = _textFieldStyle;
                skin.label = _labelStyle;
                skin.box = _boxStyle;
                skin.horizontalSlider = _sliderStyle;
                skin.horizontalSliderThumb = _sliderThumbStyle;

                float maxWidth = Mathf.Max(1f, Screen.width - 24f);
                float maxHeight = Mathf.Max(1f, Screen.height - 24f);
                _windowRect.width = Mathf.Min(_windowRect.width, maxWidth);
                _windowRect.height = Mathf.Min(_windowRect.height, maxHeight);
                _windowRect.x = Mathf.Clamp(_windowRect.x, 0f, Mathf.Max(0f, Screen.width - _windowRect.width));
                _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, Mathf.Max(0f, Screen.height - _windowRect.height));
                _windowRect = GUI.Window(WindowId, _windowRect, DrawWindow, "Custom Quick Access Bubbles");
                if (_entriesWindowOpen)
                {
                    _entriesWindowRect.width = Mathf.Min(_entriesWindowRect.width, maxWidth);
                    _entriesWindowRect.height = Mathf.Min(_entriesWindowRect.height, maxHeight);
                    _entriesWindowRect.x = Mathf.Clamp(
                        _entriesWindowRect.x,
                        0f,
                        Mathf.Max(0f, Screen.width - _entriesWindowRect.width));
                    _entriesWindowRect.y = Mathf.Clamp(
                        _entriesWindowRect.y,
                        0f,
                        Mathf.Max(0f, Screen.height - _entriesWindowRect.height));
                    _entriesWindowRect = GUI.Window(
                        EntriesWindowId,
                        _entriesWindowRect,
                        DrawEntriesWindow,
                        "Quick Access Bubble Entries");
                }
            }
            finally
            {
                skin.window = originalWindow;
                skin.button = originalButton;
                skin.toggle = originalToggle;
                skin.textField = originalTextField;
                skin.label = originalLabel;
                skin.box = originalBox;
                skin.horizontalSlider = originalSlider;
                skin.horizontalSliderThumb = originalSliderThumb;
            }
        }
    }

    private void EnsureUiStyles()
    {
        if (_windowStyle != null)
            return;

        GUISkin skin = GUI.skin;
        Texture2D windowTexture = CreateRoundedTexture(UiPanelColor, 12);
        Texture2D buttonTexture = CreateRoundedTexture(UiButtonColor, 9);
        Texture2D buttonHoverTexture = CreateRoundedTexture(UiButtonHoverColor, 9);
        Texture2D buttonActiveTexture = CreateRoundedTexture(UiButtonActiveColor, 9);
        Texture2D fieldTexture = CreateRoundedTexture(new Color(0.08f, 0.07f, 0.06f, 1f), 8);
        Texture2D boxTexture = CreateRoundedTexture(UiBoxColor, 10);
        Texture2D sliderTexture = CreateRoundedTexture(new Color(0.08f, 0.07f, 0.06f, 1f), 8);
        Texture2D sliderThumbTexture = CreateRoundedTexture(UiAccentColor, 10);

        _windowStyle = new GUIStyle(skin.window);
        _windowStyle.normal.background = windowTexture;
        _windowStyle.onNormal.background = windowTexture;
        _windowStyle.border = new RectOffset(12, 12, 12, 12);
        _windowStyle.padding = new RectOffset(13, 13, 26, 13);
        _windowStyle.normal.textColor = UiAccentColor;
        _windowStyle.fontSize = 14;
        _windowStyle.fontStyle = FontStyle.Bold;
        _windowStyle.alignment = TextAnchor.UpperCenter;

        _buttonStyle = new GUIStyle(skin.button);
        SetStyleBackgrounds(_buttonStyle, buttonTexture, buttonHoverTexture, buttonActiveTexture);
        _buttonStyle.border = new RectOffset(9, 9, 9, 9);
        _buttonStyle.padding = new RectOffset(9, 9, 5, 5);
        _buttonStyle.normal.textColor = UiTextColor;
        _buttonStyle.hover.textColor = Color.white;
        _buttonStyle.active.textColor = Color.white;
        _buttonStyle.fontSize = 12;

        _toggleStyle = new GUIStyle(skin.toggle);
        SetStyleBackgrounds(_toggleStyle, buttonTexture, buttonHoverTexture, buttonActiveTexture);
        _toggleStyle.border = new RectOffset(9, 9, 9, 9);
        _toggleStyle.padding = new RectOffset(7, 7, 4, 4);
        _toggleStyle.normal.textColor = UiTextColor;
        _toggleStyle.onNormal.textColor = UiAccentColor;
        _toggleStyle.hover.textColor = Color.white;
        _toggleStyle.onHover.textColor = Color.white;
        _toggleStyle.fontSize = 12;

        _textFieldStyle = new GUIStyle(skin.textField);
        _textFieldStyle.normal.background = fieldTexture;
        _textFieldStyle.focused.background = fieldTexture;
        _textFieldStyle.hover.background = fieldTexture;
        _textFieldStyle.border = new RectOffset(8, 8, 8, 8);
        _textFieldStyle.padding = new RectOffset(8, 8, 4, 4);
        _textFieldStyle.normal.textColor = UiTextColor;
        _textFieldStyle.focused.textColor = Color.white;
        _textFieldStyle.fontSize = 12;

        _labelStyle = new GUIStyle(skin.label);
        _labelStyle.normal.textColor = UiTextColor;
        _labelStyle.fontSize = 12;
        _labelStyle.wordWrap = true;
        _labelStyle.richText = true;

        _boxStyle = new GUIStyle(skin.box);
        _boxStyle.normal.background = boxTexture;
        _boxStyle.border = new RectOffset(10, 10, 10, 10);
        _boxStyle.padding = new RectOffset(8, 8, 7, 7);

        _sliderStyle = new GUIStyle(skin.horizontalSlider);
        _sliderStyle.normal.background = sliderTexture;
        _sliderStyle.fixedHeight = 8f;
        _sliderThumbStyle = new GUIStyle(skin.horizontalSliderThumb);
        SetStyleBackgrounds(
            _sliderThumbStyle,
            sliderThumbTexture,
            CreateRoundedTexture(new Color(1f, 0.84f, 0.50f, 1f), 10),
            CreateRoundedTexture(new Color(0.71f, 0.51f, 0.23f, 1f), 10));
        _sliderThumbStyle.border = new RectOffset(10, 10, 10, 10);
        _sliderThumbStyle.fixedWidth = 14f;
        _sliderThumbStyle.fixedHeight = 14f;
    }

    private static void SetStyleBackgrounds(
        GUIStyle style,
        Texture2D normal,
        Texture2D hover,
        Texture2D active)
    {
        style.normal.background = normal;
        style.onNormal.background = normal;
        style.hover.background = hover;
        style.onHover.background = hover;
        style.active.background = active;
        style.onActive.background = active;
        style.focused.background = hover;
        style.onFocused.background = hover;
    }

    private static Texture2D CreateRoundedTexture(Color color, int radius)
    {
        const int dimension = 64;
        Texture2D texture = new Texture2D(dimension, dimension, TextureFormat.RGBA32, false);
        texture.name = "QuickAccessBubbleColors_Rounded";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        Color[] pixels = new Color[dimension * dimension];
        const int samplesPerAxis = 4;
        for (int y = 0; y < dimension; y++)
        {
            for (int x = 0; x < dimension; x++)
            {
                int covered = 0;
                for (int sampleY = 0; sampleY < samplesPerAxis; sampleY++)
                {
                    for (int sampleX = 0; sampleX < samplesPerAxis; sampleX++)
                    {
                        float px = x + (sampleX + 0.5f) / samplesPerAxis;
                        float py = y + (sampleY + 0.5f) / samplesPerAxis;
                        float dx = Mathf.Max(Mathf.Max(radius - px, 0f), px - (dimension - radius));
                        float dy = Mathf.Max(Mathf.Max(radius - py, 0f), py - (dimension - radius));
                        if (dx * dx + dy * dy <= radius * radius)
                            covered++;
                    }
                }

                Color pixel = color;
                pixel.a *= covered / (float)(samplesPerAxis * samplesPerAxis);
                pixels[y * dimension + x] = pixel;
            }
        }
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    private void DrawWindow(int windowId)
    {
        _windowScroll = GUILayout.BeginScrollView(
            _windowScroll,
            GUILayout.Height(Mathf.Max(1f, _windowRect.height - 36f)));
        GUILayout.Space(4f);
        GUILayout.BeginHorizontal();
        DrawProfileControls();
        if (GUILayout.Button("Select a Bubble"))
            _entriesWindowOpen = true;
        GUILayout.EndHorizontal();
        Color previousBackground = GUI.backgroundColor;
        GUI.backgroundColor = new Color(1f, 0.72f, 0.72f);
        GUILayout.BeginHorizontal();
        bool resetClicked = GUILayout.Button("Reset All");
        BubbleSettings selected = GetSettings(_selectedId);
        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && selected != null;
        bool resetBubbleClicked = GUILayout.Button("Reset Bubble");
        GUI.enabled = wasEnabled;
        GUILayout.EndHorizontal();
        GUI.backgroundColor = previousBackground;
        if (resetClicked)
            ResetAllSettings();
        if (resetBubbleClicked)
            ResetBubbleSettings(selected);
        if (GUILayout.Button("Reset All Sizes"))
            ResetAllSizes();

        selected = GetSettings(_selectedId);
        if (selected != null)
        {
            GUILayout.Space(6f);
            DrawSizeSection(selected);
        }

        selected = GetSettings(_selectedId);
        if (selected == null)
        {
            GUILayout.Label("No quick-access bubbles have been detected yet.");
            GUILayout.Label("Open the in-game quick-access wheel to detect its entries.");
        }
        else
        {
            GUILayout.Space(6f);
            GUILayout.Label("Editing: " + selected.Label);
            DrawColorSection(selected, BubbleColorPart.Outer);
            GUILayout.Space(4f);
            DrawColorSection(selected, BubbleColorPart.Inner);
            GUILayout.Space(4f);
            DrawColorSection(selected, BubbleColorPart.Icon);
        }

        GUILayout.Space(8f);
        GUILayout.Label(_status);
        GUILayout.Label("Press End to close.");
        GUILayout.EndScrollView();
        GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
    }

    private void DrawEntriesWindow(int windowId)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("Choose a bubble to edit");
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Scan for bubbles (F8)", GUILayout.Width(160f)))
            CheckForBubbles();
        if (GUILayout.Button("Close", GUILayout.Width(60f)))
            _entriesWindowOpen = false;
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Search", GUILayout.Width(52f));
        _bubbleSearchText = GUILayout.TextField(_bubbleSearchText);
        if (GUILayout.Button("Clear", GUILayout.Width(60f)))
            _bubbleSearchText = string.Empty;
        GUILayout.EndHorizontal();

        _selectionScroll = GUILayout.BeginScrollView(
            _selectionScroll,
            GUILayout.Height(Mathf.Max(1f, _entriesWindowRect.height - 156f)));
        List<string> ids = GetSortedIds();
        int visibleCount = 0;
        string searchText = _bubbleSearchText.Trim();
        for (int i = 0; i < ids.Count; i++)
        {
            BubbleSettings settings = GetSettings(ids[i]);
            if (searchText.Length > 0 &&
                settings.Label.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            visibleCount++;
            Color previousBackground = GUI.backgroundColor;
            bool selected = string.Equals(ids[i], _selectedId, StringComparison.OrdinalIgnoreCase);
            if (selected)
                GUI.backgroundColor = new Color(0.72f, 1f, 0.72f);
            bool clicked = GUILayout.Button(settings.Label);
            GUI.backgroundColor = previousBackground;
            if (clicked)
                SelectBubble(ids[i]);
        }
        GUILayout.EndScrollView();
        if (ids.Count == 0)
            GUILayout.Label("No bubbles detected. Scan for bubbles here.");
        else if (visibleCount == 0)
            GUILayout.Label("No bubbles match your search.");
        GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
    }

    private void DrawProfileControls()
    {
        GUILayout.BeginVertical();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(
            "Profile " + _selectedProfile + (_profileDropdownOpen ? " ▲" : " ▼"),
            GUILayout.Width(130f)))
            _profileDropdownOpen = !_profileDropdownOpen;
        if (GUILayout.Button("Save", GUILayout.Width(70f)))
            _status = SaveSettings(GetProfilePath(_selectedProfile))
                ? "Saved Profile " + _selectedProfile + "."
                : "Could not save Profile " + _selectedProfile + ". Check the MelonLoader log.";
        if (GUILayout.Button("Apply", GUILayout.Width(70f)))
            ApplySelectedProfile();
        GUILayout.EndHorizontal();

        if (_profileDropdownOpen)
        {
            GUILayout.BeginVertical("box");
            for (int first = 1; first <= MaxProfiles; first += 3)
            {
                GUILayout.BeginHorizontal();
                for (int profile = first; profile < first + 3 && profile <= MaxProfiles; profile++)
                {
                    if (GUILayout.Button("Profile " + profile))
                    {
                        _selectedProfile = profile;
                        _profileDropdownOpen = false;
                    }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
        }
        GUILayout.EndVertical();
    }

    private void ApplySelectedProfile()
    {
        string path = GetProfilePath(_selectedProfile);
        if (!File.Exists(path))
        {
            _status = "Profile " + _selectedProfile + " has not been saved yet.";
            return;
        }

        List<BubbleSettings> previousSettings = new List<BubbleSettings>();
        foreach (BubbleSettings settings in _settings.Values)
            previousSettings.Add(CloneSettings(settings));

        _settings.Clear();
        if (!LoadSettings(path))
        {
            _settings.Clear();
            for (int i = 0; i < previousSettings.Count; i++)
                _settings[previousSettings[i].Id] = previousSettings[i];
            _status = "Could not apply Profile " + _selectedProfile + ". Check the MelonLoader log.";
            return;
        }

        for (int i = 0; i < previousSettings.Count; i++)
        {
            BubbleSettings previous = previousSettings[i];
            if (!_settings.ContainsKey(previous.Id))
                GetSettings(previous.Id).Label = previous.Label;
        }

        foreach (NativeBubble bubble in _nativeBubbles.Values)
        {
            if (bubble.Component != null)
                ApplyNativeBubble(bubble.Component, GetSettings(bubble.Id), true);
        }
        foreach (ExternalBubble bubble in _externalBubbles.Values)
            ApplyExternalBubble(bubble, GetSettings(bubble.Id));

        if (string.IsNullOrEmpty(_selectedId) || !_settings.ContainsKey(_selectedId))
            _selectedId = null;
        RefreshSelection();
        if (!string.IsNullOrEmpty(_selectedId))
            SelectBubble(_selectedId);
        if (!SaveSettings())
        {
            _status = "Profile applied, but the main config could not be saved. Check the MelonLoader log.";
            return;
        }
        _status = "Applied Profile " + _selectedProfile + " to the main config.";
    }

    private static BubbleSettings CloneSettings(BubbleSettings source)
    {
        BubbleSettings clone = new BubbleSettings(source.Id);
        clone.Label = source.Label;
        clone.OuterEnabled = source.OuterEnabled;
        clone.OuterColor = source.OuterColor;
        clone.InnerEnabled = source.InnerEnabled;
        clone.InnerColor = source.InnerColor;
        clone.IconEnabled = source.IconEnabled;
        clone.IconColor = source.IconColor;
        clone.SizeMultiplier = source.SizeMultiplier;
        return clone;
    }

    private void DrawColorSection(BubbleSettings settings, BubbleColorPart part)
    {
        string title = GetPartName(part);
        bool enabled = GetEnabled(settings, part);
        bool newEnabled = GUILayout.Toggle(enabled, "Customize " + title);
        if (newEnabled != enabled)
        {
            SetEnabled(settings, part, newEnabled);
            if (GetApplyToAll(part))
            {
                List<BubbleSettings> targets = new List<BubbleSettings>(_settings.Values);
                for (int i = 0; i < targets.Count; i++)
                {
                    BubbleSettings target = targets[i];
                    if (string.Equals(target.Id, settings.Id, StringComparison.OrdinalIgnoreCase))
                        continue;
                    SetEnabled(target, part, newEnabled);
                    ApplyProfile(target.Id);
                }
            }
            ApplyProfile(settings.Id);
            SaveSettings();
        }

        if (!newEnabled)
            return;

        Color color = GetDraftColor(part);
        string hex = GetDraftHex(part);

        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && newEnabled;
        GUILayout.BeginHorizontal();
        GUILayout.Label("Hex", GUILayout.Width(38f));
        hex = GUILayout.TextField(hex, GUILayout.Width(100f));
        Color parsed;
        if (TryParseRgbHex(hex, out parsed))
            color = parsed;
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("R", GUILayout.Width(16f));
        float red = GUILayout.HorizontalSlider(color.r, 0f, 1f);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label("G", GUILayout.Width(16f));
        float green = GUILayout.HorizontalSlider(color.g, 0f, 1f);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label("B", GUILayout.Width(16f));
        float blue = GUILayout.HorizontalSlider(color.b, 0f, 1f);
        GUILayout.EndHorizontal();
        if (Mathf.Abs(red - color.r) > 0.001f ||
            Mathf.Abs(green - color.g) > 0.001f ||
            Mathf.Abs(blue - color.b) > 0.001f)
        {
            color = new Color(red, green, blue, 1f);
            hex = "#" + ColorUtility.ToHtmlStringRGB(color);
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label("R " + Mathf.RoundToInt(color.r * 255f) +
            "   G " + Mathf.RoundToInt(color.g * 255f) +
            "   B " + Mathf.RoundToInt(color.b * 255f));
        bool applyToAll = GUILayout.Toggle(GetApplyToAll(part), "Apply to all");
        SetApplyToAll(part, applyToAll);
        if (GUILayout.Button("Apply", GUILayout.Width(70f)))
        {
            Color parsedColor;
            if (!TryParseRgbHex(hex, out parsedColor))
            {
                _status = "Enter a six-digit RGB hex color, such as #36A2FF.";
            }
            else
            {
                color = new Color(parsedColor.r, parsedColor.g, parsedColor.b, 1f);
                hex = "#" + ColorUtility.ToHtmlStringRGB(color);
                SetColor(settings, part, color);
                SetDraftColor(part, color);
                SetDraftHex(part, hex);
                if (applyToAll)
                    ApplyColorToAll(settings, part, color, newEnabled);
                else
                {
                    ApplyProfile(settings.Id);
                    SaveSettings();
                    _status = title + " color applied.";
                }
            }
        }
        GUILayout.EndHorizontal();
        GUI.enabled = wasEnabled;

        SetDraftColor(part, color);
        SetDraftHex(part, hex);
    }

    private void DrawSizeSection(BubbleSettings settings)
    {
        GUILayout.Label("Bubble size");
        GUILayout.BeginHorizontal();
        _sizeDraft = GUILayout.HorizontalSlider(_sizeDraft, 0.01f, 3f);
        GUILayout.Label(_sizeDraft.ToString("0.00") + "x", GUILayout.Width(48f));
        _applySizeToAll = GUILayout.Toggle(_applySizeToAll, "Apply to all");
        if (GUILayout.Button("Apply", GUILayout.Width(70f)))
        {
            List<BubbleSettings> targets = _applySizeToAll
                ? new List<BubbleSettings>(_settings.Values)
                : new List<BubbleSettings> { settings };
            for (int i = 0; i < targets.Count; i++)
            {
                targets[i].SizeMultiplier = _sizeDraft;
                ApplyProfile(targets[i].Id);
            }
            SaveSettings();
            _status = _applySizeToAll
                ? "Bubble size applied to all bubble entries."
                : "Bubble size applied.";
        }
        GUILayout.EndHorizontal();
    }

    private static string GetPartName(BubbleColorPart part)
    {
        switch (part)
        {
            case BubbleColorPart.Outer:
                return "Unselected bubble shell";
            case BubbleColorPart.Inner:
                return "Selected bubble shell";
            default:
                return "Icon";
        }
    }

    private static bool GetEnabled(BubbleSettings settings, BubbleColorPart part)
    {
        switch (part)
        {
            case BubbleColorPart.Outer:
                return settings.OuterEnabled;
            case BubbleColorPart.Inner:
                return settings.InnerEnabled;
            default:
                return settings.IconEnabled;
        }
    }

    private static void SetEnabled(BubbleSettings settings, BubbleColorPart part, bool enabled)
    {
        switch (part)
        {
            case BubbleColorPart.Outer:
                settings.OuterEnabled = enabled;
                break;
            case BubbleColorPart.Inner:
                settings.InnerEnabled = enabled;
                break;
            default:
                settings.IconEnabled = enabled;
                break;
        }
    }

    private static void SetColor(BubbleSettings settings, BubbleColorPart part, Color color)
    {
        switch (part)
        {
            case BubbleColorPart.Outer:
                settings.OuterColor = color;
                break;
            case BubbleColorPart.Inner:
                settings.InnerColor = color;
                break;
            default:
                settings.IconColor = color;
                break;
        }
    }

    private bool GetApplyToAll(BubbleColorPart part)
    {
        switch (part)
        {
            case BubbleColorPart.Outer:
                return _applyOuterToAll;
            case BubbleColorPart.Inner:
                return _applyInnerToAll;
            default:
                return _applyIconToAll;
        }
    }

    private void SetApplyToAll(BubbleColorPart part, bool enabled)
    {
        switch (part)
        {
            case BubbleColorPart.Outer:
                _applyOuterToAll = enabled;
                break;
            case BubbleColorPart.Inner:
                _applyInnerToAll = enabled;
                break;
            default:
                _applyIconToAll = enabled;
                break;
        }
    }

    private void ApplyColorToAll(
        BubbleSettings selected,
        BubbleColorPart part,
        Color color,
        bool enabled)
    {
        List<BubbleSettings> targets = new List<BubbleSettings>(_settings.Values);
        for (int i = 0; i < targets.Count; i++)
        {
            BubbleSettings target = targets[i];
            if (string.Equals(target.Id, selected.Id, StringComparison.OrdinalIgnoreCase))
                continue;

            SetEnabled(target, part, enabled);
            SetColor(target, part, color);
            ApplyProfile(target.Id);
        }

        SetEnabled(selected, part, enabled);
        SetColor(selected, part, color);
        ApplyProfile(selected.Id);
        _status = SaveSettings()
            ? GetPartName(part) + " color applied to all bubble entries."
            : "Color applied, but settings could not be saved. Check the MelonLoader log.";
    }

    private Color GetDraftColor(BubbleColorPart part)
    {
        switch (part)
        {
            case BubbleColorPart.Outer:
                return _outerDraft;
            case BubbleColorPart.Inner:
                return _innerDraft;
            default:
                return _iconDraft;
        }
    }

    private string GetDraftHex(BubbleColorPart part)
    {
        switch (part)
        {
            case BubbleColorPart.Outer:
                return _outerHex;
            case BubbleColorPart.Inner:
                return _innerHex;
            default:
                return _iconHex;
        }
    }

    private void SetDraftColor(BubbleColorPart part, Color color)
    {
        switch (part)
        {
            case BubbleColorPart.Outer:
                _outerDraft = color;
                break;
            case BubbleColorPart.Inner:
                _innerDraft = color;
                break;
            default:
                _iconDraft = color;
                break;
        }
    }

    private void SetDraftHex(BubbleColorPart part, string hex)
    {
        switch (part)
        {
            case BubbleColorPart.Outer:
                _outerHex = hex;
                break;
            case BubbleColorPart.Inner:
                _innerHex = hex;
                break;
            default:
                _iconHex = hex;
                break;
        }
    }

    private void CheckForBubbles()
    {
        if (_bubbleScan != null)
        {
            _status = "A bubble scan is already in progress.";
            return;
        }

        _status = "Scanning for bubbles...";
        _bubbleScan = ScanBubbles();
    }

    private void ProcessBubbleScan()
    {
        if (_bubbleScan == null)
            return;

        try
        {
            if (_bubbleScan.MoveNext())
                return;
            _bubbleScan = null;
        }
        catch (Exception exception)
        {
            _bubbleScan = null;
            _status = "Bubble scan failed. Check the MelonLoader log.";
            LoggerInstance.Error(_status + Environment.NewLine + exception);
        }

        if (_vrUiOpen && IsShowingTabletPage())
            TabletUI.Refresh();
    }

    private IEnumerator ScanBubbles()
    {
        Stopwatch frameTime = Stopwatch.StartNew();
        UnityEngine.Object[] foundBubbles = UnityEngine.Object.FindObjectsOfType(_bubbleType);
        for (int i = 0; i < foundBubbles.Length; i++)
        {
            object bubble = foundBubbles[i];
            if (foundBubbles[i] != null)
                OnNativeBubbleUpdated(bubble, _contentField.GetValue(bubble));
            if (frameTime.ElapsedMilliseconds >= ScanFrameBudgetMilliseconds)
            {
                yield return null;
                frameTime.Restart();
            }
        }

        // The scene-wide renderer lookup itself is synchronous, so give it a separate frame.
        yield return null;
        frameTime.Restart();
        Renderer[] renderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
        Dictionary<string, ExternalBubble> foundExternal =
            new Dictionary<string, ExternalBubble>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer != null)
            {
                Transform root = FindExternalBubbleRoot(renderer.transform);
                if (root != null && !HasNativeBubbleParent(renderer.transform))
                {
                    string id = "external:" + GetHierarchyKey(root);
                    ExternalBubble group;
                    if (!foundExternal.TryGetValue(id, out group))
                    {
                        int rootId = root.GetInstanceID();
                        Vector3 baseScale;
                        if (!_externalBaseScales.TryGetValue(rootId, out baseScale))
                        {
                            baseScale = root.localScale;
                            _externalBaseScales.Add(rootId, baseScale);
                        }
                        group = new ExternalBubble(id, "External: " + root.name, root, baseScale);
                        foundExternal.Add(id, group);
                    }
                    group.Renderers.Add(renderer);
                }
            }

            if (frameTime.ElapsedMilliseconds >= ScanFrameBudgetMilliseconds)
            {
                yield return null;
                frameTime.Restart();
            }
        }

        _externalBubbles = foundExternal;
        foreach (ExternalBubble group in foundExternal.Values)
        {
            if (group.Root != null)
            {
                BubbleSettings settings = GetSettings(group.Id);
                settings.Label = group.Label;
                ApplyExternalBubble(group, settings);
            }
            if (frameTime.ElapsedMilliseconds >= ScanFrameBudgetMilliseconds)
            {
                yield return null;
                frameTime.Restart();
            }
        }

        RemoveDestroyedNativeBubbles();
        RefreshSelection();
        _status = "Check complete: " + _nativeBubbles.Count + " game bubble(s), " +
            _externalBubbles.Count + " custom bubble(s).";
    }

    private void ResetAllSettings()
    {
        List<BubbleSettings> targets = new List<BubbleSettings>(_settings.Values);
        for (int i = 0; i < targets.Count; i++)
        {
            BubbleSettings target = targets[i];
            target.OuterEnabled = false;
            target.OuterColor = Color.white;
            target.InnerEnabled = false;
            target.InnerColor = Color.white;
            target.IconEnabled = false;
            target.IconColor = Color.white;
            ApplyProfile(target.Id);
        }

        _selectedId = null;
        _outerDraft = Color.white;
        _innerDraft = Color.white;
        _iconDraft = Color.white;
        _outerHex = "#FFFFFF";
        _innerHex = "#FFFFFF";
        _iconHex = "#FFFFFF";
        bool saved = SaveSettings();
        RefreshSelection();
        _status = saved
            ? "All custom colors reset to the game defaults."
            : "Colors were reset, but settings could not be saved. Check the MelonLoader log.";
    }

    private void ResetAllSizes()
    {
        List<BubbleSettings> targets = new List<BubbleSettings>(_settings.Values);
        for (int i = 0; i < targets.Count; i++)
        {
            targets[i].SizeMultiplier = 1f;
            ApplyProfile(targets[i].Id);
        }

        _sizeDraft = 1f;
        _status = SaveSettings()
            ? "All bubble sizes reset to 1.00x."
            : "Sizes were reset, but settings could not be saved. Check the MelonLoader log.";
    }

    private void ResetBubbleSettings(BubbleSettings settings)
    {
        settings.OuterEnabled = false;
        settings.OuterColor = Color.white;
        settings.InnerEnabled = false;
        settings.InnerColor = Color.white;
        settings.IconEnabled = false;
        settings.IconColor = Color.white;
        settings.SizeMultiplier = 1f;
        ApplyProfile(settings.Id);
        bool saved = SaveSettings();
        SelectBubble(settings.Id);
        _status = saved
            ? settings.Label + " reset to the game defaults."
            : settings.Label + " was reset, but settings could not be saved. Check the MelonLoader log.";
    }

    private void SelectBubble(string id)
    {
        _selectedId = id;
        BubbleSettings settings = GetSettings(id);
        _outerDraft = settings.OuterColor;
        _innerDraft = settings.InnerColor;
        _iconDraft = settings.IconColor;
        _sizeDraft = settings.SizeMultiplier;
        _outerHex = "#" + ColorUtility.ToHtmlStringRGB(_outerDraft);
        _innerHex = "#" + ColorUtility.ToHtmlStringRGB(_innerDraft);
        _iconHex = "#" + ColorUtility.ToHtmlStringRGB(_iconDraft);
        _status = BubbleSelectedStatus;
    }

    private void OnNativeBubbleUpdated(object bubbleObject, object content)
    {
        Component bubble = bubbleObject as Component;
        UnityEngine.Object action = content as UnityEngine.Object;
        if (bubble == null)
            return;

        string actionName = action == null ? bubble.gameObject.name : action.name;
        if (string.IsNullOrEmpty(actionName))
            actionName = "Quick Access Bubble";
        string id = "native:" + actionName;
        string label = GetDisplayName(content, actionName);
        GetSettings(id).Label = label;
        int instanceId = bubble.GetInstanceID();
        if (!_nativeBubbles.ContainsKey(instanceId))
        {
            _nativeBubbles.Add(instanceId, new NativeBubble(
                bubble,
                id,
                _selectedScaleField.GetValue(bubble),
                _unselectedScaleField.GetValue(bubble),
                _scaleAnimationSpeedField.GetValue(bubble)));
        }
        else
        {
            _nativeBubbles[instanceId].Id = id;
        }
        ApplyNativeBubble(bubble, GetSettings(id), true);
        RefreshSelection();
    }

    private void OnNativeBubbleStateUpdated(object bubbleObject)
    {
        Component bubble = bubbleObject as Component;
        if (bubble == null)
            return;

        NativeBubble record;
        if (_nativeBubbles.TryGetValue(bubble.GetInstanceID(), out record))
            ApplyNativeBubble(bubble, GetSettings(record.Id), false);
    }

    private void OnTavernFunColorsApplied(object bubbleObject)
    {
        Component bubble = bubbleObject as Component;
        if (bubble == null)
            return;

        object content = _contentField.GetValue(bubble);
        UnityEngine.Object action = content as UnityEngine.Object;
        string actionName = action == null ? bubble.gameObject.name : action.name;
        if (string.IsNullOrEmpty(actionName))
            actionName = "Quick Access Bubble";
        BubbleSettings settings = GetSettings("native:" + actionName);
        if (!settings.OuterEnabled && !settings.InnerEnabled && !settings.IconEnabled)
            return;

        OnNativeBubbleUpdated(bubble, content);
    }

    private static string GetDisplayName(object content, string fallback)
    {
        if (content == null)
            return fallback;

        PropertyInfo property = content.GetType().GetProperty("DisplayName");
        if (property != null && property.PropertyType == typeof(string))
        {
            string displayName = property.GetValue(content, null) as string;
            if (!string.IsNullOrWhiteSpace(displayName))
                return displayName;
        }
        return fallback;
    }

    private void ApplyNativeBubble(Component bubble, BubbleSettings settings, bool forceRefresh)
    {
        if (bubble == null || settings == null)
            return;

        NativeBubble nativeBubble;
        _nativeBubbles.TryGetValue(bubble.GetInstanceID(), out nativeBubble);
        bool selected = (bool)_isBubbleSelectedField.GetValue(bubble);
        GameObject activePrefab = _activePrefabField.GetValue(bubble) as GameObject;
        if (!forceRefresh && nativeBubble != null &&
            nativeBubble.HasAppliedVisualState &&
            nativeBubble.MatchesVisualState(settings, selected, activePrefab))
            return;

        Renderer outer = _bubbleRendererField.GetValue(bubble) as Renderer;
        if (outer != null)
        {
            ApplyTint(
                outer,
                selected ? settings.InnerEnabled : settings.OuterEnabled,
                selected ? settings.InnerColor : settings.OuterColor);
        }

        if (activePrefab != null)
        {
            Renderer[] iconRenderers = nativeBubble == null
                ? activePrefab.GetComponentsInChildren<Renderer>(true)
                : nativeBubble.GetIconRenderers(activePrefab, forceRefresh);
            for (int i = 0; i < iconRenderers.Length; i++)
                ApplyTint(iconRenderers[i], settings.IconEnabled, settings.IconColor);
        }

        if (nativeBubble != null)
        {
            ApplyNativeSize(nativeBubble, settings.SizeMultiplier);
            nativeBubble.StoreVisualState(settings, selected, activePrefab);
        }
    }

    private void ApplyNativeSize(NativeBubble bubble, float multiplier)
    {
        if (bubble.Component == null || _scaleLerpMethod == null)
            return;

        bool selected = (bool)_isBubbleSelectedField.GetValue(bubble.Component);
        float targetScale = selected ? bubble.SelectedScale : bubble.UnselectedScale;
        if (selected)
        {
            object content = _contentField.GetValue(bubble.Component);
            PropertyInfo overrideScale = content == null
                ? null
                : content.GetType().GetProperty("OverrideSelectedScale");
            if (overrideScale != null && overrideScale.PropertyType == typeof(float))
            {
                float customScale = (float)overrideScale.GetValue(content, null);
                if (customScale > 0f)
                    targetScale = customScale;
            }
        }

        _scaleLerpMethod.Invoke(
            bubble.Component,
            new object[] { targetScale * multiplier, bubble.AnimationSpeed });
    }

    private bool HasNativeBubbleParent(Transform transform)
    {
        for (Transform current = transform; current != null; current = current.parent)
        {
            if (current.GetComponent(_bubbleType) != null)
                return true;
        }
        return false;
    }

    private static Transform FindExternalBubbleRoot(Transform transform)
    {
        bool hasQuickAccessContext = false;
        Transform bubbleRoot = null;
        int depth = 0;
        for (Transform current = transform; current != null && depth < 10; current = current.parent, depth++)
        {
            string name = current.name;
            string compactName = name.Replace(" ", string.Empty).Replace("_", string.Empty);
            if (compactName.IndexOf("quickaccess", StringComparison.OrdinalIgnoreCase) >= 0 ||
                compactName.IndexOf("quickmenu", StringComparison.OrdinalIgnoreCase) >= 0 ||
                compactName.IndexOf("radial", StringComparison.OrdinalIgnoreCase) >= 0 ||
                compactName.IndexOf("wheel", StringComparison.OrdinalIgnoreCase) >= 0)
                hasQuickAccessContext = true;

            if (bubbleRoot == null && name.IndexOf("bubble", StringComparison.OrdinalIgnoreCase) >= 0)
                bubbleRoot = current;
        }

        return hasQuickAccessContext ? bubbleRoot : null;
    }

    private static string GetHierarchyKey(Transform root)
    {
        List<string> names = new List<string>();
        Transform current = root;
        int depth = 0;
        while (current != null && depth < 4)
        {
            names.Add(current.name.Replace("|", "_"));
            current = current.parent;
            depth++;
        }
        names.Reverse();
        return string.Join("/", names.ToArray());
    }

    private void ApplyExternalBubble(ExternalBubble group, BubbleSettings settings)
    {
        if (group.Root != null)
            group.Root.localScale = group.BaseScale * settings.SizeMultiplier;

        Renderer outer = null;
        for (int i = 0; i < group.Renderers.Count; i++)
        {
            Renderer renderer = group.Renderers[i];
            if (renderer != null && renderer.transform == group.Root)
            {
                outer = renderer;
                break;
            }
        }
        if (outer == null && group.Renderers.Count > 0)
            outer = group.Renderers[0];

        for (int i = 0; i < group.Renderers.Count; i++)
        {
            Renderer renderer = group.Renderers[i];
            if (renderer == null)
                continue;
            if (renderer == outer)
            {
                ApplyTint(renderer, settings.OuterEnabled, settings.OuterColor);
            }
            else if (IsIconRenderer(renderer.transform, group.Root))
            {
                ApplyTint(renderer, settings.IconEnabled, settings.IconColor);
            }
            else
            {
                ApplyTint(renderer, settings.InnerEnabled, settings.InnerColor);
            }
        }
    }

    private static bool IsIconRenderer(Transform transform, Transform bubbleRoot)
    {
        for (Transform current = transform; current != null && current != bubbleRoot; current = current.parent)
        {
            string name = current.name;
            if (name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("emblem", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("content", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private void ApplyProfile(string id)
    {
        BubbleSettings settings = GetSettings(id);
        foreach (NativeBubble bubble in _nativeBubbles.Values)
        {
            if (bubble.Component != null && string.Equals(bubble.Id, id, StringComparison.OrdinalIgnoreCase))
                ApplyNativeBubble(bubble.Component, settings, true);
        }

        ExternalBubble external;
        if (_externalBubbles.TryGetValue(id, out external))
            ApplyExternalBubble(external, settings);
    }

    private void ApplyTint(Renderer renderer, bool enabled, Color color)
    {
        if (renderer == null)
            return;

        Material[] materials = renderer.sharedMaterials;
        bool changed = false;
        for (int i = 0; i < materials.Length; i++)
        {
            Material current = materials[i];
            if (current == null)
                continue;

            Material source = GetSourceMaterial(current);
            Material tinted = enabled ? GetTintedMaterial(source, color) : source;
            if (tinted == null)
                continue;

            if (current != tinted)
            {
                materials[i] = tinted;
                changed = true;
            }
        }

        if (changed)
            renderer.sharedMaterials = materials;
    }

    private Material GetSourceMaterial(Material material)
    {
        Material source;
        return _cloneSources.TryGetValue(material.GetInstanceID(), out source) ? source : material;
    }

    private Material GetTintedMaterial(Material source, Color color)
    {
        if (source == null)
            return null;

        string cacheKey = source.GetInstanceID().ToString() + ":" +
            ColorUtility.ToHtmlStringRGBA(color);
        Material tinted;
        if (_tintedMaterials.TryGetValue(cacheKey, out tinted))
            return tinted;

        Shader shader = source.shader;
        bool hasColor = false;
        if (shader != null)
        {
            int propertyCount = shader.GetPropertyCount();
            for (int i = 0; i < propertyCount; i++)
            {
                if (shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Color)
                    continue;

                hasColor = true;
                break;
            }
        }
        if (!hasColor)
        {
            int sourceId = source.GetInstanceID();
            if (_unsupportedMaterials.Add(sourceId))
                LoggerInstance.Warning("Cannot tint bubble material '" + source.name +
                    "' because its shader exposes no color properties.");
            return source;
        }

        tinted = new Material(source);
        int tintedPropertyCount = tinted.shader.GetPropertyCount();
        for (int i = 0; i < tintedPropertyCount; i++)
        {
            if (tinted.shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Color)
                tinted.SetColor(tinted.shader.GetPropertyName(i), color);
        }
        _cloneSources[tinted.GetInstanceID()] = source;
        _tintedMaterials.Add(cacheKey, tinted);
        return tinted;
    }

    private void RemoveDestroyedNativeBubbles()
    {
        List<int> destroyed = null;
        foreach (KeyValuePair<int, NativeBubble> entry in _nativeBubbles)
        {
            if (entry.Value.Component == null)
            {
                if (destroyed == null)
                    destroyed = new List<int>();
                destroyed.Add(entry.Key);
            }
        }
        if (destroyed != null)
        {
            for (int i = 0; i < destroyed.Count; i++)
                _nativeBubbles.Remove(destroyed[i]);
        }
    }

    private void RefreshSelection()
    {
        if (_selectedId != null && _settings.ContainsKey(_selectedId))
            return;

        List<string> ids = GetSortedIds();
        if (ids.Count > 0)
            SelectBubble(ids[0]);
    }

    private List<string> GetSortedIds()
    {
        List<string> ids = new List<string>(_settings.Keys);
        ids.Sort(StringComparer.OrdinalIgnoreCase);
        return ids;
    }

    private BubbleSettings GetSettings(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        BubbleSettings settings;
        if (!_settings.TryGetValue(id, out settings))
        {
            settings = new BubbleSettings(id);
            _settings.Add(id, settings);
        }
        if (string.IsNullOrEmpty(settings.Label))
            settings.Label = id;
        return settings;
    }

    private void InitializeProfiles()
    {
        string directory = GetProfilesDirectory();
        string mainConfigPath = GetSettingsPath();
        string firstProfilePath = GetProfilePath(1);
        try
        {
            Directory.CreateDirectory(directory);
            if (File.Exists(mainConfigPath))
            {
                if (!File.Exists(firstProfilePath))
                {
                    File.Copy(mainConfigPath, firstProfilePath);
                    LoggerInstance.Msg("Imported existing bubble settings as Profile 1.");
                }
            }
            else if (File.Exists(firstProfilePath))
            {
                File.Copy(firstProfilePath, mainConfigPath);
                LoggerInstance.Msg("Initialized the main bubble settings from Profile 1.");
            }
        }
        catch (IOException exception)
        {
            LoggerInstance.Error("Could not initialize bubble color profiles: " + exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            LoggerInstance.Error("Could not initialize bubble color profiles: " + exception.Message);
        }

        LoadSettings(mainConfigPath);
    }

    private bool LoadSettings(string path)
    {
        if (!File.Exists(path))
            return false;

        bool rewriteSettings = false;
        BubbleSettings currentSection = null;
        try
        {
            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                    continue;
                try
                {
                    if (line[0] == '[' && line[line.Length - 1] == ']')
                    {
                        string id = UnescapeConfigText(line.Substring(1, line.Length - 2));
                        if (string.IsNullOrWhiteSpace(id))
                            throw new FormatException("Empty bubble section name.");
                        currentSection = GetSettings(id);
                        continue;
                    }

                    if (currentSection != null)
                    {
                        int separator = line.IndexOf('=');
                        if (separator <= 0)
                            throw new FormatException("Expected a key=value setting.");
                        ApplyConfigValue(
                            currentSection,
                            line.Substring(0, separator).Trim(),
                            UnescapeConfigText(line.Substring(separator + 1).Trim()));
                        continue;
                    }

                    LoadLegacySetting(line);
                    rewriteSettings = true;
                }
                catch (FormatException exception)
                {
                    LoggerInstance.Warning("Ignoring invalid bubble color setting on line " +
                        (i + 1) + ": " + exception.Message);
                }
                catch (ArgumentException exception)
                {
                    LoggerInstance.Warning("Ignoring invalid bubble color setting on line " +
                        (i + 1) + ": " + exception.Message);
                }
            }

            if (rewriteSettings)
                SaveSettings(path);
            return true;
        }

        catch (IOException exception)
        {
            LoggerInstance.Error("Could not read bubble color settings: " + exception.Message);
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            LoggerInstance.Error("Could not read bubble color settings: " + exception.Message);
            return false;
        }
    }

    private void LoadLegacySetting(string line)
    {
        string[] parts = line.Split('|');
        if (parts.Length != 5 && parts.Length != 7)
            throw new FormatException("Expected a readable INI setting or a legacy profile.");

        string id = Encoding.UTF8.GetString(Convert.FromBase64String(parts[0]));
        Color outer;
        Color inner;
        if (!TryParseStoredColor(parts[2], out outer) ||
            !TryParseStoredColor(parts[4], out inner))
            throw new FormatException("Invalid stored color.");

        BubbleSettings settings = GetSettings(id);
        settings.OuterEnabled = bool.Parse(parts[1]);
        settings.OuterColor = outer;
        if (parts.Length == 5)
        {
            settings.IconEnabled = bool.Parse(parts[3]);
            settings.IconColor = inner;
        }
        else
        {
            settings.InnerEnabled = bool.Parse(parts[3]);
            settings.InnerColor = inner;
            Color icon;
            if (!TryParseStoredColor(parts[6], out icon))
                throw new FormatException("Invalid stored icon color.");
            settings.IconEnabled = bool.Parse(parts[5]);
            settings.IconColor = icon;
        }
    }

    private static void ApplyConfigValue(BubbleSettings settings, string key, string value)
    {
        switch (key)
        {
            case "Label":
                settings.Label = value;
                break;
            case "OuterEnabled":
                settings.OuterEnabled = bool.Parse(value);
                break;
            case "OuterColor":
                if (!TryParseStoredColor(value, out settings.OuterColor))
                    throw new FormatException("Invalid outer color.");
                break;
            case "InnerEnabled":
                settings.InnerEnabled = bool.Parse(value);
                break;
            case "InnerColor":
                if (!TryParseStoredColor(value, out settings.InnerColor))
                    throw new FormatException("Invalid inner color.");
                break;
            case "IconEnabled":
                settings.IconEnabled = bool.Parse(value);
                break;
            case "IconColor":
                if (!TryParseStoredColor(value, out settings.IconColor))
                    throw new FormatException("Invalid icon color.");
                break;
            case "SizeMultiplier":
                float size;
                if (!float.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out size) ||
                    float.IsNaN(size) || float.IsInfinity(size) || size < 0.01f || size > 3f)
                    throw new FormatException("SizeMultiplier must be between 0.01 and 3.0.");
                settings.SizeMultiplier = size;
                break;
            default:
                throw new FormatException("Unknown setting '" + key + "'.");
        }
    }

    private static string EscapeConfigText(string value)
    {
        return value.Replace("%", "%25")
            .Replace("\r", "%0D")
            .Replace("\n", "%0A")
            .Replace("[", "%5B")
            .Replace("]", "%5D");
    }

    private static string UnescapeConfigText(string value)
    {
        return value.Replace("%5B", "[")
            .Replace("%5D", "]")
            .Replace("%0D", "\r")
            .Replace("%0A", "\n")
            .Replace("%25", "%");
    }

    private static bool TryParseStoredColor(string value, out Color color)
    {
        if (string.IsNullOrEmpty(value))
        {
            color = Color.white;
            return false;
        }

        string htmlColor = value[0] == '#' ? value : "#" + value;
        if (!ColorUtility.TryParseHtmlString(htmlColor, out color))
            return false;

        color.a = 1f;
        return true;
    }

    private static bool TryParseRgbHex(string value, out Color color)
    {
        string hex = value != null && value.StartsWith("#", StringComparison.Ordinal)
            ? value.Substring(1)
            : value;
        if (hex == null || hex.Length != 6)
        {
            color = Color.white;
            return false;
        }

        if (!ColorUtility.TryParseHtmlString("#" + hex, out color))
            return false;

        color.a = 1f;
        return true;
    }

    private bool SaveSettings()
    {
        return SaveSettings(GetSettingsPath());
    }

    private bool SaveSettings(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            List<string> lines = new List<string>();
            lines.Add("# Quick Access Bubble Colors settings.");
            lines.Add("# Profile CFG files can be copied to export or share a profile.");
            lines.Add("# Colors use six-digit RGB hex (#RRGGBB).");
            List<string> ids = GetSortedIds();
            for (int i = 0; i < ids.Count; i++)
            {
                BubbleSettings settings = GetSettings(ids[i]);
                lines.Add(string.Empty);
                lines.Add("[" + EscapeConfigText(settings.Id) + "]");
                lines.Add("Label=" + EscapeConfigText(settings.Label));
                lines.Add("OuterEnabled=" + settings.OuterEnabled.ToString().ToLowerInvariant());
                lines.Add("OuterColor=#" + ColorUtility.ToHtmlStringRGB(settings.OuterColor));
                lines.Add("InnerEnabled=" + settings.InnerEnabled.ToString().ToLowerInvariant());
                lines.Add("InnerColor=#" + ColorUtility.ToHtmlStringRGB(settings.InnerColor));
                lines.Add("IconEnabled=" + settings.IconEnabled.ToString().ToLowerInvariant());
                lines.Add("IconColor=#" + ColorUtility.ToHtmlStringRGB(settings.IconColor));
                lines.Add("SizeMultiplier=" + settings.SizeMultiplier.ToString(
                    "0.00", System.Globalization.CultureInfo.InvariantCulture));
            }
            File.WriteAllLines(path, lines.ToArray());
            return true;
        }
        catch (IOException exception)
        {
            LoggerInstance.Error("Could not save bubble color settings: " + exception.Message);
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            LoggerInstance.Error("Could not save bubble color settings: " + exception.Message);
            return false;
        }
    }

    private static string GetProfilesDirectory()
    {
        return Path.Combine(
            MelonLoader.Utils.MelonEnvironment.UserDataDirectory,
            "QuickAccessBubbleProfiles");
    }

    private static string GetProfilePath(int profile)
    {
        if (profile < 1 || profile > MaxProfiles)
            throw new ArgumentOutOfRangeException("profile");
        return Path.Combine(GetProfilesDirectory(), "Profile" + profile + ".cfg");
    }

    private static string GetSettingsPath()
    {
        return Path.Combine(
            MelonLoader.Utils.MelonEnvironment.UserDataDirectory,
            "QuickAccessBubbleColors.cfg");
    }

    private sealed class VrSettingsPanel
    {
        private const float PanelWidth = 900f;
        private const float PanelHeight = 1260f;
        private const float UiScale = 0.0011f;
        private const float InitialPanelScale = 0.5f;
        private const float AttachmentRadius = 0.10f;

        private readonly QuickAccessBubbleColorsMod _owner;
        private readonly Camera _camera;
        private readonly List<UnityEngine.XR.InputDevice> _devices =
            new List<UnityEngine.XR.InputDevice>(2);
        private readonly List<Button> _buttons = new List<Button>();
        private readonly List<Slider> _sliders = new List<Slider>();
        private readonly List<InputField> _inputFields = new List<InputField>();
        private readonly Dictionary<BubbleColorPart, InputField> _hexFields =
            new Dictionary<BubbleColorPart, InputField>();
        private readonly Dictionary<InputField, BubbleColorPart> _hexFieldParts =
            new Dictionary<InputField, BubbleColorPart>();
        private readonly object[] _textChatPointerArguments =
            { false, 0f, Vector3.zero, Vector3.forward };
        private readonly object[] _textChatTriggerArguments = { false };
        private GameObject _root;
        private GameObject _attachment;
        private GameObject _eventSystemObject;
        private GameObject _hexKeyboard;
        private Material _attachmentMaterial;
        private RectTransform _content;
        private Material _laserMaterial;
        private HandPointer _leftPointer;
        private HandPointer _rightPointer;
        private Text _statusText;
        private MethodInfo _textChatPointerMethod;
        private MethodInfo _textChatTriggerMethod;
        private PropertyInfo _grabAssistInstanceProperty;
        private PropertyInfo _assistGrabDirectionProperty;
        private float _panelScale = InitialPanelScale;
        private HandPointer _moveOwner;
        private InputField _activeHexField;
        private string _activeHexId;
        private BubbleColorPart _activeHexPart;
        private bool _textChatPointerFailed;
        private bool _textChatTriggerFailed;
        private bool _fingerRayUnavailable;

        internal VrSettingsPanel(QuickAccessBubbleColorsMod owner, Camera camera)
        {
            _owner = owner;
            _camera = camera;
        }

        internal void Build()
        {
            EnsureEventSystem();
            CreateAttachment();
            _root = new GameObject("CustomQuickAccessBubbles VR Settings");
            Canvas canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = _camera;
            RectTransform canvasRect = _root.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            _root.transform.SetParent(_attachment.transform, false);
            _root.transform.localPosition = Vector3.left * (PanelWidth * UiScale * _panelScale * 0.5f);
            _root.transform.localRotation = Quaternion.identity;
            _root.transform.localScale = Vector3.one * (UiScale * _panelScale);

            Image background = _root.AddComponent<Image>();
            background.color = new Color(0.12f, 0.11f, 0.10f, 0.98f);
            CacheTextChatInputMethods();

            Shader laserShader = Shader.Find("Sprites/Default");
            if (laserShader != null)
                _laserMaterial = new Material(laserShader);
            _leftPointer = CreatePointer("Left hand pointer", new Color(0.91f, 0.71f, 0.34f, 1f));
            _rightPointer = CreatePointer("Right hand pointer", new Color(0.94f, 0.91f, 0.83f, 1f));
            Rebuild();
        }

        internal void Update()
        {
            if (_root == null)
            {
                Dispose();
                _owner._vrUiOpen = false;
                return;
            }

            UpdatePointer(false, XRNode.LeftHand, _leftPointer);
            if (_root == null)
                return;
            UpdatePointer(true, XRNode.RightHand, _rightPointer);
        }

        internal void Dispose()
        {
            if (_root != null)
            {
                _root.SetActive(false);
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
            DestroyPointer(_leftPointer);
            DestroyPointer(_rightPointer);
            _leftPointer = null;
            _rightPointer = null;
            if (_attachment != null)
            {
                UnityEngine.Object.Destroy(_attachment);
                _attachment = null;
            }
            if (_eventSystemObject != null)
            {
                UnityEngine.Object.Destroy(_eventSystemObject);
                _eventSystemObject = null;
            }
            if (_attachmentMaterial != null)
            {
                UnityEngine.Object.Destroy(_attachmentMaterial);
                _attachmentMaterial = null;
            }
            if (_laserMaterial != null)
            {
                UnityEngine.Object.Destroy(_laserMaterial);
                _laserMaterial = null;
            }
        }

        private void Rebuild()
        {
            if (_content != null)
            {
                _content.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(_content.gameObject);
            }

            GameObject contentObject = new GameObject("Settings Content", typeof(RectTransform));
            contentObject.transform.SetParent(_root.transform, false);
            _content = contentObject.GetComponent<RectTransform>();
            _content.anchorMin = new Vector2(0.5f, 1f);
            _content.anchorMax = new Vector2(0.5f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            _buttons.Clear();
            _sliders.Clear();
            _inputFields.Clear();
            _hexFields.Clear();
            _hexFieldParts.Clear();
            _activeHexField = null;
            _activeHexId = null;
            if (_hexKeyboard != null)
            {
                _hexKeyboard.SetActive(false);
                UnityEngine.Object.Destroy(_hexKeyboard);
                _hexKeyboard = null;
            }

            MakeText(_content, "Quick Access Bubbles", 0f, 24f, 54f, 40, TextAnchor.MiddleCenter);
            MakeText(
                _content,
                "Point with your index finger; use either trigger to interact. Grab the orb or header to move.",
                0f,
                76f,
                36f,
                19,
                TextAnchor.MiddleCenter,
                new Color(0.72f, 0.66f, 0.53f, 1f));
            MakeButton(_content, "Close", 345f, 22f, 130f, 54f, _owner.CloseVrUi);

            List<string> ids = _owner.GetSortedIds();
            if (ids.Count == 0)
            {
                MakeText(
                    _content,
                    "No bubbles detected. Use Scan to find them.",
                    0f,
                    140f,
                    60f,
                    24,
                    TextAnchor.MiddleCenter);
            }
            else
            {
                int selectedIndex = ids.IndexOf(_owner._selectedId);
                if (selectedIndex < 0)
                    selectedIndex = 0;
                string selectedId = ids[selectedIndex];
                BubbleSettings selected = _owner.GetSettings(selectedId);

                MakeButton(
                    _content,
                    "<",
                    -320f,
                    130f,
                    90f,
                    64f,
                    delegate
                    {
                        _owner.SelectBubble(ids[(selectedIndex + ids.Count - 1) % ids.Count]);
                        Rebuild();
                    });
                MakeButton(
                    _content,
                    ">",
                    320f,
                    130f,
                    90f,
                    64f,
                    delegate
                    {
                        _owner.SelectBubble(ids[(selectedIndex + 1) % ids.Count]);
                        Rebuild();
                    });
                MakeText(
                    _content,
                    selected.Label,
                    0f,
                    137f,
                    50f,
                    27,
                    TextAnchor.MiddleCenter);

                float y = 190f;
                AddColorControls(selectedId, BubbleColorPart.Outer, "Outer bubble", y);
                AddColorControls(selectedId, BubbleColorPart.Inner, "Inner bubble", y + 220f);
                AddColorControls(selectedId, BubbleColorPart.Icon, "Icon", y + 440f);

                MakeText(_content, "Bubble size", -300f, 870f, 40f, 23, TextAnchor.MiddleCenter);
                Text sizeValue = MakeText(
                    _content,
                    Mathf.RoundToInt(selected.SizeMultiplier * 100f) + "%",
                    345f,
                    870f,
                    100f,
                    20,
                    TextAnchor.MiddleCenter);
                Slider sizeSlider = MakeSlider(
                    _content,
                    40f,
                    878f,
                    500f,
                    28f,
                    1f,
                    300f,
                    selected.SizeMultiplier * 100f,
                    delegate(float value)
                    {
                        _owner.GetSettings(selectedId).SizeMultiplier = value / 100f;
                        _owner.ApplyProfile(selectedId);
                    });
                sizeSlider.onValueChanged.AddListener(
                    delegate(float value) { sizeValue.text = Mathf.RoundToInt(value) + "%"; });
                MakeButton(
                    _content,
                    _owner._applySizeToAll ? "Apply size to all: On" : "Apply size to all: Off",
                    -90f,
                    920f,
                    260f,
                    48f,
                    delegate
                    {
                        _owner._applySizeToAll = !_owner._applySizeToAll;
                        Rebuild();
                    });
                MakeButton(_content, "Apply size", 250f, 920f, 180f, 48f, delegate
                {
                    if (_owner._applySizeToAll)
                    {
                        ApplySizeToAll(selectedId);
                    }
                    else
                    {
                        _owner.ApplyProfile(selectedId);
                        _owner.SaveSettings();
                        _owner._status = "Bubble size applied.";
                    }
                    RefreshStatus();
                });

                MakeText(
                    _content,
                    "Profile " + _owner._selectedProfile + " of " + MaxProfiles,
                    -220f,
                    978f,
                    48f,
                    24,
                    TextAnchor.MiddleCenter);
                MakeButton(
                    _content,
                    "Previous",
                    25f,
                    978f,
                    150f,
                    52f,
                    delegate
                    {
                        _owner._selectedProfile =
                            (_owner._selectedProfile + MaxProfiles - 2) % MaxProfiles + 1;
                        Rebuild();
                    });
                MakeButton(
                    _content,
                    "Next",
                    205f,
                    978f,
                    150f,
                    52f,
                    delegate
                    {
                        _owner._selectedProfile = _owner._selectedProfile % MaxProfiles + 1;
                        Rebuild();
                    });
                MakeButton(
                    _content,
                    "Reset bubble",
                    -170f,
                    1050f,
                    150f,
                    48f,
                    delegate
                    {
                        _owner.ResetBubbleSettings(_owner.GetSettings(selectedId));
                        Rebuild();
                    });
            }

            MakeButton(_content, "Reset all", -340f, 1050f, 150f, 48f, delegate
            {
                _owner.ResetAllSettings();
                Rebuild();
            });
            MakeButton(_content, "Reset all sizes", 0f, 1050f, 150f, 48f, delegate
            {
                _owner.ResetAllSizes();
                Rebuild();
            });
            MakeButton(_content, "Scan", 170f, 1050f, 120f, 48f, delegate
            {
                _owner.CheckForBubbles();
                Rebuild();
            });
            MakeButton(_content, "Save profile", 340f, 1050f, 150f, 48f, delegate
            {
                _owner._status = _owner.SaveSettings(GetProfilePath(_owner._selectedProfile))
                    ? "Saved Profile " + _owner._selectedProfile + "."
                    : "Could not save the selected profile. Check the MelonLoader log.";
                RefreshStatus();
            });
            MakeButton(_content, "Apply profile", -160f, 1120f, 180f, 48f, delegate
            {
                _owner.ApplySelectedProfile();
                Rebuild();
            });
            MakeButton(_content, "Save settings", 30f, 1120f, 180f, 48f, delegate
            {
                _owner._status = _owner.SaveSettings()
                    ? "Saved bubble settings."
                    : "Could not save bubble settings. Check the MelonLoader log.";
                RefreshStatus();
            });
            _statusText = MakeText(
                _content,
                _owner._status,
                0f,
                1170f,
                30f,
                16,
                TextAnchor.MiddleCenter,
                new Color(0.91f, 0.71f, 0.34f, 1f));
            CreateHexKeyboard();
        }

        private void AddColorControls(string id, BubbleColorPart part, string title, float y)
        {
            GameObject section = CreateRect(_content, title + " section", 850f, 210f, 0f, y);
            Image sectionImage = section.AddComponent<Image>();
            sectionImage.color = new Color(0.18f, 0.16f, 0.13f, 0.98f);
            section.transform.SetAsFirstSibling();

            BubbleSettings settings = _owner.GetSettings(id);
            MakeText(section.transform, title, -275f, 8f, 42f, 24, TextAnchor.MiddleCenter);
            bool enabled = GetEnabled(settings, part);
            MakeButton(
                section.transform,
                enabled ? "On" : "Off",
                300f,
                8f,
                135f,
                42f,
                delegate
                {
                    BubbleSettings current = _owner.GetSettings(id);
                    SetEnabled(current, part, !GetEnabled(current, part));
                    _owner.ApplyProfile(id);
                    Rebuild();
                });

            Color color = GetColor(settings, part);
            MakeChannelSlider(section.transform, id, part, 0, "R", 58f, color.r);
            MakeChannelSlider(section.transform, id, part, 1, "G", 102f, color.g);
            MakeChannelSlider(section.transform, id, part, 2, "B", 146f, color.b);
            InputField hexField = MakeInputField(
                section.transform,
                _owner.GetDraftHex(part),
                165f,
                34f,
                delegate(string value) { _owner.SetDraftHex(part, value); });
            _hexFields[part] = hexField;
            _hexFieldParts[hexField] = part;
            MakeText(section.transform, "Hex", -310f, 168f, 30f, 18, TextAnchor.MiddleCenter);
            MakeButton(
                section.transform,
                "Apply hex",
                80f,
                165f,
                125f,
                34f,
                delegate { ApplyHex(id, part, hexField.text); });
            bool applyToAll = _owner.GetApplyToAll(part);
            MakeButton(
                section.transform,
                applyToAll ? "All: On" : "All: Off",
                260f,
                165f,
                115f,
                34f,
                delegate
                {
                    _owner.SetApplyToAll(part, !_owner.GetApplyToAll(part));
                    Rebuild();
                });
        }

        private void EnsureEventSystem()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                _eventSystemObject = new GameObject(
                    "CustomQuickAccessBubbles VR Input",
                    typeof(EventSystem),
                    typeof(StandaloneInputModule));
                eventSystem = _eventSystemObject.GetComponent<EventSystem>();
            }
            else if (eventSystem.GetComponent<BaseInputModule>() == null)
            {
                eventSystem.gameObject.AddComponent<StandaloneInputModule>();
            }
        }

        private void CreateAttachment()
        {
            _attachment = new GameObject("CustomQuickAccessBubbles Local Skill Orb Handle");
            SetAttachmentPose();
            GameObject skillOrb = FindSkillOrbVisualSource();
            if (skillOrb != null)
            {
                CopyVisualHierarchy(skillOrb.transform, _attachment.transform, true);
                Transform visual = _attachment.transform.GetChild(0);
                visual.localScale = Vector3.one * 0.12f;
                _owner.LoggerInstance.Msg("Using the loaded skill-orb visual for the local VR panel handle.");
                return;
            }

            GameObject fallback = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            fallback.name = "Local orb handle (skill-orb visual unavailable)";
            fallback.transform.SetParent(_attachment.transform, false);
            fallback.transform.localScale = Vector3.one * 0.12f;
            Component collider = fallback.GetComponent("SphereCollider");
            if (collider != null)
                UnityEngine.Object.Destroy(collider);
            MeshRenderer renderer = fallback.GetComponent<MeshRenderer>();
            Shader shader = Shader.Find("Standard");
            if (renderer != null && shader != null)
            {
                _attachmentMaterial = new Material(shader);
                _attachmentMaterial.color = new Color(0.35f, 0.78f, 1f, 1f);
                renderer.material = _attachmentMaterial;
            }
            _owner.LoggerInstance.Warning(
                "No loaded skill-orb visual was found; using a local orb-shaped handle. " +
                "The handle is client-side and is not registered with the network.");
        }

        private GameObject FindSkillOrbVisualSource()
        {
            GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
            GameObject bestMatch = null;
            int bestScore = 0;
            for (int i = 0; i < objects.Length; i++)
            {
                GameObject candidate = objects[i];
                if (candidate == null || candidate == _root || candidate == _attachment)
                    continue;

                string compactName = candidate.name.Replace(" ", string.Empty)
                    .Replace("_", string.Empty)
                    .Replace("-", string.Empty);
                int score = string.Equals(compactName, "skillorb", StringComparison.OrdinalIgnoreCase)
                    ? 3
                    : compactName.IndexOf("skillorb", StringComparison.OrdinalIgnoreCase) >= 0
                        ? 2
                        : compactName.IndexOf("orbskill", StringComparison.OrdinalIgnoreCase) >= 0
                            ? 1
                            : 0;
                if (score <= bestScore ||
                    candidate.GetComponentInChildren<MeshFilter>(true) == null ||
                    candidate.GetComponentInChildren<MeshRenderer>(true) == null)
                    continue;

                bestMatch = candidate;
                bestScore = score;
            }
            return bestMatch;
        }

        private static GameObject CopyVisualHierarchy(Transform source, Transform parent, bool isRoot)
        {
            GameObject copy = new GameObject(source.name);
            Transform copyTransform = copy.transform;
            copyTransform.SetParent(parent, false);
            copyTransform.localPosition = isRoot ? Vector3.zero : source.localPosition;
            copyTransform.localRotation = isRoot ? Quaternion.identity : source.localRotation;
            copyTransform.localScale = isRoot ? Vector3.one : source.localScale;

            MeshFilter meshFilter = source.GetComponent<MeshFilter>();
            MeshRenderer meshRenderer = source.GetComponent<MeshRenderer>();
            if (meshFilter != null && meshFilter.sharedMesh != null && meshRenderer != null)
            {
                copy.AddComponent<MeshFilter>().sharedMesh = meshFilter.sharedMesh;
                copy.AddComponent<MeshRenderer>().sharedMaterials = meshRenderer.sharedMaterials;
            }

            for (int i = 0; i < source.childCount; i++)
                CopyVisualHierarchy(source.GetChild(i), copyTransform, false);
            return copy;
        }

        private void SetAttachmentPose()
        {
            Quaternion facing = Quaternion.LookRotation(_camera.transform.forward, _camera.transform.up);
            float halfWidth = PanelWidth * UiScale * _panelScale * 0.5f;
            _attachment.transform.SetPositionAndRotation(
                _camera.transform.position + _camera.transform.forward * 0.85f +
                    facing * (Vector3.right * halfWidth),
                facing);
        }

        private InputField MakeInputField(
            Transform parent,
            string value,
            float y,
            float height,
            Action<string> changed)
        {
            GameObject fieldObject = CreateRect(parent, "Hex color input", 150f, height, -105f, y);
            Image background = fieldObject.AddComponent<Image>();
            background.color = new Color(0.08f, 0.08f, 0.07f, 1f);
            InputField field = fieldObject.AddComponent<InputField>();
            field.targetGraphic = background;
            field.lineType = InputField.LineType.SingleLine;
            field.characterLimit = 7;

            GameObject textObject = new GameObject("Text", typeof(RectTransform));
            textObject.transform.SetParent(fieldObject.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(6f, 2f);
            textRect.offsetMax = new Vector2(-6f, -2f);
            Text text = textObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 19;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.supportRichText = false;
            text.raycastTarget = false;
            field.textComponent = text;
            field.text = value;
            field.onValueChanged.AddListener(delegate(string nextValue) { changed(nextValue); });
            _inputFields.Add(field);
            return field;
        }

        private void ApplyHex(string id, BubbleColorPart part, string hex)
        {
            Color color;
            if (!TryParseRgbHex(hex, out color))
            {
                _owner._status = "Enter a six-digit RGB hex color, such as #36A2FF.";
                RefreshStatus();
                return;
            }

            color.a = 1f;
            BubbleSettings settings = _owner.GetSettings(id);
            _owner.SetDraftColor(part, color);
            _owner.SetDraftHex(part, "#" + ColorUtility.ToHtmlStringRGB(color));
            if (_owner.GetApplyToAll(part))
            {
                _owner.ApplyColorToAll(settings, part, color, GetEnabled(settings, part));
            }
            else
            {
                SetColor(settings, part, color);
                _owner.ApplyProfile(id);
                _owner.SaveSettings();
                _owner._status = GetPartName(part) + " color applied.";
            }
            RefreshStatus();
            InputField field;
            if (_hexFields.TryGetValue(part, out field))
                field.text = _owner.GetDraftHex(part);
        }

        private void ApplySizeToAll(string selectedId)
        {
            float size = _owner.GetSettings(selectedId).SizeMultiplier;
            foreach (BubbleSettings settings in _owner._settings.Values)
            {
                settings.SizeMultiplier = size;
                _owner.ApplyProfile(settings.Id);
            }
            _owner.SaveSettings();
            _owner._status = "Bubble size applied to all bubble entries.";
        }

        private void CreateHexKeyboard()
        {
            _hexKeyboard = CreateRect(_root.transform, "VR Hex Keyboard", 410f, 320f, 0f, 500f);
            Image background = _hexKeyboard.AddComponent<Image>();
            background.color = new Color(0.10f, 0.09f, 0.08f, 0.99f);
            Button inputBlocker = _hexKeyboard.AddComponent<Button>();
            inputBlocker.targetGraphic = background;
            inputBlocker.onClick.AddListener(delegate { });
            _buttons.Add(inputBlocker);
            MakeText(_hexKeyboard.transform, "Hex color", 0f, 8f, 28f, 20, TextAnchor.MiddleCenter);

            const string digits = "0123456789ABCDEF";
            for (int i = 0; i < digits.Length; i++)
            {
                char digit = digits[i];
                int column = i % 4;
                int row = i / 4;
                MakeButton(
                    _hexKeyboard.transform,
                    digit.ToString(),
                    (column - 1.5f) * 90f,
                    42f + row * 46f,
                    76f,
                    40f,
                    delegate { AppendHexDigit(digit); });
            }

            MakeButton(_hexKeyboard.transform, "Back", -135f, 235f, 90f, 40f, RemoveHexDigit);
            MakeButton(_hexKeyboard.transform, "Clear", -45f, 235f, 90f, 40f, ClearHexInput);
            MakeButton(_hexKeyboard.transform, "Apply", 45f, 235f, 90f, 40f, ApplyActiveHex);
            MakeButton(_hexKeyboard.transform, "Hide", 135f, 235f, 90f, 40f, HideHexKeyboard);
            _hexKeyboard.SetActive(false);
        }

        private void AppendHexDigit(char digit)
        {
            if (_activeHexField == null)
                return;
            string value = string.IsNullOrEmpty(_activeHexField.text)
                ? "#"
                : _activeHexField.text.ToUpperInvariant();
            if (value.Length < 7)
                _activeHexField.text = value + digit;
        }

        private void RemoveHexDigit()
        {
            if (_activeHexField == null)
                return;
            string value = _activeHexField.text;
            if (!string.IsNullOrEmpty(value) && value.Length > 1)
                _activeHexField.text = value.Substring(0, value.Length - 1);
        }

        private void ClearHexInput()
        {
            if (_activeHexField != null)
                _activeHexField.text = "#";
        }

        private void ApplyActiveHex()
        {
            if (_activeHexField == null || string.IsNullOrEmpty(_activeHexId))
                return;
            ApplyHex(_activeHexId, _activeHexPart, _activeHexField.text);
            HideHexKeyboard();
        }

        private void HideHexKeyboard()
        {
            if (_hexKeyboard != null)
                _hexKeyboard.SetActive(false);
        }

        private void MakeChannelSlider(
            Transform parent,
            string id,
            BubbleColorPart part,
            int channel,
            string label,
            float y,
            float value)
        {
            MakeText(parent, label, -300f, y - 2f, 34f, 20, TextAnchor.MiddleCenter);
            Text valueText = MakeText(
                parent,
                Mathf.RoundToInt(value * 255f).ToString(),
                340f,
                y - 2f,
                70f,
                20,
                TextAnchor.MiddleCenter);
            Slider slider = MakeSlider(
                parent,
                20f,
                y,
                500f,
                24f,
                0f,
                255f,
                value * 255f,
                delegate(float nextValue)
                {
                    BubbleSettings settings = _owner.GetSettings(id);
                    Color nextColor = GetColor(settings, part);
                    float normalized = Mathf.RoundToInt(nextValue) / 255f;
                    if (channel == 0)
                        nextColor.r = normalized;
                    else if (channel == 1)
                        nextColor.g = normalized;
                    else
                        nextColor.b = normalized;
                    nextColor.a = 1f;
                    SetColor(settings, part, nextColor);
                    _owner.ApplyProfile(id);
                    InputField hexField;
                    if (_hexFields.TryGetValue(part, out hexField))
                    {
                        string hex = "#" + ColorUtility.ToHtmlStringRGB(nextColor);
                        hexField.text = hex;
                        _owner.SetDraftHex(part, hex);
                    }
                });
            slider.onValueChanged.AddListener(
                delegate(float nextValue) { valueText.text = Mathf.RoundToInt(nextValue).ToString(); });
        }

        private Slider MakeSlider(
            Transform parent,
            float x,
            float y,
            float width,
            float height,
            float min,
            float max,
            float value,
            Action<float> changed)
        {
            GameObject sliderObject = CreateRect(parent, "Slider", width, height, x, y);
            Image track = sliderObject.AddComponent<Image>();
            track.color = new Color(0.08f, 0.08f, 0.07f, 1f);
            Slider slider = sliderObject.AddComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = true;
            slider.direction = Slider.Direction.LeftToRight;
            slider.value = Mathf.Clamp(value, min, max);

            GameObject fillAreaObject = new GameObject("Fill Area", typeof(RectTransform));
            fillAreaObject.transform.SetParent(sliderObject.transform, false);
            RectTransform fillArea = fillAreaObject.GetComponent<RectTransform>();
            fillArea.anchorMin = Vector2.zero;
            fillArea.anchorMax = Vector2.one;
            fillArea.offsetMin = new Vector2(4f, 4f);
            fillArea.offsetMax = new Vector2(-4f, -4f);
            GameObject fillObject = new GameObject("Fill", typeof(RectTransform));
            fillObject.transform.SetParent(fillArea, false);
            RectTransform fill = fillObject.GetComponent<RectTransform>();
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            Image fillImage = fillObject.AddComponent<Image>();
            fillImage.color = new Color(0.91f, 0.71f, 0.34f, 1f);

            GameObject handleAreaObject = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleAreaObject.transform.SetParent(sliderObject.transform, false);
            RectTransform handleArea = handleAreaObject.GetComponent<RectTransform>();
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(12f, 0f);
            handleArea.offsetMax = new Vector2(-12f, 0f);
            GameObject handleObject = CreateRect(handleArea, "Handle", 26f, height, 0f, 0f);
            Image handleImage = handleObject.AddComponent<Image>();
            handleImage.color = new Color(0.94f, 0.91f, 0.83f, 1f);

            slider.fillRect = fill;
            slider.handleRect = handleObject.GetComponent<RectTransform>();
            slider.targetGraphic = handleImage;
            slider.onValueChanged.AddListener(delegate(float nextValue) { changed(nextValue); });
            _sliders.Add(slider);
            return slider;
        }

        private Button MakeButton(
            Transform parent,
            string text,
            float x,
            float y,
            float width,
            float height,
            Action clicked)
        {
            GameObject buttonObject = CreateRect(parent, text + " button", width, height, x, y);
            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.34f, 0.25f, 0.13f, 1f);
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.34f, 0.25f, 0.13f, 1f);
            colors.highlightedColor = new Color(0.47f, 0.35f, 0.17f, 1f);
            colors.pressedColor = new Color(0.56f, 0.42f, 0.21f, 1f);
            button.colors = colors;
            button.onClick.AddListener(delegate { clicked(); });
            _buttons.Add(button);
            MakeText(buttonObject.transform, text, 0f, 0f, height, 22, TextAnchor.MiddleCenter);
            return button;
        }

        private Text MakeText(
            Transform parent,
            string value,
            float x,
            float y,
            float height,
            int fontSize,
            TextAnchor alignment,
            Color? color = null)
        {
            GameObject textObject = CreateRect(parent, "Label", 800f, height, x, y);
            Text text = textObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (text.font == null)
                text.font = Font.CreateDynamicFontFromOSFont("Arial", fontSize);
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color ?? new Color(0.94f, 0.91f, 0.83f, 1f);
            text.text = value;
            text.raycastTarget = false;
            return text;
        }

        private static GameObject CreateRect(
            Transform parent,
            string name,
            float width,
            float height,
            float x,
            float y)
        {
            GameObject result = new GameObject(name, typeof(RectTransform));
            result.transform.SetParent(parent, false);
            RectTransform rect = result.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return result;
        }

        private HandPointer CreatePointer(string name, Color color)
        {
            HandPointer pointer = new HandPointer();
            if (_laserMaterial == null)
                return pointer;

            GameObject laserObject = new GameObject(name);
            pointer.Laser = laserObject.AddComponent<LineRenderer>();
            pointer.Laser.material = _laserMaterial;
            pointer.Laser.positionCount = 2;
            pointer.Laser.useWorldSpace = true;
            pointer.Laser.startWidth = 0.0025f;
            pointer.Laser.endWidth = 0.001f;
            pointer.Laser.startColor = color;
            pointer.Laser.endColor = color;
            pointer.Laser.enabled = false;
            return pointer;
        }

        private void CacheTextChatInputMethods()
        {
            Type bridgeType = AccessTools.TypeByName("TextChat.GameBridge");
            _textChatPointerMethod = bridgeType == null
                ? null
                : AccessTools.Method(bridgeType, "TryGetPointer");
            Type handInputType = AccessTools.TypeByName("TextChat.HandInput");
            _textChatTriggerMethod = handInputType == null
                ? null
                : AccessTools.Method(handInputType, "Held");
            Type grabAssistType = AccessTools.TypeByName("GrabAssistSettings");
            if (grabAssistType != null)
            {
                _grabAssistInstanceProperty = grabAssistType.GetProperty(
                    "Instance",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                _assistGrabDirectionProperty = grabAssistType.GetProperty(
                    "AssistGrabDirection",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            }
            if (_textChatPointerMethod != null && _textChatTriggerMethod != null)
                _owner.LoggerInstance.Msg("Using TextChat controller pointing and trigger input for the VR panel.");
            else
                _owner.LoggerInstance.Warning(
                    "TextChat hand input was not found; using game controller transforms and XR trigger input.");
        }

        private void UpdatePointer(bool right, XRNode node, HandPointer pointer)
        {
            Vector3 rayOrigin;
            Vector3 rayDirection;
            if (!TryGetPointerRay(right, out rayOrigin, out rayDirection))
            {
                ClearPointer(pointer);
                return;
            }

            Ray ray = new Ray(rayOrigin, rayDirection);
            Plane panelPlane = new Plane(_root.transform.forward, _root.transform.position);
            float distance;
            bool intersects = panelPlane.Raycast(ray, out distance) && distance > 0f && distance < 5f;
            Vector3 endPoint = intersects ? ray.GetPoint(distance) : ray.GetPoint(2f);
            if (pointer.Laser != null)
            {
                pointer.Laser.enabled = true;
                pointer.Laser.SetPosition(0, rayOrigin);
                pointer.Laser.SetPosition(1, endPoint);
            }

            Button hoveredButton = intersects ? FindButton(endPoint) : null;
            Slider hoveredSlider = hoveredButton == null && intersects ? FindSlider(endPoint) : null;
            InputField hoveredInput = intersects &&
                (_hexKeyboard == null || !_hexKeyboard.activeSelf)
                    ? FindInputField(endPoint)
                    : null;
            bool hoveringAttachment = IsPointingAtAttachment(ray);
            SetHoveredButton(pointer, hoveredButton);
            bool inputHeld = ReadHandInput(right, node);

            if (inputHeld && !pointer.InputHeld)
            {
                pointer.PressedButton = hoveredButton;
                pointer.PressedSlider = hoveredSlider;
                if (hoveringAttachment)
                {
                    pointer.MovingPanel = true;
                    pointer.MoveOffset = _attachment.transform.position - rayOrigin;
                    _moveOwner = pointer;
                }
                else if (hoveredInput != null)
                {
                    EventSystem eventSystem = EventSystem.current;
                    if (eventSystem != null)
                        eventSystem.SetSelectedGameObject(hoveredInput.gameObject);
                    hoveredInput.ActivateInputField();
                    _activeHexField = hoveredInput;
                    _activeHexId = _owner._selectedId;
                    _activeHexPart = _hexFieldParts[hoveredInput];
                    _hexKeyboard.SetActive(true);
                    _hexKeyboard.transform.SetAsLastSibling();
                }
                else if (hoveredSlider != null)
                    UpdateSliderValue(hoveredSlider, endPoint);
                else if (hoveredButton == null && intersects && IsHeaderPoint(endPoint))
                {
                    pointer.MovingPanel = true;
                    pointer.MoveOffset = _attachment.transform.position - rayOrigin;
                    _moveOwner = pointer;
                }
            }
            else if (inputHeld)
            {
                if (pointer.MovingPanel && _moveOwner == pointer)
                    _attachment.transform.position = rayOrigin + pointer.MoveOffset;
                else if (pointer.PressedSlider != null && intersects)
                    UpdateSliderValue(pointer.PressedSlider, endPoint);
            }
            else if (pointer.InputHeld)
            {
                Button clicked = pointer.PressedButton;
                bool shouldClick = clicked != null && clicked == hoveredButton && clicked.interactable;
                pointer.PressedButton = null;
                pointer.PressedSlider = null;
                pointer.MovingPanel = false;
                if (_moveOwner == pointer)
                    _moveOwner = null;
                if (shouldClick)
                    clicked.onClick.Invoke();
            }

            pointer.InputHeld = inputHeld;
        }

        private bool TryGetPointerRay(bool right, out Vector3 origin, out Vector3 direction)
        {
            origin = Vector3.zero;
            direction = Vector3.zero;
            if (TryGetIndexFingerRay(right, out origin, out direction))
                return true;

            if (_textChatPointerMethod != null)
            {
                _textChatPointerArguments[0] = right;
                _textChatPointerArguments[2] = origin;
                _textChatPointerArguments[3] = direction;
                try
                {
                    bool found = (bool)_textChatPointerMethod.Invoke(null, _textChatPointerArguments);
                    if (found)
                    {
                        origin = (Vector3)_textChatPointerArguments[2];
                        direction = (Vector3)_textChatPointerArguments[3];
                        if (direction.sqrMagnitude > 0.0001f)
                        {
                            direction.Normalize();
                            return true;
                        }
                    }
                }
                catch (Exception exception)
                {
                    if (!_textChatPointerFailed)
                    {
                        _owner.LoggerInstance.Warning(
                            "TextChat hand-pointer integration failed; using the game controller transform instead: " +
                            exception.Message);
                        _textChatPointerFailed = true;
                    }
                    _textChatPointerMethod = null;
                }
            }

            direction = Vector3.zero;
            PlayerController playerController = PlayerController.Current;
            if (playerController == null)
                return false;
            Controller hand = right
                ? playerController.RightController
                : playerController.LeftController;
            if (hand == null)
                return false;

            Transform pointerTransform = hand.transform;
            Interactor interactor = hand.Interactor;
            if (interactor != null && interactor.TriggerObjectTransform != null)
                pointerTransform = interactor.TriggerObjectTransform;

            origin = pointerTransform.position;
            if (_grabAssistInstanceProperty != null &&
                _assistGrabDirectionProperty != null &&
                interactor != null &&
                interactor.TriggerObjectTransform != null)
            {
                object grabAssist = _grabAssistInstanceProperty.GetValue(null, null);
                if (grabAssist != null)
                {
                    Vector3 assistDirection =
                        (Vector3)_assistGrabDirectionProperty.GetValue(grabAssist, null);
                    direction = pointerTransform.rotation * assistDirection * (right ? 1f : -1f);
                }
            }
            if (direction.sqrMagnitude < 0.0001f)
                direction = pointerTransform.forward;
            direction.Normalize();
            return true;
        }

        private bool TryGetIndexFingerRay(bool right, out Vector3 origin, out Vector3 direction)
        {
            origin = Vector3.zero;
            direction = Vector3.zero;
            PlayerController playerController = PlayerController.Current;
            if (playerController == null)
                return false;

            Controller controller = right
                ? playerController.RightController
                : playerController.LeftController;
            Alta.Character.Hand hand = controller == null ? null : controller.Hand;
            Animator animator = hand == null ? null : hand.HandAnimator;
            if (animator == null || !animator.isHuman)
            {
                if (!_fingerRayUnavailable)
                {
                    _owner.LoggerInstance.Warning(
                        "The local hand does not expose an index-finger bone; using the controller pointer fallback.");
                    _fingerRayUnavailable = true;
                }
                return false;
            }

            HumanBodyBones proximalBone = right
                ? HumanBodyBones.RightIndexProximal
                : HumanBodyBones.LeftIndexProximal;
            HumanBodyBones intermediateBone = right
                ? HumanBodyBones.RightIndexIntermediate
                : HumanBodyBones.LeftIndexIntermediate;
            HumanBodyBones distalBone = right
                ? HumanBodyBones.RightIndexDistal
                : HumanBodyBones.LeftIndexDistal;
            Transform proximal = animator.GetBoneTransform(proximalBone);
            Transform intermediate = animator.GetBoneTransform(intermediateBone);
            Transform distal = animator.GetBoneTransform(distalBone);
            Transform start = intermediate != null ? intermediate : proximal;
            Transform fingertip = distal != null ? distal : intermediate;
            if (start == null || fingertip == null || start == fingertip)
                return false;

            Vector3 fingerDirection = fingertip.position - start.position;
            if (fingerDirection.sqrMagnitude < 0.0001f)
                return false;

            origin = fingertip.position;
            direction = fingerDirection.normalized;
            return true;
        }

        private bool ReadHandInput(bool right, XRNode node)
        {
            bool pressed = false;
            if (_textChatTriggerMethod != null)
            {
                _textChatTriggerArguments[0] = right;
                try
                {
                    pressed = (bool)_textChatTriggerMethod.Invoke(null, _textChatTriggerArguments);
                }
                catch (Exception exception)
                {
                    if (!_textChatTriggerFailed)
                    {
                        _owner.LoggerInstance.Warning(
                            "TextChat trigger integration failed; using XR controller input instead: " +
                            exception.Message);
                        _textChatTriggerFailed = true;
                    }
                    _textChatTriggerMethod = null;
                }
            }

            UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid)
            {
                _devices.Clear();
                InputDevices.GetDevicesAtXRNode(node, _devices);
                for (int i = 0; i < _devices.Count; i++)
                {
                    if (_devices[i].isValid)
                    {
                        device = _devices[i];
                        break;
                    }
                }
            }

            if (device.isValid)
            {
                bool triggerButton;
                float trigger;
                pressed |= device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out triggerButton) &&
                    triggerButton;
                pressed |= device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out trigger) &&
                    trigger > 0.55f;
            }
            return pressed;
        }

        private Button FindButton(Vector3 worldPoint)
        {
            for (int i = _buttons.Count - 1; i >= 0; i--)
            {
                Button button = _buttons[i];
                if (button == null || !button.isActiveAndEnabled || !button.interactable)
                    continue;
                RectTransform rectTransform = button.transform as RectTransform;
                Vector3 localPoint = rectTransform.InverseTransformPoint(worldPoint);
                if (rectTransform.rect.Contains(new Vector2(localPoint.x, localPoint.y)))
                    return button;
            }
            return null;
        }

        private Slider FindSlider(Vector3 worldPoint)
        {
            for (int i = _sliders.Count - 1; i >= 0; i--)
            {
                Slider slider = _sliders[i];
                if (slider == null || !slider.isActiveAndEnabled || !slider.interactable)
                    continue;
                RectTransform rectTransform = slider.transform as RectTransform;
                Vector3 localPoint = rectTransform.InverseTransformPoint(worldPoint);
                if (rectTransform.rect.Contains(new Vector2(localPoint.x, localPoint.y)))
                    return slider;
            }
            return null;
        }

        private InputField FindInputField(Vector3 worldPoint)
        {
            for (int i = _inputFields.Count - 1; i >= 0; i--)
            {
                InputField field = _inputFields[i];
                if (field == null || !field.isActiveAndEnabled || !field.interactable)
                    continue;
                RectTransform rectTransform = field.transform as RectTransform;
                Vector3 localPoint = rectTransform.InverseTransformPoint(worldPoint);
                if (rectTransform.rect.Contains(new Vector2(localPoint.x, localPoint.y)))
                    return field;
            }
            return null;
        }

        private bool IsPointingAtAttachment(Ray ray)
        {
            if (_attachment == null)
                return false;

            Vector3 offset = _attachment.transform.position - ray.origin;
            float distance = Vector3.Dot(offset, ray.direction);
            if (distance <= 0f || distance > 5f)
                return false;

            Vector3 closestPoint = ray.GetPoint(distance);
            return (closestPoint - _attachment.transform.position).sqrMagnitude <=
                AttachmentRadius * AttachmentRadius;
        }

        private void UpdateSliderValue(Slider slider, Vector3 worldPoint)
        {
            RectTransform rectTransform = slider.transform as RectTransform;
            Vector3 localPoint = rectTransform.InverseTransformPoint(worldPoint);
            float normalized = Mathf.InverseLerp(
                rectTransform.rect.xMin,
                rectTransform.rect.xMax,
                localPoint.x);
            slider.normalizedValue = normalized;
        }

        private bool IsHeaderPoint(Vector3 worldPoint)
        {
            RectTransform panelRect = _root.transform as RectTransform;
            Vector3 localPoint = panelRect.InverseTransformPoint(worldPoint);
            return localPoint.y >= panelRect.rect.yMax - 105f &&
                localPoint.x >= panelRect.rect.xMin &&
                localPoint.x <= panelRect.rect.xMax;
        }

        private void SetHoveredButton(HandPointer pointer, Button button)
        {
            if (pointer.HoveredButton == button)
                return;
            SetButtonColor(pointer.HoveredButton, false);
            pointer.HoveredButton = button;
            SetButtonColor(pointer.HoveredButton, true);
        }

        private static void SetButtonColor(Button button, bool highlighted)
        {
            if (button == null || button.targetGraphic == null)
                return;
            ColorBlock colors = button.colors;
            button.targetGraphic.color = highlighted ? colors.highlightedColor : colors.normalColor;
        }

        private void ClearPointer(HandPointer pointer)
        {
            SetHoveredButton(pointer, null);
            pointer.PressedButton = null;
            pointer.PressedSlider = null;
            pointer.InputHeld = false;
            pointer.MovingPanel = false;
            if (_moveOwner == pointer)
                _moveOwner = null;
            if (pointer.Laser != null)
                pointer.Laser.enabled = false;
        }

        private void SetPanelScale(float scale)
        {
            _panelScale = Mathf.Clamp(scale, 0.3f, 1.8f);
            _root.transform.localPosition = Vector3.left *
                (PanelWidth * UiScale * _panelScale * 0.5f);
            _root.transform.localScale = Vector3.one * (UiScale * _panelScale);
        }

        private void Recenter()
        {
            SetAttachmentPose();
        }

        private void DestroyPointer(HandPointer pointer)
        {
            if (pointer != null && pointer.Laser != null)
                UnityEngine.Object.Destroy(pointer.Laser.gameObject);
        }

        private void RefreshStatus()
        {
            if (_statusText != null)
                _statusText.text = _owner._status;
        }

        private sealed class HandPointer
        {
            internal LineRenderer Laser;
            internal Button HoveredButton;
            internal Button PressedButton;
            internal Slider PressedSlider;
            internal bool InputHeld;
            internal bool MovingPanel;
            internal Vector3 MoveOffset;
        }
    }

    private sealed class BubbleSettings
    {
        internal readonly string Id;
        internal string Label;
        internal bool OuterEnabled;
        internal Color OuterColor = Color.white;
        internal bool InnerEnabled;
        internal Color InnerColor = Color.white;
        internal bool IconEnabled;
        internal Color IconColor = Color.white;
        internal float SizeMultiplier = 1f;

        internal BubbleSettings(string id)
        {
            Id = id;
            Label = id;
        }

    }

    private enum BubbleColorPart
    {
        Outer,
        Inner,
        Icon
    }

    private sealed class NativeBubble
    {
        internal readonly Component Component;
        internal string Id;
        internal readonly float SelectedScale;
        internal readonly float UnselectedScale;
        internal readonly float AnimationSpeed;
        private GameObject _cachedActivePrefab;
        private Renderer[] _cachedIconRenderers;
        private bool _hasAppliedVisualState;
        private bool _lastSelected;
        private bool _lastOuterEnabled;
        private Color _lastOuterColor;
        private bool _lastInnerEnabled;
        private Color _lastInnerColor;
        private bool _lastIconEnabled;
        private Color _lastIconColor;
        private float _lastSizeMultiplier;

        internal bool HasAppliedVisualState
        {
            get { return _hasAppliedVisualState; }
        }

        internal NativeBubble(
            Component component,
            string id,
            object selectedScale,
            object unselectedScale,
            object animationSpeed)
        {
            Component = component;
            Id = id;
            SelectedScale = (float)selectedScale;
            UnselectedScale = (float)unselectedScale;
            AnimationSpeed = (float)animationSpeed;
        }

        internal Renderer[] GetIconRenderers(GameObject activePrefab, bool refresh)
        {
            if (refresh || _cachedActivePrefab != activePrefab || _cachedIconRenderers == null)
            {
                _cachedActivePrefab = activePrefab;
                _cachedIconRenderers = activePrefab.GetComponentsInChildren<Renderer>(true);
            }
            return _cachedIconRenderers;
        }

        internal bool MatchesVisualState(BubbleSettings settings, bool selected, GameObject activePrefab)
        {
            return _lastSelected == selected &&
                _cachedActivePrefab == activePrefab &&
                _lastOuterEnabled == settings.OuterEnabled &&
                _lastOuterColor == settings.OuterColor &&
                _lastInnerEnabled == settings.InnerEnabled &&
                _lastInnerColor == settings.InnerColor &&
                _lastIconEnabled == settings.IconEnabled &&
                _lastIconColor == settings.IconColor &&
                Mathf.Approximately(_lastSizeMultiplier, settings.SizeMultiplier);
        }

        internal void StoreVisualState(BubbleSettings settings, bool selected, GameObject activePrefab)
        {
            _cachedActivePrefab = activePrefab;
            _hasAppliedVisualState = true;
            _lastSelected = selected;
            _lastOuterEnabled = settings.OuterEnabled;
            _lastOuterColor = settings.OuterColor;
            _lastInnerEnabled = settings.InnerEnabled;
            _lastInnerColor = settings.InnerColor;
            _lastIconEnabled = settings.IconEnabled;
            _lastIconColor = settings.IconColor;
            _lastSizeMultiplier = settings.SizeMultiplier;
        }
    }

    private sealed class ExternalBubble
    {
        internal readonly string Id;
        internal readonly string Label;
        internal readonly Transform Root;
        internal readonly Vector3 BaseScale;
        internal readonly List<Renderer> Renderers = new List<Renderer>();

        internal ExternalBubble(string id, string label, Transform root, Vector3 baseScale)
        {
            Id = id;
            Label = label;
            Root = root;
            BaseScale = baseScale;
        }
    }

    private static class BubbleHooks
    {
        private static void ControllerInitializedPostfix(object __instance)
        {
            if (_instance != null)
                _instance.QueueMenuController(__instance);
        }

        private static void UpdateContentPostfix(object __instance, object content)
        {
            if (_instance != null)
                _instance.OnNativeBubbleUpdated(__instance, content);
        }

        private static void UpdateActivePostfix(object __instance)
        {
            if (_instance != null)
                _instance.OnNativeBubbleStateUpdated(__instance);
        }

        private static void TavernFunApplyColorsPostfix(object __0)
        {
            if (_instance != null)
                _instance.OnTavernFunColorsApplied(__0);
        }
    }
}

public sealed class ShareKeyboardUser : IUseVirtualKeyboard
{
    public int MaxCharactersLimit
    {
        get { return 7; }
    }

    public void HandleKeyboardSubmit()
    {
        QuickAccessBubbleColorsMod.ShareKeyboardSubmit();
    }

    public void HandleKeyboardInput(string input)
    {
        QuickAccessBubbleColorsMod.ShareKeyboardInput(input);
    }

    public void Next()
    {
    }

    public void Previous()
    {
    }

    public void LostFocus()
    {
        QuickAccessBubbleColorsMod.ShareKeyboardLost();
    }

    public bool IsShowingSecret()
    {
        return false;
    }
}

public sealed class QuickAccessBubbleUiAction : QuickAccessMenuAction
{
    public override bool IsValid
    {
        get { return true; }
    }

    public override bool IsActive
    {
        get { return QuickAccessBubbleColorsMod.VrUiOpen; }
    }

    public override bool IsIgnored
    {
        get { return false; }
    }

    public override bool HasEffectOnRun
    {
        get { return true; }
    }

    public override void Run(Controller controller)
    {
        QuickAccessBubbleColorsMod.RunVrUiAction(controller);
    }
}
