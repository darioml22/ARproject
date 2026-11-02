using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Xrqr.Unity
{
    /// <summary>
    /// The client's screen-space panel, built in code so that the scene needs no prefab: a
    /// toolbar, a status line, a card with the thumbnail and the facts a user checks before
    /// placing (title, publisher, resolver), a text field for typing a code or pasting a
    /// resolver entry, and a column of action buttons.
    /// </summary>
    public sealed class XrqrPanel
    {
        static readonly Color s_Background = new Color(0.08f, 0.08f, 0.1f, 0.88f);
        static readonly Color s_Button = new Color(0.22f, 0.24f, 0.3f, 1f);
        static readonly Color s_Accent = new Color(0.16f, 0.45f, 0.85f, 1f);
        static readonly Color s_Warning = new Color(1f, 0.75f, 0.3f, 1f);

        readonly GameObject m_Root;
        readonly TextMeshProUGUI m_Status;
        readonly GameObject m_Card;
        readonly RawImage m_Thumbnail;
        readonly TextMeshProUGUI m_Title;
        readonly TextMeshProUGUI m_Details;
        readonly GameObject m_InputRow;
        readonly TMP_InputField m_Input;
        readonly RectTransform m_Actions;

        /// <summary>Raised with the text of the input field when the user submits it.</summary>
        public event Action<string> Submitted;

        public XrqrPanel(Action onScan, Action onResolvers)
        {
            m_Root = new GameObject("XRQR UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = m_Root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = m_Root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            var panel = Panel("Panel", m_Root.transform);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.offsetMin = new Vector2(32, 0);
            rect.offsetMax = new Vector2(-32, -140);
            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 24, 24);
            layout.spacing = 18;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var toolbar = Row("Toolbar", panel.transform, 16);
            Label("XRQR", toolbar.transform, 40, FontStyles.Bold).gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            Button("Scan", toolbar.transform, s_Button, onScan, 180);
            Button("Type", toolbar.transform, s_Button, () => m_InputRow.SetActive(!m_InputRow.activeSelf), 180);
            Button("Resolvers", toolbar.transform, s_Button, onResolvers, 240);

            m_Status = Label("Point the camera at an XRQR code.", panel.transform, 34, FontStyles.Normal);

            m_Card = Row("Card", panel.transform, 24);
            var thumbnail = new GameObject("Thumbnail", typeof(RectTransform), typeof(RawImage));
            thumbnail.transform.SetParent(m_Card.transform, false);
            m_Thumbnail = thumbnail.GetComponent<RawImage>();
            var thumbnailSize = thumbnail.AddComponent<LayoutElement>();
            thumbnailSize.preferredWidth = thumbnailSize.preferredHeight = 220;
            thumbnailSize.minWidth = thumbnailSize.minHeight = 220;
            var texts = new GameObject("Texts", typeof(RectTransform));
            texts.transform.SetParent(m_Card.transform, false);
            texts.AddComponent<LayoutElement>().flexibleWidth = 1;
            var textLayout = texts.AddComponent<VerticalLayoutGroup>();
            textLayout.spacing = 8;
            textLayout.childControlWidth = textLayout.childControlHeight = true;
            textLayout.childForceExpandHeight = false;
            m_Title = Label(string.Empty, texts.transform, 42, FontStyles.Bold);
            m_Details = Label(string.Empty, texts.transform, 30, FontStyles.Normal);
            m_Card.SetActive(false);

            m_InputRow = Row("Input", panel.transform, 16);
            m_Input = InputField(m_InputRow.transform);
            Button("Go", m_InputRow.transform, s_Accent, () => Submitted?.Invoke(m_Input.text), 140);
            m_InputRow.SetActive(false);

            var actions = new GameObject("Actions", typeof(RectTransform));
            actions.transform.SetParent(panel.transform, false);
            var actionLayout = actions.AddComponent<VerticalLayoutGroup>();
            actionLayout.spacing = 12;
            actionLayout.childControlWidth = actionLayout.childControlHeight = true;
            actionLayout.childForceExpandHeight = false;
            m_Actions = (RectTransform)actions.transform;
        }

        public void SetStatus(string text, bool warning = false)
        {
            m_Status.text = text;
            m_Status.color = warning ? s_Warning : Color.white;
        }

        /// <summary>Shows a model's card; <paramref name="thumbnail"/> may be <c>null</c>.</summary>
        public void ShowCard(Texture thumbnail, string title, string details)
        {
            m_Thumbnail.texture = thumbnail;
            m_Thumbnail.gameObject.SetActive(thumbnail != null);
            m_Title.text = title;
            m_Details.text = details;
            m_Card.SetActive(true);
        }

        public void HideCard() => m_Card.SetActive(false);

        /// <summary>Replaces the action buttons; each pair is a label and its action.</summary>
        public void SetActions(params (string label, Action action)[] actions)
        {
            for (var i = m_Actions.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(m_Actions.GetChild(i).gameObject);
            for (var i = 0; i < actions.Length; i++)
                Button(actions[i].label, m_Actions, i == 0 ? s_Accent : s_Button, actions[i].action, 0);
        }

        public void ClearInput() => m_Input.text = string.Empty;

        public void Destroy() => UnityEngine.Object.Destroy(m_Root);

        static GameObject Panel(string name, Transform parent)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            panel.GetComponent<Image>().color = s_Background;
            return panel;
        }

        static GameObject Row(string name, Transform parent, float spacing)
        {
            var row = new GameObject(name, typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return row;
        }

        static TextMeshProUGUI Label(string text, Transform parent, float size, FontStyles style)
        {
            var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            label.transform.SetParent(parent, false);
            var tmp = label.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.color = Color.white;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.raycastTarget = false;
            return tmp;
        }

        static void Button(string text, Transform parent, Color color, Action action, float width)
        {
            var button = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button));
            button.transform.SetParent(parent, false);
            button.GetComponent<Image>().color = color;
            button.GetComponent<Button>().onClick.AddListener(() => action());
            var size = button.AddComponent<LayoutElement>();
            size.minHeight = size.preferredHeight = 96;
            if (width > 0)
                size.minWidth = size.preferredWidth = width;
            var label = Label(text, button.transform, 32, FontStyles.Bold);
            label.alignment = TextAlignmentOptions.Center;
            var rect = (RectTransform)label.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(12, 0);
            rect.offsetMax = new Vector2(-12, 0);
        }

        static TMP_InputField InputField(Transform parent)
        {
            var field = new GameObject("Field", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            field.transform.SetParent(parent, false);
            field.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
            var size = field.AddComponent<LayoutElement>();
            size.flexibleWidth = 1;
            size.minHeight = size.preferredHeight = 96;

            var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            area.transform.SetParent(field.transform, false);
            var areaRect = (RectTransform)area.transform;
            areaRect.anchorMin = Vector2.zero;
            areaRect.anchorMax = Vector2.one;
            areaRect.offsetMin = new Vector2(16, 8);
            areaRect.offsetMax = new Vector2(-16, -8);

            var placeholder = Label("XRQR:... or resolver entry JSON", area.transform, 30, FontStyles.Italic);
            placeholder.color = new Color(1f, 1f, 1f, 0.45f);
            var text = Label(string.Empty, area.transform, 30, FontStyles.Normal);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            foreach (var child in new[] { placeholder, text })
            {
                var rect = (RectTransform)child.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
            }

            var input = field.GetComponent<TMP_InputField>();
            input.textViewport = areaRect;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.pointSize = 30;
            return input;
        }
    }
}
