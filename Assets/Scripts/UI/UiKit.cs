using UnityEngine;
using UnityEngine.UI;

namespace FpsGame.UI
{
    /// <summary>运行时 UI 构建工具：统一字体与基础控件创建。</summary>
    public static class UiKit
    {
        private static Font _font;

        public static Font Font
        {
            get
            {
                if (_font == null)
                {
                    _font = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 16);
                    if (_font == null) _font = Font.CreateDynamicFontFromOSFont("SimHei", 16);
                    if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
                return _font;
            }
        }

        /// <summary>把自身矩形铺满父级（修复脚本创建的容器默认 100x100 居中的问题）。</summary>
        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// 取 UI 根矩形：自身是 RectTransform 则铺满返回；否则（旧场景遗留普通 Transform）
        /// 在所属 Canvas 下新建一个铺满的容器返回。
        /// </summary>
        public static RectTransform EnsureRoot(Component self, string fallbackName)
        {
            if (self.transform is RectTransform rt)
            {
                Stretch(rt);
                return rt;
            }
            Canvas canvas = self.GetComponentInParent<Canvas>();
            var go = new GameObject(fallbackName, typeof(RectTransform));
            var root = (RectTransform)go.transform;
            root.SetParent(canvas != null ? canvas.transform : null, false);
            Stretch(root);
            return root;
        }

        /// <summary>创建铺满父级的容器（可选底色）。</summary>
        public static RectTransform FullRect(Transform parent, string name, Color color)
        {
            var rt = NewRect(parent, name);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            if (color.a > 0f) AddImage(rt, color);
            return rt;
        }

        /// <summary>按锚点+中心位置+尺寸创建矩形。</summary>
        public static RectTransform Place(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var rt = NewRect(parent, name);
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static Image AddImage(RectTransform rt, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        public static Text MakeText(Transform parent, string name, string content, int size,
            TextAnchor align, Vector2 pos, Vector2 size2, Color? color = null)
        {
            var rt = Place(parent, name, new Vector2(0.5f, 0.5f), pos, size2);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = Font;
            text.text = content;
            text.fontSize = size;
            text.alignment = align;
            text.color = color ?? Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        public static Button MakeButton(Transform parent, string name, string label, Vector2 pos, Vector2 size, int fontSize = 18)
        {
            var rt = Place(parent, name, new Vector2(0.5f, 0.5f), pos, size);
            AddImage(rt, new Color(0.24f, 0.5f, 0.86f, 1f));

            var button = rt.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = new Color(0.24f, 0.5f, 0.86f, 1f);
            colors.highlightedColor = new Color(0.33f, 0.6f, 0.95f, 1f);
            colors.pressedColor = new Color(0.16f, 0.38f, 0.7f, 1f);
            colors.selectedColor = colors.normalColor;
            colors.disabledColor = new Color(0.4f, 0.4f, 0.4f, 1f);
            button.colors = colors;

            var text = MakeText(rt, "Label", label, fontSize, TextAnchor.MiddleCenter, Vector2.zero, size);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.anchoredPosition = Vector2.zero;
            text.rectTransform.sizeDelta = Vector2.zero;
            text.raycastTarget = false;
            return button;
        }

        public static InputField MakeInput(Transform parent, string name, string placeholder,
            Vector2 pos, Vector2 size, int fontSize = 16)
        {
            var rt = Place(parent, name, new Vector2(0.5f, 0.5f), pos, size);
            AddImage(rt, new Color(0.13f, 0.14f, 0.18f, 1f));

            var input = rt.gameObject.AddComponent<InputField>();

            var textRt = Place(rt, "Text", new Vector2(0f, 0f), Vector2.zero, size - new Vector2(14f, 6f));
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(8f, 3f);
            textRt.offsetMax = new Vector2(-8f, -3f);
            var text = textRt.gameObject.AddComponent<Text>();
            text.font = Font;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.white;
            text.supportRichText = false;

            var phRt = Place(rt, "Placeholder", new Vector2(0f, 0f), Vector2.zero, size);
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = new Vector2(8f, 3f);
            phRt.offsetMax = new Vector2(-8f, -3f);
            var ph = phRt.gameObject.AddComponent<Text>();
            ph.font = Font;
            ph.fontSize = fontSize;
            ph.alignment = TextAnchor.MiddleLeft;
            ph.color = new Color(1f, 1f, 1f, 0.35f);
            ph.text = placeholder;

            input.textComponent = text;
            input.placeholder = ph;
            input.text = "";
            input.caretColor = Color.white;
            input.selectionColor = new Color(1f, 1f, 1f, 0.3f);
            return input;
        }

        private static RectTransform NewRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }
    }
}
