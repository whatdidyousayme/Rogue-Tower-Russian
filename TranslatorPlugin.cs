using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RogueTowerRussian
{
    [BepInPlugin("com.rogueTower.russian", "Rogue Tower Russian Translator", "1.3.0")]
    public class TranslatorPlugin : BaseUnityPlugin
    {
        public const string ModVersion = "1.3";
        public static bool TranslationEnabled = true;
        private static TranslatorPlugin instance;
        private static TranslationCatalog catalog;
        private ConfigEntry<bool> enabledSetting;
        private ConfigEntry<float> scaleSetting;
        private readonly TextLayout layout = new TextLayout();
        private Harmony harmony;
        private float timer;
        private Font uiFont;
        private Font controlFont;
        private TMP_FontAsset tmpFont;
        private GUIStyle buttonStyle;
        private GUIStyle labelStyle;
        private readonly Dictionary<string, string> cache = new Dictionary<string, string>();
        private readonly Dictionary<int, TextState> states = new Dictionary<int, TextState>();
        private readonly HashSet<string> missing = new HashSet<string>();
        private readonly List<string> pendingMissing = new List<string>();
        private string missingPath;
        private bool writingText;

        private class TextState
        {
            public UnityEngine.Object Owner;
            public string Original, Translated;
            public Font Font;
            public TMP_FontAsset TMPFont;
        }

        private void Awake()
        {
            instance = this;
            enabledSetting = Config.Bind("General", "Enabled", true, "Включить русский перевод");
            scaleSetting = Config.Bind("General", "TextScale", 1f,
                new ConfigDescription("Масштаб текста; надписи дополнительно уменьшаются до границ элемента.", new AcceptableValueRange<float>(0.5f, 1.4f)));
            TranslationEnabled = enabledSetting.Value;
            string pluginDir = Path.GetDirectoryName(Info.Location);
            string json = Path.Combine(pluginDir, "translations.json");
            try
            {
                if (!File.Exists(json)) throw new FileNotFoundException("Не найден словарь переводов", json);
                var source = JsonConvert.DeserializeObject<Dictionary<string,string>>(File.ReadAllText(json, System.Text.Encoding.UTF8));
                catalog = new TranslationCatalog(source);
                missingPath = Path.Combine(Paths.ConfigPath, "RogueTowerRussian.untranslated.jsonl");
                if (File.Exists(missingPath))
                    foreach (string line in File.ReadAllLines(missingPath))
                        try { missing.Add(JsonConvert.DeserializeObject<string>(line)); } catch { }
                uiFont = Font.CreateDynamicFontFromOSFont(new string[] { "Arial", "Segoe UI", "Tahoma" }, 18);
                controlFont = Font.CreateDynamicFontFromOSFont("Arial", 16);
                if (controlFont != null) DontDestroyOnLoad(controlFont);
                if (uiFont != null)
                {
                    DontDestroyOnLoad(uiFont);
                    tmpFont = TMP_FontAsset.CreateFontAsset(uiFont);
                    if (tmpFont != null)
                    {
                        tmpFont.isMultiAtlasTexturesEnabled = true;
                        tmpFont.TryAddCharacters("АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя№—«»");
                        DontDestroyOnLoad(tmpFont);
                    }
                }
                harmony = new Harmony("com.rogueTower.russian");
                PatchSetter(typeof(Text));
                PatchSetter(typeof(TMP_Text));
                PatchSetter(typeof(TextMesh));
                // TMP SetText overloads bypass its text property. The periodic scan
                // also covers character buffers and text serialized in scenes.
                foreach (MethodInfo method in typeof(TMP_Text).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    if (method.Name == "SetText" && !method.ContainsGenericParameters)
                        harmony.Patch(method, postfix: new HarmonyMethod(typeof(TranslatorPlugin), "AfterTMPSetText"));
                Logger.LogInfo("Русский перевод " + ModVersion + ": загружено " + source.Count + " записей; словарь " + json);
                Scan();
            }
            catch (Exception error) { Logger.LogError("Не удалось включить русский перевод: " + error); }
        }

        private void PatchSetter(Type type)
        {
            MethodInfo setter = AccessTools.PropertySetter(type, "text");
            if (setter != null) harmony.Patch(setter, prefix: new HarmonyMethod(typeof(TranslatorPlugin), "BeforeSetText"));
        }

        private static void BeforeSetText(UnityEngine.Object __instance, ref string value)
        {
            if (instance == null || instance.writingText || catalog == null) return;
            value = instance.Process(__instance, value);
        }

        private static void AfterTMPSetText(TMP_Text __instance)
        {
            if (instance != null && !instance.writingText) instance.Refresh(__instance);
        }

        private string Process(UnityEngine.Object owner, string original)
        {
            if (owner == null) return original;
            int id = owner.GetInstanceID();
            TextState state;
            if (states.TryGetValue(id, out state) && state.Owner == owner && original == state.Translated)
                return TranslationEnabled ? state.Translated : state.Original;
            if (state == null || state.Owner != owner)
            {
                layout.Capture(owner);
                state = new TextState { Owner = owner };
                Text ui = owner as Text;
                TMP_Text tmp = owner as TMP_Text;
                TextMesh mesh = owner as TextMesh;
                state.Font = ui != null ? ui.font : mesh != null ? mesh.font : null;
                state.TMPFont = tmp != null ? tmp.font : null;
                states[id] = state;
            }
            // Update on every game assignment, including pooled cards and empty text.
            state.Original = original;
            state.Translated = TextLayout.IsLoadingText(owner as Text) ? TranslationCatalog.TranslateLoadingProgress(original) : Translate(original);
            ApplyFont(state, TranslationEnabled);
            layout.Apply(owner, TranslationEnabled ? state.Translated : original, scaleSetting.Value, TranslationEnabled, uiFont);
            return TranslationEnabled ? state.Translated : original;
        }

        private string Translate(string input)
        {
            if (String.IsNullOrEmpty(input) || catalog == null) return input;
            string result;
            if (cache.TryGetValue(input, out result)) return result;
            result = catalog.Translate(input);
            if (cache.Count < 10000) cache[input] = result;
            string plain = System.Text.RegularExpressions.Regex.Replace(result, @"<[^>]+>|Rogue Tower|\b(?:Esc|Shift|[IVXLCDM]+)\b", "");
            if (System.Text.RegularExpressions.Regex.IsMatch(plain, @"[A-Za-z]{2,}") && missing.Count < 5000 && missing.Add(input))
                pendingMissing.Add(JsonConvert.SerializeObject(input));
            return result;
        }

        private void ApplyFont(TextState state, bool russian)
        {
            Text ui = state.Owner as Text;
            TMP_Text tmp = state.Owner as TMP_Text;
            TextMesh mesh = state.Owner as TextMesh;
            if (ui != null) ui.font = russian && uiFont != null && !TextLayout.IsWorldNumber(ui, state.Translated) ? uiFont : state.Font;
            // Preserve each TMP object's own font/material; add Cyrillic as fallback.
            if (tmp != null && russian && tmpFont != null && tmp.font != null && tmp.font != tmpFont)
            {
                if (tmp.font.fallbackFontAssetTable == null) tmp.font.fallbackFontAssetTable = new List<TMP_FontAsset>();
                if (!tmp.font.fallbackFontAssetTable.Contains(tmpFont)) tmp.font.fallbackFontAssetTable.Add(tmpFont);
            }
            if (mesh != null)
            {
                mesh.font = russian && uiFont != null ? uiFont : state.Font;
            }
        }

        private void Refresh(UnityEngine.Object owner)
        {
            if (owner == null || catalog == null) return;
            Text ui = owner as Text;
            TMP_Text tmp = owner as TMP_Text;
            TextMesh mesh = owner as TextMesh;
            string text = ui != null ? ui.text : tmp != null ? tmp.text : mesh != null ? mesh.text : null;
            TextState state;
            if (states.TryGetValue(owner.GetInstanceID(), out state) && state.Owner == owner && (text == state.Translated || text == state.Original))
            {
                text = TranslationEnabled ? state.Translated : state.Original;
                ApplyFont(state, TranslationEnabled);
            }
            else text = Process(owner, text);
            layout.Apply(owner, text, scaleSetting.Value, TranslationEnabled, uiFont);
            writingText = true;
            try
            {
                if (ui != null && ui.text != text) ui.text = text;
                else if (tmp != null && tmp.text != text) tmp.text = text;
                else if (mesh != null && mesh.text != text) mesh.text = text;
            }
            finally { writingText = false; }
        }

        private void Scan()
        {
            Text[] texts = FindObjectsOfType<Text>();
            foreach (Text text in texts) Refresh(text);
            if (TranslationEnabled) layout.AlignTableRows(texts);
            foreach (TMP_Text text in FindObjectsOfType<TMP_Text>()) Refresh(text);
            foreach (TextMesh text in FindObjectsOfType<TextMesh>()) Refresh(text);
            var dead = new List<int>();
            foreach (var state in states) if (state.Value.Owner == null) dead.Add(state.Key);
            foreach (int id in dead) states.Remove(id);
            layout.Clean();
            if (pendingMissing.Count > 0)
            {
                try { File.AppendAllLines(missingPath, pendingMissing.ToArray(), System.Text.Encoding.UTF8); pendingMissing.Clear(); }
                catch (Exception error) { Logger.LogWarning("Не удалось сохранить список пропусков: " + error.Message); pendingMissing.Clear(); }
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9))
            {
                Scan();
                File.WriteAllText(Path.Combine(Paths.ConfigPath, "RogueTowerRussian.layout.json"),
                    JsonConvert.SerializeObject(layout.Inspect(Resources.FindObjectsOfTypeAll<Text>()), Formatting.Indented), System.Text.Encoding.UTF8);
            }
            timer += Time.unscaledDeltaTime;
            if (timer < 0.5f) return;
            timer = 0;
            Scan();
        }

        private void OnGUI()
        {
            if (catalog == null) return;
            if (buttonStyle == null) buttonStyle = new GUIStyle(GUI.skin.button) { font = controlFont, fontSize = 16 };
            if (labelStyle == null) labelStyle = new GUIStyle(GUI.skin.label) { font = controlFont, fontSize = 15, alignment = TextAnchor.MiddleLeft };
            if (GUI.Button(new Rect(Math.Max(0, Screen.width - 220), 12, 208, 32), TranslationEnabled ? "Перевод: ВКЛ" : "Перевод: ВЫКЛ", buttonStyle))
            {
                TranslationEnabled = !TranslationEnabled;
                enabledSetting.Value = TranslationEnabled;
                Scan();
            }
            float left = Math.Max(0, Screen.width - 220);
            GUI.Box(new Rect(left, 49, 208, 63), "");
            GUI.Label(new Rect(left + 8, 51, 196, 25), "Масштаб текста: " + Mathf.RoundToInt(scaleSetting.Value * 100) + "%", labelStyle);
            float value = GUI.HorizontalSlider(new Rect(left + 12, 85, 184, 20), scaleSetting.Value, 0.5f, 1.4f);
            value = Mathf.Round(value * 100) / 100;
            if (Mathf.Abs(value - scaleSetting.Value) > 0.005f)
            {
                scaleSetting.Value = value;
                Scan();
            }
        }

        private void OnDestroy()
        {
            if (harmony != null) harmony.UnpatchSelf();
            instance = null;
        }
    }
}
