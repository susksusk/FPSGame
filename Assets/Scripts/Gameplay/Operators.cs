using UnityEngine;

namespace FpsGame.Gameplay
{
    /// <summary>干员定义：外观（体色）与被动数值（三角洲式选人）。</summary>
    public struct OperatorDef
    {
        public string name;     // 干员代号
        public string role;     // 定位
        public string desc;     // 特长描述
        public Color bodyColor; // 第三人称身体颜色（远端可见）
        public float maxHealth; // 最大血量
        public float moveMul;   // 移速倍率
        public float reloadMul; // 换弹速度倍率（>1 更快）

        public OperatorDef(string name, string role, string desc, Color bodyColor,
            float maxHealth, float moveMul, float reloadMul)
        {
            this.name = name;
            this.role = role;
            this.desc = desc;
            this.bodyColor = bodyColor;
            this.maxHealth = maxHealth;
            this.moveMul = moveMul;
            this.reloadMul = reloadMul;
        }
    }

    /// <summary>干员表：菜单选择 → NetworkVariable 同步 → 出生时应用配色与被动。</summary>
    public static class Operators
    {
        public static readonly OperatorDef[] All =
        {
            new OperatorDef("红狼", "突击", "全能均衡，正面交火首选", new Color(0.82f, 0.28f, 0.22f), 100f, 1f, 1f),
            new OperatorDef("夜莺", "侦察", "移动迅捷，适合游走包抄", new Color(0.24f, 0.72f, 0.66f), 90f, 1.15f, 1f),
            new OperatorDef("堡垒", "重装", "厚实装甲，扛线压点核心", new Color(0.36f, 0.46f, 0.66f), 130f, 0.9f, 1f),
            new OperatorDef("蜂鸟", "工程", "换弹极快，火力压制更持久", new Color(0.92f, 0.62f, 0.2f), 95f, 1.05f, 1.35f),
        };

        public static OperatorDef Get(int index)
        {
            int i = Mathf.Clamp(index, 0, All.Length - 1);
            return All[i];
        }
    }
}
