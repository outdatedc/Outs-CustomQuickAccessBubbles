using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Il2CppAlta.QuickAccessActions;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes;
using MelonLoader;
using TabletUILib;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: MelonInfo(typeof(CustomQuickAccessBubblesQuest.Mod), "CustomQuickAccessBubbles.Quest", "1.0.1", "Copilot")]
[assembly: MelonGame("Alta", "Att_Quest")]

namespace CustomQuickAccessBubblesQuest
{
    public sealed class Mod : MelonMod
    {
        private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        private const int CodeLength = 7;
        private static Mod instance;
        private readonly Dictionary<string, Settings> settings = new Dictionary<string, Settings>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, NativeBubble> bubbles = new Dictionary<int, NativeBubble>();
        private Dictionary<string, ExternalBubble> external = new Dictionary<string, ExternalBubble>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, Vector3> externalScales = new Dictionary<int, Vector3>();
        private readonly Dictionary<int, Material> sources = new Dictionary<int, Material>();
        private readonly Dictionary<string, Material> tints = new Dictionary<string, Material>(StringComparer.Ordinal);
        private readonly HashSet<int> unsupported = new HashSet<int>();
        private readonly Dictionary<string, Func<Page>> pages = new Dictionary<string, Func<Page>>(StringComparer.OrdinalIgnoreCase);
        private readonly bool[] applyAll = new bool[3];
        private bool applySizeAll;
        private int profile = 1;
        private string status = "";
        private string shareInput = "";
        private IEnumerator scan;
        private ShareKeyboardUser keyboardUser;
        private Il2Cpp.TouchScreenKeyboard keyboard;
        private bool keyboardOpen;
        private Func<Page> home;
        private readonly Dictionary<int, Color> tabletTextColors = new Dictionary<int, Color>();
        private readonly HashSet<int> flippedArrows = new HashSet<int>();
        private float nextDecoration;
        private bool decorationFailed;
        private bool hookFailed;

        private static string SettingsPath => Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "QuickAccessBubbleColors.cfg");
        private static string ProfilePath(int number) => Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "QuickAccessBubbleProfiles", "Profile" + number + ".cfg");

        public override void OnInitializeMelon()
        {
            instance = this;
            if (File.Exists(SettingsPath))
            {
                Load(SettingsPath, settings);
                try
                {
                    if (!File.Exists(ProfilePath(1)))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath(1)));
                        File.Copy(SettingsPath, ProfilePath(1));
                    }
                }
                catch (IOException ex) { LoggerInstance.Error("Could not initialize Profile 1: " + ex); }
                catch (UnauthorizedAccessException ex) { LoggerInstance.Error("Could not initialize Profile 1: " + ex); }
            }
            else if (File.Exists(ProfilePath(1)))
            {
                if (Load(ProfilePath(1), settings)) Save(SettingsPath);
            }
            home = Home;
            new HarmonyLib.Harmony("com.township.quickaccessbubblecolors.quest").PatchAll(typeof(Mod).Assembly);
            TabletUI.Wheel.App("Customize Bubbles", "CUSTOM", home);
            TabletUI.Wheel.Image("CustomQuickAccessBubbles.CustomizeB.png", "Customize Bubbles");
            LoggerInstance.Msg("Quest bubble settings initialized.");
        }

        public override void OnUpdate()
        {
            if (scan != null)
            {
                try
                {
                    if (!scan.MoveNext())
                    {
                        scan = null;
                        if (TabletUI.IsShowing(home)) TabletUI.Refresh();
                    }
                }
                catch (Exception ex)
                {
                    scan = null;
                    status = "Scan failed; check the log.";
                    LoggerInstance.Error("Bubble scan failed: " + ex);
                    if (TabletUI.IsShowing(home)) TabletUI.Refresh();
                }
            }
            if (keyboardOpen && !ShowingSharePage()) CloseKeyboard();
            if (!decorationFailed && TabletUI.IsShowing(home) && Time.unscaledTime >= nextDecoration)
            {
                nextDecoration = Time.unscaledTime + 0.1f;
                try { DecorateTablet(); }
                catch (Exception ex) { decorationFailed = true; LoggerInstance.Warning("Tablet decoration failed: " + ex); }
            }
        }

        private static object TabletView()
        {
            FieldInfo field = AccessTools.Field(typeof(TabletUI), "view");
            return field == null ? null : field.GetValue(null);
        }

        private void DecorateTablet()
        {
            object view = TabletView();
            if (view == null) return;
            Type type = view.GetType();
            Component next = AccessTools.Field(type, "next")?.GetValue(view) as Component;
            Component previous = AccessTools.Field(type, "prev")?.GetValue(view) as Component;
            var buttons = AccessTools.Field(type, "rowButtons")?.GetValue(view) as Component[];
            var labels = AccessTools.Field(type, "rowLabels")?.GetValue(view) as Il2Cpp.TextRenderer[];
            Page page = AccessTools.Field(type, "page")?.GetValue(view) as Page;
            FieldInfo indexField = AccessTools.Field(type, "pageIndex");
            if (next != null && previous != null && buttons != null && buttons.Length > 1 &&
                buttons[0] != null && buttons[buttons.Length - 1] != null &&
                !flippedArrows.Contains(next.gameObject.GetInstanceID()))
            {
                Vector3 across = next.transform.position - previous.transform.position;
                Vector3 down = buttons[buttons.Length - 1].transform.position - buttons[0].transform.position;
                Vector3 normal = Vector3.Cross(across, down);
                if (normal.sqrMagnitude > 1e-10f)
                {
                    flippedArrows.Add(next.gameObject.GetInstanceID());
                    next.transform.rotation = Quaternion.AngleAxis(180f, normal.normalized) * next.transform.rotation;
                }
            }
            if (page == null || buttons == null || indexField == null) return;
            int first = (int)indexField.GetValue(view) * buttons.Length;
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null) continue;
                string label = first + i < page.Rows.Count ? page.Rows[first + i].Label : null;
                Color? color = label == "Scan for Bubbles" ? new Color(0.25f, 0.8f, 0.3f, 1f) :
                    label != null && label.StartsWith("Reset", StringComparison.Ordinal) ? new Color(0.9f, 0.2f, 0.2f, 1f) : (Color?)null;
                if (labels != null && i < labels.Length) SetTextColor(labels[i], color);
                foreach (Il2Cpp.TextRenderer text in buttons[i].GetComponentsInChildren<Il2Cpp.TextRenderer>(true))
                    SetTextColor(text, color);
            }
        }

        private void SetTextColor(Il2Cpp.TextRenderer text, Color? color)
        {
            if (text == null) return;
            int id = text.GetInstanceID();
            if (!tabletTextColors.TryGetValue(id, out Color original)) tabletTextColors.Add(id, original = text.Color);
            Color wanted = color ?? original;
            if (text.Color != wanted) text.Color = wanted;
        }

        private Func<Page> Factory(string key, Func<Page> create)
        {
            if (!pages.TryGetValue(key, out Func<Page> page)) pages.Add(key, page = create);
            return page;
        }

        private Page Home()
        {
            Page page = new Page("Quick Access Bubbles", status);
            page.Link("Customizable bubbles", Factory("bubbles", BubbleList));
            page.Link("Profiles", Factory("profiles", Profiles));
            page.Link("Reset options", Factory("reset", ResetPage));
            page.Button("Save settings", () => { SaveAndReport("Saved bubble settings."); TabletUI.Refresh(); });
            return page;
        }

        private Page BubbleList()
        {
            Page page = new Page("Customizable Bubbles", status);
            page.Footer = "Select a bubble to customize";
            page.Button("Scan for Bubbles", () => { StartScan(); TabletUI.Refresh(); });
            List<string> ids = settings.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
            if (ids.Count == 0) page.Text("No bubbles found", "Open the wheel or scan for bubbles.");
            foreach (string id in ids)
                page.Link(settings[id].Label, Factory("bubble:" + id, () => BubblePage(id)));
            page.Back = TabletUI.Back;
            return page;
        }

        private Page BubblePage(string id)
        {
            Settings s = GetSettings(id);
            Page page = new Page(s.Label, status);
            page.Footer = "Choose a setting";
            for (int i = 0; i < 3; i++)
            {
                int part = i;
                string label = PartName(part);
                page.Link(label, Factory("color:" + id + ":" + part, () => ColorPage(id, part)));
            }
            page.Link("Bubble size", Factory("size:" + id, () => SizePage(id)));
            page.Link("Share colors", Factory("share:" + id, () => SharePage(id)));
            page.Button("Reset this bubble", () =>
            {
                s.ResetColors();
                s.Size = 1f;
                Apply(id);
                SaveAndReport(s.Label + " reset to the game defaults.");
                TabletUI.Refresh();
            });
            page.Back = TabletUI.Back;
            return page;
        }

        private Page ColorPage(string id, int part)
        {
            Settings s = GetSettings(id);
            string title = PartName(part);
            Page page = new Page(title, status) { Footer = s.Label };
            page.Toggle("Customize " + title, () => s.Enabled[part], value =>
            {
                s.Enabled[part] = value;
                Apply(id);
                SaveAndReport(title + " updated.");
                TabletUI.Refresh();
            });
            page.Slider("Red", () => s.Colors[part].r * 255f, value => SetChannel(id, part, 0, value), 0f, 255f, 10f);
            page.Slider("Green", () => s.Colors[part].g * 255f, value => SetChannel(id, part, 1, value), 0f, 255f, 10f);
            page.Slider("Blue", () => s.Colors[part].b * 255f, value => SetChannel(id, part, 2, value), 0f, 255f, 10f);
            page.Toggle("Apply color to all", () => applyAll[part], value => { applyAll[part] = value; TabletUI.Refresh(); });
            page.Button("Apply color", () =>
            {
                if (applyAll[part])
                    foreach (Settings target in settings.Values)
                    {
                        target.Enabled[part] = s.Enabled[part];
                        target.Colors[part] = s.Colors[part];
                        Apply(target.Id);
                    }
                else Apply(id);
                SaveAndReport(title + " color applied.");
                TabletUI.Refresh();
            });
            page.Back = TabletUI.Back;
            return page;
        }

        private void SetChannel(string id, int part, int channel, float value)
        {
            Settings s = GetSettings(id);
            Color color = s.Colors[part];
            float normalized = Mathf.RoundToInt(value) / 255f;
            if (channel == 0) color.r = normalized;
            else if (channel == 1) color.g = normalized;
            else color.b = normalized;
            color.a = 1f;
            s.Colors[part] = color;
            Apply(id);
            SaveAndReport(PartName(part) + " updated.");
        }

        private Page SizePage(string id)
        {
            Settings s = GetSettings(id);
            Page page = new Page("Bubble Size", status) { Footer = s.Label };
            page.Slider("Size (%)", () => s.Size * 100f, value => { s.Size = value / 100f; Apply(id); }, 1f, 300f, 10f);
            page.Toggle("Apply size to all", () => applySizeAll, value => { applySizeAll = value; TabletUI.Refresh(); });
            page.Button("Apply size", () =>
            {
                if (applySizeAll)
                    foreach (Settings target in settings.Values) { target.Size = s.Size; Apply(target.Id); }
                else Apply(id);
                SaveAndReport("Bubble size applied.");
                TabletUI.Refresh();
            });
            page.Back = TabletUI.Back;
            return page;
        }

        private Page Profiles()
        {
            Page page = new Page("Profiles", status);
            page.Choice("Profile", Enumerable.Range(1, 6).Select(i => "Profile " + i).ToArray(),
                () => profile - 1, value => { if (value >= 0 && value < 6) profile = value + 1; TabletUI.Refresh(); });
            page.Button("Save profile", () =>
            {
                status = Save(ProfilePath(profile)) ? "Saved Profile " + profile + "." : "Could not save profile; check the log.";
                TabletUI.Refresh();
            });
            page.Button("Apply profile", () =>
            {
                Dictionary<string, Settings> loaded = new Dictionary<string, Settings>(StringComparer.OrdinalIgnoreCase);
                if (!File.Exists(ProfilePath(profile)))
                    status = "Profile " + profile + " has not been saved yet.";
                else if (!Load(ProfilePath(profile), loaded))
                    status = "Could not load profile; check the log.";
                else
                {
                    foreach (Settings old in settings.Values)
                        if (!loaded.ContainsKey(old.Id)) loaded.Add(old.Id, new Settings(old.Id) { Label = old.Label });
                    settings.Clear();
                    foreach (var pair in loaded) settings.Add(pair.Key, pair.Value);
                    ApplyAll();
                    SaveAndReport("Applied Profile " + profile + ".");
                }
                TabletUI.Refresh();
            });
            page.Back = TabletUI.Back;
            return page;
        }

        private Page ResetPage()
        {
            Page page = new Page("Reset", status);
            page.Button("Reset all colors", () =>
            {
                foreach (Settings s in settings.Values) { s.ResetColors(); Apply(s.Id); }
                SaveAndReport("All custom colors reset.");
                TabletUI.Refresh();
            });
            page.Button("Reset all sizes", () =>
            {
                foreach (Settings s in settings.Values) { s.Size = 1f; Apply(s.Id); }
                SaveAndReport("All bubble sizes reset.");
                TabletUI.Refresh();
            });
            page.Back = TabletUI.Back;
            return page;
        }

        private static string PartName(int part) => part == 0 ? "Outer bubble" : part == 1 ? "Inner bubble" : "Icon color";

        private Page SharePage(string id)
        {
            Page page = new Page("Share Colors", status) { Footer = GetSettings(id).Label };
            page.Text("Your code", Encode(GetSettings(id)));
            page.Button("Entered code", shareInput.Length == 0 ? "-" : shareInput, () => { OpenKeyboard(); TabletUI.Refresh(); });
            page.Button("Apply code to this bubble", () => { ApplyCode(id, false); TabletUI.Refresh(); });
            page.Button("Apply code to all bubbles", () => { ApplyCode(id, true); TabletUI.Refresh(); });
            page.Back = TabletUI.Back;
            return page;
        }

        private static string Encode(Settings s)
        {
            ulong packed = 0;
            for (int i = 0; i < 3; i++)
            {
                Color c = s.Enabled[i] ? s.Colors[i] : Color.white;
                packed = (packed << 12) | (uint)((Mathf.RoundToInt(Mathf.Clamp01(c.r) * 15f) << 8) |
                    (Mathf.RoundToInt(Mathf.Clamp01(c.g) * 15f) << 4) | Mathf.RoundToInt(Mathf.Clamp01(c.b) * 15f));
            }
            char[] code = new char[CodeLength];
            for (int i = CodeLength - 1; i >= 0; i--) { code[i] = Alphabet[(int)(packed % 36)]; packed /= 36; }
            return new string(code);
        }

        private void ApplyCode(string id, bool all)
        {
            if (shareInput.Length != CodeLength) { status = "Enter all seven letters and numbers."; return; }
            ulong packed = 0;
            foreach (char c in shareInput)
            {
                int digit = Alphabet.IndexOf(c);
                if (digit < 0) { status = "Invalid share code."; return; }
                packed = packed * 36 + (ulong)digit;
            }
            if (packed >= (1UL << 36)) { status = "Invalid share code."; return; }
            Color[] colors = new Color[3];
            bool[] enabled = new bool[3];
            for (int i = 2; i >= 0; i--)
            {
                int c = (int)(packed & 0xFFF);
                packed >>= 12;
                enabled[i] = c != 0xFFF;
                colors[i] = new Color(((c >> 8) & 15) / 15f, ((c >> 4) & 15) / 15f, (c & 15) / 15f, 1f);
            }
            IEnumerable<Settings> targets = all ? settings.Values : new[] { GetSettings(id) };
            foreach (Settings target in targets)
            {
                for (int i = 0; i < 3; i++) { target.Enabled[i] = enabled[i]; if (enabled[i]) target.Colors[i] = colors[i]; }
                Apply(target.Id);
            }
            SaveAndReport("Applied code " + shareInput + (all ? " to all bubbles." : " to this bubble."));
        }

        private bool ShowingSharePage()
        {
            if (!TabletUI.IsShowing(home)) return false;
            var field = AccessTools.Field(typeof(TabletUI), "current");
            Func<Page> current = field == null ? null : field.GetValue(null) as Func<Page>;
            return current != null && pages.Any(pair => pair.Key.StartsWith("share:", StringComparison.Ordinal) && pair.Value == current);
        }

        private static bool KeyboardPose(Camera camera, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            object view = TabletView();
            if (view == null) return false;
            Type type = view.GetType();
            var rows = AccessTools.Field(type, "rowButtons")?.GetValue(view) as Component[];
            Component next = AccessTools.Field(type, "next")?.GetValue(view) as Component;
            Component previous = AccessTools.Field(type, "prev")?.GetValue(view) as Component;
            if (rows == null || rows.Length < 2 || rows[0] == null || rows[rows.Length - 1] == null || next == null || previous == null) return false;
            Vector3 bottom = rows[rows.Length - 1].transform.position;
            Vector3 down = bottom - rows[0].transform.position;
            float spacing = down.magnitude / (rows.Length - 1);
            Vector3 normal = Vector3.Cross(next.transform.position - previous.transform.position, down);
            if (spacing < 1e-5f || normal.sqrMagnitude < 1e-10f) return false;
            down.Normalize();
            normal.Normalize();
            if (Vector3.Dot(normal, camera.transform.position - bottom) < 0f) normal = -normal;
            position = bottom + down * (spacing * 2.5f + 0.15f) + normal * 0.05f;
            rotation = Quaternion.LookRotation(-normal, Vector3.up) * Quaternion.Euler(30f, 0f, 0f);
            return true;
        }

        private void OpenKeyboard()
        {
            try
            {
                Il2Cpp.TouchScreenKeyboard current = Il2Cpp.TouchScreenKeyboard.Instance;
                if (current == null)
                {
                    var fields = Resources.FindObjectsOfTypeAll<Il2CppAlta.Meta.UI.InputFieldWithKeyboard>();
                    Il2Cpp.TouchScreenKeyboard prefab = null;
                    foreach (var field in fields) if (field != null && field.keyboardPrefab != null) { prefab = field.keyboardPrefab; break; }
                    if (prefab == null)
                        foreach (var candidate in Resources.FindObjectsOfTypeAll<Il2Cpp.TouchScreenKeyboard>())
                            if (candidate != null && !candidate.gameObject.scene.IsValid()) { prefab = candidate; break; }
                    if (prefab == null) { status = "Keyboard unavailable. Open server search once, then retry."; LoggerInstance.Warning(status); return; }
                    Object.Instantiate(prefab);
                    current = Il2Cpp.TouchScreenKeyboard.Instance;
                }
                Camera camera = Camera.main;
                if (camera == null)
                {
                    var cameras = Object.FindObjectsOfType<Camera>();
                    foreach (Camera candidate in cameras) if (candidate != null && candidate.isActiveAndEnabled) { camera = candidate; break; }
                }
                if (current == null || camera == null) { status = "Could not find a keyboard or camera."; LoggerInstance.Warning(status); return; }
                if (!KeyboardPose(camera, out Vector3 position, out Quaternion rotation))
                {
                    LoggerInstance.Warning("Tablet layout unavailable; placing keyboard in front of the camera.");
                    Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
                    if (forward.sqrMagnitude < 0.0001f) forward = camera.transform.forward;
                    forward.Normalize();
                    position = camera.transform.position + forward * 0.55f + Vector3.down * 0.35f;
                    rotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(30f, 0f, 0f);
                }
                if (keyboardUser == null)
                {
                    if (!ClassInjector.IsTypeRegisteredInIl2Cpp<ShareKeyboardUser>())
                        ClassInjector.RegisterTypeInIl2Cpp<ShareKeyboardUser>();
                    keyboardUser = new ShareKeyboardUser();
                }
                current.setupSecretKey = false;
                keyboard = Il2Cpp.TouchScreenKeyboard.Get(position, rotation, keyboardUser.Cast<Il2Cpp.IUseVirtualKeyboard>());
                if (keyboard == null) { status = "The keyboard did not open."; LoggerInstance.Warning(status); return; }
                keyboard.gameObject.SetActive(true);
                keyboard.EventSetUpNewKeyboard(0);
                keyboard.Input = shareInput;
                var renderers = keyboard.gameObject.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    Bounds bounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                    keyboard.transform.position = position + (position - bounds.center);
                }
                keyboardOpen = true;
                status = "Type the seven-character code.";
                LoggerInstance.Msg("Opened share keyboard at " + keyboard.transform.position + ".");
            }
            catch (Exception ex) { keyboardOpen = false; status = "Could not open keyboard; check the log."; LoggerInstance.Error(status + " " + ex); }
        }

        private void CloseKeyboard()
        {
            keyboardOpen = false;
            if (keyboard != null && keyboardUser != null)
            {
                var user = keyboardUser.Cast<Il2Cpp.IUseVirtualKeyboard>();
                if (keyboard.CurrentUser != null && keyboard.CurrentUser.Pointer == user.Pointer) keyboard.Hide(user);
            }
        }

        internal static void KeyboardInput(string input)
        {
            if (instance == null) return;
            StringBuilder clean = new StringBuilder(CodeLength);
            if (input != null)
                foreach (char value in input.ToUpperInvariant())
                    if (Alphabet.IndexOf(value) >= 0 && clean.Length < CodeLength) clean.Append(value);
            string text = clean.ToString();
            if (instance.keyboard != null && input != text) instance.keyboard.Input = text;
            if (instance.shareInput != text) { instance.shareInput = text; TabletUI.Refresh(); }
        }

        internal static void KeyboardSubmit()
        {
            if (instance == null) return;
            instance.CloseKeyboard();
            instance.status = "Code entered. Choose where to apply it.";
            TabletUI.Refresh();
        }

        internal static void KeyboardLost() { if (instance != null) instance.keyboardOpen = false; }

        private Settings GetSettings(string id)
        {
            if (!settings.TryGetValue(id, out Settings value)) settings.Add(id, value = new Settings(id));
            return value;
        }

        private void Record(QuickAccessMenuBubble bubble, bool force)
        {
            if (bubble == null) return;
            QuickAccessMenuAction content = bubble.content;
            string name = content == null ? bubble.gameObject.name : content.name;
            if (string.IsNullOrEmpty(name)) name = "Quick Access Bubble";
            string id = "native:" + name;
            Settings s = GetSettings(id);
            s.Label = content != null && !string.IsNullOrWhiteSpace(content.DisplayName) ? content.DisplayName : name;
            int key = bubble.GetInstanceID();
            if (!bubbles.TryGetValue(key, out NativeBubble record) || record.Bubble == null)
            {
                bubbles.Remove(key);
                bubbles.Add(key, record = new NativeBubble(bubble));
            }
            if (!string.Equals(record.Id, id, StringComparison.OrdinalIgnoreCase)) force = true;
            record.Id = id;
            ApplyNative(record, s, force);
        }

        private void ApplyNative(NativeBubble record, Settings s, bool force)
        {
            QuickAccessMenuBubble bubble = record.Bubble;
            if (bubble == null) return;
            bool selected = bubble.isBubbleSelected;
            GameObject prefab = bubble.activePrefab;
            if (!force && record.Matches(s, selected, prefab)) return;
            Tint(bubble.bubbleRenderer, s.Enabled[selected ? 1 : 0], s.Colors[selected ? 1 : 0]);
            if (prefab != null)
                foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
                    Tint(renderer, s.Enabled[2], s.Colors[2]);
            float scale = selected ? record.SelectedScale : record.UnselectedScale;
            if (selected && bubble.content != null && bubble.content.OverrideSelectedScale > 0f)
                scale = bubble.content.OverrideSelectedScale;
            bubble.Lerp(scale * s.Size, record.Speed);
            record.Remember(s, selected, prefab);
        }

        private void Apply(string id)
        {
            Settings s = GetSettings(id);
            foreach (NativeBubble bubble in bubbles.Values)
                if (bubble.Bubble != null && string.Equals(bubble.Id, id, StringComparison.OrdinalIgnoreCase))
                    ApplyNative(bubble, s, true);
            if (external.TryGetValue(id, out ExternalBubble group)) ApplyExternal(group, s);
        }

        private void ApplyAll()
        {
            foreach (NativeBubble bubble in bubbles.Values)
                if (bubble.Bubble != null) ApplyNative(bubble, GetSettings(bubble.Id), true);
            foreach (ExternalBubble group in external.Values) ApplyExternal(group, GetSettings(group.Id));
        }

        private void Tint(Renderer renderer, bool enabled, Color color)
        {
            if (renderer == null) return;
            var materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                Material current = materials[i];
                if (current == null) continue;
                if (!sources.TryGetValue(current.GetInstanceID(), out Material source)) source = current;
                Material wanted = enabled ? Tinted(source, color) : source;
                if (wanted != current) { materials[i] = wanted; changed = true; }
            }
            if (changed) renderer.sharedMaterials = materials;
        }

        private Material Tinted(Material source, Color color)
        {
            string key = source.GetInstanceID() + ":" + FormatColor(color, true);
            if (tints.TryGetValue(key, out Material cached) && cached != null) return cached;
            Shader shader = source.shader;
            bool hasColor = false;
            if (shader != null)
                for (int i = 0; i < shader.GetPropertyCount(); i++)
                    if (shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Color) { hasColor = true; break; }
            if (!hasColor)
            {
                if (unsupported.Add(source.GetInstanceID())) LoggerInstance.Warning("Cannot tint material " + source.name + ": no color properties.");
                return source;
            }
            Material result = new Material(source);
            for (int i = 0; i < shader.GetPropertyCount(); i++)
                if (shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Color)
                    result.SetColor(shader.GetPropertyName(i), color);
            sources[result.GetInstanceID()] = source;
            tints[key] = result;
            return result;
        }

        private void StartScan()
        {
            if (scan != null) { status = "A bubble scan is already in progress."; return; }
            status = "Scanning for bubbles...";
            scan = Scan();
        }

        private IEnumerator Scan()
        {
            Stopwatch frame = Stopwatch.StartNew();
            var found = Object.FindObjectsOfType<QuickAccessMenuBubble>();
            foreach (QuickAccessMenuBubble bubble in found)
            {
                if (bubble != null) Record(bubble, true);
                if (frame.ElapsedMilliseconds >= 2) { yield return null; frame.Restart(); }
            }
            yield return null;
            frame.Restart();
            var renderers = Object.FindObjectsOfType<Renderer>();
            Dictionary<string, ExternalBubble> discovered = new Dictionary<string, ExternalBubble>(StringComparer.OrdinalIgnoreCase);
            foreach (Renderer renderer in renderers)
            {
                if (renderer != null)
                {
                    Transform root = ExternalRoot(renderer.transform);
                    if (root != null && !HasNativeParent(renderer.transform))
                    {
                        string id = "external:" + HierarchyKey(root);
                        if (!discovered.TryGetValue(id, out ExternalBubble group))
                        {
                            int key = root.GetInstanceID();
                            if (!externalScales.TryGetValue(key, out Vector3 scale)) externalScales.Add(key, scale = root.localScale);
                            discovered.Add(id, group = new ExternalBubble(id, root, scale));
                        }
                        group.Renderers.Add(renderer);
                    }
                }
                if (frame.ElapsedMilliseconds >= 2) { yield return null; frame.Restart(); }
            }
            external = discovered;
            foreach (ExternalBubble group in discovered.Values)
            {
                if (group.Root != null)
                {
                    Settings s = GetSettings(group.Id);
                    s.Label = "External: " + group.Root.name;
                    ApplyExternal(group, s);
                }
                if (frame.ElapsedMilliseconds >= 2) { yield return null; frame.Restart(); }
            }
            foreach (int id in bubbles.Where(pair => pair.Value.Bubble == null).Select(pair => pair.Key).ToArray()) bubbles.Remove(id);
            status = "Check complete: " + bubbles.Count + " game bubble(s), " + external.Count + " custom bubble(s).";
        }

        private static bool HasNativeParent(Transform transform)
        {
            for (Transform current = transform; current != null; current = current.parent)
                if (current.GetComponent<QuickAccessMenuBubble>() != null) return true;
            return false;
        }

        private static Transform ExternalRoot(Transform transform)
        {
            bool context = false;
            Transform root = null;
            int depth = 0;
            for (Transform current = transform; current != null && depth < 10; current = current.parent, depth++)
            {
                string name = current.name;
                string compact = name.Replace(" ", "").Replace("_", "");
                if (compact.IndexOf("quickaccess", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    compact.IndexOf("quickmenu", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    compact.IndexOf("radial", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    compact.IndexOf("wheel", StringComparison.OrdinalIgnoreCase) >= 0) context = true;
                if (root == null && name.IndexOf("bubble", StringComparison.OrdinalIgnoreCase) >= 0) root = current;
            }
            return context ? root : null;
        }

        private static string HierarchyKey(Transform root)
        {
            List<string> names = new List<string>();
            for (Transform current = root; current != null && names.Count < 4; current = current.parent)
                names.Add(current.name.Replace("|", "_"));
            names.Reverse();
            return string.Join("/", names);
        }

        private void ApplyExternal(ExternalBubble group, Settings s)
        {
            if (group.Root == null) return;
            group.Root.localScale = group.Scale * s.Size;
            Renderer outer = group.Renderers.FirstOrDefault(r => r != null && r.transform == group.Root);
            if (outer == null && group.Renderers.Count > 0) outer = group.Renderers[0];
            foreach (Renderer renderer in group.Renderers)
            {
                if (renderer == null) continue;
                int part = renderer == outer ? 0 : IsIcon(renderer.transform, group.Root) ? 2 : 1;
                Tint(renderer, s.Enabled[part], s.Colors[part]);
            }
        }

        private static bool IsIcon(Transform transform, Transform root)
        {
            for (Transform current = transform; current != null && current != root; current = current.parent)
                if (current.name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    current.name.IndexOf("emblem", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    current.name.IndexOf("content", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private void SaveAndReport(string success)
        {
            status = Save(SettingsPath) ? success : "Could not save settings; check the log.";
        }

        private bool Save(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                List<string> lines = new List<string> { "# Quick Access Bubble Colors settings.", "# Colors use six-digit RGB hex (#RRGGBB)." };
                string[] names = { "Outer", "Inner", "Icon" };
                foreach (Settings s in settings.Values.OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase))
                {
                    lines.Add("");
                    lines.Add("[" + Escape(s.Id) + "]");
                    lines.Add("Label=" + Escape(s.Label));
                    for (int i = 0; i < 3; i++)
                    {
                        lines.Add(names[i] + "Enabled=" + s.Enabled[i].ToString().ToLowerInvariant());
                        lines.Add(names[i] + "Color=#" + FormatColor(s.Colors[i], false));
                    }
                    lines.Add("SizeMultiplier=" + s.Size.ToString("0.00", CultureInfo.InvariantCulture));
                }
                File.WriteAllLines(path, lines);
                return true;
            }
            catch (IOException ex) { LoggerInstance.Error("Could not save settings: " + ex); return false; }
            catch (UnauthorizedAccessException ex) { LoggerInstance.Error("Could not save settings: " + ex); return false; }
        }

        private bool Load(string path, Dictionary<string, Settings> target)
        {
            if (!File.Exists(path)) return false;
            try
            {
                Settings current = null;
                int number = 0;
                foreach (string raw in File.ReadAllLines(path))
                {
                    number++;
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                    try
                    {
                        if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                        {
                            string id = Unescape(line.Substring(1, line.Length - 2));
                            if (string.IsNullOrWhiteSpace(id)) throw new FormatException("Empty section.");
                            if (!target.TryGetValue(id, out current)) target.Add(id, current = new Settings(id));
                            continue;
                        }
                        int separator = line.IndexOf('=');
                        if (current == null || separator <= 0) throw new FormatException("Expected a section and key=value.");
                        string key = line.Substring(0, separator).Trim();
                        string value = Unescape(line.Substring(separator + 1).Trim());
                        if (key == "Label") current.Label = value;
                        else if (key == "SizeMultiplier")
                        {
                            float size = float.Parse(value, CultureInfo.InvariantCulture);
                            if (float.IsNaN(size) || float.IsInfinity(size) || size < 0.01f || size > 3f) throw new FormatException("Size out of range.");
                            current.Size = size;
                        }
                        else
                        {
                            string[] names = { "Outer", "Inner", "Icon" };
                            int part = Array.FindIndex(names, name => key == name + "Enabled" || key == name + "Color");
                            if (part < 0) throw new FormatException("Unknown key " + key);
                            if (key.EndsWith("Enabled", StringComparison.Ordinal)) current.Enabled[part] = bool.Parse(value);
                            else if (TryParseColor(value, out Color color))
                            {
                                color.a = 1f;
                                current.Colors[part] = color;
                            }
                            else throw new FormatException("Invalid color.");
                        }
                    }
                    catch (FormatException ex) { LoggerInstance.Warning("Invalid setting on line " + number + ": " + ex.Message); }
                }
                return true;
            }
            catch (IOException ex) { LoggerInstance.Error("Could not load settings: " + ex); return false; }
            catch (UnauthorizedAccessException ex) { LoggerInstance.Error("Could not load settings: " + ex); return false; }
        }

        private static string Escape(string value) => value.Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A").Replace("[", "%5B").Replace("]", "%5D");
        private static string Unescape(string value) => value.Replace("%5B", "[").Replace("%5D", "]").Replace("%0D", "\r").Replace("%0A", "\n").Replace("%25", "%");

        private static string FormatColor(Color color, bool alpha)
        {
            string result = ColorByte(color.r).ToString("X2") + ColorByte(color.g).ToString("X2") +
                ColorByte(color.b).ToString("X2");
            return alpha ? result + ColorByte(color.a).ToString("X2") : result;
        }

        private static int ColorByte(float channel)
        {
            return (int)Math.Round(Math.Max(0f, Math.Min(1f, channel)) * 255f);
        }

        private static bool TryParseColor(string value, out Color color)
        {
            color = default(Color);
            string hex = value.StartsWith("#", StringComparison.Ordinal) ? value.Substring(1) : value;
            if (hex.Length != 3 && hex.Length != 4 && hex.Length != 6 && hex.Length != 8) return false;
            if (!uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint packed)) return false;
            if (hex.Length == 3 || hex.Length == 4)
            {
                if (hex.Length == 4) packed >>= 4;
                color = new Color(((packed >> 8) & 15) / 15f, ((packed >> 4) & 15) / 15f, (packed & 15) / 15f, 1f);
            }
            else
            {
                if (hex.Length == 8) packed >>= 8;
                color = new Color(((packed >> 16) & 255) / 255f, ((packed >> 8) & 255) / 255f, (packed & 255) / 255f, 1f);
            }
            return true;
        }

        private static void SafeRecord(QuickAccessMenuBubble bubble, bool force)
        {
            if (instance == null || instance.hookFailed) return;
            try
            {
                if (force) instance.Record(bubble, true);
                else if (bubble != null && instance.bubbles.TryGetValue(bubble.GetInstanceID(), out NativeBubble record))
                    instance.ApplyNative(record, instance.GetSettings(record.Id), false);
            }
            catch (Exception ex)
            {
                instance.hookFailed = true;
                instance.status = "Bubble updates stopped; check the log.";
                instance.LoggerInstance.Error("Bubble update failed; disabling bubble hooks: " + ex);
            }
        }

        [HarmonyPatch(typeof(QuickAccessMenuBubble), nameof(QuickAccessMenuBubble.UpdateContent))]
        private static class ContentHook
        {
            private static void Postfix(QuickAccessMenuBubble __instance) { SafeRecord(__instance, true); }
        }

        [HarmonyPatch(typeof(QuickAccessMenuBubble), nameof(QuickAccessMenuBubble.UpdateActive))]
        private static class ActiveHook
        {
            private static void Postfix(QuickAccessMenuBubble __instance) { SafeRecord(__instance, false); }
        }

        [HarmonyPatch(typeof(QuickAccessMenuBubble), nameof(QuickAccessMenuBubble.MarkSelected))]
        private static class SelectHook
        {
            private static void Postfix(QuickAccessMenuBubble __instance) { SafeRecord(__instance, false); }
        }

        [HarmonyPatch(typeof(QuickAccessMenuBubble), nameof(QuickAccessMenuBubble.UnmarkSelected))]
        private static class UnselectHook
        {
            private static void Postfix(QuickAccessMenuBubble __instance) { SafeRecord(__instance, false); }
        }
    }

    internal sealed class Settings
    {
        internal readonly string Id;
        internal string Label;
        internal readonly bool[] Enabled = new bool[3];
        internal readonly Color[] Colors = { Color.white, Color.white, Color.white };
        internal float Size = 1f;
        internal Settings(string id) { Id = id; Label = id; }
        internal void ResetColors() { for (int i = 0; i < 3; i++) { Enabled[i] = false; Colors[i] = Color.white; } }
    }

    internal sealed class NativeBubble
    {
        internal readonly QuickAccessMenuBubble Bubble;
        internal readonly float SelectedScale, UnselectedScale, Speed;
        internal string Id;
        private bool hasState, lastSelected;
        private GameObject lastPrefab;
        private readonly bool[] lastEnabled = new bool[3];
        private readonly Color[] lastColors = new Color[3];
        private float lastSize;
        internal NativeBubble(QuickAccessMenuBubble bubble)
        {
            Bubble = bubble;
            SelectedScale = bubble.selectedScale;
            UnselectedScale = bubble.unselectedScale;
            Speed = bubble.scaleAnimationSpeed;
        }

        internal bool Matches(Settings settings, bool selected, GameObject prefab)
        {
            if (!hasState || lastSelected != selected || lastPrefab != prefab || !Mathf.Approximately(lastSize, settings.Size)) return false;
            for (int i = 0; i < 3; i++)
                if (lastEnabled[i] != settings.Enabled[i] || lastColors[i] != settings.Colors[i]) return false;
            return true;
        }

        internal void Remember(Settings settings, bool selected, GameObject prefab)
        {
            hasState = true;
            lastSelected = selected;
            lastPrefab = prefab;
            lastSize = settings.Size;
            for (int i = 0; i < 3; i++) { lastEnabled[i] = settings.Enabled[i]; lastColors[i] = settings.Colors[i]; }
        }
    }

    internal sealed class ExternalBubble
    {
        internal readonly string Id;
        internal readonly Transform Root;
        internal readonly Vector3 Scale;
        internal readonly List<Renderer> Renderers = new List<Renderer>();
        internal ExternalBubble(string id, Transform root, Vector3 scale) { Id = id; Root = root; Scale = scale; }
    }

    [Il2CppImplements(typeof(Il2Cpp.IUseVirtualKeyboard))]
    public class ShareKeyboardUser : Il2CppSystem.Object
    {
        public ShareKeyboardUser(IntPtr pointer) : base(pointer) { }
        public ShareKeyboardUser() : base(ClassInjector.DerivedConstructorPointer<ShareKeyboardUser>()) { ClassInjector.DerivedConstructorBody(this); }
        public int get_MaxCharactersLimit() => 7;
        public void HandleKeyboardSubmit() { Mod.KeyboardSubmit(); }
        public void HandleKeyboardInput(string input) { Mod.KeyboardInput(input); }
        public void Next() { }
        public void Previous() { }
        public void LostFocus() { Mod.KeyboardLost(); }
        public bool IsShowingSecret() => false;
    }
}
