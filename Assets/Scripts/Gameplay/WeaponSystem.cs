using System;
using Unity.Netcode;
using UnityEngine;

namespace FpsGame.Gameplay
{
    /// <summary>单把武器配置（由预制体搭建脚本赋值并随预制体序列化）。</summary>
    [Serializable]
    public class WeaponSpec
    {
        public string displayName = "武器";
        public GameObject model;          // 该武器在 ViewRig 下的模型根（切换时显隐）
        public Transform muzzle;          // 枪口（近战为空）
        public Transform magazine;        // 弹匣（换弹动画用，可为空）
        public Vector3 aimOffset;         // 开镜视图偏移（zero = 用 ArmsAnimator 默认）
        public bool melee;                // 近战：无弹药/无换弹/无弹道
        public float heavyDamage = 0f;    // 近战重击伤害（0 = 无重击）
        public float heavyInterval = 0f;  // 近战重击间隔（0 = fireInterval × 2）
        public float damage = 20f;
        public float headshotMultiplier = 2f;
        public float fireInterval = 0.15f;
        public float range = 100f;
        public int magazineSize = 30;     // 0 = 无弹药（近战）
        public float reloadTime = 1.5f;
    }

    /// <summary>
    /// 本地武器逻辑：三槽位（主武器/副武器/近战），射线射击与近战判定、弹药、换弹、切换。
    /// 伤害通过目标的 ServerRpc 提交（局域网可信环境，客户端判定、服务端结算）。
    /// </summary>
    public class WeaponSystem : MonoBehaviour
    {
        [Header("武器槽（0=主武器 1=副武器 2=近战）")]
        public WeaponSpec[] weapons;

        [Header("开镜（仅枪械）")]
        public float aimFov = 50f;       // 开镜视野
        public float aimLerpSpeed = 12f; // 开镜过渡速度

        [Header("干员被动")]
        public float reloadSpeedMul = 1f; // 换弹速度倍率（出生时按所选干员赋值，>1 更快）

        public event Action<int, int> AmmoChanged;       // (当前, 弹匣容量)
        public event Action<bool> ReloadChanged;         // 是否换弹中
        public event Action HitConfirmed;
        public event Action<bool> MeleeSwing;            // 近战挥砍（true=重击），驱动第一人称动画
        public event Action<bool> AimingChanged;         // 开镜状态变化（仅枪械）
        public event Action<int> WeaponSwitched;         // 切换后的槽位索引

        public int CurrentIndex => _index;
        public WeaponSpec Current => weapons != null && weapons.Length > 0 ? weapons[_index] : null;
        public string CurrentName => Current != null ? Current.displayName : "";
        public int Ammo => _ammo;
        public int MagazineSize => Current != null ? Current.magazineSize : 0;
        public bool IsReloading => _reloading;
        public bool IsAiming => _aiming;
        public bool IsMelee => Current != null && Current.melee;

        private int _index;
        private int _ammo;
        private bool _reloading;
        private float _nextFireTime;
        private float _reloadEndTime;
        private float _switchLockUntil; // 切枪动作期间禁止开火
        private bool _aiming;
        private float _baseFov;

        private void Awake()
        {
            _ammo = MagazineSize;
            ApplyModels();
        }

        private void OnEnable() => AmmoChanged?.Invoke(_ammo, MagazineSize);

        private void Update()
        {
            if (_reloading && Time.time >= _reloadEndTime)
            {
                _reloading = false;
                _ammo = MagazineSize;
                AmmoChanged?.Invoke(_ammo, MagazineSize);
                ReloadChanged?.Invoke(false);
            }

            // 开镜视野平滑过渡
            if (viewCamera != null)
            {
                if (_baseFov <= 0f) _baseFov = viewCamera.fieldOfView;
                float targetFov = _aiming ? aimFov : _baseFov;
                if (Mathf.Abs(viewCamera.fieldOfView - targetFov) > 0.05f)
                    viewCamera.fieldOfView = Mathf.Lerp(viewCamera.fieldOfView, targetFov, aimLerpSpeed * Time.deltaTime);
            }
        }

        // ---------- 切换 ----------

        public void SwitchTo(int index)
        {
            if (weapons == null || index < 0 || index >= weapons.Length || index == _index) return;
            _index = index;
            _reloading = false;
            _ammo = MagazineSize;
            ApplyModels();
            AmmoChanged?.Invoke(_ammo, MagazineSize);
            ReloadChanged?.Invoke(false);
            WeaponSwitched?.Invoke(_index);
            _switchLockUntil = Time.time + 0.35f; // 与 ArmsAnimator.switchDuration 一致
            if (_aiming) { _aiming = false; AimingChanged?.Invoke(false); }
        }

        /// <summary>滚轮循环切换：direction 正=下一个槽位，负=上一个。</summary>
        public void CycleWeapon(int direction)
        {
            if (weapons == null || weapons.Length == 0) return;
            int n = weapons.Length;
            SwitchTo(((_index + direction) % n + n) % n);
        }

        /// <summary>右键开镜（仅枪械生效，近战忽略）。</summary>
        public void SetAiming(bool want)
        {
            bool target = want && Current != null && !Current.melee;
            if (_aiming == target) return;
            _aiming = target;
            AimingChanged?.Invoke(target);
        }

        private void ApplyModels()
        {
            for (int i = 0; i < weapons.Length; i++)
            {
                var spec = weapons[i];
                if (spec != null && spec.model != null)
                    spec.model.SetActive(i == _index);
            }
        }

        /// <summary>重生调用：回到主武器并补满弹药。</summary>
        public void ResetAmmo()
        {
            _reloading = false;
            if (_index != 0)
            {
                _index = 0;
                ApplyModels();
                WeaponSwitched?.Invoke(0);
            }
            _ammo = MagazineSize;
            AmmoChanged?.Invoke(_ammo, MagazineSize);
            ReloadChanged?.Invoke(false);
        }

        // ---------- 开火 ----------

        public void BeginReload()
        {
            var spec = Current;
            if (spec == null || spec.melee || _reloading || _ammo == spec.magazineSize) return;
            _reloading = true;
            _reloadEndTime = Time.time + spec.reloadTime / reloadSpeedMul; // 干员换弹速度被动
            ReloadChanged?.Invoke(true);
        }

        public void TryFire()
        {
            var spec = Current;
            if (spec == null || _reloading || Time.time < _nextFireTime || Time.time < _switchLockUntil) return;

            if (spec.melee)
            {
                _nextFireTime = Time.time + spec.fireInterval;
                MeleeSwing?.Invoke(false); // 左键轻击
                MeleeAttack(spec, spec.damage);
                return;
            }
            if (_ammo <= 0) { BeginReload(); return; }
            _nextFireTime = Time.time + spec.fireInterval;
            _ammo--;
            AmmoChanged?.Invoke(_ammo, spec.magazineSize);
            FireRaycast(spec);
        }

        /// <summary>右键重击（仅近战）：更高伤害、更长间隔。</summary>
        public void TryHeavyAttack()
        {
            var spec = Current;
            if (spec == null || !spec.melee || spec.heavyDamage <= 0f) return;
            if (_reloading || Time.time < _nextFireTime || Time.time < _switchLockUntil) return;

            _nextFireTime = Time.time + (spec.heavyInterval > 0f ? spec.heavyInterval : spec.fireInterval * 2f);
            MeleeSwing?.Invoke(true);
            MeleeAttack(spec, spec.heavyDamage);
        }

        private void MeleeAttack(WeaponSpec spec, float damage)
        {
            Camera cam = viewCamera != null ? viewCamera : Camera.main;
            if (cam == null) return;

            Transform camT = cam.transform;
            Vector3 origin = camT.position;
            Vector3 direction = camT.forward;

            // 短距离近战判定（命中最近的有效目标，跳过自己）
            RaycastHit[] hits = Physics.RaycastAll(origin, direction, spec.range);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.transform.root == transform.root) continue;

                PlayerNetwork victim = hit.collider.GetComponentInParent<PlayerNetwork>();
                if (victim != null)
                {
                    float dmg = damage;
                    if (hit.collider.name == "Head") dmg *= spec.headshotMultiplier;
                    if (victim.Alive.Value)
                    {
                        NetworkManager nm = NetworkManager.Singleton;
                        if (nm != null)
                            victim.TakeDamageServerRpc(dmg, nm.LocalClientId);
                        HitConfirmed?.Invoke();
                    }
                }
                break;
            }
            // 近战无弹道特效
        }

        private void FireRaycast(WeaponSpec spec)
        {
            Camera cam = viewCamera != null ? viewCamera : Camera.main;
            if (cam == null) return;

            Transform camT = cam.transform;
            Vector3 origin = camT.position;
            Vector3 direction = camT.forward;
            Vector3 hitPoint = origin + direction * spec.range;

            // 命中最近的有效目标（跳过自己）
            RaycastHit[] hits = Physics.RaycastAll(origin, direction, spec.range);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.transform.root == transform.root) continue;

                hitPoint = hit.point;
                PlayerNetwork victim = hit.collider.GetComponentInParent<PlayerNetwork>();
                if (victim != null)
                {
                    float dmg = spec.damage;
                    if (hit.collider.name == "Head") dmg *= spec.headshotMultiplier;
                    if (victim.Alive.Value)
                    {
                        NetworkManager nm = NetworkManager.Singleton;
                        if (nm != null)
                            victim.TakeDamageServerRpc(dmg, nm.LocalClientId);
                        HitConfirmed?.Invoke();
                    }
                }
                break;
            }

            Vector3 muzzlePos = spec.muzzle != null ? spec.muzzle.position : origin + direction * 0.5f;

            // 本地立即画弹道，其余客户端通过 RPC 绘制（本地那份由 RPC 跳过）
            UI.HudUI.Instance?.DrawTracer(muzzlePos, hitPoint);
            if (owner != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
                owner.BroadcastTracer(muzzlePos, hitPoint);
        }

        [Header("引用（由场景搭建脚本赋值）")]
        public Camera viewCamera;
        public PlayerNetwork owner;
    }
}
