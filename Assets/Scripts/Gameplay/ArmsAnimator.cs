using UnityEngine;

namespace FpsGame.Gameplay
{
    /// <summary>
    /// 第一人称视图动画（程序化，无 Animator）：切枪下压/抬起、生成装备动作、近战轻/重挥砍、开镜贴脸。
    /// 挂在 Head 下的 ViewRig 上（包含 ArmsView 与全部武器模型）。
    /// 每帧统一计算位姿：基础位姿 → 开镜偏移 → 切枪下压 → 挥砍覆盖。
    /// </summary>
    public class ArmsAnimator : MonoBehaviour
    {
        [Header("引用（由预制体搭建脚本赋值）")]
        public WeaponSystem weapon;

        [Header("切枪动画")]
        public float switchDuration = 0.35f; // 动作时长（秒）
        public float dipDepth = 0.25f;       // 下压深度（米）
        public float dipPitch = 40f;         // 下压俯角（度，枪口朝下）

        [Header("挥砍动画")]
        public float lightDuration = 0.32f;  // 轻击时长
        public float heavyDuration = 0.62f;  // 重击时长（更长前摇与收势）

        [Header("开镜动画")]
        public float aimSpeed = 10f;                                    // 开镜姿态过渡速度
        public Vector3 aimPosOffset = new(-0.22f, 0.035f, -0.12f);      // 开镜时视图整体偏移（武器居中贴脸）

        [Header("换弹动画")]
        public float reloadDrop = 0.16f;   // 换弹时武器下沉深度（米）
        public float reloadRoll = 30f;     // 换弹时武器侧倾角度（度）
        public float reloadWobble = 12f;   // 换弹中操作动作的晃动幅度（度）

        [Header("检视动画")]
        public float inspectDuration = 2.4f; // 检视动作时长（秒）

        private Vector3 _basePos;
        private Quaternion _baseRot;
        private float _t = -1f;  // 切枪动画计时，<0 = 空闲
        private float _dir = 1f; // 1 = 下压再抬起（切枪）；-1 = 从压低抬起（装备）
        private float _aimTarget;
        private float _aimWeight;

        // 挥砍状态
        private float _swingT = -1f;
        private bool _heavy;
        private Transform _meleeModel;      // 当前近战武器模型根（挥砍直接驱动刀体）
        private Vector3 _meleeBasePos;
        private Quaternion _meleeBaseRot;
        private Transform _leftArm;         // 左臂（单手持刀时隐藏）

        // 换弹状态（时长跟随武器 reloadTime）
        private float _reloadT = -1f;
        private float _reloadDur = 1f;
        private Transform _mag;    // 换弹动画中的弹匣
        private Vector3 _magBase;  // 弹匣原始本地位置

        // 检视状态
        private float _inspectT = -1f;

        // 挥砍关键姿态（刀体绕刀柄轴心的欧拉角偏移）：抬刀（俯仰向上）→ 竖直下劈（俯仰向下）
        // 注意 rotX 正值 = 前端向下压，负值 = 前端向上抬；军刀基础俯仰 -35°（windup -55 → 刀刃朝上，strike +75 → 刀刃朝下前）
        private static readonly Vector3 LightWindupRot = new(-55f, 0f, 8f);
        private static readonly Vector3 LightStrikeRot = new(75f, 0f, -6f);
        private static readonly Vector3 HeavyWindupRot = new(-75f, 0f, 10f);
        private static readonly Vector3 HeavyStrikeRot = new(95f, 0f, -8f);
        private static readonly Vector3 GripLocal = new(0f, 0f, -0.04f); // 刀柄中心（挥砍轴心，与右手对齐）

        private void Start()
        {
            _basePos = transform.localPosition;
            _baseRot = transform.localRotation;
            if (weapon != null)
            {
                weapon.WeaponSwitched += OnWeaponSwitched;
                weapon.MeleeSwing += OnMeleeSwing;
                weapon.AimingChanged += OnAimingChanged;
                weapon.ReloadChanged += OnReloadChanged;
            }
            _leftArm = transform.Find("ArmsView/Arm_L");
            CacheMeleeModel();
            ApplyLeftArm();
            PlayEquip(); // 首次生成时从低位抬起武器
        }

        private void OnDestroy()
        {
            if (weapon != null)
            {
                weapon.WeaponSwitched -= OnWeaponSwitched;
                weapon.MeleeSwing -= OnMeleeSwing;
                weapon.AimingChanged -= OnAimingChanged;
                weapon.ReloadChanged -= OnReloadChanged;
            }
        }

        private void OnWeaponSwitched(int _)
        {
            RestoreMeleeModel(); // 挥砍被切枪打断时还原刀体位姿
            _meleeModel = null;  // 旧刀体缓存失效（Restore 不清缓存，此处显式清理）
            CacheMeleeModel();
            ApplyLeftArm();      // 单手持刀：隐藏/恢复左臂
            _swingT = -1f;
            _inspectT = -1f; // 检视被切枪打断
            _dir = 1f;
            _t = 0f;
        }

        private void OnMeleeSwing(bool heavy)
        {
            _t = -1f;
            _inspectT = -1f; // 检视被挥砍打断
            _heavy = heavy;
            _swingT = 0f;
            if (_meleeModel == null) CacheMeleeModel(); // 防御：缓存丢失时重建，保证连续挥砍
        }

        private void OnAimingChanged(bool aiming)
        {
            _aimTarget = aiming ? 1f : 0f;
            if (aiming) _inspectT = -1f; // 检视被开镜打断
        }

        private void OnReloadChanged(bool reloading)
        {
            if (reloading && weapon != null && weapon.Current != null && !weapon.Current.melee)
            {
                _inspectT = -1f; // 检视被换弹打断
                _reloadDur = Mathf.Max(0.3f, weapon.Current.reloadTime / weapon.reloadSpeedMul); // 与实际换弹时长一致（含干员被动）
                _reloadT = 0f;
                _mag = weapon.Current.magazine;
                if (_mag != null) _magBase = _mag.localPosition;
            }
            else
            {
                RestoreMag();
                _reloadT = -1f; // 完成/换弹/切枪打断
            }
        }

        /// <summary>播放装备动作（从压低状态抬起）。</summary>
        public void PlayEquip()
        {
            _dir = -1f;
            _t = switchDuration;
        }

        /// <summary>播放检视动作（V 键）：抬枪翻面展示两侧后归位；忙碌时忽略。</summary>
        public void PlayInspect()
        {
            if (_aimTarget > 0f || _reloadT >= 0f || _t >= 0f || _swingT >= 0f) return;
            _inspectT = 0f;
        }

        private void Update()
        {
            // 开镜姿态（平滑过渡，武器居中贴脸）
            _aimWeight = Mathf.MoveTowards(_aimWeight, _aimTarget, aimSpeed * Time.deltaTime);

            Vector3 pos = _basePos;
            Quaternion rot = _baseRot;
            if (_aimWeight > 0f)
            {
                // 每把武器可有独立开镜偏移（对准镜轴/红点），未配置则用默认
                var spec = weapon != null ? weapon.Current : null;
                Vector3 off = spec != null && spec.aimOffset != Vector3.zero ? spec.aimOffset : aimPosOffset;
                pos = Vector3.Lerp(pos, pos + off, _aimWeight);
            }

            // 换弹动作：拆下弹匣 → 丢弃 → 新弹匣推入，随 reloadTime 结束自动归位
            if (_reloadT >= 0f)
            {
                _reloadT += Time.deltaTime;
                float p = _reloadT / _reloadDur;
                if (p >= 1f)
                {
                    RestoreMag();
                    _reloadT = -1f;
                }
                else
                {
                    float e = Mathf.Sin(p * Mathf.PI);                         // 平滑进入退出
                    float eject = p < 0.35f ? EaseOut(p / 0.35f) : 1f;         // 拆匣进度
                    float insert = p > 0.5f ? EaseOut((p - 0.5f) / 0.3f) : 0f; // 新匣插入进度
                    pos += Vector3.down * (reloadDrop * e);
                    pos += Vector3.right * (0.05f * e);
                    pos += Vector3.down * (0.06f * eject * (1f - insert));     // 拆匣时枪身下沉更多
                    if (p > 0.72f && p < 0.85f)                                 // 插匣到位的反冲
                        pos += Vector3.up * (0.015f * Mathf.Sin((p - 0.72f) / 0.13f * Mathf.PI));
                    rot *= Quaternion.Euler(16f * e, reloadWobble * 0.4f * Mathf.Sin(p * Mathf.PI * 2f) * e, reloadRoll * e);
                    AnimateMag(eject, insert);
                }
            }

            // 切枪下压（正弦包络 0→1→0）
            if (_t >= 0f)
            {
                _t += Time.deltaTime * _dir;
                if (_t <= 0f || _t >= switchDuration)
                {
                    _t = -1f;
                }
                else
                {
                    float s = Mathf.Sin(_t / switchDuration * Mathf.PI);
                    pos += Vector3.down * (dipDepth * s);
                    rot *= Quaternion.Euler(dipPitch * s, 0f, 0f); // 枪口朝下压
                }
            }

            // 检视动作：抬枪至面前，先左翻展示右侧、再右翻展示左侧，随后归位
            if (_inspectT >= 0f)
            {
                _inspectT += Time.deltaTime;
                float p = _inspectT / inspectDuration;
                if (p >= 1f)
                {
                    _inspectT = -1f;
                }
                else
                {
                    float env = Mathf.Sin(p * Mathf.PI);       // 进入退出包络
                    float show = Mathf.Sin(p * Mathf.PI * 2f); // 先转一侧再转另一侧
                    pos += (Vector3.up * 0.07f + Vector3.left * 0.06f + Vector3.back * 0.04f) * env;
                    rot *= Quaternion.Euler(-12f * env, 65f * show, -20f * show); // 枪口微抬 + 左右偏航 + 侧倾
                }
            }

            // 挥砍：刀体绕刀柄轴心旋转（握刀的手保持不动），视图仅带轻微身体惯性
            if (_swingT >= 0f)
            {
                _swingT += Time.deltaTime;
                float p = _swingT / (_heavy ? heavyDuration : lightDuration);
                if (p >= 1f)
                {
                    _swingT = -1f;
                    RestoreMeleeModel();
                }
                else if (_meleeModel != null)
                {
                    ApplySwing(p);
                    float bodyW = Mathf.Sin(p * Mathf.PI);
                    pos += Vector3.down * (0.04f * bodyW);
                    rot *= Quaternion.Euler(6f * bodyW, 0f, 0f);
                }
            }

            transform.localPosition = pos;
            transform.localRotation = rot;
        }

        // 弹匣动作：滑出枪身 → 消失（丢弃）→ 新弹匣从下方推入
        private void AnimateMag(float eject, float insert)
        {
            if (_mag == null) return;

            bool hidden = eject >= 1f && insert <= 0f;
            if (_mag.gameObject.activeSelf == hidden)
                _mag.gameObject.SetActive(!hidden);

            float off = eject * (1f - insert); // 弹匣离枪位移量 0→1→0
            Vector3 lp = _magBase + Vector3.down * (0.16f * off);
            lp.z += 0.03f * off;               // 稍向前抽出
            _mag.localPosition = lp;
        }

        private void RestoreMag()
        {
            if (_mag == null) return;
            _mag.gameObject.SetActive(true);
            _mag.localPosition = _magBase;
            _mag = null;
        }

        // 三段式挥砍：前摇(0~0.3) → 快速劈砍(0.3~0.5) → 收势(0.5~1)
        // 直接驱动刀体绕刀柄轴心旋转（腕部发力），手部握持位置保持不变
        private void ApplySwing(float p)
        {
            Vector3 wRot = _heavy ? HeavyWindupRot : LightWindupRot;
            Vector3 sRot = _heavy ? HeavyStrikeRot : LightStrikeRot;

            Vector3 e;
            if (p < 0.3f)
                e = Vector3.Lerp(Vector3.zero, wRot, EaseOut(p / 0.3f));
            else if (p < 0.5f)
                e = Vector3.Lerp(wRot, sRot, EaseIn((p - 0.3f) / 0.2f));
            else
                e = Vector3.Lerp(sRot, Vector3.zero, EaseOut((p - 0.5f) / 0.5f));

            Quaternion delta = Quaternion.Euler(e);
            _meleeModel.localRotation = _meleeBaseRot * delta;
            // 绕刀柄轴心旋转的位移补偿：刀柄中心在旋转前后保持原位（手不动）
            _meleeModel.localPosition = _meleeBasePos + _meleeBaseRot * (GripLocal - delta * GripLocal);
        }

        // 缓存当前近战武器模型的基础位姿（切枪/初始化时调用）
        private void CacheMeleeModel()
        {
            var cur = weapon != null ? weapon.Current : null;
            if (cur != null && cur.melee && cur.model != null)
            {
                _meleeModel = cur.model.transform;
                _meleeBasePos = _meleeModel.localPosition;
                _meleeBaseRot = _meleeModel.localRotation;
            }
        }

        // 还原刀体位姿（挥砍结束/被打断时调用；保留缓存以支持连续挥砍）
        private void RestoreMeleeModel()
        {
            if (_meleeModel == null) return;
            _meleeModel.localPosition = _meleeBasePos;
            _meleeModel.localRotation = _meleeBaseRot;
        }

        // 单手持刀：持刀时隐藏左臂，切回枪械时恢复
        private void ApplyLeftArm()
        {
            if (_leftArm == null) return;
            var cur = weapon != null ? weapon.Current : null;
            _leftArm.gameObject.SetActive(cur == null || !cur.melee);
        }

        private static float EaseOut(float k) => 1f - (1f - k) * (1f - k);
        private static float EaseIn(float k) => k * k;
    }
}
