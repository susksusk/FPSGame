using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FpsGame.UI
{
    /// <summary>
    /// 手游触屏控制 UI：
    /// 左区浮动摇杆（按在哪摇杆出现在哪）+ 右侧拖动转视角 + 右下弧形按钮群
    /// （开火按住 / 跳跃 / 开镜或重击 / 换弹 / 切枪）。
    /// 按钮为程序生成的圆形贴图 + 图标（无需任何美术资源）；
    /// 鼠标同样可操作（EventSystem 兼容），M 键整体开关；暂停时自动隐藏。
    /// </summary>
    public class MobileUI : MonoBehaviour
    {
        public static MobileUI Instance { get; private set; }
        public static bool UserOn = true; // 玩家偏好：M 键切换手游控制层
        public static bool Visible => Instance != null && Instance._root.gameObject.activeSelf;

        private RectTransform _root;
        private Sprite _btnBase; // 圆形按钮底图（所有按钮共用）

        /// <summary>由 HudUI.EnterGame 调用：挂到 HUD 的 Canvas 下（与 HUD 同层渲染）。</summary>
        public static void Ensure()
        {
            if (Instance != null) return;
            Canvas canvas = HudUI.Instance != null ? HudUI.Instance.GetComponentInParent<Canvas>() : null;
            var go = new GameObject("MobileUI", typeof(RectTransform));
            go.transform.SetParent(canvas != null ? canvas.transform : null, false);
            go.AddComponent<MobileUI>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _root = (RectTransform)transform;
            UiKit.Stretch(_root); // 铺满 Canvas
            Build();
            _root.gameObject.SetActive(false); // 显隐由 HudUI 统一控制
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SetVisible(bool on)
        {
            if (_root.gameObject.activeSelf == on) return;
            _root.gameObject.SetActive(on);
            if (!on) MobileInput.ResetAll(); // 隐藏时清空输入，防止卡移动/卡开火
        }

        private void Build()
        {
            _btnBase = PaintButtonBase();

            // ---- 左侧摇杆触控区（左 38% 全高）：按下时摇杆浮到手指处 ----
            var zone = UiKit.Place(_root, "JoyZone", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            zone.anchorMin = new Vector2(0f, 0f);
            zone.anchorMax = new Vector2(0.38f, 1f);
            zone.offsetMin = Vector2.zero;
            zone.offsetMax = Vector2.zero;
            UiKit.AddImage(zone, new Color(0f, 0f, 0f, 0f)); // 透明图仍可接收事件

            var ringRt = UiKit.Place(zone, "JoyRing", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(180f, 180f));
            var ringImg = UiKit.AddImage(ringRt, new Color(1f, 1f, 1f, 0.65f));
            ringImg.sprite = PaintJoyRing();
            var knobRt = UiKit.Place(ringRt, "JoyKnob", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(66f, 66f));
            var knobImg = UiKit.AddImage(knobRt, new Color(0.92f, 0.95f, 1f, 1f));
            knobImg.sprite = PaintJoyKnob();

            var joy = zone.gameObject.AddComponent<VirtualJoystick>();
            joy.ring = ringRt;
            joy.knob = knobRt;
            joy.ringImage = ringImg;

            // ---- 右侧视角拖动区（透明 Image 才能接收事件）----
            var look = UiKit.Place(_root, "LookArea", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            look.anchorMin = new Vector2(0.38f, 0f);
            look.anchorMax = new Vector2(1f, 1f);
            look.offsetMin = Vector2.zero;
            look.offsetMax = Vector2.zero;
            UiKit.AddImage(look, new Color(0f, 0f, 0f, 0f));
            look.gameObject.AddComponent<LookDrag>();

            // ---- 右下弧形按钮群：大拇指自然弧线，主开火键最大最醒目 ----
            var slate = new Color(0.19f, 0.23f, 0.31f);
            var fire = MakeTouchButton("Btn_Fire", new Vector2(1f, 0f), new Vector2(-165f, 180f), 132f,
                new Color(0.96f, 0.36f, 0.26f), PaintIconFire(), null);
            fire.gameObject.AddComponent<HoldFire>(); // 按住持续开火，不用 onClick
            MakeTouchButton("Btn_Jump", new Vector2(1f, 0f), new Vector2(-95f, 335f), 80f, slate, PaintIconJump(), "跳跃")
                .onClick.AddListener(MobileInput.SetJump);
            MakeTouchButton("Btn_Aim", new Vector2(1f, 0f), new Vector2(-300f, 320f), 86f, slate, PaintIconAim(), "开镜")
                .onClick.AddListener(MobileInput.SetAim);
            MakeTouchButton("Btn_Reload", new Vector2(1f, 0f), new Vector2(-345f, 155f), 74f, slate, PaintIconReload(), "换弹")
                .onClick.AddListener(MobileInput.SetReload);
            MakeTouchButton("Btn_Switch", new Vector2(1f, 0f), new Vector2(-450f, 255f), 66f, slate, PaintIconSwitch(), "切枪")
                .onClick.AddListener(MobileInput.SetSwitch);

            // 右上角暂停（触屏设备没有 Esc 键）
            MakeTouchButton("Btn_Pause", new Vector2(1f, 1f), new Vector2(-56f, -56f), 56f, slate, PaintIconPause(), null)
                .onClick.AddListener(() => { if (HudUI.Instance != null) HudUI.Instance.SetPause(true); });
        }

        /// <summary>圆形按钮：圆形底图 + 居中图标 + 底部小字标签（fire 无标签图标更大）。</summary>
        private Button MakeTouchButton(string name, Vector2 anchor, Vector2 pos, float size, Color tint, Sprite icon, string label)
        {
            var rt = UiKit.Place(_root, name, anchor, pos, new Vector2(size, size));
            var img = UiKit.AddImage(rt, tint);
            img.sprite = _btnBase;

            var b = rt.gameObject.AddComponent<Button>();
            var c = b.colors; // ColorTint 与 image.color 相乘实现按压反馈
            c.normalColor = new Color(0.94f, 0.95f, 1f, 1f);
            c.highlightedColor = Color.white;
            c.pressedColor = new Color(0.62f, 0.62f, 0.68f, 1f);
            c.selectedColor = c.normalColor;
            c.fadeDuration = 0.06f;
            b.colors = c;

            if (icon != null)
            {
                float iconSize = label != null ? size * 0.42f : size * 0.52f;
                var iconRt = UiKit.Place(rt, "Icon", new Vector2(0.5f, 0.5f),
                    new Vector2(0f, label != null ? size * 0.10f : 0f), new Vector2(iconSize, iconSize));
                var ii = UiKit.AddImage(iconRt, new Color(1f, 1f, 1f, 0.95f));
                ii.sprite = icon;
                ii.raycastTarget = false;
            }
            if (label != null)
            {
                UiKit.MakeText(rt, "Label", label, Mathf.Max(11, Mathf.RoundToInt(size * 0.17f)),
                    TextAnchor.MiddleCenter, new Vector2(0f, -size * 0.28f), new Vector2(size, size * 0.24f),
                    new Color(1f, 1f, 1f, 0.92f));
            }
            return b;
        }

        // ---------- 程序化贴图（运行时生成白色形状，用 image.color 染色） ----------

        /// <summary>圆形按钮底：半透明圆盘 + 亮描边。</summary>
        private static Sprite PaintButtonBase()
        {
            var p = new Painter(128);
            p.Disc(64f, 64f, 61f, 0.72f);
            p.Ring(64f, 64f, 58f, 2.5f, 1f);
            return p.Bake();
        }

        /// <summary>摇杆外圈：微透内盘 + 主环 + 四向刻度。</summary>
        private static Sprite PaintJoyRing()
        {
            var p = new Painter(180);
            p.Disc(90f, 90f, 84f, 0.05f);
            p.Ring(90f, 90f, 80f, 8f, 0.85f);
            p.Line(90f, 146f, 90f, 158f, 5f, 0.9f); // 上
            p.Line(90f, 34f, 90f, 22f, 5f, 0.9f);   // 下
            p.Line(146f, 90f, 158f, 90f, 5f, 0.9f); // 右
            p.Line(34f, 90f, 22f, 90f, 5f, 0.9f);   // 左
            return p.Bake();
        }

        /// <summary>摇杆头：圆盘 + 边缘 + 中心点。</summary>
        private static Sprite PaintJoyKnob()
        {
            var p = new Painter(66);
            p.Disc(33f, 33f, 31f, 0.55f);
            p.Ring(33f, 33f, 30f, 3f, 0.95f);
            p.Disc(33f, 33f, 7f, 0.95f);
            return p.Bake();
        }

        /// <summary>开火图标：准星（环 + 中心点 + 四刻线）。</summary>
        private static Sprite PaintIconFire()
        {
            var p = new Painter(96);
            p.Disc(48f, 48f, 7f);
            p.Ring(48f, 48f, 30f, 7f);
            p.Line(48f, 10f, 48f, 24f, 6f);
            p.Line(48f, 72f, 48f, 86f, 6f);
            p.Line(10f, 48f, 24f, 48f, 6f);
            p.Line(72f, 48f, 86f, 48f, 6f);
            return p.Bake();
        }

        /// <summary>跳跃图标：上指双折线。</summary>
        private static Sprite PaintIconJump()
        {
            var p = new Painter(96);
            p.Line(24f, 54f, 48f, 78f, 10f);
            p.Line(48f, 78f, 72f, 54f, 10f);
            p.Line(32f, 24f, 48f, 40f, 9f);
            p.Line(48f, 40f, 64f, 24f, 9f);
            return p.Bake();
        }

        /// <summary>开镜图标：瞄具（环 + 上下左右开口十字）。</summary>
        private static Sprite PaintIconAim()
        {
            var p = new Painter(96);
            p.Ring(48f, 48f, 34f, 8f);
            p.Disc(48f, 48f, 5f);
            p.Line(48f, 8f, 48f, 26f, 7f);
            p.Line(48f, 70f, 48f, 88f, 7f);
            p.Line(8f, 48f, 26f, 48f, 7f);
            p.Line(70f, 48f, 88f, 48f, 7f);
            return p.Bake();
        }

        /// <summary>换弹图标：向下箭头入弹匣托。</summary>
        private static Sprite PaintIconReload()
        {
            var p = new Painter(96);
            p.Line(48f, 30f, 48f, 70f, 10f); // 箭杆
            p.Line(28f, 52f, 48f, 70f, 10f); // 箭头左
            p.Line(68f, 52f, 48f, 70f, 10f); // 箭头右
            p.Line(24f, 14f, 72f, 14f, 9f);  // 托底
            p.Line(24f, 14f, 24f, 28f, 9f);  // 左沿
            p.Line(72f, 14f, 72f, 28f, 9f);  // 右沿
            return p.Bake();
        }

        /// <summary>暂停图标：双竖线。</summary>
        private static Sprite PaintIconPause()
        {
            var p = new Painter(96);
            p.Line(38f, 26f, 38f, 70f, 11f);
            p.Line(58f, 26f, 58f, 70f, 11f);
            return p.Bake();
        }

        /// <summary>切枪图标：左右对向双箭头。</summary>
        private static Sprite PaintIconSwitch()
        {
            var p = new Painter(96);
            p.Line(20f, 60f, 68f, 60f, 8f);
            p.Line(50f, 74f, 68f, 60f, 8f);
            p.Line(50f, 46f, 68f, 60f, 8f);
            p.Line(76f, 34f, 28f, 34f, 8f);
            p.Line(46f, 20f, 28f, 34f, 8f);
            p.Line(46f, 48f, 28f, 34f, 8f);
            return p.Bake();
        }

        /// <summary>像素画板：白色形状 + alpha 覆盖混合，烘焙成 Sprite。</summary>
        private sealed class Painter
        {
            private readonly int _n;
            private readonly float[] _a;

            public Painter(int n)
            {
                _n = n;
                _a = new float[n * n];
            }

            public void Disc(float cx, float cy, float r, float alpha = 1f)
            {
                for (int y = 0; y < _n; y++)
                    for (int x = 0; x < _n; x++)
                    {
                        float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                        float c = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f) * alpha; // 1px 抗锯齿
                        Plot(x, y, c);
                    }
            }

            public void Ring(float cx, float cy, float r, float w, float alpha = 1f)
            {
                for (int y = 0; y < _n; y++)
                    for (int x = 0; x < _n; x++)
                    {
                        float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float c = Mathf.Clamp01(w * 0.5f - Mathf.Abs(d - r) + 0.5f) * alpha;
                        Plot(x, y, c);
                    }
            }

            public void Line(float x0, float y0, float x1, float y1, float w, float alpha = 1f)
            {
                for (int y = 0; y < _n; y++)
                    for (int x = 0; x < _n; x++)
                    {
                        float d = DistSeg(x + 0.5f, y + 0.5f, x0, y0, x1, y1);
                        float c = Mathf.Clamp01(w * 0.5f - d + 0.5f) * alpha;
                        Plot(x, y, c);
                    }
            }

            public Sprite Bake()
            {
                var tex = new Texture2D(_n, _n, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                var px = new Color[_a.Length];
                for (int i = 0; i < px.Length; i++) px[i] = new Color(1f, 1f, 1f, _a[i]); // 白色 + alpha，染色交给 Image
                tex.SetPixels(px);
                tex.Apply();
                return Sprite.Create(tex, new Rect(0f, 0f, _n, _n), new Vector2(0.5f, 0.5f), 100f);
            }

            private void Plot(int x, int y, float c)
            {
                if ((uint)x >= (uint)_n || (uint)y >= (uint)_n) return;
                int i = y * _n + x;
                if (c > _a[i]) _a[i] = c; // 取最大覆盖，交叠处干净
            }

            private static float DistSeg(float px, float py, float x0, float y0, float x1, float y1)
            {
                float vx = x1 - x0, vy = y1 - y0;
                float len2 = vx * vx + vy * vy;
                float t = len2 > 0f ? Mathf.Clamp01(((px - x0) * vx + (py - y0) * vy) / len2) : 0f;
                float qx = x0 + vx * t - px, qy = y0 + vy * t - py;
                return Mathf.Sqrt(qx * qx + qy * qy);
            }
        }

        // ---------- 触控交互组件 ----------

        /// <summary>浮动摇杆：在左区任意位置按下，外圈浮到手指处；拖动输出方向，推满自动疾跑。</summary>
        private class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
        {
            public RectTransform ring;
            public RectTransform knob;
            public Image ringImage;

            private RectTransform _zone;
            private const float Radius = 50f; // 摇杆头最大拖动半径（像素）
            private static readonly Color Idle = new Color(1f, 1f, 1f, 0.65f);
            private static readonly Color Active = Color.white;

            private void Awake() => _zone = (RectTransform)transform;

            private void OnEnable()
            {
                if (ring == null) return;
                ring.anchoredPosition = Home();
                knob.anchoredPosition = Vector2.zero;
                if (ringImage != null) ringImage.color = Idle;
            }

            public void OnPointerDown(PointerEventData e)
            {
                if (!ToLocal(e, out Vector2 lp)) return;
                ring.anchoredPosition = ClampInside(lp); // 摇杆浮到手指处
                if (ringImage != null) ringImage.color = Active;
                UpdateKnob(lp);
            }

            public void OnDrag(PointerEventData e)
            {
                if (!ToLocal(e, out Vector2 lp)) return;
                UpdateKnob(lp);
            }

            public void OnPointerUp(PointerEventData e)
            {
                ring.anchoredPosition = Home(); // 回到默认位
                knob.anchoredPosition = Vector2.zero;
                if (ringImage != null) ringImage.color = Idle;
                MobileInput.Move = Vector2.zero;
                MobileInput.Sprint = false;
            }

            private void UpdateKnob(Vector2 lp)
            {
                Vector2 d = Vector2.ClampMagnitude(lp - ring.anchoredPosition, Radius);
                knob.anchoredPosition = d;
                Vector2 m = d / Radius;
                MobileInput.Move = m;
                MobileInput.Sprint = m.y > 0.85f; // 摇杆推到顶 = 疾跑（等价 Shift）
            }

            /// <summary>默认停靠位：左下角内缩 190px。</summary>
            private Vector2 Home()
            {
                Rect r = _zone.rect;
                float mx = Mathf.Min(190f, r.width * 0.5f);
                float my = Mathf.Min(190f, r.height * 0.5f);
                return new Vector2(-r.width * 0.5f + mx, -r.height * 0.5f + my);
            }

            /// <summary>把浮动摇杆中心限制在触控区内（留出半环边距）。</summary>
            private Vector2 ClampInside(Vector2 lp)
            {
                Rect r = _zone.rect;
                float mx = Mathf.Min(95f, r.width * 0.5f);
                float my = Mathf.Min(95f, r.height * 0.5f);
                return new Vector2(
                    Mathf.Clamp(lp.x, -r.width * 0.5f + mx, r.width * 0.5f - mx),
                    Mathf.Clamp(lp.y, -r.height * 0.5f + my, r.height * 0.5f - my));
            }

            private bool ToLocal(PointerEventData e, out Vector2 lp) =>
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_zone, e.position, e.pressEventCamera, out lp);
        }

        /// <summary>右半屏拖动转视角：拖动增量直接累积进 MobileInput。</summary>
        private class LookDrag : MonoBehaviour, IDragHandler
        {
            public void OnDrag(PointerEventData e) => MobileInput.AddLookDelta(e.delta);
        }

        /// <summary>开火键：按住持续开火，抬起/滑出停止。</summary>
        private class HoldFire : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
        {
            public void OnPointerDown(PointerEventData e) => MobileInput.FireHeld = true;
            public void OnPointerUp(PointerEventData e) => MobileInput.FireHeld = false;
            public void OnPointerExit(PointerEventData e) => MobileInput.FireHeld = false;
        }
    }
}
