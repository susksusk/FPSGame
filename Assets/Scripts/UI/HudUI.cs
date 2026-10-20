using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace FpsGame.UI
{
    /// <summary>
    /// 战斗 HUD：准星、血量、弹药、击杀信息、计分板、暂停面板、重生提示、弹道特效。
    /// 全部 UI 在 Awake 中以代码构建（编辑器里由搭建脚本挂载本组件）。
    /// </summary>
    public class HudUI : MonoBehaviour
    {
        public static HudUI Instance { get; private set; }

        public Material tracerMaterial;

        private Gameplay.PlayerNetwork _local;
        private Gameplay.WeaponSystem _weapon;
        private RectTransform _root;

        // 根容器
        private GameObject _hudRoot, _scoreboardRoot, _pauseRoot, _respawnRoot;

        // 控件引用
        private Text _healthText, _ammoText, _reloadText, _respawnKillerText, _weaponText;
        private Image _healthFill;
        private Image _hitMarker;
        private RectTransform _feedContainer;
        private RectTransform _scoreRows;
        private readonly List<(GameObject go, float until)> _feed = new();
        private float _hitMarkerUntil;
        private float _nextScoreRefresh;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _root = UiKit.EnsureRoot(this, "HudContainer"); // 容器必须铺满画布，否则子元素锚点全部挤到屏幕中央
            BuildUI();
            _hudRoot.SetActive(false);
            _scoreboardRoot.SetActive(false);
            _pauseRoot.SetActive(false);
            _respawnRoot.SetActive(false);
        }

        private void OnEnable()
        {
            Gameplay.GameManager.KillFeedAdded += OnKillFeed;
            Gameplay.PlayerNetwork.LocalPlayerChanged += OnLocalPlayer;
        }

        private void OnDisable()
        {
            Gameplay.GameManager.KillFeedAdded -= OnKillFeed;
            Gameplay.PlayerNetwork.LocalPlayerChanged -= OnLocalPlayer;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (_local == null) return;

            // 暂停（不暂停网络，仅释放鼠标）
            if (Input.GetKeyDown(KeyCode.Escape))
                SetPause(!_pauseRoot.activeSelf);
            bool paused = _pauseRoot.activeSelf;

            // 手游控制层：M 键开关；暂停时强制隐藏（避免挡住暂停面板）
            if (Input.GetKeyDown(KeyCode.M)) MobileUI.UserOn = !MobileUI.UserOn;
            if (MobileUI.Instance != null)
                MobileUI.Instance.SetVisible(MobileUI.UserOn && !paused);

            // 鼠标锁定：键鼠模式锁定隐藏；手游层可见或暂停时释放鼠标（供触屏/点按钮）
            bool mobileVisible = MobileUI.Visible;
            Cursor.lockState = paused || mobileVisible ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = paused || mobileVisible;

            // 计分板（按住 Tab）
            bool wantScore = Input.GetKey(KeyCode.Tab);
            if (_scoreboardRoot.activeSelf != wantScore)
            {
                _scoreboardRoot.SetActive(wantScore);
                if (wantScore) RefreshScoreboard();
            }
            if (wantScore && Time.unscaledTime >= _nextScoreRefresh)
            {
                RefreshScoreboard();
                _nextScoreRefresh = Time.unscaledTime + 0.25f;
            }

            // 击杀信息过期
            for (int i = _feed.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime >= _feed[i].until)
                {
                    Destroy(_feed[i].go);
                    _feed.RemoveAt(i);
                }
            }

            // 命中标记
            if (_hitMarker.gameObject.activeSelf && Time.unscaledTime >= _hitMarkerUntil)
                _hitMarker.gameObject.SetActive(false);
        }

        // ---------- 对外接口 ----------

        public void EnterGame()
        {
            _hudRoot.SetActive(true);
            MobileUI.Ensure(); // 手游控制层（首次进入时挂到 HUD Canvas 下）
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        public void ShowRespawn(string killerName)
        {
            _respawnKillerText.text = $"你被 {killerName} 击杀了";
            _respawnRoot.SetActive(true);
        }

        public void HideRespawn() => _respawnRoot.SetActive(false);

        public void DrawTracer(Vector3 from, Vector3 to)
        {
            var go = new GameObject("Tracer");
            var lr = go.AddComponent<LineRenderer>();
            lr.material = tracerMaterial != null ? tracerMaterial : new Material(Shader.Find("Sprites/Default"));
            lr.startColor = lr.endColor = new Color(1f, 0.85f, 0.35f, 0.85f);
            lr.startWidth = lr.endWidth = 0.03f;
            lr.positionCount = 2;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            Destroy(go, 0.06f);
        }

        /// <summary>主动断开（暂停面板/意外掉线后回到菜单）。</summary>
        public void Disconnect()
        {
            SetPause(false);
            _respawnRoot.SetActive(false);
            _scoreboardRoot.SetActive(false);

            NetworkManager nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening) nm.Shutdown();

            Network.LanDiscovery.Instance?.Stop();

            GameObject menuCam = GameObject.Find("MenuCamera");
            if (menuCam != null) menuCam.SetActive(true);

            MenuUI.Instance?.Show();
        }

        /// <summary>被动掉线（主机退出）时调用：清理本地状态并回菜单显示原因。</summary>
        public void DisconnectForceMenu(string reason)
        {
            Disconnect();
            if (MenuUI.Instance != null && string.IsNullOrEmpty(reason) == false)
                MenuUI.Instance.SetStatus(reason);
        }

        // ---------- 内部 ----------

        private void OnLocalPlayer(Gameplay.PlayerNetwork player)
        {
            if (_weapon != null)
            {
                _weapon.AmmoChanged -= OnAmmoChanged;
                _weapon.ReloadChanged -= OnReloadChanged;
                _weapon.HitConfirmed -= ShowHitMarker;
                _weapon.WeaponSwitched -= OnWeaponSwitched;
            }

            _local = player;
            _weapon = player != null ? player.GetComponent<Gameplay.WeaponSystem>() : null;

            _hudRoot.SetActive(player != null);
            if (player == null) return;

            player.Health.OnValueChanged += OnHealthChanged;
            _weapon.AmmoChanged += OnAmmoChanged;
            _weapon.ReloadChanged += OnReloadChanged;
            _weapon.HitConfirmed += ShowHitMarker;
            _weapon.WeaponSwitched += OnWeaponSwitched;

            OnHealthChanged(player.Health.Value, player.Health.Value);
            OnWeaponSwitched(_weapon.CurrentIndex);
            OnReloadChanged(_weapon.IsReloading);
        }

        private void OnHealthChanged(float _, float value)
        {
            float t = Mathf.Clamp01(value / 100f);
            if (_healthFill != null)
            {
                _healthFill.fillAmount = t;
                _healthFill.color = Color.Lerp(new Color(0.9f, 0.25f, 0.2f), new Color(0.3f, 0.85f, 0.35f), t);
            }
            _healthText.text = $"{Mathf.CeilToInt(value)}";
        }

        private void OnWeaponSwitched(int slotIndex)
        {
            if (_weapon == null) return;
            _weaponText.text = $"[{slotIndex + 1}] {_weapon.CurrentName}";
            OnAmmoChanged(_weapon.Ammo, _weapon.MagazineSize);
        }

        private void OnAmmoChanged(int ammo, int magSize)
        {
            if (magSize <= 0) // 近战：无弹药概念
            {
                _ammoText.text = "∞";
                _ammoText.color = Color.white;
                return;
            }
            _ammoText.text = ammo > 0 ? $"{ammo} / {magSize}" : "空仓";
            _ammoText.color = ammo > 0 ? Color.white : new Color(1f, 0.55f, 0.4f);
        }

        private void OnReloadChanged(bool reloading)
        {
            _reloadText.gameObject.SetActive(reloading);
        }

        private void ShowHitMarker()
        {
            _hitMarkerUntil = Time.unscaledTime + 0.18f;
            _hitMarker.gameObject.SetActive(true);
        }

        private void OnKillFeed(string killer, string victim)
        {
            var text = UiKit.MakeText(_feedContainer, "Feed", $"{killer}  击杀了  {victim}", 16,
                TextAnchor.MiddleLeft, Vector2.zero, new Vector2(440f, 24f));
            var rt = text.rectTransform;
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(0f, -_feed.Count * 28f - 4f);
            _feed.Add((text.gameObject, Time.unscaledTime + 6f));
        }

        private void RefreshScoreboard()
        {
            for (int i = _scoreRows.childCount - 1; i >= 0; i--)
                Destroy(_scoreRows.GetChild(i).gameObject);

            var players = new List<Gameplay.PlayerNetwork>(FindObjectsOfType<Gameplay.PlayerNetwork>());
            players.Sort((a, b) =>
            {
                int byKills = b.Kills.Value.CompareTo(a.Kills.Value);
                return byKills != 0 ? byKills : a.Deaths.Value.CompareTo(b.Deaths.Value);
            });

            const float rowH = 30f;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                float y = -i * rowH - rowH * 0.5f;
                var rawName = p.PlayerName.Value;
                string name = rawName.Length == 0 ? $"玩家{p.OwnerClientId:D2}" : rawName.ToString();
                if (p.IsLocalPlayer) name = "▶ " + name;

                UiKit.MakeText(_scoreRows, "Name", name, 17, TextAnchor.MiddleLeft,
                    new Vector2(-130f, y), new Vector2(300f, rowH));
                UiKit.MakeText(_scoreRows, "Kills", p.Kills.Value.ToString(), 17, TextAnchor.MiddleCenter,
                    new Vector2(60f, y), new Vector2(100f, rowH));
                UiKit.MakeText(_scoreRows, "Deaths", p.Deaths.Value.ToString(), 17, TextAnchor.MiddleCenter,
                    new Vector2(180f, y), new Vector2(100f, rowH));
            }
            _scoreRows.sizeDelta = new Vector2(0f, Mathf.Max(240f, players.Count * rowH));
        }

        public void SetPause(bool paused)
        {
            _pauseRoot.SetActive(paused);
            if (paused) Cursor.lockState = CursorLockMode.None;
        }

        // ---------- UI 构建 ----------

        private void BuildUI()
        {
            _hudRoot = UiKit.FullRect(_root, "HudRoot", Color.clear).gameObject;
            BuildCrosshair(_hudRoot.transform);
            BuildHealth(_hudRoot.transform);
            BuildAmmo(_hudRoot.transform);
            BuildHitMarker(_hudRoot.transform);

            var feedRt = UiKit.Place(_hudRoot.transform, "KillFeed", new Vector2(1f, 1f), new Vector2(-20f, -20f), new Vector2(460f, 240f));
            feedRt.anchorMin = new Vector2(1f, 1f);
            feedRt.anchorMax = new Vector2(1f, 1f);
            feedRt.pivot = new Vector2(1f, 1f);
            _feedContainer = feedRt;

            UiKit.MakeText(_hudRoot.transform, "Hint",
                "WASD 移动 · 左键 射击/轻击 · 右键 开镜/重击 · Q/E 探头 · 滚轮/1-3 切换武器 · R 换弹 · V 检视 · F5 第三人称 · Shift 疾跑 · 空格 跳跃 · Tab 战况 · Esc 暂停",
                14, TextAnchor.MiddleCenter, new Vector2(0f, 16f), new Vector2(1000f, 24f),
                new Color(1f, 1f, 1f, 0.55f));

            BuildScoreboard();
            BuildRespawn();
            BuildPause();
        }

        private void BuildCrosshair(Transform parent)
        {
            var root = UiKit.Place(parent, "Crosshair", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f));
            UiKit.AddImage(UiKit.Place(root, "Dot", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4f, 4f)),
                new Color(1f, 1f, 1f, 0.9f));
            void Bar(string n, Vector2 pos, Vector2 size) =>
                UiKit.AddImage(UiKit.Place(root, n, new Vector2(0.5f, 0.5f), pos, size),
                    new Color(1f, 1f, 1f, 0.8f));
            Bar("Up", new Vector2(0f, 10f), new Vector2(2f, 9f));
            Bar("Down", new Vector2(0f, -10f), new Vector2(2f, 9f));
            Bar("Left", new Vector2(-10f, 0f), new Vector2(9f, 2f));
            Bar("Right", new Vector2(10f, 0f), new Vector2(9f, 2f));
        }

        private void BuildHealth(Transform parent)
        {
            // 右下角，与弹药并排：血条在左，弹药在右
            var bg = UiKit.Place(parent, "HealthBg", new Vector2(1f, 0f), new Vector2(-410f, 42f), new Vector2(280f, 26f));
            bg.anchorMin = new Vector2(1f, 0f);
            bg.anchorMax = new Vector2(1f, 0f);
            UiKit.AddImage(bg, new Color(0f, 0f, 0f, 0.45f));

            var fillRt = UiKit.Place(bg, "Fill", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(280f, 26f));
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            _healthFill = fillRt.gameObject.AddComponent<Image>();
            _healthFill.type = Image.Type.Filled;
            _healthFill.fillMethod = Image.FillMethod.Horizontal;
            _healthFill.fillAmount = 1f;
            _healthFill.color = new Color(0.3f, 0.85f, 0.35f);

            _healthText = UiKit.MakeText(bg, "HealthText", "100", 18, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(280f, 26f));
            _healthText.rectTransform.anchorMin = Vector2.zero;
            _healthText.rectTransform.anchorMax = Vector2.one;
            _healthText.rectTransform.anchoredPosition = Vector2.zero;
            _healthText.rectTransform.sizeDelta = Vector2.zero;
        }

        private void BuildAmmo(Transform parent)
        {
            var rt = UiKit.Place(parent, "Ammo", new Vector2(1f, 0f), new Vector2(-130f, 60f), new Vector2(240f, 60f));
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            _weaponText = UiKit.MakeText(rt, "WeaponText", "[1] 突击步枪", 17, TextAnchor.MiddleCenter, new Vector2(0f, 40f), new Vector2(240f, 22f),
                new Color(1f, 1f, 1f, 0.85f));
            _ammoText = UiKit.MakeText(rt, "AmmoText", "30 / 30", 30, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(240f, 40f));
            _reloadText = UiKit.MakeText(rt, "ReloadText", "换弹中…", 16, TextAnchor.MiddleCenter, new Vector2(0f, -24f), new Vector2(240f, 22f),
                new Color(1f, 0.8f, 0.3f));
            _reloadText.gameObject.SetActive(false);
        }

        private void BuildHitMarker(Transform parent)
        {
            var root = UiKit.Place(parent, "HitMarker", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(36f, 36f));
            void Bar(float angle)
            {
                var rt = UiKit.Place(root, "Bar", new Vector2(0.5f, 0.5f),
                    new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad) * 10f, Mathf.Sin(angle * Mathf.Deg2Rad) * 10f),
                    new Vector2(2.5f, 11f));
                UiKit.AddImage(rt, new Color(1f, 0.35f, 0.3f, 0.95f)).transform.rotation = Quaternion.Euler(0f, 0f, angle);
            }
            Bar(45f); Bar(135f); Bar(225f); Bar(315f);
            _hitMarker = root.GetComponentInChildren<Image>(true);
            root.gameObject.SetActive(false);
        }

        private void BuildScoreboard()
        {
            var root = UiKit.FullRect(_root, "Scoreboard", new Color(0f, 0f, 0f, 0.55f));
            _scoreboardRoot = root.gameObject;

            var panel = UiKit.Place(root.transform, "Panel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620f, 360f));
            UiKit.AddImage(panel, new Color(0.08f, 0.09f, 0.12f, 0.95f));

            UiKit.MakeText(panel, "Title", "战况", 24, TextAnchor.MiddleCenter, new Vector2(0f, 140f), new Vector2(600f, 40f));

            UiKit.MakeText(panel, "HeadName", "名字", 16, TextAnchor.MiddleLeft, new Vector2(-130f, 104f), new Vector2(300f, 26f), new Color(1f, 1f, 1f, 0.6f));
            UiKit.MakeText(panel, "HeadKills", "击杀", 16, TextAnchor.MiddleCenter, new Vector2(60f, 104f), new Vector2(100f, 26f), new Color(1f, 1f, 1f, 0.6f));
            UiKit.MakeText(panel, "HeadDeaths", "死亡", 16, TextAnchor.MiddleCenter, new Vector2(180f, 104f), new Vector2(100f, 26f), new Color(1f, 1f, 1f, 0.6f));

            var rows = UiKit.Place(panel, "Rows", new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(600f, 240f));
            _scoreRows = rows;

            UiKit.MakeText(panel, "Tip", "按住 Tab 查看 · 松开关闭", 13, TextAnchor.MiddleCenter,
                new Vector2(0f, -160f), new Vector2(600f, 22f), new Color(1f, 1f, 1f, 0.4f));
        }

        private void BuildRespawn()
        {
            var root = UiKit.FullRect(_root, "Respawn", new Color(0.25f, 0f, 0f, 0.55f));
            _respawnRoot = root.gameObject;
            _respawnKillerText = UiKit.MakeText(root.transform, "Killer", "你被 XXX 击杀了", 32, TextAnchor.MiddleCenter,
                new Vector2(0f, 30f), new Vector2(900f, 50f));
            UiKit.MakeText(root.transform, "Sub", "3 秒后自动重生…", 18, TextAnchor.MiddleCenter,
                new Vector2(0f, -24f), new Vector2(900f, 30f), new Color(1f, 1f, 1f, 0.7f));
        }

        private void BuildPause()
        {
            var root = UiKit.FullRect(_root, "Pause", new Color(0f, 0f, 0f, 0.6f));
            _pauseRoot = root.gameObject;

            var panel = UiKit.Place(root.transform, "Panel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(380f, 240f));
            UiKit.AddImage(panel, new Color(0.1f, 0.11f, 0.15f, 0.97f));

            UiKit.MakeText(panel, "Title", "已暂停", 26, TextAnchor.MiddleCenter, new Vector2(0f, 78f), new Vector2(360f, 40f));

            var resume = UiKit.MakeButton(panel, "Resume", "返回对局", new Vector2(0f, 16f), new Vector2(240f, 46f));
            resume.onClick.AddListener(() => SetPause(false));

            var leave = UiKit.MakeButton(panel, "Leave", "断开连接", new Vector2(0f, -44f), new Vector2(240f, 46f));
            leave.onClick.AddListener(Disconnect);
        }
    }
}
