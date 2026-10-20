using UnityEngine;

namespace FpsGame.UI
{
    /// <summary>
    /// 手游输入桥：MobileUI 写入，PlayerController 读取。
    /// Take* 系列每帧消费后自动清零（点按型）；Move/FireHeld 为持续状态（由 UI 在抬起时复位）。
    /// </summary>
    public static class MobileInput
    {
        public static Vector2 Move;   // 虚拟摇杆输出（-1..1）
        public static bool FireHeld;  // 开火按钮按住
        public static bool Sprint;    // 摇杆推满自动疾跑（由 MobileUI 维护）

        private static Vector2 _lookDelta;
        private static bool _jump, _reload, _aim, _switch;

        public static void AddLookDelta(Vector2 d) => _lookDelta += d;
        public static Vector2 TakeLookDelta()
        {
            Vector2 v = _lookDelta;
            _lookDelta = Vector2.zero;
            return v;
        }

        public static void SetJump() => _jump = true;
        public static void SetReload() => _reload = true;
        public static void SetAim() => _aim = true;
        public static void SetSwitch() => _switch = true;

        public static bool TakeJump() { bool v = _jump; _jump = false; return v; }
        public static bool TakeReload() { bool v = _reload; _reload = false; return v; }
        public static bool TakeAim() { bool v = _aim; _aim = false; return v; }
        public static bool TakeSwitch() { bool v = _switch; _switch = false; return v; }

        /// <summary>UI 隐藏 / 断线时清空全部输入，防止卡移动或卡开火。</summary>
        public static void ResetAll()
        {
            Move = Vector2.zero;
            FireHeld = false;
            Sprint = false;
            _lookDelta = Vector2.zero;
            _jump = _reload = _aim = _switch = false;
        }
    }
}
