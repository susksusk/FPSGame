#if UNITY_EDITOR
using System.IO;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using FpsGame.Gameplay;
using FpsGame.Network;
using FpsGame.UI;

namespace FpsGame.EditorTools
{
    /// <summary>
    /// 一键搭建：竞技场场景、玩家预制体、NetworkManager、UI 根节点、构建配置。
    /// 菜单入口：FPS → 搭建 FPS 场景（一键生成）。
    /// </summary>
    public static class FpsSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/GameScene.unity";
        private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
        private const string TracerMatPath = "Assets/Materials/Tracer.mat";

        [MenuItem("FPS/搭建 FPS 场景 (一键生成)")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EnsureFolders();
            Material tracerMat = EnsureTracerMaterial();
            GameObject playerPrefab = BuildPlayerPrefab();
            BuildScene(playerPrefab, tracerMat);

            AssetDatabase.SaveAssets();
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true),
            };

            EditorUtility.DisplayDialog("搭建完成",
                "已生成：\n" + ScenePath + "\n" + PlayerPrefabPath + "\n\n" +
                "使用方法：\n1. 打开 GameScene 按 Play\n2. 点「创建主机」\n" +
                "3. 局域网内其它电脑打开游戏，在列表点主机名加入\n" +
                "4. 首次运行如弹出防火墙提示请选择「允许」",
                "好的");
            Debug.Log("[FpsSceneBuilder] 场景搭建完成 → " + ScenePath);
        }

        // ---------- 预制体 ----------

        private static GameObject BuildPlayerPrefab()
        {
            var root = new GameObject("Player");
            try
            {
                var cc = root.AddComponent<CharacterController>();
                cc.height = 1.8f;
                cc.radius = 0.4f;
                cc.center = new Vector3(0f, 0.9f, 0f);

                var controller = root.AddComponent<PlayerController>();
                var weapon = root.AddComponent<WeaponSystem>();
                var network = root.AddComponent<PlayerNetwork>();
                controller.enabled = false; // 预制体默认禁用：仅本人由 PlayerNetwork.OnNetworkSpawn 启用，防止远端实例读取本地输入
                weapon.enabled = false;
                root.AddComponent<NetworkObject>();
                var nt = root.AddComponent<ClientNetworkTransform>(); // 所有者权威：玩家本人移动/视角可复制，否则纯客户端会被服务端快照拉回原点
                nt.Interpolate = false; // 规避 NGO 1.8.1 NetworkTransform 插值空引用问题；局域网低延迟下无感

                // 身体（本人隐藏，仅远端可见）：空根节点，士兵部件在材质与工具函数就绪后拼装
                var visual = new GameObject("Visual");
                visual.transform.SetParent(root.transform, false);

                // 头部（俯仰轴 + 爆头判定）
                var head = new GameObject("Head");
                head.transform.SetParent(root.transform, false);
                head.transform.localPosition = new Vector3(0f, 1.55f, 0f);
                var headCol = head.AddComponent<SphereCollider>();
                headCol.radius = 0.16f;

                var camGo = new GameObject("PlayerCamera");
                camGo.transform.SetParent(head.transform, false);
                var cam = camGo.AddComponent<Camera>();
                camGo.tag = "MainCamera";
                cam.fieldOfView = 75f;
                cam.nearClipPlane = 0.08f;
                camGo.AddComponent<AudioListener>();
                camGo.SetActive(false); // 仅本地玩家启用

                // 武器（三槽位：突击步枪 / 手枪 / 军刀，挂在 Head 下随视角俯仰）
                Material gunMat = EnsureMaterialAsset("Assets/Materials/PlayerGun.mat", new Color(0.13f, 0.13f, 0.15f));
                gunMat.SetFloat("_Metallic", 0.55f);
                gunMat.SetFloat("_Glossiness", 0.35f);
                Material bladeMat = EnsureMaterialAsset("Assets/Materials/Blade.mat", new Color(0.78f, 0.81f, 0.86f));
                bladeMat.SetFloat("_Metallic", 0.85f);
                bladeMat.SetFloat("_Glossiness", 0.65f);
                Material glassMat = EnsureGlassMaterial("Assets/Materials/ScopeGlass.mat", new Color(0.6f, 0.85f, 0.9f, 0.08f)); // 淡青薄玻璃（低透明度，视觉轻薄）
                Material redDotMat = EnsureMaterialAsset("Assets/Materials/RedDot.mat", new Color(1f, 0.12f, 0.08f));
                redDotMat.EnableKeyword("_EMISSION");
                redDotMat.SetColor("_EmissionColor", new Color(1f, 0.1f, 0.05f) * 2.5f);
                Material gripMat = EnsureMaterialAsset("Assets/Materials/Grip.mat", new Color(0.07f, 0.07f, 0.085f)); // 深色哑光（握把/枪托/护木/纹路）
                gripMat.SetFloat("_Metallic", 0.25f);
                gripMat.SetFloat("_Glossiness", 0.15f);
                Material bodyMat = EnsureMaterialAsset("Assets/Materials/PlayerBody.mat", new Color(0.2f, 0.45f, 0.8f)); // 作战服（按干员配色）
                Material gearMat = EnsureMaterialAsset("Assets/Materials/PlayerGear.mat", new Color(0.16f, 0.17f, 0.2f)); // 战术装具（固定深色）
                gearMat.SetFloat("_Metallic", 0.2f);
                gearMat.SetFloat("_Glossiness", 0.25f);

                Transform WeaponPart(Transform parent, string partName, Vector3 pos, Vector3 scale, Material mat, float rotX = 0f, PrimitiveType prim = PrimitiveType.Cube)
                {
                    var part = GameObject.CreatePrimitive(prim);
                    Object.DestroyImmediate(part.GetComponent<Collider>());
                    part.name = partName;
                    part.transform.SetParent(parent, false);
                    part.transform.localPosition = pos;
                    part.transform.localRotation = Quaternion.Euler(rotX, 0f, 0f);
                    part.transform.localScale = scale;
                    part.GetComponent<MeshRenderer>().sharedMaterial = mat;
                    return part.transform;
                }

                // 红点镜（步枪/手枪通用）：方形玻璃镜片（1mm 薄片）+ 细边框（左/右/下，顶部开放）+ 发光红点
                void AddRedDot(Transform body, Vector3 mountPos)
                {
                    Vector3 lens = mountPos + new Vector3(0f, 0.037f, 0f);
                    WeaponPart(body, "DotMount", mountPos, new Vector3(0.025f, 0.04f, 0.05f), gunMat);
                    WeaponPart(body, "DotLens", lens, new Vector3(0.036f, 0.036f, 0.001f), glassMat);  // 方形玻璃片
                    WeaponPart(body, "DotFrameL", lens + Vector3.left * 0.0195f, new Vector3(0.003f, 0.04f, 0.004f), gunMat);  // 细框-左
                    WeaponPart(body, "DotFrameR", lens + Vector3.right * 0.0195f, new Vector3(0.003f, 0.04f, 0.004f), gunMat); // 细框-右
                    WeaponPart(body, "DotFrameB", lens + Vector3.down * 0.0195f, new Vector3(0.042f, 0.003f, 0.004f), gunMat); // 细框-下
                    WeaponPart(body, "DotEmitter", lens + Vector3.back * 0.004f, new Vector3(0.004f, 0.004f, 0.004f), redDotMat);
                }

                // 第三人称士兵模型：Suit=作战服（干员色）、SuitDark=裤装（干员色加深）、Gear=固定深色装具
                var suit = new GameObject("Suit");
                suit.transform.SetParent(visual.transform, false);
                var suitDark = new GameObject("SuitDark");
                suitDark.transform.SetParent(visual.transform, false);
                var gear = new GameObject("Gear");
                gear.transform.SetParent(visual.transform, false);

                // 下肢：靴底 / 作战靴 / 裤脚 / 护胫（胶囊） / 膝盖（球）+护膝 / 大腿（胶囊）
                WeaponPart(gear.transform, "Sole_L", new Vector3(-0.13f, 0.025f, 0.03f), new Vector3(0.15f, 0.05f, 0.32f), gripMat);
                WeaponPart(gear.transform, "Sole_R", new Vector3(0.13f, 0.025f, 0.03f), new Vector3(0.15f, 0.05f, 0.32f), gripMat);
                WeaponPart(gear.transform, "Boot_L", new Vector3(-0.13f, 0.08f, 0.02f), new Vector3(0.14f, 0.11f, 0.28f), gearMat);
                WeaponPart(gear.transform, "Boot_R", new Vector3(0.13f, 0.08f, 0.02f), new Vector3(0.14f, 0.11f, 0.28f), gearMat);
                WeaponPart(suitDark.transform, "Cuff_L", new Vector3(-0.13f, 0.16f, -0.005f), new Vector3(0.135f, 0.09f, 0.15f), bodyMat);
                WeaponPart(suitDark.transform, "Cuff_R", new Vector3(0.13f, 0.16f, -0.005f), new Vector3(0.135f, 0.09f, 0.15f), bodyMat);
                WeaponPart(suitDark.transform, "Shin_L", new Vector3(-0.13f, 0.40f, -0.005f), new Vector3(0.115f, 0.21f, 0.115f), bodyMat, 0f, PrimitiveType.Capsule);
                WeaponPart(suitDark.transform, "Shin_R", new Vector3(0.13f, 0.40f, -0.005f), new Vector3(0.115f, 0.21f, 0.115f), bodyMat, 0f, PrimitiveType.Capsule);
                WeaponPart(suitDark.transform, "Knee_L", new Vector3(-0.13f, 0.625f, 0.005f), new Vector3(0.115f, 0.115f, 0.115f), bodyMat, 0f, PrimitiveType.Sphere);
                WeaponPart(suitDark.transform, "Knee_R", new Vector3(0.13f, 0.625f, 0.005f), new Vector3(0.115f, 0.115f, 0.115f), bodyMat, 0f, PrimitiveType.Sphere);
                WeaponPart(gear.transform, "KneePad_L", new Vector3(-0.13f, 0.635f, 0.065f), new Vector3(0.10f, 0.09f, 0.05f), gearMat);
                WeaponPart(gear.transform, "KneePad_R", new Vector3(0.13f, 0.635f, 0.065f), new Vector3(0.10f, 0.09f, 0.05f), gearMat);
                WeaponPart(suitDark.transform, "Thigh_L", new Vector3(-0.125f, 0.845f, 0f), new Vector3(0.13f, 0.19f, 0.13f), bodyMat, 0f, PrimitiveType.Capsule);
                WeaponPart(suitDark.transform, "Thigh_R", new Vector3(0.125f, 0.845f, 0f), new Vector3(0.13f, 0.19f, 0.13f), bodyMat, 0f, PrimitiveType.Capsule);

                // 骨盆 / 腰带+扣 / 腿部枪套与杂物包（右侧挂枪套，左侧挂包）
                WeaponPart(suitDark.transform, "Pelvis", new Vector3(0f, 1.06f, 0f), new Vector3(0.34f, 0.17f, 0.22f), bodyMat);
                WeaponPart(gear.transform, "Belt", new Vector3(0f, 0.975f, 0f), new Vector3(0.36f, 0.06f, 0.24f), gearMat);
                WeaponPart(gear.transform, "BeltBuckle", new Vector3(0f, 0.975f, 0.125f), new Vector3(0.055f, 0.045f, 0.015f), gearMat);
                WeaponPart(gear.transform, "Holster", new Vector3(0.20f, 0.85f, 0.02f), new Vector3(0.085f, 0.15f, 0.11f), gearMat);
                WeaponPart(gear.transform, "HolsterStrap", new Vector3(0.20f, 0.94f, 0f), new Vector3(0.095f, 0.03f, 0.24f), gearMat);
                WeaponPart(gear.transform, "LegPouch_L", new Vector3(-0.20f, 0.84f, 0.02f), new Vector3(0.075f, 0.11f, 0.10f), gearMat);
                WeaponPart(gear.transform, "LegPouchStrap_L", new Vector3(-0.20f, 0.905f, 0f), new Vector3(0.085f, 0.025f, 0.24f), gearMat);

                // 上身：腹部收腰 / 胸腔（胶囊四肢+球关节形成圆滑轮廓）
                WeaponPart(suit.transform, "Abdomen", new Vector3(0f, 1.175f, 0f), new Vector3(0.35f, 0.13f, 0.20f), bodyMat);
                WeaponPart(suit.transform, "Chest", new Vector3(0f, 1.345f, 0f), new Vector3(0.40f, 0.24f, 0.225f), bodyMat);

                // 装具：胸挂 / 侧板 / 弹匣包x3 / 电台+天线 / 背包+睡袋+天线
                WeaponPart(gear.transform, "Vest", new Vector3(0f, 1.33f, 0.115f), new Vector3(0.36f, 0.30f, 0.055f), gearMat);
                WeaponPart(gear.transform, "VestSide_L", new Vector3(-0.19f, 1.33f, 0.01f), new Vector3(0.045f, 0.28f, 0.19f), gearMat);
                WeaponPart(gear.transform, "VestSide_R", new Vector3(0.19f, 1.33f, 0.01f), new Vector3(0.045f, 0.28f, 0.19f), gearMat);
                WeaponPart(gear.transform, "MagPouch_A", new Vector3(-0.10f, 1.27f, 0.155f), new Vector3(0.085f, 0.12f, 0.05f), gearMat);
                WeaponPart(gear.transform, "MagPouch_B", new Vector3(0f, 1.27f, 0.155f), new Vector3(0.085f, 0.12f, 0.05f), gearMat);
                WeaponPart(gear.transform, "MagPouch_C", new Vector3(0.10f, 1.27f, 0.155f), new Vector3(0.085f, 0.12f, 0.05f), gearMat);
                WeaponPart(gear.transform, "Radio", new Vector3(-0.20f, 1.42f, 0.055f), new Vector3(0.05f, 0.10f, 0.035f), gearMat);
                WeaponPart(gear.transform, "RadioAntenna", new Vector3(-0.20f, 1.545f, 0.055f), new Vector3(0.008f, 0.06f, 0.008f), gearMat, 0f, PrimitiveType.Capsule);
                WeaponPart(gear.transform, "Backpack", new Vector3(0f, 1.32f, -0.175f), new Vector3(0.28f, 0.34f, 0.11f), gearMat);
                WeaponPart(gear.transform, "Bedroll", new Vector3(0f, 1.525f, -0.165f), new Vector3(0.05f, 0.085f, 0.05f), gearMat, 90f, PrimitiveType.Capsule); // 横置睡袋
                WeaponPart(gear.transform, "PackAntenna", new Vector3(0.09f, 1.675f, -0.205f), new Vector3(0.006f, 0.10f, 0.006f), gearMat, 0f, PrimitiveType.Capsule);

                // 双臂：两节 IK（肩→肘→手），右手握枪械握把、左手前托护木，肘部自然下弯
                void PoseArm(string side, Vector3 shoulder, Vector3 hand, Vector3 pole, float upperLen, float foreLen, Quaternion? handRot = null)
                {
                    bool right = side == "R";
                    Vector3 axis = hand - shoulder;
                    float d = Mathf.Clamp(axis.magnitude, 0.05f, upperLen + foreLen - 0.01f);
                    axis.Normalize();
                    float x = (upperLen * upperLen - foreLen * foreLen + d * d) / (2f * d);
                    float h = Mathf.Sqrt(Mathf.Max(0.0004f, upperLen * upperLen - x * x));
                    Vector3 polePerp = pole - shoulder;
                    polePerp = (polePerp - Vector3.Dot(polePerp, axis) * axis).normalized;
                    Vector3 elbow = shoulder + axis * x + polePerp * h;

                    WeaponPart(suit.transform, "Shoulder_" + side, shoulder, new Vector3(0.125f, 0.125f, 0.125f), bodyMat, 0f, PrimitiveType.Sphere);
                    WeaponPart(gear.transform, "Pauldron_" + side, shoulder + new Vector3(right ? 0.03f : -0.03f, 0.01f, 0f), new Vector3(0.15f, 0.115f, 0.15f), gearMat, 0f, PrimitiveType.Sphere);
                    var upper = WeaponPart(suit.transform, "UpperArm_" + side, (shoulder + elbow) * 0.5f,
                        new Vector3(0.095f, Mathf.Max(0.05f, Vector3.Distance(shoulder, elbow)) * 0.5f, 0.095f), bodyMat);
                    upper.localRotation = Quaternion.FromToRotation(Vector3.up, (elbow - shoulder).normalized);
                    WeaponPart(suit.transform, "Elbow_" + side, elbow, new Vector3(0.085f, 0.085f, 0.085f), bodyMat, 0f, PrimitiveType.Sphere);
                    Vector3 fdir = (hand - elbow).normalized;
                    var fore = WeaponPart(suit.transform, "Forearm_" + side, (elbow + hand) * 0.5f,
                        new Vector3(0.085f, Mathf.Max(0.05f, Vector3.Distance(elbow, hand)) * 0.5f, 0.085f), bodyMat);
                    fore.localRotation = Quaternion.FromToRotation(Vector3.up, fdir);
                    Quaternion gRot = handRot ?? Quaternion.FromToRotation(Vector3.up, fdir);
                    var glove = WeaponPart(gear.transform, "Glove_" + side, hand, new Vector3(0.09f, 0.11f, 0.10f), gearMat);
                    glove.localRotation = gRot;
                    Vector3 kdir = handRot.HasValue ? (gRot * Vector3.forward) : fdir;
                    var knuckle = WeaponPart(gear.transform, "Knuckle_" + side, hand + kdir * 0.05f, new Vector3(0.075f, 0.045f, 0.02f), gripMat);
                    knuckle.localRotation = gRot;
                }

                // 手部对位（按枪模实测坐标）：枪身在身体系 (0.22, 1.39, 0.42) —
                // 右手 = 握把中心 (0.22, 1.28, 0.29)，按握把 20° 姿态包握；左手 = 弹匣左侧托握 (0.15, 1.24, 0.44)
                PoseArm("R", new Vector3(0.235f, 1.50f, 0f), new Vector3(0.22f, 1.28f, 0.29f), new Vector3(0.635f, 0.5f, -0.4f), 0.26f, 0.27f, Quaternion.Euler(20f, 0f, 0f));
                PoseArm("L", new Vector3(-0.235f, 1.50f, 0f), new Vector3(0.15f, 1.24f, 0.44f), new Vector3(0.265f, 0.5f, -0.15f), 0.30f, 0.34f);

                // 头部：脖颈 / 衣领 / 头（球） / 头盔+盔沿 / NVG 座 / 护目镜
                WeaponPart(suitDark.transform, "Neck", new Vector3(0f, 1.555f, 0f), new Vector3(0.09f, 0.07f, 0.09f), bodyMat);
                WeaponPart(gear.transform, "Collar", new Vector3(0f, 1.525f, -0.005f), new Vector3(0.16f, 0.05f, 0.145f), gearMat);
                WeaponPart(suit.transform, "Head", new Vector3(0f, 1.645f, 0.005f), new Vector3(0.20f, 0.22f, 0.21f), bodyMat, 0f, PrimitiveType.Sphere);
                WeaponPart(gear.transform, "Helmet", new Vector3(0f, 1.68f, -0.02f), new Vector3(0.24f, 0.185f, 0.25f), gearMat, 0f, PrimitiveType.Sphere);
                WeaponPart(gear.transform, "HelmetRim", new Vector3(0f, 1.605f, -0.02f), new Vector3(0.235f, 0.03f, 0.24f), gearMat);
                WeaponPart(gear.transform, "NvgPlate", new Vector3(0f, 1.715f, 0.085f), new Vector3(0.05f, 0.05f, 0.035f), gearMat);
                WeaponPart(gear.transform, "Visor", new Vector3(0f, 1.66f, 0.095f), new Vector3(0.165f, 0.055f, 0.02f), gripMat);

                // 视图装配（手臂 + 武器模型的整体挂载点，切枪时由 ArmsAnimator 做下压/抬起动作）
                var rig = new GameObject("ViewRig");
                rig.transform.SetParent(head.transform, false);
                var rigAnim = rig.AddComponent<ArmsAnimator>();

                // 主武器：突击步枪
                var gun = new GameObject("Weapon_Rifle");
                gun.transform.SetParent(rig.transform, false);
                gun.transform.localPosition = new Vector3(0.22f, -0.16f, 0.42f);
                WeaponPart(gun.transform, "Receiver", Vector3.zero, new Vector3(0.09f, 0.13f, 0.42f), gunMat);          // 机匣
                WeaponPart(gun.transform, "Barrel", new Vector3(0f, 0.025f, 0.36f), new Vector3(0.05f, 0.05f, 0.36f), gunMat); // 枪管
                Transform rifleMag = WeaponPart(gun.transform, "Magazine", new Vector3(0f, -0.12f, 0.03f), new Vector3(0.06f, 0.15f, 0.09f), gunMat); // 弹匣（换弹动画对象）
                WeaponPart(rifleMag, "MagBase", new Vector3(0f, -0.54f, 0f), new Vector3(1.1f, 0.08f, 1.1111f), gunMat); // 弹匣底板（随换弹动画移动，已按父级缩放补偿）
                WeaponPart(gun.transform, "Grip", new Vector3(0f, -0.11f, -0.13f), new Vector3(0.06f, 0.14f, 0.07f), gripMat, 20f); // 握把（哑光）
                // 细节：枪托 / 护木 / 前握把 / 消焰器 / 顶部导轨 / 拉机柄 / 抛壳窗 / 扳机护圈
                WeaponPart(gun.transform, "StockNeck", new Vector3(0f, -0.015f, -0.25f), new Vector3(0.04f, 0.05f, 0.1f), gripMat);        // 枪托颈
                WeaponPart(gun.transform, "Stock", new Vector3(0f, -0.025f, -0.33f), new Vector3(0.05f, 0.1f, 0.09f), gripMat);            // 枪托
                WeaponPart(gun.transform, "ButtPad", new Vector3(0f, -0.025f, -0.378f), new Vector3(0.055f, 0.11f, 0.015f), gunMat);       // 托底板
                WeaponPart(gun.transform, "Handguard", new Vector3(0f, 0.025f, 0.31f), new Vector3(0.06f, 0.06f, 0.16f), gripMat);         // 护木
                WeaponPart(gun.transform, "Foregrip", new Vector3(0f, -0.05f, 0.3f), new Vector3(0.028f, 0.09f, 0.03f), gripMat, -8f);     // 前握把
                WeaponPart(gun.transform, "FlashHider", new Vector3(0f, 0.025f, 0.555f), new Vector3(0.032f, 0.032f, 0.05f), gunMat);      // 消焰器
                WeaponPart(gun.transform, "FlashHiderTip", new Vector3(0f, 0.025f, 0.578f), new Vector3(0.038f, 0.038f, 0.008f), gunMat);  // 消焰器口
                WeaponPart(gun.transform, "TopRail", new Vector3(0f, 0.069f, 0f), new Vector3(0.014f, 0.008f, 0.42f), gunMat);             // 顶部导轨
                WeaponPart(gun.transform, "RailRibA", new Vector3(0f, 0.076f, -0.12f), new Vector3(0.012f, 0.008f, 0.018f), gunMat);       // 导轨肋
                WeaponPart(gun.transform, "RailRibB", new Vector3(0f, 0.076f, 0f), new Vector3(0.012f, 0.008f, 0.018f), gunMat);
                WeaponPart(gun.transform, "RailRibC", new Vector3(0f, 0.076f, 0.075f), new Vector3(0.012f, 0.008f, 0.018f), gunMat);
                WeaponPart(gun.transform, "ChargingHandle", new Vector3(0.05f, 0.02f, -0.05f), new Vector3(0.025f, 0.02f, 0.06f), gunMat); // 拉机柄
                WeaponPart(gun.transform, "EjectionPort", new Vector3(0.0455f, 0.01f, 0.06f), new Vector3(0.002f, 0.028f, 0.085f), gripMat); // 抛壳窗
                WeaponPart(gun.transform, "Trigger", new Vector3(0f, -0.05f, -0.065f), new Vector3(0.01f, 0.03f, 0.012f), gunMat, 12f);    // 扳机
                WeaponPart(gun.transform, "TriggerGuard", new Vector3(0f, -0.09f, -0.055f), new Vector3(0.014f, 0.007f, 0.075f), gunMat);  // 护圈
                var muzzle = new GameObject("Muzzle");
                muzzle.transform.SetParent(gun.transform, false);
                muzzle.transform.localPosition = new Vector3(0f, 0.025f, 0.585f); // 枪口前端（消焰器口外）

                // 步枪红点镜（与手枪同款）：镜座落在机匣顶部
                AddRedDot(gun.transform, new Vector3(0f, 0.085f, 0.12f));

                // 副武器：手枪
                var pistol = new GameObject("Weapon_Pistol");
                pistol.transform.SetParent(rig.transform, false);
                pistol.transform.localPosition = new Vector3(0.2f, -0.15f, 0.38f);
                WeaponPart(pistol.transform, "Slide", new Vector3(0f, 0.01f, 0.05f), new Vector3(0.06f, 0.08f, 0.2f), gunMat); // 套筒
                WeaponPart(pistol.transform, "PGrip", new Vector3(0f, -0.1f, -0.04f), new Vector3(0.055f, 0.13f, 0.07f), gripMat, 14f); // 握把（哑光）
                Transform pistolMag = WeaponPart(pistol.transform, "PMag", new Vector3(0f, -0.09f, 0.02f), new Vector3(0.045f, 0.12f, 0.06f), gunMat); // 弹匣（换弹动画对象）
                WeaponPart(pistolMag, "PMagBase", new Vector3(0f, -0.55f, 0f), new Vector3(1.1111f, 0.1f, 1.1667f), gunMat); // 弹匣底板（随换弹动画移动，已按父级缩放补偿）
                // 细节：套筒防滑纹 / 击锤 / 枪口圈 / 扳机护圈 / 握把贴片
                for (int i = 0; i < 3; i++)
                {
                    float sz = 0.1f + i * 0.02f;
                    WeaponPart(pistol.transform, "SlideSerrL" + i, new Vector3(-0.0305f, 0.01f, sz), new Vector3(0.002f, 0.05f, 0.01f), gripMat); // 防滑纹-左
                    WeaponPart(pistol.transform, "SlideSerrR" + i, new Vector3(0.0305f, 0.01f, sz), new Vector3(0.002f, 0.05f, 0.01f), gripMat);  // 防滑纹-右
                }
                WeaponPart(pistol.transform, "Hammer", new Vector3(0f, 0.015f, -0.062f), new Vector3(0.018f, 0.028f, 0.015f), gunMat, -25f);  // 击锤
                WeaponPart(pistol.transform, "MuzzleRing", new Vector3(0f, 0.01f, 0.155f), new Vector3(0.028f, 0.028f, 0.012f), gunMat);      // 枪口圈
                WeaponPart(pistol.transform, "Trigger", new Vector3(0f, -0.045f, -0.02f), new Vector3(0.008f, 0.025f, 0.01f), gunMat, 15f);   // 扳机
                WeaponPart(pistol.transform, "TriggerGuard", new Vector3(0f, -0.075f, -0.015f), new Vector3(0.012f, 0.006f, 0.06f), gunMat);  // 护圈
                WeaponPart(pistol.transform, "GripPanelL", new Vector3(-0.0285f, -0.1f, -0.04f), new Vector3(0.002f, 0.09f, 0.05f), gunMat, 14f); // 握把贴片-左
                WeaponPart(pistol.transform, "GripPanelR", new Vector3(0.0285f, -0.1f, -0.04f), new Vector3(0.002f, 0.09f, 0.05f), gunMat, 14f);  // 握把贴片-右
                var pistolMuzzle = new GameObject("Muzzle");
                pistolMuzzle.transform.SetParent(pistol.transform, false);
                pistolMuzzle.transform.localPosition = new Vector3(0f, 0.01f, 0.17f);

                // 手枪红点镜（与步枪同款）
                AddRedDot(pistol.transform, new Vector3(0f, 0.07f, 0.05f));

                // 近战武器：军刀
                var knife = new GameObject("Weapon_Knife");
                knife.transform.SetParent(rig.transform, false);
                knife.transform.localPosition = new Vector3(0.215f, -0.25f, 0.32f); // 刀柄中心对准右手 Hand_R
                knife.transform.localRotation = Quaternion.Euler(-35f, -8f, 10f);   // 刀尖斜向上、微内倾
                WeaponPart(knife.transform, "Blade", new Vector3(0f, 0.02f, 0.2f), new Vector3(0.018f, 0.07f, 0.3f), bladeMat); // 刀刃
                WeaponPart(knife.transform, "BladeTip", new Vector3(0f, 0.035f, 0.385f), new Vector3(0.012f, 0.028f, 0.09f), bladeMat, 18f); // 刀尖（微下掠）
                WeaponPart(knife.transform, "FullerL", new Vector3(-0.0095f, 0.02f, 0.2f), new Vector3(0.001f, 0.012f, 0.22f), gripMat);     // 血槽-左
                WeaponPart(knife.transform, "FullerR", new Vector3(0.0095f, 0.02f, 0.2f), new Vector3(0.001f, 0.012f, 0.22f), gripMat);      // 血槽-右
                WeaponPart(knife.transform, "Guard", new Vector3(0f, 0f, 0.04f), new Vector3(0.055f, 0.11f, 0.025f), gunMat);  // 护手
                WeaponPart(knife.transform, "Handle", new Vector3(0f, -0.005f, -0.04f), new Vector3(0.04f, 0.055f, 0.13f), gunMat); // 刀柄
                for (int i = 0; i < 3; i++)
                    WeaponPart(knife.transform, "HandleRing" + i, new Vector3(0f, -0.005f, -0.02f - i * 0.025f), new Vector3(0.044f, 0.059f, 0.012f), gripMat); // 缠柄环
                WeaponPart(knife.transform, "Pommel", new Vector3(0f, -0.005f, -0.115f), new Vector3(0.046f, 0.062f, 0.018f), gunMat); // 柄尾

                // 第一人称人物组（手臂/躯干，仅本人可见；挂 ViewRig 下随视角俯仰）
                var arms = new GameObject("ArmsView");
                arms.transform.SetParent(rig.transform, false);
                arms.SetActive(false); // 预制体默认隐藏：远端玩家不渲染，本地由 PlayerNetwork 控制显隐

                Transform ArmPart(string partName, PrimitiveType prim, Vector3 pos, Vector3 scale, float rotX, float rotZ = 0f)
                {
                    var part = GameObject.CreatePrimitive(prim);
                    Object.DestroyImmediate(part.GetComponent<Collider>());
                    part.name = partName;
                    part.transform.SetParent(arms.transform, false);
                    part.transform.localPosition = pos;
                    part.transform.localRotation = Quaternion.Euler(rotX, 0f, rotZ);
                    part.transform.localScale = scale;
                    part.GetComponent<MeshRenderer>().sharedMaterial = bodyMat;
                    return part.transform;
                }

                ArmPart("Arm_R", PrimitiveType.Capsule, new Vector3(0.27f, -0.34f, 0.16f), new Vector3(0.09f, 0.16f, 0.09f), -32f);      // 右臂
                ArmPart("Hand_R", PrimitiveType.Cube, new Vector3(0.22f, -0.27f, 0.29f), new Vector3(0.09f, 0.09f, 0.1f), 0f);           // 右手（对准握把）
                ArmPart("Arm_L", PrimitiveType.Capsule, new Vector3(0.1f, -0.36f, 0.45f), new Vector3(0.09f, 0.16f, 0.09f), -40f, -20f); // 左臂
                ArmPart("Torso", PrimitiveType.Cube, new Vector3(0f, -0.5f, 0.2f), new Vector3(0.4f, 0.45f, 0.24f), 0f);                 // 躯干（低头可见）

                // 接线
                controller.pitchRoot = head.transform;
                controller.weapon = weapon;
                controller.viewAnim = rigAnim;
                rigAnim.weapon = weapon;
                weapon.viewCamera = cam;
                weapon.owner = network;
                network.visualRoot = visual.transform;
                network.armsView = arms.transform;
                network.playerCamera = cam;
                network.controller = controller;
                network.weapon = weapon;
                weapon.weapons = new[]
                {
                    new WeaponSpec { displayName = "突击步枪", model = gun, muzzle = muzzle.transform, magazine = rifleMag,
                        aimOffset = new Vector3(-0.22f, 0.038f, -0.19f),
                        damage = 20f, fireInterval = 0.12f, range = 100f, magazineSize = 30, reloadTime = 1.5f },
                    new WeaponSpec { displayName = "手枪", model = pistol, muzzle = pistolMuzzle.transform, magazine = pistolMag,
                        aimOffset = new Vector3(-0.2f, 0.043f, -0.08f),
                        damage = 15f, fireInterval = 0.25f, range = 100f, magazineSize = 12, reloadTime = 1.2f },
                    new WeaponSpec { displayName = "军刀", model = knife, melee = true,
                        damage = 30f, heavyDamage = 75f, headshotMultiplier = 1.5f,
                        fireInterval = 0.45f, heavyInterval = 0.9f, range = 2.2f },
                };

                return PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ---------- 场景 ----------

        private static void BuildScene(GameObject playerPrefab, Material tracerMat)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 竞技场（地面/围墙/中央高地/四角特色区/装饰）
            var arenaRoot = new GameObject("Arena");
            FpsArenaDesigner.Build(arenaRoot.transform);
            FpsArenaDesigner.ApplyAtmosphere(); // 渐变云层天空盒 + 远景雾

            // 菜单相机（本地玩家生成时自动关闭）
            var camGo = new GameObject("MenuCamera");
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0f, 58f, -66f);
            camGo.transform.rotation = Quaternion.Euler(44f, 0f, 0f);
            camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();

            // 出生点
            var spawnRoot = new GameObject("SpawnPoints");
            Vector2[] spots =
            {
                new(-40f, -40f), new(40f, -40f), new(-40f, 40f), new(40f, 40f),
                new(0f, -40f), new(0f, 40f), new(-40f, 0f), new(40f, 0f),
            };
            var spawnList = new Transform[spots.Length];
            for (int i = 0; i < spots.Length; i++)
            {
                var p = new GameObject("Spawn_" + i);
                p.transform.SetParent(spawnRoot.transform, false);
                p.transform.position = new Vector3(spots[i].x, 0.1f, spots[i].y);
                FpsArenaDesigner.SpawnPad(spawnRoot.transform, new Vector3(spots[i].x, 0f, spots[i].y));
                spawnList[i] = p.transform;
            }

            // 游戏管理
            var gmGo = new GameObject("GameManager");
            gmGo.AddComponent<GameManager>().spawnPoints = spawnList;

            // 网络管理
            var nmGo = new GameObject("NetworkManager");
            var nm = nmGo.AddComponent<NetworkManager>();
            var transport = nmGo.AddComponent<UnityTransport>();
            nmGo.AddComponent<LanDiscovery>();
            nm.NetworkConfig.PlayerPrefab = playerPrefab;
            nm.NetworkConfig.NetworkTransport = transport; // AddComponent 顺序导致 OnValidate 不会自动绑定，需显式赋值
            nm.NetworkConfig.TickRate = 60; // 关闭插值后用更高同步频率保持远端玩家移动平滑

            // UI 根
            var uiRoot = new GameObject("UIRoot", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = uiRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = uiRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            var esGo = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            var hudGo = new GameObject("HUD", typeof(RectTransform), typeof(HudUI));
            hudGo.transform.SetParent(uiRoot.transform, false);
            UiKit.Stretch((RectTransform)hudGo.transform);
            hudGo.GetComponent<HudUI>().tracerMaterial = tracerMat;

            var menuGo = new GameObject("Menu", typeof(RectTransform), typeof(MenuUI));
            menuGo.transform.SetParent(uiRoot.transform, false);
            UiKit.Stretch((RectTransform)menuGo.transform);

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        // ---------- 工具 ----------

        /// <summary>材质资产（内存材质无法被 SaveAsPrefabAsset 持久化，必须落盘为 .mat）。</summary>
        private static Material EnsureMaterialAsset(string path, Color color)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader shader = Shader.Find("Standard");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                mat = new Material(shader) { color = color };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.color = color;
            return mat;
        }

        // 透明玻璃材质：Legacy Transparent/Specular 天生透明混合（不依赖 Standard 关键词设置），
        // 白色高光呈玻璃反光；旧资材也会被强制重指到该着色器，修复此前不透明的问题
        private static Material EnsureGlassMaterial(string path, Color color)
        {
            const string GlassShader = "Legacy Shaders/Transparent/Specular";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find(GlassShader));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = Shader.Find(GlassShader); // 强制刷新（修复旧资材残留的不透明设置）
            mat.color = color;
            mat.SetColor("_SpecColor", new Color(1f, 1f, 1f, 1f));
            mat.SetFloat("_Shininess", 0.75f);
            return mat;
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
            if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
            if (!AssetDatabase.IsValidFolder("Assets/Scripts/Editor") && Directory.Exists("Assets/Scripts"))
                AssetDatabase.CreateFolder("Assets/Scripts", "Editor");
        }

        private static Material EnsureTracerMaterial()
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(TracerMatPath);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Sprites/Default"));
            AssetDatabase.CreateAsset(mat, TracerMatPath);
            return mat;
        }
    }
}
#endif
