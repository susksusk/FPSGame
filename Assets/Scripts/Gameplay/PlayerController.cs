using UnityEngine;

namespace FpsGame.Gameplay
{
    /// <summary>
    /// 本地第一人称控制：移动、跳跃、鼠标视角、开火/换弹输入。
    /// 仅本地玩家启用；Transform 由 NetworkTransform(Owner 权威) 同步。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("移动")]
        public float walkSpeed = 6f;
        public float sprintSpeed = 9f;
        public float jumpHeight = 0.75f; // 标准 FPS 跳跃高度：过高会从集装箱顶跳出围墙
        public float gravity = 20f;

        [Header("视角")]
        public float mouseSensitivity = 2.2f;
        public float maxPitch = 88f;

        [Header("探头")]
        public float leanDistance = 0.45f; // 头部侧移距离（米）
        public float leanAngle = 14f;      // 视角滚转角度（度）
        public float leanSpeed = 7f;       // 探头过渡速度

        public Transform pitchRoot; // Head
        public WeaponSystem weapon;
        public ArmsAnimator viewAnim; // 第一人称视图动画（V 键检视）
        public float moveSpeedMul = 1f; // 干员移速倍率（出生时按所选干员赋值）

        private CharacterController _cc;
        private float _pitch;
        private float _vy;
        private float _nextWheelSwitch;
        private float _lean; // -1=左倾 1=右倾
        private bool _thirdPerson; // F5 第三人称观察

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
        }

        private void OnEnable()
        {
            _pitch = pitchRoot != null ? pitchRoot.localEulerAngles.x : 0f;
            if (_pitch > 180f) _pitch -= 360f;
        }

        private void Update()
        {
            // 键鼠模式要求鼠标锁定；手游控制层可见时无需锁定（触屏/鼠标点 UI）
            bool mobile = FpsGame.UI.MobileUI.Visible;
            if (Cursor.lockState != CursorLockMode.Locked && !mobile) return;

            Look();
            Move();
            if (!mobile) Lean(); // 探头是键鼠专属（Q/E）

            if (weapon != null)
            {
                if (Input.GetMouseButton(0) || FpsGame.UI.MobileInput.FireHeld) weapon.TryFire();
                if (Input.GetMouseButtonDown(1) || FpsGame.UI.MobileInput.TakeAim())
                {
                    if (weapon.IsMelee) weapon.TryHeavyAttack();
                    else weapon.SetAiming(!weapon.IsAiming); // 点按切换开镜：按一下开，再按关
                }
                if (Input.GetKeyDown(KeyCode.R) || FpsGame.UI.MobileInput.TakeReload()) weapon.BeginReload();
                if (Input.GetKeyDown(KeyCode.V) && viewAnim != null) viewAnim.PlayInspect(); // V 检视武器

                // F5 第三人称观察：显示自己的士兵模型、相机后移并隐藏第一人称手臂
                if (Input.GetKeyDown(KeyCode.F5) && weapon.owner != null)
                {
                    _thirdPerson = !_thirdPerson;
                    weapon.owner.SetThirdPerson(_thirdPerson);
                    if (weapon.owner.playerCamera != null)
                        weapon.owner.playerCamera.transform.localPosition = _thirdPerson ? new Vector3(0f, 0.35f, -3.2f) : Vector3.zero;
                }

                // 数字键直切槽位
                if (Input.GetKeyDown(KeyCode.Alpha1)) weapon.SwitchTo(0);
                if (Input.GetKeyDown(KeyCode.Alpha2)) weapon.SwitchTo(1);
                if (Input.GetKeyDown(KeyCode.Alpha3)) weapon.SwitchTo(2);

                // 滚轮循环切换（上=上一把，下=下一把），带间隔防连跳
                float scroll = Input.mouseScrollDelta.y;
                if (Mathf.Abs(scroll) > 0.01f && Time.time >= _nextWheelSwitch)
                {
                    _nextWheelSwitch = Time.time + 0.25f;
                    weapon.CycleWeapon(scroll > 0f ? -1 : 1);
                }

                // 手游：切枪按钮（循环下一把）
                if (FpsGame.UI.MobileInput.TakeSwitch()) weapon.CycleWeapon(1);
            }
        }

        private void Look()
        {
            // 开镜时降低灵敏度提升精准度
            float sens = mouseSensitivity * (weapon != null && weapon.IsAiming ? 0.6f : 1f);
            float mx = Input.GetAxis("Mouse X") * sens;
            float my = Input.GetAxis("Mouse Y") * sens;
            // 手游视角：拖动增量（像素）按灵敏度换算
            Vector2 td = FpsGame.UI.MobileInput.TakeLookDelta() * (sens * 0.4f);
            mx += td.x;
            my += td.y;
            transform.Rotate(0f, mx, 0f);
            _pitch = Mathf.Clamp(_pitch - my, -maxPitch, maxPitch);
            if (pitchRoot != null) pitchRoot.localRotation = Quaternion.Euler(_pitch, 0f, -leanAngle * _lean);
        }

        // Q/E 探头：头部侧移 + 视角轻微滚转
        private void Lean()
        {
            float target = (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);
            _lean = Mathf.MoveTowards(_lean, target, leanSpeed * Time.deltaTime);
            if (pitchRoot != null)
            {
                Vector3 lp = pitchRoot.localPosition;
                lp.x = leanDistance * _lean;
                pitchRoot.localPosition = lp;
            }
        }

        private void Move()
        {
            bool grounded = _cc.isGrounded;
            if (grounded && _vy < 0f) _vy = -2f;

            Vector2 mi = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (FpsGame.UI.MobileInput.Move.sqrMagnitude > mi.sqrMagnitude) mi = FpsGame.UI.MobileInput.Move; // 摇杆有输入时优先
            Vector3 input = new Vector3(mi.x, 0f, mi.y);
            input = Vector3.ClampMagnitude(input, 1f);
            Vector3 move = transform.TransformDirection(input);

            bool sprint = Input.GetKey(KeyCode.LeftShift) || FpsGame.UI.MobileInput.Sprint; // 摇杆推满自动疾跑
            float speed = sprint && input.z > 0.1f ? sprintSpeed : walkSpeed;
            speed *= moveSpeedMul; // 干员移速被动
            if (grounded && (Input.GetKeyDown(KeyCode.Space) || FpsGame.UI.MobileInput.TakeJump()))
                _vy = Mathf.Sqrt(2f * gravity * jumpHeight);

            _vy -= gravity * Time.deltaTime;
            move.y = _vy;
            _cc.Move(move * speed * Time.deltaTime);
        }
    }
}
