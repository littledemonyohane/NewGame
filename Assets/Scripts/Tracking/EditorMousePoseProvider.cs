using UnityEngine;
using SpatialRhythm.Core;

namespace SpatialRhythm.Tracking
{
    /// <summary>
    /// Editor 内的姿态模拟：鼠标移动 = 手机的 yaw / pitch，Q/E = roll，R = 重定心。
    ///
    /// 输出的是【物理姿态】，灵敏度按"一次舒适的鼠标横扫 ≈ 物理 ±37°"标定，
    /// 这样配合默认 G=1.8 才能覆盖虚拟角度笼的 ±75°，与真机手感量级一致。
    ///
    /// 注意：本 Provider 拿不到真实抖动、真实延迟、真实疲劳。
    /// P0 的三条验收指标一条都不能靠它得出，它只用于验证逻辑与调参敏感度。
    /// </summary>
    public sealed class EditorMousePoseProvider : IPoseProvider
    {
        /// <summary>
        /// 每单位 "Mouse X/Y" 输入对应的物理角度。
        ///
        /// GetAxisRaw 返回的是【每帧增量】而不是速率，所以绝不能再乘帧率。
        /// 标定：一次舒适的横扫约 400 像素 ≈ 40 个输入单位 ≈ 物理 36°，
        /// 配合默认 G=1.8 正好覆盖虚拟角度笼的 ±65°。
        /// </summary>
        private const float DegreesPerMouseUnit = 0.9f;

        private const float RollDegreesPerSecond = 45f;

        // 手举着手机在身前，yaw 物理可及范围远小于 180°；
        // 顺带避免 _yaw 越过 ±180 后在 DeltaAngle 处发生翻转。
        private const float YawPhysicalLimit = 120f;

        /// <summary>
        /// 桌面大小的可活动体积（米，半宽/半高/半深）。
        /// 约 70cm 宽 × 44cm 高 × 40cm 深，相当于坐在桌前手臂能覆盖的范围。
        /// </summary>
        private static readonly Vector3 DeskVolumeHalfExtents = new Vector3(0.35f, 0.22f, 0.20f);

        private const float TranslationSpeed = 0.45f;

        private Vector3 _simulatedPosition;

        private float _yaw;
        private float _pitch;
        private float _roll;

        private Vector3 _lastEuler;
        private double _lastTimestamp;
        private bool _hasLast;

        public string ProviderName => "EditorMouse";

        public bool IsAvailable => Application.isEditor || Application.platform == RuntimePlatform.WindowsPlayer;

        public void Initialize()
        {
            _yaw = 0f;
            _pitch = 0f;
            _roll = 0f;
            _hasLast = false;
        }

        public PoseSample Sample(double timestamp)
        {
            float dt = _hasLast ? (float)(timestamp - _lastTimestamp) : 0f;

            UpdateCursorLock();

            // 只在光标锁定时响应鼠标：否则在调参面板上移动鼠标也会转动视角。
            if (IsLocked)
            {
                _yaw += Input.GetAxisRaw("Mouse X") * DegreesPerMouseUnit;
                _pitch -= Input.GetAxisRaw("Mouse Y") * DegreesPerMouseUnit;
            }

            if (Input.GetKey(KeyCode.Q))
            {
                _roll -= RollDegreesPerSecond * dt;
            }

            if (Input.GetKey(KeyCode.E))
            {
                _roll += RollDegreesPerSecond * dt;
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                Recenter();
            }

            // 物理可及范围的硬边界（人的手腕/前臂极限），与虚拟角度笼是两回事。
            _yaw = Mathf.Clamp(_yaw, -YawPhysicalLimit, YawPhysicalLimit);
            _pitch = Mathf.Clamp(_pitch, -70f, 70f);
            _roll = Mathf.Clamp(_roll, -60f, 60f);

            var euler = new Vector3(_pitch, _yaw, _roll);

            UpdateSimulatedTranslation(euler.y, dt);

            Vector3 angularVelocity = Vector3.zero;
            if (_hasLast && dt > 0f)
            {
                angularVelocity = new Vector3(
                    Mathf.DeltaAngle(_lastEuler.x, euler.x),
                    Mathf.DeltaAngle(_lastEuler.y, euler.y),
                    Mathf.DeltaAngle(_lastEuler.z, euler.z)) / dt;
            }

            _lastEuler = euler;
            _lastTimestamp = timestamp;
            _hasLast = true;

            return new PoseSample
            {
                Timestamp = timestamp,
                Rotation = Quaternion.Euler(euler),
                AngularVelocity = angularVelocity,
                Quality = 1f,
                Position = _simulatedPosition
            };
        }

        /// <summary>
        /// WASD / Space / Ctrl 模拟设备在桌面大小体积内的平移。
        ///
        /// **这是在模拟未来 6DoF VIO 才能提供的独立位移。**
        /// 真机在无摄像头的 3DoF 下拿不到它——位置只能由肩关节杠杆从旋转派生，
        /// 与朝向刚性耦合，玩家无法独立控制。
        /// 它的用途是：在承诺接入摄像头之前，先在 Editor 里验证桌面尺度的位移玩法是否成立。
        /// </summary>
        private void UpdateSimulatedTranslation(float yawDegrees, float dt)
        {
            if (dt <= 0f || !IsLocked)
            {
                return;
            }

            var input = new Vector3(
                (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f),
                (Input.GetKey(KeyCode.Space) ? 1f : 0f) - (Input.GetKey(KeyCode.LeftControl) ? 1f : 0f),
                (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f));

            if (input.sqrMagnitude > 0.0001f)
            {
                // 只用 yaw 构造移动基，避免俯仰时上下键把人推离水平面。
                Quaternion planar = Quaternion.Euler(0f, yawDegrees, 0f);
                _simulatedPosition += planar * input.normalized * (TranslationSpeed * dt);
            }

            _simulatedPosition = new Vector3(
                Mathf.Clamp(_simulatedPosition.x, -DeskVolumeHalfExtents.x, DeskVolumeHalfExtents.x),
                Mathf.Clamp(_simulatedPosition.y, -DeskVolumeHalfExtents.y, DeskVolumeHalfExtents.y),
                Mathf.Clamp(_simulatedPosition.z, -DeskVolumeHalfExtents.z, DeskVolumeHalfExtents.z));
        }

        public void Recenter()
        {
            _yaw = 0f;
            _pitch = 0f;
            _roll = 0f;
            _simulatedPosition = Vector3.zero;
        }

        /// <summary>光标是否已锁定。未锁定时视角不响应鼠标。</summary>
        public static bool IsLocked => Cursor.lockState == CursorLockMode.Locked;

        /// <summary>供调参面板调用：打开面板时必须释放光标才能点到控件。</summary>
        public static void Unlock()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>
        /// 点击画面锁定、Esc 释放。
        ///
        /// 灵敏度校正后一次完整扫视约需 800 像素，不锁定会在窗口边缘卡住，
        /// 表现出来就像"转不动了"，很容易被误判成判定或滤波的问题。
        /// </summary>
        private static void UpdateCursorLock()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Unlock();
                return;
            }

            if (!IsLocked && Input.GetMouseButtonDown(0))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        public void Shutdown()
        {
        }
    }
}
