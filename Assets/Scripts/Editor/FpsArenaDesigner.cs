using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FpsGame.EditorTools
{
    /// <summary>
    /// 竞技场场景设计（100x100 内场）：棋盘格地面、围墙（壁柱+压顶）、中央高地与坡道、
    /// 废墟掩体、四角特色区（塔楼 / 拱门 / 树石园 / 集装箱区）、沙袋、灯柱与散件。
    /// 天空：代码生成渐变+云层全景纹理作为天空盒，配合远景雾遮蔽场地边界。
    /// 全部使用基础几何体 + 共享材质调色板（无外部资源依赖）。
    /// </summary>
    internal static class FpsArenaDesigner
    {
        private static readonly Dictionary<string, Material> Palette = new();

        public static void Build(Transform root)
        {
            Palette.Clear();
            BuildSun(root);
            BuildGround(root);
            BuildWalls(root);
            BuildCenter(root);
            BuildRuins(root);        // 东西两侧废墟断墙
            BuildTower(root);        // 西北角：坡道塔楼
            BuildArches(root);       // 东北角：三连拱门
            BuildPark(root);         // 西南角：树石园
            BuildContainers(root);   // 东南角：集装箱区
            BuildSandbags(root);
            BuildProps(root);        // 灯柱、桶、板条箱
        }

        /// <summary>天空盒 + 雾 + 环境光（由搭建脚本调用）。</summary>
        public static void ApplyAtmosphere()
        {
            const string texPath = "Assets/Materials/SkyPanorama.asset";
            const string matPath = "Assets/Materials/SkyPanorama.mat";

            // 每次重建重新生成天空纹理（参数可能已更新；旧 asset 直接覆盖）
            Texture2D tex = MakeSkyTexture();
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(texPath) == null)
            {
                AssetDatabase.CreateAsset(tex, texPath);
            }
            else
            {
                AssetDatabase.DeleteAsset(texPath);
                AssetDatabase.CreateAsset(tex, texPath);
            }
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                Shader shader = Shader.Find("Skybox/Panoramic");
                if (shader == null) shader = Shader.Find("Skybox/Procedural");
                mat = new Material(shader);
                if (shader != null && shader.name == "Skybox/Panoramic") mat.SetTexture("_MainTex", tex);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else
            {
                Shader shader = Shader.Find("Skybox/Panoramic");
                if (shader != null && shader.name == "Skybox/Panoramic") mat.SetTexture("_MainTex", tex);
            }

            // 暖阳 + 三色环境光（Trilight 渐变值是场景资产 internal 字段，经 SerializedObject 写入）
            RenderSettings.ambientMode = AmbientMode.Trilight;
            foreach (var rs in Resources.FindObjectsOfTypeAll<RenderSettings>())
            {
                var so = new SerializedObject(rs);
                so.FindProperty("m_AmbientSkyColor").colorValue = new Color(0.52f, 0.60f, 0.72f);
                so.FindProperty("m_AmbientEquatorColor").colorValue = new Color(0.60f, 0.57f, 0.52f);
                so.FindProperty("m_AmbientGroundColor").colorValue = new Color(0.34f, 0.32f, 0.30f);
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            var horizon = new Color(0.86f, 0.88f, 0.91f);
            RenderSettings.skybox = mat;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 65f;
            RenderSettings.fogEndDistance = 165f;
            RenderSettings.fogColor = horizon;
            DynamicGI.UpdateEnvironment();
        }

        /// <summary>生成渐变天空全景纹理（上半球蓝天渐变 + 平铺云层 + 太阳光晕，下半球暗色地面）。</summary>
        private static Texture2D MakeSkyTexture()
        {
            const int w = 1024, h = 512;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color[w * h];
            var horizon = new Color(0.86f, 0.88f, 0.91f);
            var zenith = new Color(0.22f, 0.43f, 0.74f);
            var warm = new Color(0.97f, 0.90f, 0.80f);
            var nadir = new Color(0.30f, 0.34f, 0.40f);

            for (int y = 0; y < h; y++)
            {
                float v = y / (float)(h - 1);
                Color c;
                if (v < 0.5f)
                {
                    float t = Mathf.SmoothStep(0f, 1f, v / 0.5f);
                    c = Color.Lerp(nadir, horizon, t);
                }
                else
                {
                    float t = Mathf.Pow((v - 0.5f) / 0.5f, 0.8f);
                    c = Color.Lerp(horizon, zenith, t);
                    // 地平线上方的暖色亮带（午后霞光）
                    float band = Mathf.Exp(-Mathf.Pow((v - 0.5f) / 0.06f, 2f)) * 0.55f;
                    c = Color.Lerp(c, warm, band);
                }
                for (int x = 0; x < w; x++) px[y * w + x] = c;
            }

            // 云层（横向环绕，接缝不可见）
            Random.InitState(2026);
            for (int i = 0; i < 18; i++)
            {
                float cx = Random.Range(0f, w);
                float cy = Random.Range(h * 0.62f, h * 0.96f);
                float rx = Random.Range(60f, 170f);
                float ry = rx * Random.Range(0.25f, 0.45f);
                float alpha = Random.Range(0.30f, 0.55f);
                int x0 = Mathf.FloorToInt(cx - rx) - 1, x1 = Mathf.CeilToInt(cx + rx) + 1;
                int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - ry)), y1 = Mathf.Min(h - 1, Mathf.CeilToInt(cy + ry));
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = Mathf.Abs(x - cx);
                    dx = Mathf.Min(dx, w - dx); // 横向环绕
                    float fx = dx / rx, fy = (y - cy) / ry;
                    float f = 1f - fx * fx - fy * fy;
                    if (f <= 0f) continue;
                    int xi = ((x % w) + w) % w;
                    px[y * w + xi] = Color.Lerp(px[y * w + xi], Color.white, alpha * f);
                }
            }

            // 太阳光晕（与 Sun 灯角度一致的亮斑）
            {
                float sx = w * (0.62f), sy = h * 0.86f;
                const float r = 46f;
                for (int y = Mathf.FloorToInt(sy - r - 8); y <= Mathf.CeilToInt(sy + r + 8); y++)
                {
                    if (y < 0 || y >= h) continue;
                    for (int x = 0; x < w; x++)
                    {
                        float dx = Mathf.Abs(x - sx);
                        dx = Mathf.Min(dx, w - dx);
                        float d = Mathf.Sqrt(dx * dx + (y - sy) * (y - sy));
                        if (d > r + 8f) continue;
                        float core = 1f - Mathf.SmoothStep(0f, 1f, d / r);
                        float halo = Mathf.Exp(-(d / r) * (d / r) * 2.2f) * 0.45f;
                        float a = Mathf.Clamp01(core + halo);
                        px[y * w + x] = Color.Lerp(px[y * w + x], new Color(1f, 0.98f, 0.9f), a);
                    }
                }
            }

            tex.SetPixels(px);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();
            return tex;
        }

        /// <summary>出生点垫片（由搭建脚本调用）。</summary>
        public static void SpawnPad(Transform parent, Vector3 pos)
        {
            Cyl(parent, "SpawnPad", pos + new Vector3(0f, 0.03f, 0f), new Vector3(1.6f, 0.06f, 1.6f),
                Mat("pad", new Color(0.25f, 0.5f, 0.8f)));
        }

        // ---------- 各区域 ----------

        private static void BuildSun(Transform root)
        {
            var sun = new GameObject("Sun", typeof(Light));
            sun.transform.SetParent(root, false);
            var light = sun.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.18f;
            light.color = new Color(1f, 0.95f, 0.86f); // 午后暖阳
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.75f;
            sun.transform.rotation = Quaternion.Euler(52f, -32f, 0f);
        }

        private static void BuildGround(Transform root)
        {
            // 大地板负责碰撞；程序混凝土纹理（4m 砖缝分格 + 噪点 + 污渍）平铺
            var baseFloor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            baseFloor.name = "FloorBase";
            baseFloor.transform.SetParent(root, false);
            baseFloor.transform.localScale = new Vector3(22f, 1f, 22f); // 220x220
            var floorMat = MatT("floorA", new Color(0.80f, 0.79f, 0.76f), MakeConcreteTexture());
            floorMat.SetTextureScale("_MainTex", new Vector2(55f, 55f)); // 每次平铺 4m
            floorMat.SetTextureOffset("_MainTex", new Vector2(0.5f, 0.5f)); // 让出生点落在砖缝格中央
            baseFloor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;
        }

        private static void BuildWalls(Transform root)
        {
            // 墙面贴砖纹（一张纹理 = 4x2.5m，Repeat 平铺）；壁柱/压顶深色石面
            var wall = MatT("wall", new Color(0.52f, 0.54f, 0.58f), MakeWallTexture(),
                0f, 0.12f, new Vector2(25f, 2f));
            var trim = MatT("trim", new Color(0.32f, 0.34f, 0.39f), MakeWallTexture(),
                0f, 0.2f, new Vector2(3f, 1f));

            void Wall(string name, Vector3 pos, Vector3 size)
            {
                Box(root, name, pos, size, wall);
            }
            // 墙高 6.5m：最高站立点（集装箱顶 5m）+ 跳跃 0.75m 也越不过，兜底防出界
            Wall("Wall_N", new Vector3(0f, 3.25f, -50.5f), new Vector3(102f, 6.5f, 1f));
            Wall("Wall_S", new Vector3(0f, 3.25f, 50.5f), new Vector3(102f, 6.5f, 1f));
            Wall("Wall_W", new Vector3(-50.5f, 3.25f, 0f), new Vector3(1f, 6.5f, 102f));
            Wall("Wall_E", new Vector3(50.5f, 3.25f, 0f), new Vector3(1f, 6.5f, 102f));

            // 压顶条
            Box(root, "Trim_N", new Vector3(0f, 6.65f, -50.5f), new Vector3(102f, 0.35f, 1.5f), trim);
            Box(root, "Trim_S", new Vector3(0f, 6.65f, 50.5f), new Vector3(102f, 0.35f, 1.5f), trim);
            Box(root, "Trim_W", new Vector3(-50.5f, 6.65f, 0f), new Vector3(1.5f, 0.35f, 102f), trim);
            Box(root, "Trim_E", new Vector3(50.5f, 6.65f, 0f), new Vector3(1.5f, 0.35f, 102f), trim);

            // 壁柱（每 10m 一根）
            for (int i = -4; i <= 4; i++)
            {
                float p = i * 10f;
                Box(root, $"Pilaster_N{i}", new Vector3(p, 3.35f, -49.7f), new Vector3(1.2f, 6.7f, 0.7f), trim);
                Box(root, $"Pilaster_S{i}", new Vector3(p, 3.35f, 49.7f), new Vector3(1.2f, 6.7f, 0.7f), trim);
                Box(root, $"Pilaster_W{i}", new Vector3(-49.7f, 3.35f, p), new Vector3(0.7f, 6.7f, 1.2f), trim);
                Box(root, $"Pilaster_E{i}", new Vector3(49.7f, 3.35f, p), new Vector3(0.7f, 6.7f, 1.2f), trim);
            }
        }

        private static void BuildCenter(Transform root)
        {
            var concrete = MatT("concrete", new Color(0.56f, 0.57f, 0.58f), MakeConcreteTexture(),
                0f, 0.08f, new Vector2(7f, 7f));
            var metal = MatT("metal", new Color(0.45f, 0.49f, 0.53f), null, 0.75f, 0.55f);
            var cloth = Mat("cloth", new Color(0.82f, 0.32f, 0.26f));

            // 高地平台
            Box(root, "CenterPlatform", new Vector3(0f, 0.75f, 0f), new Vector3(14f, 1.5f, 14f), concrete);
            // 平台顶边金属包条（四边）
            Box(root, "Edge_N", new Vector3(0f, 1.48f, -6.9f), new Vector3(14.1f, 0.1f, 0.25f), metal);
            Box(root, "Edge_S", new Vector3(0f, 1.48f, 6.9f), new Vector3(14.1f, 0.1f, 0.25f), metal);
            Box(root, "Edge_W", new Vector3(-6.9f, 1.48f, 0f), new Vector3(0.25f, 0.1f, 14.1f), metal);
            Box(root, "Edge_E", new Vector3(6.9f, 1.48f, 0f), new Vector3(0.25f, 0.1f, 14.1f), metal);
            // 四面坡道
            Ramp(root, "Ramp_N", new Vector3(0f, 0.72f, -9.9f), 17f, 0f);
            Ramp(root, "Ramp_S", new Vector3(0f, 0.72f, 9.9f), -17f, 0f);
            Ramp(root, "Ramp_W", new Vector3(-9.9f, 0.72f, 0f), 0f, 17f);
            Ramp(root, "Ramp_E", new Vector3(9.9f, 0.72f, 0f), 0f, -17f);

            // 平台中央掩体 + 顶部警示灯
            Box(root, "CenterBlock", new Vector3(0f, 2.05f, 0f), new Vector3(2.4f, 1.1f, 2.4f), metal);
            Box(root, "CenterBeacon", new Vector3(0f, 2.68f, 0f), new Vector3(0.5f, 0.12f, 0.5f),
                Emissive("beaconGlow", new Color(1f, 0.55f, 0.15f)));

            // 旗帜两杆（带杆顶球）
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -5.5f : 5.5f;
                Cyl(root, $"FlagPole{i}", new Vector3(x, 1.7f, 0f), new Vector3(0.09f, 1.7f, 0.09f), metal);
                var knob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(knob.GetComponent<SphereCollider>());
                knob.name = $"FlagKnob{i}";
                knob.transform.SetParent(root, false);
                knob.transform.position = new Vector3(x, 3.5f, 0f);
                knob.transform.localScale = new Vector3(0.22f, 0.22f, 0.22f);
                knob.GetComponent<MeshRenderer>().sharedMaterial = Emissive("beaconGlow", new Color(1f, 0.55f, 0.15f));
                Box(root, $"Flag{i}", new Vector3(x + (i == 0 ? 0.65f : -0.65f), 2.85f, 0f),
                    new Vector3(1.2f, 0.7f, 0.06f), cloth);
            }
        }

        private static void Ramp(Transform root, string name, Vector3 pos, float rotX, float rotZ)
        {
            Box(root, name, pos, new Vector3(3.4f, 0.4f, 6.4f),
                MatT("ramp", new Color(0.52f, 0.53f, 0.56f), MakeConcreteTexture(),
                    0f, 0.08f, new Vector2(2f, 3f)), rotX, rotZ);
        }

        private static void BuildRuins(Transform root)
        {
            // 石面统一带砖纹（与拱门同款 stone 材质）
            var stone = MatT("stone", new Color(0.60f, 0.59f, 0.57f), MakeWallTexture(),
                0f, 0.1f, new Vector2(3f, 2f));
            var stoneDark = MatT("stoneDark", new Color(0.50f, 0.49f, 0.47f), MakeWallTexture(),
                0f, 0.1f, new Vector2(2f, 1f));

            // 东侧废墟（错落断墙 + 碎块）
            Box(root, "RuinsE_Main", new Vector3(22f, 1.0f, -12f), new Vector3(7f, 2.0f, 0.8f), stone, 0f, 0f, 10f);
            Box(root, "RuinsE_Side", new Vector3(25.4f, 0.6f, -9.6f), new Vector3(0.8f, 1.2f, 3.4f), stoneDark);
            Box(root, "RuinsE_Debris1", new Vector3(19.5f, 0.25f, -10.5f), new Vector3(1.2f, 0.5f, 1.2f), stoneDark, 0f, 0f, 25f);
            Box(root, "RuinsE_Debris2", new Vector3(24.5f, 0.2f, -13.5f), new Vector3(0.9f, 0.4f, 0.9f), stone, 0f, 0f, 40f);

            // 西侧废墟（镜像）
            Box(root, "RuinsW_Main", new Vector3(-22f, 1.0f, 12f), new Vector3(7f, 2.0f, 0.8f), stone, 0f, 0f, -10f);
            Box(root, "RuinsW_Side", new Vector3(-25.4f, 0.6f, 9.6f), new Vector3(0.8f, 1.2f, 3.4f), stoneDark);
            Box(root, "RuinsW_Debris1", new Vector3(-19.5f, 0.25f, 10.5f), new Vector3(1.2f, 0.5f, 1.2f), stoneDark, 0f, 0f, 25f);
            Box(root, "RuinsW_Debris2", new Vector3(-24.5f, 0.2f, 13.5f), new Vector3(0.9f, 0.4f, 0.9f), stone, 0f, 0f, 40f);

            // 南北低矮断墙
            Box(root, "RuinsN_Low", new Vector3(-10f, 0.75f, -26f), new Vector3(9f, 1.5f, 0.9f), stone);
            Box(root, "RuinsS_Low", new Vector3(10f, 0.75f, 26f), new Vector3(9f, 1.5f, 0.9f), stone);
        }

        private static void BuildTower(Transform root)
        {
            var concrete = Palette.ContainsKey("concrete") ? Palette["concrete"] : Mat("concrete", new Color(0.55f, 0.56f, 0.58f));
            var wood = MatT("wood", new Color(0.64f, 0.49f, 0.32f), null, 0f, 0.15f);
            var metal = Palette.ContainsKey("metal") ? Palette["metal"] : Mat("metal", new Color(0.42f, 0.46f, 0.50f));

            // 可站立平台 + 坡道 + 凉棚（含横梁与斜撑）
            Box(root, "TowerBase", new Vector3(-35f, 0.55f, -35f), new Vector3(5f, 1.1f, 5f), concrete);
            Box(root, "TowerRamp", new Vector3(-31.2f, 0.62f, -35f), new Vector3(5.6f, 0.35f, 3f),
                Palette["ramp"], 0f, -12f);
            Cyl(root, "TowerPost1", new Vector3(-37.5f, 1.7f, -37.5f), new Vector3(0.22f, 1.7f, 0.22f), metal);
            Cyl(root, "TowerPost2", new Vector3(-33.5f, 1.7f, -37.5f), new Vector3(0.22f, 1.7f, 0.22f), metal);
            Box(root, "TowerRoof", new Vector3(-35.5f, 3.5f, -37f), new Vector3(5f, 0.25f, 2.4f), wood);
            Box(root, "TowerBeam", new Vector3(-35.5f, 3.25f, -37f), new Vector3(4.6f, 0.18f, 0.18f), wood); // 檐口横梁
        }

        private static void BuildArches(Transform root)
        {
            var stone = MatT("stone", new Color(0.60f, 0.59f, 0.57f), MakeWallTexture(),
                0f, 0.1f, new Vector2(2f, 4f));
            var trim = Palette.ContainsKey("trim") ? Palette["trim"] : Mat("trim", new Color(0.32f, 0.34f, 0.39f));
            var knobMat = Emissive("archKnob", new Color(0.9f, 0.75f, 0.4f));

            for (int i = 0; i < 3; i++)
            {
                float z = -36f + i * 6f;
                Box(root, $"ArchL{i}", new Vector3(36.5f, 1.75f, z), new Vector3(0.8f, 3.5f, 0.8f), stone);
                Box(root, $"ArchR{i}", new Vector3(39.5f, 1.75f, z), new Vector3(0.8f, 3.5f, 0.8f), stone);
                Box(root, $"ArchTop{i}", new Vector3(38f, 3.55f, z), new Vector3(4.6f, 0.7f, 1.0f), trim);
                // 顶部鎏金球饰
                var knob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(knob.GetComponent<SphereCollider>());
                knob.name = $"ArchKnob{i}";
                knob.transform.SetParent(root, false);
                knob.transform.position = new Vector3(38f, 4.15f, z);
                knob.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
                knob.GetComponent<MeshRenderer>().sharedMaterial = knobMat;
            }
        }

        private static void BuildPark(Transform root)
        {
            var trunk = MatT("trunk", new Color(0.47f, 0.37f, 0.27f), null, 0f, 0.1f);
            var leaf = MatT("leaf", new Color(0.37f, 0.57f, 0.32f), null, 0f, 0.2f);
            var leafAlt = MatT("leafAlt", new Color(0.32f, 0.52f, 0.36f), null, 0f, 0.2f);
            var rock = MatT("rock", new Color(0.54f, 0.54f, 0.52f), MakeConcreteTexture(),
                0f, 0.05f, new Vector2(1.5f, 1f));

            Vector2[] trees = { new(-38f, 18f), new(-30f, 24f), new(-40f, 30f), new(-32f, 36f), new(-22f, 20f), new(-24f, 32f) };
            for (int i = 0; i < trees.Length; i++)
            {
                Tree(root, new Vector3(trees[i].x, 0f, trees[i].y), trunk, i % 2 == 0 ? leaf : leafAlt);
            }
            Vector2[] rocks = { new(-34f, 20f), new(-28f, 28f), new(-36f, 34f), new(-26f, 38f), new(-20f, 26f), new(-42f, 24f), new(-30f, 17f), new(-38f, 40f) };
            foreach (var r in rocks)
            {
                Rock(root, new Vector3(r.x, 0.3f, r.y), rock);
            }
        }

        private static void Tree(Transform root, Vector3 pos, Material trunk, Material leaf)
        {
            Cyl(root, "TreeTrunk", pos + new Vector3(0f, 1.1f, 0f), new Vector3(0.35f, 1.1f, 0.35f), trunk);
            // 三层团状树冠（大 + 两个偏移副冠，打破规则球形）
            for (int i = 0; i < 3; i++)
            {
                var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(crown.GetComponent<SphereCollider>()); // 树冠不阻挡
                crown.name = "TreeCrown" + i;
                crown.transform.SetParent(root, false);
                Vector3 off = i switch
                {
                    0 => new Vector3(0f, 2.8f, 0f),
                    1 => new Vector3(0.55f, 3.3f, 0.35f),
                    _ => new Vector3(-0.45f, 2.45f, -0.4f),
                };
                Vector3 scl = i switch
                {
                    0 => new Vector3(2.0f, 1.6f, 2.0f),
                    1 => new Vector3(1.2f, 0.95f, 1.2f),
                    _ => new Vector3(1.35f, 1.05f, 1.35f),
                };
                crown.transform.position = pos + off;
                crown.transform.localScale = scl;
                crown.GetComponent<MeshRenderer>().sharedMaterial = leaf;
            }
        }

        private static void Rock(Transform root, Vector3 pos, Material rock)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            s.name = "Rock";
            s.transform.SetParent(root, false);
            s.transform.position = pos;
            s.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            s.transform.localScale = new Vector3(1.2f, 0.65f, 1.0f);
            s.GetComponent<MeshRenderer>().sharedMaterial = rock;
        }

        private static void BuildContainers(Transform root)
        {
            var rust = MatT("rust", new Color(0.60f, 0.42f, 0.30f), null, 0.15f, 0.3f);
            var blue = MatT("cargoBlue", new Color(0.28f, 0.46f, 0.64f), null, 0.3f, 0.4f);
            var green = MatT("cargoGreen", new Color(0.34f, 0.48f, 0.38f), null, 0.3f, 0.4f);

            // 集装箱：主体 + 两侧竖波纹棱 + 角柱（rotY 任意，棱随本体朝向）
            void Container(string name, Vector3 pos, Material mat, float rotY)
            {
                Box(root, name, pos, new Vector3(2.5f, 2.5f, 6f), mat, 0f, 0f, rotY);
                Quaternion rot = Quaternion.Euler(0f, rotY, 0f);
                for (int i = 0; i < 5; i++)
                {
                    float zo = -2.2f + i * 1.1f;
                    Box(root, name + "_RidgeA" + i, pos + rot * new Vector3(1.28f, 0f, zo),
                        new Vector3(0.1f, 2.5f, 0.28f), mat, 0f, 0f, rotY);
                    Box(root, name + "_RidgeB" + i, pos + rot * new Vector3(-1.28f, 0f, zo),
                        new Vector3(0.1f, 2.5f, 0.28f), mat, 0f, 0f, rotY);
                }
            }

            Container("ContainerA", new Vector3(34f, 1.25f, 28f), rust, 0f);
            Container("ContainerB", new Vector3(37.8f, 1.25f, 30f), blue, 90f);
            Container("ContainerC", new Vector3(34f, 3.75f, 28.7f), green, 12f);
        }

        private static void BuildSandbags(Transform root)
        {
            var sand = MatT("sand", new Color(0.78f, 0.72f, 0.54f), null, 0f, 0.25f);
            var sandDark = MatT("sandDark", new Color(0.68f, 0.62f, 0.45f), null, 0f, 0.25f);
            // 东西两侧沙袋矮墙（下层五段 + 上层错缝三段）
            foreach (float x in new[] { -36f, 36f })
            {
                foreach (float z in new[] { -6f, -3f, 0f, 3f, 6f })
                {
                    Box(root, $"Sandbag_{x}_{z}", new Vector3(x, 0.28f, z), new Vector3(2.2f, 0.55f, 0.75f),
                        ((z / 3f) % 2 == 0) ? sand : sandDark, 0f, 0f, (z * 7f) % 4f);
                }
                foreach (float z in new[] { -4.5f, 1.5f, 4.5f })
                {
                    Box(root, $"SandbagU_{x}_{z}", new Vector3(x, 0.82f, z), new Vector3(2.0f, 0.5f, 0.7f),
                        ((z / 1.5f) % 2 == 0) ? sand : sandDark, 0f, 0f, (z * 5f) % 4f);
                }
            }
            // 南北各一段错位掩体（同样双层）
            Box(root, "Sandbag_N", new Vector3(-10f, 0.28f, -36f), new Vector3(0.75f, 0.55f, 7f), sand);
            Box(root, "Sandbag_N_U", new Vector3(-10f, 0.82f, -35f), new Vector3(0.7f, 0.5f, 6f), sandDark);
            Box(root, "Sandbag_S", new Vector3(10f, 0.28f, 36f), new Vector3(0.75f, 0.55f, 7f), sand);
            Box(root, "Sandbag_S_U", new Vector3(10f, 0.82f, 37f), new Vector3(0.7f, 0.5f, 6f), sandDark);
        }

        private static void BuildProps(Transform root)
        {
            var metal = Palette.ContainsKey("metal") ? Palette["metal"] : Mat("metal", new Color(0.42f, 0.46f, 0.50f));
            var metalDark = MatT("metalDark", new Color(0.27f, 0.29f, 0.32f), null, 0.6f, 0.5f);
            var wood = Palette.ContainsKey("wood") ? Palette["wood"] : Mat("wood", new Color(0.62f, 0.47f, 0.30f));
            var woodDark = MatT("woodDark", new Color(0.52f, 0.40f, 0.26f), null, 0f, 0.15f);
            var glow = Emissive("lampGlow", new Color(1f, 0.85f, 0.55f));

            // 灯柱（带点光源）
            foreach (var p in new Vector2[] { new(-28f, -28f), new(28f, -28f), new(-28f, 28f), new(28f, 28f) })
            {
                LampPost(root, new Vector3(p.x, 0f, p.y), metalDark, glow);
            }

            // 油桶
            Vector2[] barrels =
            {
                new(-12f, -20f), new(9f, -22f), new(20f, -8f), new(26f, 6f),
                new(14f, 10f), new(-16f, 6f), new(-28f, -10f), new(6f, 18f),
                new(-6f, 28f), new(24f, 18f),
            };
            foreach (var b in barrels)
            {
                Cyl(root, "Barrel", new Vector3(b.x, 0.45f, b.y), new Vector3(0.5f, 0.45f, 0.5f),
                    Random.value > 0.5f ? metal : MatT("barrelRust", new Color(0.57f, 0.38f, 0.27f), null, 0.2f, 0.3f));
            }

            // 板条箱（三箱一堆，顶层斜放）
            Vector2[] crates =
            {
                new(-40f, -10f), new(-40f, -8.2f), new(-39.9f, -9.1f),
                new(40f, 10f), new(40f, 11.8f), new(39.9f, 10.9f),
                new(-10f, -40f), new(-8.4f, -40f), new(-9.2f, -39.6f),
                new(10f, 40f), new(8.4f, 40f), new(9.2f, 39.6f),
                new(14f, -30f), new(-14f, 30f), new(30f, -14f),
            };
            for (int i = 0; i < crates.Length; i++)
            {
                bool stacked = i % 3 == 2;
                float size = stacked ? 0.9f : 1.25f;
                Box(root, "Crate", new Vector3(crates[i].x, stacked ? 1.25f + size * 0.5f : size * 0.5f, crates[i].y),
                    new Vector3(size, size, size), i % 2 == 0 ? wood : woodDark, 0f, 0f, Random.Range(0f, 30f));
            }
        }

        private static void LampPost(Transform root, Vector3 pos, Material pole, Material glow)
        {
            Cyl(root, "LampPole", pos + new Vector3(0f, 1.6f, 0f), new Vector3(0.14f, 1.6f, 0.14f), pole);
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "LampHead";
            head.transform.SetParent(root, false);
            head.transform.position = pos + new Vector3(0f, 3.3f, 0f);
            head.transform.localScale = new Vector3(0.42f, 0.42f, 0.42f);
            head.GetComponent<MeshRenderer>().sharedMaterial = glow;

            var lightGo = new GameObject("LampLight", typeof(Light));
            lightGo.transform.SetParent(root, false);
            lightGo.transform.position = pos + new Vector3(0f, 3.25f, 0f);
            var l = lightGo.GetComponent<Light>();
            l.type = LightType.Point;
            l.range = 9f;
            l.intensity = 0.55f;
            l.color = new Color(1f, 0.85f, 0.6f);
            l.shadows = LightShadows.None;
        }

        // ---------- 基础图元工具 ----------

        /// <summary>生成混凝土地面纹理：4m 分格砖缝 + 双频噪点 + 随机污渍斑点（一张 = 一个 4m 格，Repeat 平铺）。</summary>
        private static Texture2D MakeConcreteTexture()
        {
            const int s = 256;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            var px = new Color[s * s];
            Random.InitState(2027);

            // 基底 + 双频柏林噪点（大面积色斑 + 细颗粒）
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float n = Mathf.PerlinNoise(x * 0.06f, y * 0.06f) * 0.35f
                        + Mathf.PerlinNoise(x * 0.35f + 9f, y * 0.35f + 7f) * 0.15f;
                float v = 0.80f + n - 0.18f;
                px[y * s + x] = new Color(v, v * 0.995f, v * 0.97f);
            }

            // 随机污渍斑点（暗色软圆）
            for (int i = 0; i < 14; i++)
            {
                float cx = Random.Range(0f, s), cy = Random.Range(0f, s);
                float r = Random.Range(8f, 28f);
                for (int y = Mathf.FloorToInt(cy - r); y <= cy + r; y++)
                for (int x = Mathf.FloorToInt(cx - r); x <= cx + r; x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (d > r) continue;
                    int xi = ((x % s) + s) % s, yi = ((y % s) + s) % s;
                    float a = (1f - d / r) * 0.10f;
                    var c = px[yi * s + xi];
                    px[yi * s + xi] = new Color(c.r * (1f - a), c.g * (1f - a), c.b * (1f - a));
                }
            }

            // 四边砖缝（边缘暗线 + 缝内微亮中间）
            const int seam = 5;
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                bool inSeam = x < seam || y < seam || x >= s - seam || y >= s - seam;
                if (!inSeam) continue;
                var c = px[y * s + x];
                float k = 0.80f; // 缝整体压暗
                px[y * s + x] = new Color(c.r * k, c.g * k, c.b * k);
            }

            tex.SetPixels(px);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();
            return tex;
        }

        /// <summary>生成墙面砖纹：水平层缝 + 错缝竖缝 + 噪点 + 渍痕。</summary>
        private static Texture2D MakeWallTexture()
        {
            const int s = 256;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            var px = new Color[s * s];
            Random.InitState(99);

            const int rows = 5;          // 5 层砖
            const int rowH = s / rows;   // 每层 51px
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float n = Mathf.PerlinNoise(x * 0.08f, y * 0.08f) * 0.3f + Mathf.PerlinNoise(x * 0.4f, y * 0.4f) * 0.12f;
                float v = 0.84f + n - 0.16f;
                px[y * s + x] = new Color(v, v * 0.99f, v * 0.96f);
            }

            // 水平层缝 + 错缝竖缝
            const int seam = 3;
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                bool hSeam = y % rowH < seam;
                int row = y / rowH;
                float shift = (row % 2) * (s / 2f); // 奇数层半砖错缝
                bool vSeam = ((x + shift) % (s / 2f)) < seam; // 每层两块砖
                if (!hSeam && !vSeam) continue;
                var c = px[y * s + x];
                px[y * s + x] = new Color(c.r * 0.78f, c.g * 0.78f, c.b * 0.78f);
            }

            // 从层缝往下的水渍垂痕
            for (int i = 0; i < 10; i++)
            {
                int sx = Random.Range(0, s);
                int top = Random.Range(0, rows) * rowH + seam;
                int len = Random.Range(20, 60);
                float str = Random.Range(0.05f, 0.16f);
                for (int y = top; y < Mathf.Min(s, top + len); y++)
                for (int x = sx; x < Mathf.Min(s, sx + 3); x++)
                {
                    var c = px[y * s + x];
                    float a = str * (1f - (float)(y - top) / len);
                    px[y * s + x] = new Color(c.r * (1f - a), c.g * (1f - a), c.b * (1f - a));
                }
            }

            tex.SetPixels(px);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();
            return tex;
        }

        /// <summary>带纹理与表面参数的材质（按 key 缓存共享）。</summary>
        private static Material MatT(string key, Color color, Texture2D tex,
            float metallic = 0f, float smoothness = 0.1f, Vector2? tiling = null)
        {
            if (Palette.TryGetValue(key, out Material m)) return m;
            Shader shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            m = new Material(shader) { color = color };
            if (tex != null)
            {
                m.SetTexture("_MainTex", tex);
                if (tiling.HasValue) m.SetTextureScale("_MainTex", tiling.Value);
            }
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Glossiness", smoothness);
            Palette[key] = m;
            return m;
        }

        private static void Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat,
            float rotX = 0f, float rotZ = 0f, float rotY = 0f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(rotX, rotY, rotZ);
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static void Cyl(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static Material Mat(string key, Color color)
        {
            if (Palette.TryGetValue(key, out Material m)) return m;
            Shader shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            m = new Material(shader) { color = color };
            Palette[key] = m;
            return m;
        }

        private static Material Emissive(string key, Color color)
        {
            if (Palette.TryGetValue(key, out Material m)) return m;
            m = Mat(key, color);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * 1.6f);
            return m;
        }
    }
}
