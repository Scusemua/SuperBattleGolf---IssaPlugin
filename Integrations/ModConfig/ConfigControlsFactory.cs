using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ModConfig.UI;

namespace IssaPlugin.Integrations.ModConfigUI
{
    /// <summary>
    /// Builds the search/filter control widgets.
    ///
    /// Where possible these clone ModConfig's own UICloner templates so the controls
    /// inherit the game's fonts, sprites, and colors rather than looking bolted on.
    /// The search field is the exception: ModConfig's CreateStringInput is a
    /// click-to-edit widget that only commits on onEndEdit, which is wrong for a
    /// search box, so that one is built directly against TMP_InputField.onValueChanged.
    /// </summary>
    internal static class ConfigControlsFactory
    {
        /// <summary>
        /// Reports a control we could not build. These are non-fatal — the rest of the
        /// page still works — but they must never fail silently, since a missing search
        /// box with no log entry is indistinguishable from the feature not running.
        /// </summary>
        private static void Warn(string control, string reason) =>
            IssaPluginPlugin.Log.LogWarning(
                $"[ModConfig] Could not build the {control}: {reason}. "
                    + "The rest of the config page is unaffected.");

        /// <summary>
        /// A "nothing matched" label. Without this a filtered-to-empty page is just
        /// blank, which reads as the UI being broken rather than the query being too
        /// narrow. Starts hidden; the enhancer toggles it.
        /// </summary>
        public static TextMeshProUGUI CreateEmptyStateLabel(Transform content)
        {
            if (UICloner.titleTMP == null) return null;

            var labelObj = UnityEngine.Object.Instantiate(UICloner.titleTMP, content);
            labelObj.name = "ISSA_EMPTY_STATE";

            var text = labelObj.GetComponent<TextMeshProUGUI>();
            if (text == null)
            {
                UnityEngine.Object.DestroyImmediate(labelObj);
                return null;
            }

            text.fontSize -= 6;
            text.fontSizeMin -= 6;
            text.fontSizeMax -= 6;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;

            labelObj.SetActive(false);
            return text;
        }

        /// <summary>
        /// Container for the controls, placed at the top of the scroll content but
        /// below ModConfig's page title so the page still reads title-first.
        /// </summary>
        public static GameObject CreateControlStrip(Transform content)
        {
            var strip = new GameObject(
                "ISSA_CONTROL_STRIP",
                typeof(RectTransform),
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));

            strip.transform.SetParent(content, false);

            // ModConfig emits TITLE_{modName} first; sit directly after it when present.
            int insertAt = 0;
            for (int i = 0; i < content.childCount; i++)
            {
                if (content.GetChild(i).name.StartsWith("TITLE_"))
                {
                    insertAt = i + 1;
                    break;
                }
            }
            strip.transform.SetSiblingIndex(insertAt);

            var layout = strip.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.padding = new RectOffset(0, 0, 0, 16);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            // The page content uses childControlHeight, so it sizes the strip by the
            // strip's own preferred height. Without a fitter that reports 0 and every
            // control inside collapses onto the rows below it.
            strip.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            return strip;
        }

        /// <summary>
        /// A live search field. Clones the slider template for its frame, strips the
        /// slider parts, and drops an always-active TMP_InputField into the value area.
        /// </summary>
        public static void CreateSearchField(Transform parent, string placeholder, Action<string> onChanged)
        {
            GameObject row = CloneRowFrame(parent, "ISSA_SEARCH", "Search");
            if (row == null)
            {
                Warn("search field", "the slider template was unavailable");
                return;
            }

            Transform contents = row.transform.Find("Option Contents");
            Transform valueText = contents == null ? null : contents.Find("Slider Value");
            if (contents == null || valueText == null)
            {
                // Discard the half-built row rather than leaving a stray empty
                // control on the page.
                Warn("search field", "the cloned row was missing 'Option Contents/Slider Value'");
                UnityEngine.Object.DestroyImmediate(row);
                return;
            }

            if (contents.GetComponent<RectMask2D>() == null)
                contents.gameObject.AddComponent<RectMask2D>();

            // The template's own value label is reused only as a style source; the field
            // below owns the visible text, so hide it to avoid a doubled-up label.
            var templateText = valueText.GetComponent<TextMeshProUGUI>();
            var valueRect = valueText.GetComponent<RectTransform>();
            valueText.gameObject.SetActive(false);

            // Stretch the editable area across the row so there is room to type.
            var fieldArea = new Vector4(10f, 0f, -10f, 0f);

            // Mirror ModConfig's own string-input layout: TMP_InputField lives on a child
            // that also carries its textComponent, with textViewport pointing at the
            // parent. Putting the field and its viewport on the same object makes
            // TMP_InputField manage a rect it is also clipped by, which glitches the
            // caret and scrolling.
            var textView = new GameObject("ISSA_SEARCH_TEXT", typeof(RectTransform));
            textView.transform.SetParent(contents, false);
            StretchRect(textView.GetComponent<RectTransform>(), valueRect, fieldArea);

            var textComponent = textView.AddComponent<TextMeshProUGUI>();
            CopyTextStyle(templateText, textComponent);
            textComponent.text = string.Empty;
            textComponent.raycastTarget = true;

            var placeholderObj = new GameObject("ISSA_SEARCH_PLACEHOLDER", typeof(RectTransform));
            placeholderObj.transform.SetParent(contents, false);
            StretchRect(placeholderObj.GetComponent<RectTransform>(), valueRect, fieldArea);

            var placeholderText = placeholderObj.AddComponent<TextMeshProUGUI>();
            CopyTextStyle(templateText, placeholderText);
            placeholderText.text = placeholder;
            placeholderText.color = new Color(
                templateText.color.r, templateText.color.g, templateText.color.b, 0.45f);
            placeholderText.raycastTarget = false;

            var inputField = textView.AddComponent<TMP_InputField>();
            inputField.textViewport = contents.GetComponent<RectTransform>();
            inputField.textComponent = textComponent;
            inputField.placeholder = placeholderText;
            inputField.contentType = TMP_InputField.ContentType.Standard;
            inputField.lineType = TMP_InputField.LineType.SingleLine;
            inputField.targetGraphic = textComponent;
            inputField.interactable = true;
            inputField.richText = false;
            inputField.SetTextWithoutNotify(string.Empty);

            // Live filtering: fire on every keystroke rather than on commit.
            inputField.onValueChanged.AddListener(value => onChanged?.Invoke(value));
        }

        /// <summary>The item/group filter, cloned from ModConfig's dropdown template.</summary>
        public static void CreateFilterDropdown(Transform parent, List<string> options, Action<int> onChanged)
        {
            if (UICloner.dropdown == null)
            {
                Warn("filter dropdown", "the dropdown template was unavailable");
                return;
            }

            var row = UnityEngine.Object.Instantiate(UICloner.dropdown, parent);
            row.name = "ISSA_FILTER";
            SetRowHeight(row);

            Transform label = row.transform.Find("Label Text");
            if (label != null)
            {
                var tmp = label.GetComponent<TextMeshProUGUI>();
                if (tmp != null) tmp.text = "Show";
            }

            var dropdownOption = row.GetComponent<DropdownOption>();
            if (dropdownOption == null)
            {
                Warn("filter dropdown", "the cloned row had no DropdownOption component");
                UnityEngine.Object.DestroyImmediate(row);
                return;
            }

            // No need to clear `localized` here (ModConfig does, but that field is private
            // in the shipped game assembly): our options are plain strings and SetOptions
            // does not consult the localizer.
            dropdownOption.SetOptions(options);

            // Bind to the underlying TMP_Dropdown rather than DropdownOption.onChanged:
            // onChanged is a private field on the game's DropdownOption, whereas
            // TMP_Dropdown.onValueChanged is public and stable.
            TMP_Dropdown dropdown = dropdownOption.Dropdown;
            if (dropdown != null)
            {
                dropdown.SetValueWithoutNotify(0);
                dropdown.onValueChanged.AddListener(index => onChanged?.Invoke(index));
            }
        }

        /// <summary>Expand All / Collapse All, side by side.</summary>
        public static void CreateButtonRow(Transform parent, Action onExpandAll, Action onCollapseAll)
        {
            if (UICloner.button == null)
            {
                Warn("expand/collapse buttons", "the button template was unavailable");
                return;
            }

            var rowObj = new GameObject(
                "ISSA_BUTTON_ROW",
                typeof(RectTransform),
                typeof(HorizontalLayoutGroup),
                typeof(LayoutElement));

            rowObj.transform.SetParent(parent, false);

            var layout = rowObj.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleCenter;

            SetRowHeight(rowObj);

            CreateButton(rowObj.transform, "Expand All", onExpandAll);
            CreateButton(rowObj.transform, "Collapse All", onCollapseAll);
        }

        private static void CreateButton(Transform parent, string label, Action action)
        {
            var buttonObj = UnityEngine.Object.Instantiate(UICloner.button, parent);
            buttonObj.name = $"ISSA_BUTTON_{label}";

            var text = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null) text.text = label;

            var button = buttonObj.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => action?.Invoke());
            }

            buttonObj.transform.localPosition = Vector3.zero;
        }

        /// <summary>
        /// Clones the slider template and strips the slider machinery, leaving a
        /// labelled row frame we can put our own control into.
        /// </summary>
        private static GameObject CloneRowFrame(Transform parent, string name, string label)
        {
            if (UICloner.slider == null) return null;

            var row = UnityEngine.Object.Instantiate(UICloner.slider, parent);
            row.name = name;
            SetRowHeight(row);

            Transform labelText = row.transform.Find("Label Text");
            if (labelText != null)
            {
                var tmp = labelText.GetComponent<TextMeshProUGUI>();
                if (tmp != null) tmp.text = label;
            }

            var sliderOption = row.GetComponent<SliderOption>();
            if (sliderOption != null) UnityEngine.Object.DestroyImmediate(sliderOption);

            Transform contents = row.transform.Find("Option Contents");
            if (contents != null)
            {
                DestroyChild(contents, "Slider");
                DestroyChild(contents, "Caret");
            }

            return row;
        }

        private static void DestroyChild(Transform parent, string childName)
        {
            Transform child = parent.Find(childName);
            if (child != null) UnityEngine.Object.DestroyImmediate(child.gameObject);
        }

        /// <summary>
        /// Stretches <paramref name="target"/> across its parent, inset by
        /// <paramref name="padding"/> (left, bottom, right, top). The source rect only
        /// supplies the pivot so the cloned styling stays consistent.
        /// </summary>
        private static void StretchRect(RectTransform target, RectTransform source, Vector4 padding)
        {
            target.pivot = source.pivot;
            target.anchorMin = Vector2.zero;
            target.anchorMax = Vector2.one;
            target.offsetMin = new Vector2(padding.x, padding.y);
            target.offsetMax = new Vector2(padding.z, padding.w);
        }

        /// <summary>
        /// Height of one control row, matching the game's own option rows.
        /// </summary>
        private const float RowHeight = 55f;

        /// <summary>
        /// Pins a row to <see cref="RowHeight"/>. The cloned templates are normally laid
        /// out by the game's own option container; inside our strip nothing else supplies
        /// a height, so without this they collapse and overlap each other.
        /// </summary>
        private static void SetRowHeight(GameObject row, float height = RowHeight)
        {
            var element = row.GetComponent<LayoutElement>();
            if (element == null) element = row.AddComponent<LayoutElement>();

            element.minHeight = height;
            element.preferredHeight = height;
            element.flexibleHeight = 0f;
        }

        /// <summary>Copies the font styling from a template label onto a new one.</summary>
        private static void CopyTextStyle(TextMeshProUGUI source, TextMeshProUGUI target)
        {
            if (source == null) return;

            target.font = source.font;
            target.fontSize = source.fontSize;
            target.fontSizeMin = source.fontSizeMin;
            target.fontSizeMax = source.fontSizeMax;
            target.fontStyle = source.fontStyle;
            target.color = source.color;
            target.alignment = TextAlignmentOptions.Left;
            target.textWrappingMode = TextWrappingModes.NoWrap;
            target.overflowMode = TextOverflowModes.Masking;
        }
    }
}
