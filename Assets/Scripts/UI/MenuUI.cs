using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FpsGame.UI
{
    /// <summary>
    /// 主菜单：创建主机、按 IP 加入、局域网服务器自动发现列表。
    /// 连接流程通过 NetworkManager 回调驱动，所有 UI 在 Awake 中以代码构建。
    /// </summary>
    public class MenuUI : MonoBehaviour
    {
        public static MenuUI Instance { get; private set; }

        /// <summary>玩家名（进对局前在菜单输入，PlayerNetwork 读取）。</summary>
        public static string PlayerName = "";

        /// <summary>选中干员索引（选人栏切换，PlayerNetwork 读取）。</summary>
        public static int SelectedOperator = 0;

        private GameObject _panelRoot;
        private GameObject _opSelectRoot; // 选人界面（创建/加入后进入，选中干员并点开始才真正开局）
        private InputField _nameInput, _ipInput;
        private Text _statusText;
        private Text _opNameText, _opDescText, _opIndexText;
        private Text _roleBadgeText;                    // 定位徽章文字（突击/侦察…）
        private Image _roleBadge;                        // 定位徽章底色（干员色）
        private Image[] _avAccent;                       // 立绘肩甲/臂（跟随干员色）
        private readonly Image[] _statBar = new Image[3];       // 属性前景条（生命/移速/换弹）
        private readonly Text[] _statValue = new Text[3];       // 属性数值文字
        private Image _avHead, _avTorso; // 立绘部件（切换干员时重着色）
        private Button _startBtn;         // 开始游戏（直接可点）
        private Image[] _cardFrames;       // 干员卡片背景（底部一排）
        private UnityEngine.UI.Outline[] _cardOutlines;   // 卡片选中金框
        private readonly List<Image[]> _cardTint = new(); // 各卡片立绘着色部件（头/躯干，用各自干员色）
        private RectTransform _tooltipRoot; // 悬停数值面板（跟随鼠标）
        private Text _tipName, _tipSub, _tipHp, _tipMove, _tipReload; // 面板文本
        private System.Action _pendingStart; // 选人确认后执行：创建主机或加入指定 IP
        private RectTransform _serverList;
        private RectTransform _root;
        private readonly Dictionary<string, GameObject> _entries = new();

        private NetworkManager _nm;
        private Network.LanDiscovery _discovery;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _root = UiKit.EnsureRoot(this, "MenuContainer"); // 容器必须铺满画布，否则子元素锚点全部挤到屏幕中央
            BuildUI();
        }

        private void Start()
        {
            _nm = FindObjectOfType<NetworkManager>();
            if (_nm != null)
            {
                _nm.OnClientConnectedCallback += OnClientConnected;
                _nm.OnClientDisconnectCallback += OnClientDisconnected;
            }
            else
            {
                _statusText.text = "⚠ 场景中缺少 NetworkManager，请先执行菜单 FPS → 搭建 FPS 场景";
            }
            _nameInput.text = "玩家" + Random.Range(10, 99);
            Show();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SetStatus(string message) => _statusText.text = message;

        // ---------- 流程 ----------

        public void Show()
        {
            if (_opSelectRoot != null) _opSelectRoot.SetActive(false);
            _panelRoot.SetActive(true);
            _statusText.text = "";
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            StartScan();
        }

        public void Hide()
        {
            _panelRoot.SetActive(false);
            if (_opSelectRoot != null) _opSelectRoot.SetActive(false);
            StopScan();
        }

        public void OnHostClicked()
        {
            if (!EnsureNetworkManager()) return;
            PlayerName = _nameInput.text.Trim();
            _pendingStart = _nm.IsListening
                ? () => StartCoroutine(RestartThen(StartHostInternal))
                : StartHostInternal;
            ShowOperatorSelect(); // 先选干员，点击头像后才真正创建主机
        }

        private void StartHostInternal()
        {
            PlayerName = _nameInput.text.Trim();
            _statusText.text = "正在创建主机…";

            UnityTransport transport = _nm.GetComponent<UnityTransport>();
            if (transport == null)
            {
                _statusText.text = "⚠ NetworkManager 上缺少 UnityTransport 组件";
                return;
            }
            // 兼容旧版搭建脚本生成的场景：NetworkConfig 里未绑定 transport 时自动补绑
            if (_nm.NetworkConfig.NetworkTransport == null)
                _nm.NetworkConfig.NetworkTransport = transport;
            if (_nm.NetworkConfig.TickRate < 60) _nm.NetworkConfig.TickRate = 60;
            transport.SetConnectionData("0.0.0.0", Network.LanDiscovery.GamePort, "0.0.0.0");

            Network.LanDiscovery discovery = Discovery;
            if (discovery != null)
                discovery.StartHost(PlayerName + " 的房间");

            _nm.StartHost(); // 成功后经 OnClientConnected → Hide()
        }

        public void OnJoinClicked()
        {
            string ip = _ipInput.text.Trim();
            if (string.IsNullOrEmpty(ip)) ip = "127.0.0.1";
            Join(ip);
        }

        public void Join(string ip)
        {
            if (!EnsureNetworkManager()) return;
            PlayerName = _nameInput.text.Trim();
            _pendingStart = _nm.IsListening
                ? () => StartCoroutine(RestartThen(() => JoinInternal(ip)))
                : () => JoinInternal(ip);
            ShowOperatorSelect(); // 先选干员，点击头像后才真正连接
        }

        private void JoinInternal(string ip)
        {
            PlayerName = _nameInput.text.Trim();
            _statusText.text = $"正在连接 {ip}:{Network.LanDiscovery.GamePort} …";
            StopScan();

            UnityTransport transport = _nm.GetComponent<UnityTransport>();
            if (transport == null)
            {
                _statusText.text = "⚠ NetworkManager 上缺少 UnityTransport 组件";
                return;
            }
            if (_nm.NetworkConfig.NetworkTransport == null)
                _nm.NetworkConfig.NetworkTransport = transport;
            if (_nm.NetworkConfig.TickRate < 60) _nm.NetworkConfig.TickRate = 60;
            transport.SetConnectionData(ip, Network.LanDiscovery.GamePort);
            _nm.StartClient();
        }

        private bool EnsureNetworkManager()
        {
            if (_nm != null) return true;
            _nm = FindObjectOfType<NetworkManager>();
            if (_nm == null)
                _statusText.text = "⚠ 场景中缺少 NetworkManager，请先执行菜单 FPS → 搭建 FPS 场景";
            return _nm != null;
        }

        private IEnumerator RestartThen(System.Action action)
        {
            // 上一次对局刚结束的同一帧内不能立刻重开，等一帧让 Shutdown 完成
            if (_nm != null && _nm.IsListening) _nm.Shutdown();
            yield return null;
            action();
        }

        private void OnClientConnected(ulong clientId)
        {
            if (NetworkManager.Singleton == null) return;
            if (clientId != NetworkManager.Singleton.LocalClientId) return;
            Hide();
            HudUI.Instance?.EnterGame();
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (NetworkManager.Singleton == null) return;
            if (clientId != NetworkManager.Singleton.LocalClientId) return;
            if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer) return; // 主机主动断开由 HUD 处理
            StartCoroutine(BackToMenuNextFrame("与主机的连接已断开"));
        }

        private IEnumerator BackToMenuNextFrame(string reason)
        {
            yield return null;
            Network.LanDiscovery.Instance?.Stop();
            HudUI.Instance?.DisconnectForceMenu(reason);
        }

        // ---------- 局域网扫描 ----------

        private Network.LanDiscovery Discovery => _discovery != null ? _discovery : (_discovery = FindObjectOfType<Network.LanDiscovery>());

        private void StartScan()
        {
            ClearServerList();
            Network.LanDiscovery discovery = Discovery;
            if (discovery == null) return;
            discovery.ServerFound -= OnServerFound; // 防止多次 Show 重复订阅
            discovery.ServerFound += OnServerFound;
            discovery.StartScan();
        }

        private void StopScan()
        {
            Network.LanDiscovery discovery = Discovery;
            if (discovery != null) discovery.ServerFound -= OnServerFound;
            discovery?.Stop();
        }

        private void OnServerFound(Network.LanServerInfo info)
        {
            string key = $"{info.Ip}:{info.Port}";
            if (_entries.ContainsKey(key)) return;
            if (_entries.Count >= 6) return;

            string label = $"{info.Name}   [{info.Players} 人]   {key}";
            var row = UiKit.MakeButton(_serverList, "Server_" + _entries.Count, label,
                new Vector2(0f, -_entries.Count * 44f - 22f), new Vector2(440f, 38f), 15);
            string capturedIp = info.Ip;
            row.onClick.AddListener(() => Join(capturedIp));
            _entries[key] = row.gameObject;

            if (_entries.Count == 1 && _statusText.text.Length == 0)
                _statusText.text = "发现局域网主机，点击列表加入";
        }

        private void ClearServerList()
        {
            foreach (GameObject go in _entries.Values)
                if (go != null) Destroy(go);
            _entries.Clear();
        }

        // ---------- UI 构建 ----------

        private void BuildUI()
        {
            _panelRoot = UiKit.FullRect(_root, "MenuRoot", new Color(0.07f, 0.08f, 0.11f, 0.98f)).gameObject;

            UiKit.MakeText(_panelRoot.transform, "Title", "局域网 FPS · 死斗", 40, TextAnchor.MiddleCenter,
                new Vector2(0f, 250f), new Vector2(900f, 70f));

            UiKit.MakeText(_panelRoot.transform, "NameLabel", "玩家名", 15, TextAnchor.MiddleCenter,
                new Vector2(-170f, 176f), new Vector2(120f, 24f), new Color(1f, 1f, 1f, 0.6f));
            _nameInput = UiKit.MakeInput(_panelRoot.transform, "NameInput", "输入名字", new Vector2(30f, 176f), new Vector2(340f, 40f));

            var hostBtn = UiKit.MakeButton(_panelRoot.transform, "HostBtn", "创建主机", new Vector2(0f, 110f), new Vector2(370f, 50f), 22);
            hostBtn.onClick.AddListener(OnHostClicked);

            UiKit.MakeText(_panelRoot.transform, "JoinLabel", "或输入 IP 直接加入：", 15, TextAnchor.MiddleCenter,
                new Vector2(0f, 66f), new Vector2(400f, 24f), new Color(1f, 1f, 1f, 0.6f));
            _ipInput = UiKit.MakeInput(_panelRoot.transform, "IpInput", "默认 127.0.0.1", new Vector2(-95f, 28f), new Vector2(240f, 40f));
            var joinBtn = UiKit.MakeButton(_panelRoot.transform, "JoinBtn", "加入", new Vector2(115f, 28f), new Vector2(150f, 40f));
            joinBtn.onClick.AddListener(OnJoinClicked);

            var listPanel = UiKit.Place(_panelRoot.transform, "ServerPanel", new Vector2(0.5f, 0.5f), new Vector2(0f, -120f), new Vector2(480f, 220f));
            UiKit.AddImage(listPanel, new Color(1f, 1f, 1f, 0.05f));
            UiKit.MakeText(listPanel, "ListTitle", "局域网主机（自动发现）", 15, TextAnchor.MiddleCenter,
                new Vector2(0f, 92f), new Vector2(460f, 26f), new Color(1f, 1f, 1f, 0.65f));
            _serverList = UiKit.Place(listPanel, "ServerList", new Vector2(0.5f, 0.5f), new Vector2(0f, -18f), new Vector2(460f, 160f));

            UiKit.MakeText(_panelRoot.transform, "Hint", "创建主机或加入后先选择干员；局域网内其它电脑会自动出现在下方列表。", 13, TextAnchor.MiddleCenter,
                new Vector2(0f, -250f), new Vector2(900f, 22f), new Color(1f, 1f, 1f, 0.45f));

            _statusText = UiKit.MakeText(_panelRoot.transform, "Status", "", 16, TextAnchor.MiddleCenter,
                new Vector2(0f, -290f), new Vector2(900f, 26f), new Color(1f, 0.75f, 0.35f));
        }

        // ---------- 选人界面（创建/加入后进入，点击头像才真正开局） ----------

        private void BuildOperatorSelect()
        {
            _opSelectRoot = UiKit.FullRect(_root, "OpSelectRoot", new Color(0.07f, 0.08f, 0.11f, 0.98f)).gameObject;
            _opSelectRoot.SetActive(false);

            // ---- 顶部：居中标题 + 左上角返回（通用习惯：返回在左上，不与主按钮抢底部） ----
            UiKit.MakeText(_opSelectRoot.transform, "OpTitle", "选择干员", 28, TextAnchor.MiddleCenter,
                new Vector2(0f, 292f), new Vector2(600f, 40f));
            UiKit.MakeText(_opSelectRoot.transform, "OpSubTitle", "O P E R A T O R   S E L E C T", 12, TextAnchor.MiddleCenter,
                new Vector2(0f, 266f), new Vector2(600f, 18f), new Color(1f, 1f, 1f, 0.35f));
            var backBtn = UiKit.MakeButton(_opSelectRoot.transform, "BackBtn", "←  返回", Vector2.zero, new Vector2(130f, 42f), 17);
            StyleDarkButton(backBtn);
            var brt = (RectTransform)backBtn.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(0f, 1f);
            brt.anchoredPosition = new Vector2(100f, -46f);
            backBtn.onClick.AddListener(BackFromOperatorSelect);

            // ---- 左侧大立绘（280x350，悬停查看数值详情） ----
            var portrait = UiKit.Place(_opSelectRoot.transform, "Portrait", new Vector2(0.5f, 0.5f), new Vector2(-340f, 66f), new Vector2(280f, 350f));
            UiKit.AddImage(portrait, new Color(0.1f, 0.12f, 0.16f, 0.92f));

            // ---- 右侧信息面板（500x320） ----
            var info = UiKit.Place(_opSelectRoot.transform, "OpInfo", new Vector2(0.5f, 0.5f), new Vector2(160f, 70f), new Vector2(500f, 320f));
            UiKit.AddImage(info, new Color(0.09f, 0.11f, 0.15f, 0.9f));
            var infoOutline = info.gameObject.AddComponent<Outline>();
            infoOutline.effectColor = new Color(1f, 1f, 1f, 0.12f);
            infoOutline.effectDistance = new Vector2(1f, -1f);

            // ---- 大立绘部件：头盔/护目镜/肩甲/臂/腰带/弹匣包/枪套，底部渐隐（280x350 框内） ----
            Color gearC = new Color(0.13f, 0.14f, 0.17f); // 固定装具色
            _avHead = UiKit.AddImage(UiKit.Place(portrait, "AvHead", new Vector2(0.5f, 0.5f), new Vector2(0f, 104f), new Vector2(96f, 96f)), Color.white);
            UiKit.AddImage(UiKit.Place(portrait, "AvHelmet", new Vector2(0.5f, 0.5f), new Vector2(0f, 146f), new Vector2(82f, 28f)), gearC); // 头盔顶
            UiKit.AddImage(UiKit.Place(portrait, "AvVisor", new Vector2(0.5f, 0.5f), new Vector2(0f, 108f), new Vector2(80f, 20f)), new Color(0.07f, 0.08f, 0.1f)); // 护目镜
            _avTorso = UiKit.AddImage(UiKit.Place(portrait, "AvTorso", new Vector2(0.5f, 0.5f), new Vector2(0f, -72f), new Vector2(214f, 160f)), Color.white);
            UiKit.AddImage(UiKit.Place(portrait, "AvChest", new Vector2(0.5f, 0.5f), new Vector2(0f, -56f), new Vector2(124f, 80f)), new Color(0.1f, 0.11f, 0.13f)); // 胸挂
            UiKit.AddImage(UiKit.Place(portrait, "AvBelt", new Vector2(0.5f, 0.5f), new Vector2(0f, -134f), new Vector2(182f, 24f)), gearC); // 腰带
            UiKit.AddImage(UiKit.Place(portrait, "AvMag", new Vector2(0.5f, 0.5f), new Vector2(52f, -100f), new Vector2(30f, 48f)), gearC); // 弹匣包
            UiKit.AddImage(UiKit.Place(portrait, "AvHolster", new Vector2(0.5f, 0.5f), new Vector2(-52f, -96f), new Vector2(28f, 52f)), gearC); // 枪套
            _avAccent = new[]
            {
                UiKit.AddImage(UiKit.Place(portrait, "AvPauldronL", new Vector2(0.5f, 0.5f), new Vector2(-128f, 26f), new Vector2(50f, 82f)), Color.white), // 肩甲-左
                UiKit.AddImage(UiKit.Place(portrait, "AvPauldronR", new Vector2(0.5f, 0.5f), new Vector2(128f, 26f), new Vector2(50f, 82f)), Color.white),   // 肩甲-右
                UiKit.AddImage(UiKit.Place(portrait, "AvArmL", new Vector2(0.5f, 0.5f), new Vector2(-136f, -74f), new Vector2(44f, 90f)), Color.white),     // 臂-左
                UiKit.AddImage(UiKit.Place(portrait, "AvArmR", new Vector2(0.5f, 0.5f), new Vector2(136f, -74f), new Vector2(44f, 90f)), Color.white),      // 臂-右
            };
            // 底部渐隐两条（后建覆盖在上层，营造立绘融入底色的效果）
            UiKit.AddImage(UiKit.Place(portrait, "FadeA", new Vector2(0.5f, 0.5f), new Vector2(0f, -150f), new Vector2(280f, 26f)),
                new Color(0.1f, 0.12f, 0.16f, 0.55f));
            UiKit.AddImage(UiKit.Place(portrait, "FadeB", new Vector2(0.5f, 0.5f), new Vector2(0f, -160f), new Vector2(280f, 22f)),
                new Color(0.1f, 0.12f, 0.16f, 0.92f));

            // ---- 信息面板内容（500x320 内） ----
            // 名字行：干员名 + 定位徽章（干员色）+ 编号
            _opNameText = UiKit.MakeText(info, "OpName", "", 26, TextAnchor.MiddleLeft, new Vector2(-110f, 120f), new Vector2(240f, 36f));
            var badge = UiKit.Place(info, "RoleBadge", new Vector2(0.5f, 0.5f), new Vector2(70f, 118f), new Vector2(64f, 26f));
            _roleBadge = UiKit.AddImage(badge, Color.white);
            _roleBadgeText = UiKit.MakeText(badge, "Role", "", 12, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(64f, 26f));
            _opIndexText = UiKit.MakeText(info, "OpIndex", "", 14, TextAnchor.MiddleRight, new Vector2(210f, 120f), new Vector2(80f, 24f), new Color(1f, 1f, 1f, 0.45f));
            UiKit.AddImage(UiKit.Place(info, "InfoRule", new Vector2(0.5f, 0.5f), new Vector2(0f, 96f), new Vector2(440f, 2f)), new Color(1f, 1f, 1f, 0.12f));

            // 属性条三行：标签 + 背景槽 + 干员色前景条 + 数值
            string[] statLabels = { "生命", "移速", "换弹" };
            float[] statY = { 62f, 34f, 6f };
            for (int i = 0; i < 3; i++)
            {
                UiKit.MakeText(info, "StatL" + i, statLabels[i], 13, TextAnchor.MiddleLeft,
                    new Vector2(-198f, statY[i]), new Vector2(64f, 20f), new Color(1f, 1f, 1f, 0.6f));
                UiKit.AddImage(UiKit.Place(info, "StatBg" + i, new Vector2(0.5f, 0.5f), new Vector2(-20f, statY[i]), new Vector2(250f, 10f)),
                    new Color(1f, 1f, 1f, 0.08f));
                var bar = UiKit.Place(info, "StatBar" + i, new Vector2(0.5f, 0.5f), new Vector2(-145f, statY[i]), new Vector2(250f, 10f));
                bar.pivot = new Vector2(0f, 0.5f); // 锚左端，宽度即填充比例
                _statBar[i] = UiKit.AddImage(bar, Color.white);
                _statValue[i] = UiKit.MakeText(info, "StatV" + i, "", 13, TextAnchor.MiddleRight,
                    new Vector2(202f, statY[i]), new Vector2(96f, 20f));
            }

            UiKit.AddImage(UiKit.Place(info, "InfoRule2", new Vector2(0.5f, 0.5f), new Vector2(0f, -18f), new Vector2(440f, 1f)), new Color(1f, 1f, 1f, 0.08f));
            _opDescText = UiKit.MakeText(info, "OpDesc", "", 15, TextAnchor.MiddleLeft, new Vector2(0f, -60f), new Vector2(460f, 64f), new Color(1f, 1f, 1f, 0.65f));

            // ---- 开始游戏：右下角金色主按钮（选完卡片顺手一点，通用习惯） ----
            _startBtn = UiKit.MakeButton(_opSelectRoot.transform, "StartBtn", "开始游戏", Vector2.zero, new Vector2(300f, 64f), 22);
            StyleGoldButton(_startBtn);
            var srt = (RectTransform)_startBtn.transform;
            srt.anchorMin = srt.anchorMax = new Vector2(1f, 0f);
            srt.anchoredPosition = new Vector2(-170f, 86f);
            _startBtn.onClick.AddListener(OnStartClicked);

            // ---- 左下角操作提示 ----
            var footHint = UiKit.MakeText(_opSelectRoot.transform, "OpFootHint",
                "点击卡片切换干员 · 悬停卡片或立绘预览数值 · Enter 开始 / Esc 返回", 13, TextAnchor.MiddleLeft,
                Vector2.zero, new Vector2(760f, 22f), new Color(1f, 1f, 1f, 0.4f));
            var frt = footHint.rectTransform;
            frt.anchorMin = frt.anchorMax = new Vector2(0f, 0f);
            frt.anchoredPosition = new Vector2(400f, 34f);

            // ---- 底部干员卡片栏 ----
            BuildOperatorCards();

            // ---- 悬停数值面板：鼠标放到立绘/卡片上显示详细数值（最后构建保证绘制在最上层） ----
            BuildOperatorTooltip();
            var hover = portrait.gameObject.AddComponent<HoverListener>();
            hover.OnHover = ShowOperatorTooltip;
            hover.Index = -1; // 大立绘 = 当前选中干员

            RefreshOperatorUI();
        }

        /// <summary>悬停面板：深色底 + 金边，内容 = 干员名 / 定位编号 / 三项详细数值。</summary>
        private void BuildOperatorTooltip()
        {
            _tooltipRoot = UiKit.Place(_opSelectRoot.transform, "OpTooltip", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240f, 164f));
            _tooltipRoot.pivot = new Vector2(0f, 1f); // 锚左上角，贴鼠标右下方
            var bg = UiKit.AddImage(_tooltipRoot, new Color(0.05f, 0.06f, 0.09f, 0.97f));
            var outline = bg.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 0.78f, 0.4f, 0.8f);
            outline.effectDistance = new Vector2(2f, -2f);
            _tooltipRoot.gameObject.SetActive(false);

            _tipName = UiKit.MakeText(_tooltipRoot, "TipName", "", 18, TextAnchor.MiddleLeft, new Vector2(120f, -22f), new Vector2(216f, 26f));
            _tipSub = UiKit.MakeText(_tooltipRoot, "TipSub", "", 13, TextAnchor.MiddleLeft, new Vector2(120f, -46f), new Vector2(216f, 20f), new Color(1f, 1f, 1f, 0.6f));
            UiKit.AddImage(UiKit.Place(_tooltipRoot, "TipRule", new Vector2(0.5f, 0.5f), new Vector2(120f, -60f), new Vector2(216f, 2f)), new Color(1f, 0.78f, 0.4f, 0.6f));

            // 数值三行：标签 + 右对齐数值
            string[] labels = { "生命值", "移动速度", "换弹速度" };
            float[] ys = { -82f, -108f, -134f };
            for (int i = 0; i < 3; i++)
            {
                UiKit.MakeText(_tooltipRoot, "TipL" + i, labels[i], 13, TextAnchor.MiddleLeft,
                    new Vector2(60f, ys[i]), new Vector2(96f, 20f), new Color(1f, 1f, 1f, 0.6f));
            }
            _tipHp = UiKit.MakeText(_tooltipRoot, "TipHp", "", 13, TextAnchor.MiddleRight, new Vector2(168f, -82f), new Vector2(120f, 20f));
            _tipMove = UiKit.MakeText(_tooltipRoot, "TipMove", "", 13, TextAnchor.MiddleRight, new Vector2(168f, -108f), new Vector2(120f, 20f));
            _tipReload = UiKit.MakeText(_tooltipRoot, "TipReload", "", 13, TextAnchor.MiddleRight, new Vector2(168f, -134f), new Vector2(120f, 20f));
        }

        /// <summary>悬停显隐（立绘/卡片上的 HoverListener 回调；index=-1 表示当前选中干员）。</summary>
        private void ShowOperatorTooltip(bool show, int index)
        {
            if (_tooltipRoot == null) return;
            _tooltipRoot.gameObject.SetActive(show);
            if (show) FillOperatorTooltip(index < 0 ? SelectedOperator : index);
        }

        /// <summary>填充悬停面板数值（指定干员：悬停卡片即可预览，不必点选）。</summary>
        private void FillOperatorTooltip(int index)
        {
            Gameplay.OperatorDef op = Gameplay.Operators.Get(index);
            if (_tipName != null) { _tipName.text = op.name; _tipName.color = op.bodyColor; }
            if (_tipSub != null) _tipSub.text = $"{op.role} · 编号 {index + 1:00}/{Gameplay.Operators.All.Length:00}";
            if (_tipHp != null) _tipHp.text = $"{op.maxHealth:0}";
            if (_tipMove != null) _tipMove.text = $"{6f * op.moveMul:0.0} m/s"; // 6 = PlayerController.walkSpeed 基准
            if (_tipReload != null) _tipReload.text = $"×{op.reloadMul:0.00}（{(op.reloadMul - 1f) * 100f:+0;-0}%）";
        }

        private void Update()
        {
            // 选人界面键盘习惯：Esc 返回主菜单 / Enter 开始游戏
            if (_opSelectRoot != null && _opSelectRoot.activeSelf)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) BackFromOperatorSelect();
                else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) OnStartClicked();
            }

            // 悬停面板跟随鼠标（右下优先，屏幕边缘自动翻面）
            if (_tooltipRoot == null || !_tooltipRoot.gameObject.activeSelf) return;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, Input.mousePosition, null, out Vector2 local))
            {
                const float tipW = 240f, tipH = 164f;
                float halfW = _root.rect.width * 0.5f, halfH = _root.rect.height * 0.5f;
                Vector2 pos = local + new Vector2(18f, -18f);
                if (pos.x + tipW > halfW) pos.x = local.x - 18f - tipW;   // 右侧越界 → 翻到鼠标左侧
                if (pos.y - tipH < -halfH) pos.y = local.y - 18f + tipH; // 底部越界 → 翻到鼠标上方
                _tooltipRoot.anchoredPosition = pos;
            }
        }

        /// <summary>指针悬停监听（挂到立绘/卡片上，回调 MenuUI 并带上干员索引）。</summary>
        private class HoverListener : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public System.Action<bool, int> OnHover;
            public int Index = -1; // -1 = 当前选中干员（大立绘），>=0 = 对应卡片
            public void OnPointerEnter(PointerEventData e) => OnHover?.Invoke(true, Index);
            public void OnPointerExit(PointerEventData e) => OnHover?.Invoke(false, Index);
        }

        /// <summary>底部干员卡片栏：每张卡片 = 迷你立绘（各自干员色）+ 名字条，点击切换展示干员。</summary>
        private void BuildOperatorCards()
        {
            int n = Gameplay.Operators.All.Length;
            _cardFrames = new Image[n];
            _cardOutlines = new UnityEngine.UI.Outline[n];
            _cardTint.Clear();
            const float span = 146f;
            for (int i = 0; i < n; i++)
            {
                float x = -(n - 1) * 0.5f * span + i * span;
                var card = UiKit.Place(_opSelectRoot.transform, "OpCard" + i, new Vector2(0.5f, 0.5f), new Vector2(x, -210f), new Vector2(132f, 172f));
                var frame = UiKit.AddImage(card, new Color(0.12f, 0.14f, 0.18f, 0.9f));
                var outline = frame.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(1f, 0.78f, 0.4f);
                outline.effectDistance = new Vector2(3f, -3f);
                outline.enabled = false;
                _cardFrames[i] = frame;
                _cardOutlines[i] = outline;

                // 迷你立绘：头 + 护目镜 + 躯干（用该卡片对应的干员色，不随当前选中变化）
                Gameplay.OperatorDef op = Gameplay.Operators.Get(i);
                var head = UiKit.AddImage(UiKit.Place(card, "CHead", new Vector2(0.5f, 0.5f), new Vector2(0f, 42f), new Vector2(48f, 48f)), op.bodyColor * 0.85f);
                UiKit.AddImage(UiKit.Place(card, "CVisor", new Vector2(0.5f, 0.5f), new Vector2(0f, 44f), new Vector2(40f, 11f)), new Color(0.07f, 0.08f, 0.1f));
                var torso = UiKit.AddImage(UiKit.Place(card, "CTorso", new Vector2(0.5f, 0.5f), new Vector2(0f, -12f), new Vector2(90f, 72f)), op.bodyColor);
                _cardTint.Add(new[] { head, torso });
                UiKit.AddImage(UiKit.Place(card, "CNameBar", new Vector2(0.5f, 0.5f), new Vector2(0f, -58f), new Vector2(132f, 28f)), new Color(0f, 0f, 0f, 0.35f));
                UiKit.MakeText(card, "CName", op.name, 14, TextAnchor.MiddleCenter, new Vector2(0f, -58f), new Vector2(132f, 24f));

                // 点击卡片 = 切换到该干员（Button 的 hover tint 自动提供悬停提亮）
                int idx = i;
                var cardBtn = card.gameObject.AddComponent<Button>();
                cardBtn.onClick.AddListener(() => SelectOperator(idx));

                // 悬停卡片 = 预览该干员数值（不必点选）
                var hover = card.gameObject.AddComponent<HoverListener>();
                hover.OnHover = ShowOperatorTooltip;
                hover.Index = idx;
            }
        }

        /// <summary>点击卡片切换展示干员。</summary>
        private void SelectOperator(int index)
        {
            if (index == SelectedOperator) return;
            SelectedOperator = index;
            RefreshOperatorUI();
        }

        /// <summary>深色次按钮（返回）。</summary>
        private void StyleDarkButton(Button b)
        {
            var c = b.colors;
            c.normalColor = new Color(0.15f, 0.17f, 0.21f);
            c.highlightedColor = new Color(0.22f, 0.25f, 0.3f);
            c.pressedColor = new Color(0.1f, 0.11f, 0.14f);
            c.selectedColor = c.normalColor;
            b.colors = c;
        }

        /// <summary>金色主按钮（开始游戏）：底图保持白，tint 相乘出金色。</summary>
        private void StyleGoldButton(Button b)
        {
            var c = b.colors;
            c.normalColor = new Color(0.92f, 0.72f, 0.30f);
            c.highlightedColor = new Color(1f, 0.84f, 0.45f);
            c.pressedColor = new Color(0.75f, 0.58f, 0.22f);
            c.selectedColor = c.normalColor;
            c.disabledColor = new Color(0.35f, 0.35f, 0.35f);
            b.colors = c;
            b.GetComponentInChildren<Text>().color = new Color(0.1f, 0.08f, 0.03f);
        }

        /// <summary>点开始游戏直接创建主机 / 加入。</summary>
        private void OnStartClicked()
        {
            System.Action start = _pendingStart;
            _opSelectRoot.SetActive(false);
            start?.Invoke();
        }

        private void ShowOperatorSelect()
        {
            if (_opSelectRoot == null) BuildOperatorSelect(); // 首次进入时再构建，主菜单加载不用建两套 UI
            _panelRoot.SetActive(false);
            _opSelectRoot.SetActive(true);
            _statusText.text = "";
        }

        public void BackFromOperatorSelect()
        {
            _pendingStart = null;
            Show(); // 返回主菜单（Show 内会隐藏选人面板并恢复主机扫描）
        }

        // ---------- 干员选择 ----------

        private void RefreshOperatorUI()
        {
            Gameplay.OperatorDef op = Gameplay.Operators.Get(SelectedOperator);
            if (_opNameText != null) _opNameText.text = op.name;
            if (_roleBadgeText != null) _roleBadgeText.text = op.role;
            if (_roleBadge != null) _roleBadge.color = op.bodyColor;
            if (_opIndexText != null) _opIndexText.text = $"{SelectedOperator + 1:00} / {Gameplay.Operators.All.Length:00}";
            if (_opDescText != null) _opDescText.text = op.desc;

            // 属性条：数值 → 0..1 比例（覆盖表内全部干员的取值范围）
            float[] ratio = { Mathf.InverseLerp(80f, 140f, op.maxHealth), Mathf.InverseLerp(0.85f, 1.2f, op.moveMul), Mathf.InverseLerp(0.95f, 1.4f, op.reloadMul) };
            string[] value =
            {
                $"{op.maxHealth:0}",
                op.moveMul >= 1f ? $"+{(op.moveMul - 1f) * 100f:0}%" : $"{(op.moveMul - 1f) * 100f:0}%",
                op.reloadMul >= 1f ? $"+{(op.reloadMul - 1f) * 100f:0}%" : $"{(op.reloadMul - 1f) * 100f:0}%",
            };
            for (int i = 0; i < 3; i++)
            {
                if (_statBar[i] != null)
                {
                    _statBar[i].color = op.bodyColor;
                    _statBar[i].rectTransform.sizeDelta = new Vector2(Mathf.Clamp01(ratio[i]) * 250f, 10f);
                }
                if (_statValue[i] != null) _statValue[i].text = value[i];
            }

            // 立绘着色：头/躯干 = 干员色，肩甲/臂 = 加深一档
            if (_avHead != null) _avHead.color = op.bodyColor * 0.85f;
            if (_avTorso != null) _avTorso.color = op.bodyColor;
            if (_avAccent != null)
                foreach (Image a in _avAccent) a.color = op.bodyColor * 0.72f;

            // 卡片栏：当前展示的干员金框高亮 + 微放大
            if (_cardFrames != null)
            {
                for (int i = 0; i < _cardFrames.Length; i++)
                {
                    bool cur = i == SelectedOperator;
                    if (_cardOutlines[i] != null) _cardOutlines[i].enabled = cur;
                    if (_cardFrames[i] != null)
                    {
                        _cardFrames[i].color = cur ? new Color(0.17f, 0.2f, 0.26f, 0.95f) : new Color(0.12f, 0.14f, 0.18f, 0.9f);
                        _cardFrames[i].transform.localScale = cur ? new Vector3(1.07f, 1.07f, 1f) : Vector3.one;
                    }
                }
            }

            // 悬停面板若正显示（切干员时），同步刷新数值
            if (_tooltipRoot != null && _tooltipRoot.gameObject.activeSelf) FillOperatorTooltip(SelectedOperator);
        }
    }
}
