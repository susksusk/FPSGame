using System.Collections;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace FpsGame.Gameplay
{
    /// <summary>
    /// 玩家网络实体：血量/存活/战绩/名字的权威同步，受伤结算、死亡与重生。
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class PlayerNetwork : NetworkBehaviour
    {
        /// <summary>本地玩家生成/销毁时通知 HUD 绑定（null 表示无本地玩家）。</summary>
        public static event System.Action<PlayerNetwork> LocalPlayerChanged;

        [Header("网络状态（服务端写）")]
        public NetworkVariable<float> Health = new(100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<bool> Alive = new(true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<ulong> Kills = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<ulong> Deaths = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        [Header("网络状态（本人写）")]
        // 注意：NGO 1.8.x 运行时未内置 NetworkVariable<string> 序列化，须使用 FixedString32Bytes
        public NetworkVariable<FixedString32Bytes> PlayerName = new(string.Empty, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<byte> OperatorId = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        [Header("引用（由预制体搭建脚本赋值）")]
        public Transform visualRoot;   // 第三人称身体模型（远端可见，Suit/Gear 组）
        public Transform armsView;     // 第一人称手臂/躯干（仅本人可见）
        public Camera playerCamera;
        public PlayerController controller;
        public WeaponSystem weapon;

        private const float RespawnDelay = 3f;

        public override void OnNetworkSpawn()
        {
            Alive.OnValueChanged += (_, alive) => ApplyVisual(alive);
            OperatorId.OnValueChanged += (_, id) => ApplyOperator(id); // 干员变更：重上色 + 服务端刷新满血

            if (IsOwner)
            {
                if (playerCamera != null) playerCamera.gameObject.SetActive(true);

                // 关闭场景里其它相机（如菜单相机），避免重复渲染/AudioListener
                foreach (Camera cam in FindObjectsOfType<Camera>())
                    if (cam != playerCamera) cam.gameObject.SetActive(false);

                SetVisualVisible(Alive.Value);
                if (controller != null) controller.enabled = true;
                if (weapon != null) weapon.enabled = true;

                string name = UI.MenuUI.PlayerName;
                if (string.IsNullOrEmpty(name)) name = "玩家" + OwnerClientId.ToString("D2");
                if (name.Length > 24) name = name.Substring(0, 24); // FixedString32Bytes 容量保护
                PlayerName.Value = name;

                // 应用干员：外观 + 被动（本人视角的移速/换弹加成）
                OperatorId.Value = (byte)UI.MenuUI.SelectedOperator;
                OperatorDef op = Operators.Get(OperatorId.Value);
                if (controller != null) controller.moveSpeedMul = op.moveMul;
                if (weapon != null) weapon.reloadSpeedMul = op.reloadMul;

                if (GameManager.Instance != null)
                    transform.position = GameManager.Instance.GetSpawnForClientId(OwnerClientId);

                LocalPlayerChanged?.Invoke(this);
            }
            else
            {
                // 远端玩家：必须禁用输入与武器，否则其 Update 会读取本地鼠标键盘，
                // 并以他人对象身份调用需所有权的服务 ServerRpc（Only the owner can invoke）
                if (controller != null) controller.enabled = false;
                if (weapon != null) weapon.enabled = false;
                SetVisualVisible(Alive.Value);
            }
            ApplyOperator(OperatorId.Value); // 初次上色（主机自身已提前就位，后进玩家此刻才收到值）
        }

        /// <summary>按干员定义上色（身体实例化材质，避免污染共享资材；第一人称手臂同步着色）。</summary>
        private void ApplyOperator(int opId)
        {
            Color c = Operators.Get(opId).bodyColor;
            if (visualRoot != null)
            {
                // 士兵模型：Suit=作战服（干员色）、SuitDark=裤装（加深一档）；旧版单胶囊预制体回退到首个渲染器
                Transform suit = visualRoot.Find("Suit");
                if (suit != null)
                {
                    foreach (Renderer r in suit.GetComponentsInChildren<Renderer>())
                        r.material.color = c;
                    Transform dark = visualRoot.Find("SuitDark");
                    if (dark != null)
                        foreach (Renderer r in dark.GetComponentsInChildren<Renderer>())
                            r.material.color = c * 0.72f;
                }
                else
                {
                    Renderer body = visualRoot.GetComponentInChildren<Renderer>();
                    if (body != null) body.material.color = c;
                }
            }
            if (armsView != null)
                foreach (Renderer r in armsView.GetComponentsInChildren<Renderer>())
                    r.material.color = c;
            if (IsServer) Health.Value = Operators.Get(opId).maxHealth; // 干员最大血量
        }

        public override void OnNetworkDespawn()
        {
            if (IsLocalPlayer) LocalPlayerChanged?.Invoke(null);
        }

        // ---------- 伤害 ----------

        [ServerRpc(RequireOwnership = false)]
        public void TakeDamageServerRpc(float amount, ulong attackerClientId)
        {
            if (!IsServer || !Alive.Value) return;

            Health.Value = Mathf.Max(0f, Health.Value - amount);
            if (Health.Value <= 0f) Die(attackerClientId);
        }

        private void Die(ulong attackerClientId)
        {
            Alive.Value = false;
            Deaths.Value += 1;

            string killerName = "未知";
            NetworkManager nm = NetworkManager.Singleton;
            if (nm != null && nm.ConnectedClients.TryGetValue(attackerClientId, out NetworkClient client)
                && client.PlayerObject != null)
            {
                PlayerNetwork killer = client.PlayerObject.GetComponent<PlayerNetwork>();
                if (killer != null)
                {
                    killer.Kills.Value += 1;
                    killerName = killer.PlayerName.Value.ToString();
                }
            }

            StartCoroutine(RespawnAfter(RespawnDelay));
            AnnounceDeathClientRpc(killerName, PlayerName.Value.ToString());
        }

        [ClientRpc]
        private void AnnounceDeathClientRpc(string killerName, string victimName)
        {
            GameManager.BroadcastKillFeed(killerName, victimName);
            SetVisualVisible(false);

            if (IsOwner)
            {
                if (controller != null) controller.enabled = false;
                if (weapon != null) { weapon.SetAiming(false); weapon.enabled = false; }
                UI.HudUI.Instance?.ShowRespawn(killerName);
            }
        }

        private IEnumerator RespawnAfter(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (!IsServer) yield break;

            Vector3 pos = GameManager.Instance != null ? GameManager.Instance.GetRandomSpawn() : Vector3.up;
            Health.Value = Operators.Get(OperatorId.Value).maxHealth; // 按干员恢复满血
            Alive.Value = true;
            RespawnClientRpc(pos + Vector3.up * 0.1f);
        }

        [ClientRpc]
        private void RespawnClientRpc(Vector3 spawnPos)
        {
            SetVisualVisible(true);
            if (IsOwner)
            {
                transform.position = spawnPos;
                if (controller != null) controller.enabled = true;
                if (weapon != null) { weapon.enabled = true; weapon.ResetAmmo(); }
                UI.HudUI.Instance?.HideRespawn();
            }
        }

        // ---------- 弹道 ----------

        /// <summary>由本地武器调用。客户端路径需先经 ServerRpc 交由服务端广播（ClientRpc 只能由服务端发起）。</summary>
        public void BroadcastTracer(Vector3 from, Vector3 to)
        {
            if (IsServer) ShowTracerClientRpc(from, to);
            else BroadcastTracerServerRpc(from, to);
        }

        [ServerRpc]
        private void BroadcastTracerServerRpc(Vector3 from, Vector3 to)
        {
            ShowTracerClientRpc(from, to);
        }

        [ClientRpc]
        private void ShowTracerClientRpc(Vector3 from, Vector3 to)
        {
            if (!IsOwner) UI.HudUI.Instance?.DrawTracer(from, to);
        }

        // ---------- 视觉 ----------

        private void ApplyVisual(bool alive) => SetVisualVisible(alive);

        private bool _thirdPerson; // 本人 F5 第三人称观察：临时显示自己的身体模型

        /// <summary>切换本人第三人称观察（仅本地视角，不影响网络同步）。</summary>
        public void SetThirdPerson(bool on)
        {
            _thirdPerson = on;
            SetVisualVisible(Alive.Value);
        }

        /// <summary>本人隐藏第三人称身体（第一人称），远端玩家按存活状态显示；第一人称手臂仅本人可见。</summary>
        private void SetVisualVisible(bool visible)
        {
            // ViewRig（ArmsView 的父级，含全部武器模型）：死亡时整体隐藏，否则死者的枪会悬空留在原地
            if (armsView != null && armsView.parent != null) armsView.parent.gameObject.SetActive(visible);
            if (visualRoot != null) visualRoot.gameObject.SetActive(visible && (!IsLocalPlayer || _thirdPerson));
            if (armsView != null) armsView.gameObject.SetActive(visible && IsLocalPlayer && !_thirdPerson);
        }
    }
}
