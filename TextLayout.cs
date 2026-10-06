using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RogueTowerRussian
{
    // Keep the game's layout and row spacing, and fit translated glyphs into it.
    internal sealed class TextLayout
    {
        private readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();
        private readonly TextGenerator measure = new TextGenerator();
        private Material uiRussianMaterial;
        private sealed class Entry
        {
            public UnityEngine.Object Owner;
            public float Size, MinSize, MaxSize;
            public Vector2 OriginalPreferred, RectSizeDelta, AnchoredPosition, Pivot;
            public TextAnchor Alignment;
            public bool BestFit, Wrap, Raycast;
            public HorizontalWrapMode Horizontal;
            public VerticalWrapMode Vertical;
            public TextOverflowModes TMPOverflow;
            public string LastText;
            public Vector2 LastBounds;
            public float LastScale = -1;
            public Material OriginalMaterial, RussianMaterial;
        }

        public void Capture(UnityEngine.Object owner)
        {
            int id = owner.GetInstanceID();
            Entry old;
            if (entries.TryGetValue(id, out old) && old.Owner == owner) return;
            var e = new Entry { Owner = owner };
            Text ui = owner as Text;
            TMP_Text tmp = owner as TMP_Text;
            TextMesh mesh = owner as TextMesh;
            if (ui != null)
            {
                e.Size = ui.fontSize;
                e.RectSizeDelta = ui.rectTransform.sizeDelta;
                e.AnchoredPosition = ui.rectTransform.anchoredPosition;
                e.Pivot = ui.rectTransform.pivot;
                e.Alignment = ui.alignment;
                e.Raycast = ui.raycastTarget;
                e.OriginalPreferred = new Vector2(ui.preferredWidth, ui.preferredHeight);
                e.MinSize = ui.resizeTextMinSize;
                e.MaxSize = ui.resizeTextMaxSize;
                e.BestFit = ui.resizeTextForBestFit;
                e.Horizontal = ui.horizontalOverflow;
                e.Vertical = ui.verticalOverflow;
                e.OriginalMaterial = ui.material;
            }
            else if (tmp != null)
            {
                e.Size = tmp.fontSize;
                e.MinSize = tmp.fontSizeMin;
                e.MaxSize = tmp.fontSizeMax;
                e.BestFit = tmp.enableAutoSizing;
                e.Wrap = tmp.enableWordWrapping;
                e.TMPOverflow = tmp.overflowMode;
            }
            else if (mesh != null)
            {
                e.Size = mesh.characterSize;
                Renderer renderer = mesh.GetComponent<Renderer>();
                if (renderer != null) e.OriginalMaterial = renderer.sharedMaterial;
            }
            entries[id] = e;
        }

        public void Apply(UnityEngine.Object owner, string text, float scale, bool enabled, Font russianFont)
        {
            if (owner == null) return;
            Capture(owner);
            Entry e = entries[owner.GetInstanceID()];
            Text ui = owner as Text;
            TMP_Text tmp = owner as TMP_Text;
            TextMesh mesh = owner as TextMesh;
            if (!enabled)
            {
                if (ui != null)
                {
                    ui.rectTransform.sizeDelta = e.RectSizeDelta;
                    ui.rectTransform.pivot = e.Pivot;
                    ui.rectTransform.anchoredPosition = e.AnchoredPosition;
                    ui.alignment = e.Alignment;
                    ui.raycastTarget = e.Raycast;
                    ui.fontSize = (int)e.Size;
                    ui.resizeTextForBestFit = e.BestFit;
                    ui.resizeTextMinSize = (int)e.MinSize;
                    ui.resizeTextMaxSize = (int)e.MaxSize;
                    ui.horizontalOverflow = e.Horizontal;
                    ui.verticalOverflow = e.Vertical;
                    ui.material = e.OriginalMaterial;
                }
                else if (tmp != null)
                {
                    tmp.enableAutoSizing = e.BestFit;
                    tmp.fontSize = e.Size;
                    tmp.fontSizeMin = e.MinSize;
                    tmp.fontSizeMax = e.MaxSize;
                    tmp.enableWordWrapping = e.Wrap;
                    tmp.overflowMode = e.TMPOverflow;
                }
                else if (mesh != null)
                {
                    mesh.characterSize = e.Size;
                    var renderer = mesh.GetComponent<Renderer>();
                    if (renderer != null) renderer.sharedMaterial = e.OriginalMaterial;
                }
                e.LastScale = -1;
                return;
            }

            if (ui != null)
            {
                // Placement bonuses and damage numbers intentionally overflow
                // tiny rects and render above terrain. Keep their game material.
                if (IsWorldNumber(ui, text))
                {
                    ui.material = e.OriginalMaterial;
                    ui.resizeTextForBestFit = false;
                    ui.horizontalOverflow = HorizontalWrapMode.Overflow;
                    ui.verticalOverflow = VerticalWrapMode.Overflow;
                    ui.fontSize = Math.Max(1, Mathf.RoundToInt(e.Size * scale));
                    return;
                }
                // OS fonts use GUI/Text Shader by default, which renders through
                // terrain. UI/Default respects the world canvas depth and masks.
                bool usesRussianFont = russianFont != null && ui.font == russianFont;
                if (usesRussianFont && ui.canvas != null && ui.canvas.renderMode == RenderMode.WorldSpace)
                {
                    if (uiRussianMaterial == null)
                    {
                        uiRussianMaterial = new Material(Shader.Find("UI/Default"));
                        uiRussianMaterial.name = "RogueTowerRussian UI";
                    }
                    uiRussianMaterial.SetInt("unity_GUIZTestMode", 4);
                    uiRussianMaterial.mainTexture = russianFont.material.mainTexture;
                    ui.material = uiRussianMaterial;
                }
                else ui.material = e.OriginalMaterial;
                bool terrainLetter = IsTerrainLetter(ui);
                bool loading = IsLoadingText(ui);
                bool tip = IsLoadingTip(ui);
                bool buff = HasAncestor(ui.transform, "MonsterBuffs");
                if (terrainLetter)
                {
                    ui.raycastTarget = false;
                    // The thank-you letter is on the underside of the starting
                    // tile. Give paragraphs a bounded square instead of one
                    // overflowing line which extends beyond the terrain.
                    ui.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 1600);
                    ui.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 1600);
                    // The original letter starts near a tile edge. A square
                    // centred there still sticks out: centre it under the tile.
                    ui.rectTransform.anchoredPosition = Vector2.zero;
                    ui.horizontalOverflow = HorizontalWrapMode.Wrap;
                }
                else if (loading || tip || buff)
                {
                    Vector2 allocation = FreeLabelBounds(ui, loading, tip);
                    ui.rectTransform.pivot = buff ? new Vector2(0, 0.5f) : new Vector2(0.5f, 0.5f);
                    ui.rectTransform.anchoredPosition = buff ? e.AnchoredPosition + new Vector2(4, 0) :
                        new Vector2(0, tip ? e.AnchoredPosition.y * 1.6f : e.AnchoredPosition.y);
                    ui.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, allocation.x);
                    ui.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, allocation.y);
                    ui.alignment = buff ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
                }
                Vector2 bounds = AvailableBounds(ui);
                if (bounds.x < 1 || bounds.y < 1) return;
                if (e.LastText == text && e.LastBounds == bounds && Mathf.Abs(e.LastScale - scale) < 0.001f) return;
                e.LastText = text; e.LastBounds = bounds; e.LastScale = scale;
                ui.resizeTextForBestFit = false;
                bool panelDescription = ui.name == "test" && ui.transform.parent != null && ui.transform.parent.name == "Panel";
                ui.horizontalOverflow = terrainLetter || tip || panelDescription ? HorizontalWrapMode.Wrap : e.Horizontal;
                ui.verticalOverflow = terrainLetter ? VerticalWrapMode.Truncate : VerticalWrapMode.Overflow;
                int desired = Math.Max(1, Mathf.RoundToInt(e.Size * scale));
                int low = 1, high = desired;
                while (low < high)
                {
                    int candidate = (low + high + 1) / 2;
                    // Fit the completed word once, not each changing prefix.
                    if (Fits(ui, loading ? "Загрузка..." : text, candidate, bounds)) low = candidate;
                    else high = candidate - 1;
                }
                ui.fontSize = low >= 40 ? low - low % 4 : low;
            }
            else if (tmp != null)
            {
                tmp.enableAutoSizing = true;
                tmp.fontSizeMax = Math.Max(1, e.Size * scale);
                tmp.fontSizeMin = 1;
                tmp.fontSize = tmp.fontSizeMax;
                tmp.enableWordWrapping = e.Wrap;
                tmp.overflowMode = TextOverflowModes.Truncate;
            }
            else if (mesh != null)
            {
                mesh.characterSize = e.Size * scale;
                Renderer renderer = mesh.GetComponent<Renderer>();
                if (renderer != null && russianFont != null && mesh.font == russianFont)
                {
                    if (e.RussianMaterial == null)
                    {
                        Shader shader = Shader.Find("GUI/3D Text Shader");
                        e.RussianMaterial = new Material(shader != null ? shader : e.OriginalMaterial.shader);
                    }
                    e.RussianMaterial.mainTexture = russianFont.material.mainTexture;
                    renderer.sharedMaterial = e.RussianMaterial;
                }
            }
        }

        private static bool IsTerrainLetter(Text ui)
        {
            Transform parent = ui.transform.parent;
            return ui.name == "Text" && parent != null && parent.name == "Canvas" && parent.parent != null && parent.parent.name == "MainTower";
        }

        internal static bool HasAncestor(Transform child, string name)
        {
            for (Transform t = child; t != null; t = t.parent)
                if (t.name == name || t.name == name + "(Clone)") return true;
            return false;
        }

        internal static bool IsLoadingText(Text ui)
        {
            return ui != null && ui.name == "Text" && HasAncestor(ui.transform, "LevelLoader");
        }

        private static bool IsLoadingTip(Text ui)
        {
            return ui.name == "TipText" && HasAncestor(ui.transform, "LevelLoader");
        }

        internal static bool IsWorldNumber(Text ui, string text)
        {
            if (ui == null || ui.canvas == null || ui.canvas.renderMode != RenderMode.WorldSpace) return false;
            return HasAncestor(ui.transform, "BuildingGhost") || HasAncestor(ui.transform, "DamageNumber") ||
                Regex.IsMatch(Regex.Replace(text ?? "", @"<[^>]+>", "").Trim(), @"^[+-]?\d+(?:[.,]\d+)?%?$");
        }

        private static Vector2 FreeLabelBounds(Text ui, bool loading, bool tip)
        {
            if (!loading && !tip) return new Vector2(2400, 600);
            RectTransform rect = ui.rectTransform;
            Camera camera = ui.canvas != null && ui.canvas.renderMode != RenderMode.ScreenSpaceOverlay ? ui.canvas.worldCamera : null;
            Vector2 origin = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(Vector3.zero));
            float sx = (RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(Vector3.right)) - origin).magnitude;
            float sy = (RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(Vector3.up)) - origin).magnitude;
            return new Vector2(Screen.width * 0.8f / Mathf.Max(0.001f, sx),
                Screen.height * (tip ? 0.07f : 0.2f) / Mathf.Max(0.001f, sy));
        }

        private bool Fits(Text ui, string text, int size, Vector2 bounds)
        {
            TextGenerationSettings settings = ui.GetGenerationSettings(bounds);
            // Measure at one size: requesting hundreds of candidate sizes
            // fills Unity's dynamic atlas and can evict visible glyphs.
            const int referenceSize = 64;
            float ratio = (float)size / referenceSize;
            settings = ui.GetGenerationSettings(bounds / ratio);
            settings.fontSize = referenceSize;
            settings.resizeTextForBestFit = false;
            settings.verticalOverflow = VerticalWrapMode.Overflow;
            float pixels = ui.pixelsPerUnit;
            if (ui.horizontalOverflow == HorizontalWrapMode.Overflow)
            {
                foreach (string line in (text ?? "").Split('\n'))
                    if (measure.GetPreferredWidth(line, settings) * ratio / pixels > bounds.x) return false;
            }
            return measure.GetPreferredHeight(text ?? "", settings) * ratio / pixels <= bounds.y;
        }

        private Vector2 AvailableBounds(Text ui)
        {
            RectTransform rect = ui.rectTransform;
            Vector2 size = rect.rect.size;
            bool panelDescription = ui.name == "test" && ui.transform.parent != null && ui.transform.parent.name == "Panel";
            bool overflowing = ui.horizontalOverflow == HorizontalWrapMode.Overflow || panelDescription;
            if (IsLoadingText(ui) || IsLoadingTip(ui) || HasAncestor(ui.transform, "MonsterBuffs"))
                return new Vector2(Mathf.Max(1, size.x - 4), Mathf.Max(1, size.y - 2));
            // Rogue Tower deliberately gives overflowing text a tiny scaled
            // rect (e.g. 160x30 at scale 0.1 inside a 160x30 button).
            // Its actual allocation is the parent background, in text-local units.
            RectTransform panel = rect.parent as RectTransform;
            while (panel != null && panel.GetComponent<Image>() == null && panel.GetComponent<Canvas>() == null)
                panel = panel.parent as RectTransform;
            if (panel != null && panel.GetComponent<Image>() != null && overflowing)
            {
                Vector3[] panelCorners = new Vector3[4];
                panel.GetWorldCorners(panelCorners);
                Vector3 lower = rect.InverseTransformPoint(panelCorners[0]);
                Vector3 upper = rect.InverseTransformPoint(panelCorners[2]);
                Rect r = rect.rect;
                int anchor = (int)ui.alignment;
                float x = anchor % 3 == 0 ? r.xMin : anchor % 3 == 1 ? r.center.x : r.xMax;
                float y = anchor / 3 == 0 ? r.yMax : anchor / 3 == 1 ? r.center.y : r.yMin;
                float w = anchor % 3 == 0 ? upper.x - x : anchor % 3 == 1 ? 2 * Mathf.Min(x - lower.x, upper.x - x) : x - lower.x;
                float h = anchor / 3 == 0 ? y - lower.y : anchor / 3 == 1 ? 2 * Mathf.Min(y - lower.y, upper.y - y) : upper.y - y;
                size = new Vector2(w > 8 ? w * 0.94f : size.x, h > 8 ? h * 0.9f : size.y);
            }
            else if (overflowing)
            {
                Entry entry;
                if (entries.TryGetValue(ui.GetInstanceID(), out entry))
                    size = Vector2.Max(size, entry.OriginalPreferred * 1.05f);
            }
            // Table columns often have rects wider than their allotted cells.
            // Stop at the next column rather than drawing into its numbers.
            Transform parent = rect.parent;
            if (parent != null && ui.canvas != null && ui.canvas.renderMode != RenderMode.WorldSpace)
            {
                Vector3[] corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                float left = corners[0].x, bottom = corners[0].y, top = corners[1].y;
                foreach (Transform child in parent)
                {
                    Text other = child.GetComponent<Text>();
                    if (other == null || other == ui || !other.isActiveAndEnabled) continue;
                    Vector3[] next = new Vector3[4];
                    other.rectTransform.GetWorldCorners(next);
                    // Titles and their descriptions need separate vertical space.
                    if (ui.alignment <= TextAnchor.UpperRight && next[1].y < top - 1 && next[2].x > left + 1 && next[0].x < corners[2].x - 1)
                    {
                        float height = rect.rect.yMax - rect.InverseTransformPoint(next[1]).y - 4;
                        if (height > 8) size.y = Mathf.Min(size.y, height);
                    }
                    bool tableColumns = (ui.text ?? "").Split('\n').Length >= 4 && (other.text ?? "").Split('\n').Length >= 4;
                    if (next[0].x > left + 0.01f && (tableColumns || (next[0].y < top && next[1].y > bottom)))
                    {
                        float right = rect.InverseTransformPoint(next[0]).x - rect.rect.xMin - 4;
                        if (right > 4) size.x = Mathf.Min(size.x, right);
                    }
                }
            }
            return new Vector2(Mathf.Max(1, size.x - 4), Mathf.Max(1, size.y - 2));
        }

        public void AlignTableRows(Text[] texts)
        {
            var groups = new Dictionary<Transform, List<Text>>();
            foreach (Text ui in texts)
            {
                // The game's statistics are one multiline Text per column.
                // A different font size in each column would misalign every row.
                if (ui == null || ui.transform.parent == null || String.IsNullOrEmpty(ui.text) || ui.text.Split('\n').Length < 4 || ui.horizontalOverflow != HorizontalWrapMode.Overflow) continue;
                List<Text> group;
                if (!groups.TryGetValue(ui.transform.parent, out group))
                    groups[ui.transform.parent] = group = new List<Text>();
                group.Add(ui);
            }
            foreach (var group in groups.Values)
            {
                if (group.Count < 2) continue;
                int size = Int32.MaxValue;
                foreach (Text ui in group) size = Math.Min(size, ui.fontSize);
                foreach (Transform child in group[0].transform.parent)
                {
                    Text ui = child.GetComponent<Text>();
                    if (ui != null && ui.transform.localScale == group[0].transform.localScale)
                        ui.fontSize = Math.Min(ui.fontSize, size);
                }
            }
        }

        public void Clean()
        {
            var dead = new List<int>();
            foreach (var pair in entries)
                if (pair.Value.Owner == null)
                {
                    if (pair.Value.RussianMaterial != null) UnityEngine.Object.Destroy(pair.Value.RussianMaterial);
                    dead.Add(pair.Key);
                }
            foreach (int id in dead) entries.Remove(id);
        }

        public List<Dictionary<string, object>> Inspect(Text[] texts)
        {
            var report = new List<Dictionary<string, object>>();
            foreach (Text ui in texts)
            {
                Vector2 bounds = AvailableBounds(ui);
                string path = ui.name;
                for (Transform parent = ui.transform.parent; parent != null; parent = parent.parent) path = parent.name + "/" + path;
                report.Add(new Dictionary<string, object> {
                    {"path", path}, {"text", ui.text}, {"fontSize", ui.fontSize},
                    {"width", bounds.x}, {"height", bounds.y}, {"wrap", ui.horizontalOverflow.ToString()},
                    {"fits", Fits(ui, ui.text, ui.fontSize, bounds)},
                    {"pixels", ui.pixelsPerUnit}, {"font", ui.font != null ? ui.font.name : "none"},
                    {"fontBaseSize", ui.font != null ? ui.font.fontSize : 0}, {"dynamic", ui.font != null && ui.font.dynamic},
                    {"bestFit", ui.resizeTextForBestFit}, {"generatedBestFit", ui.cachedTextGenerator.fontSizeUsedForBestFit},
                    {"vertices", ui.cachedTextGenerator.vertexCount},
                    {"texture", ui.mainTexture != null ? ui.mainTexture.name + " " + ui.mainTexture.width + "x" + ui.mainTexture.height : "none"},
                    {"fontTexture", ui.font != null && ui.font.material != null && ui.font.material.mainTexture != null ? ui.font.material.mainTexture.name + " " + ui.font.material.mainTexture.width + "x" + ui.font.material.mainTexture.height : "none"},
                    {"localScale", ui.transform.localScale.ToString()},
                    {"rect", ui.rectTransform.rect.size.ToString()},
                    {"anchoredPosition", ui.rectTransform.anchoredPosition.ToString()},
                    {"originalSize", entries.ContainsKey(ui.GetInstanceID()) ? entries[ui.GetInstanceID()].Size : ui.fontSize},
                    {"canvas", ui.canvas != null ? ui.canvas.renderMode.ToString() : "none"},
                    {"shader", ui.material != null && ui.material.shader != null ? ui.material.shader.name : "none"}
                });
            }
            return report;
        }
    }
}
