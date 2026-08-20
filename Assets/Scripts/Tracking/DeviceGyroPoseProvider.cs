using UnityEngine;

namespace SpatialRhythm.Tracking
{
    /// <summary>
    /// 真机陀螺仪姿态源。Stage 4 使用。
    ///
    /// iOS 的 <see cref="Input.gyro"/> 返回的 attitude 使用右手坐标系且 Z 轴朝向相反，
    /// 需要转换到 Unity 的左手坐标系；横屏握持还要再补一次基座旋转。
    /// 这段转换是真机上最容易出错的地方，Stage 4 的第一件事就是用 HUD 核对轴向。
    /// </summary>
    public sealed class DeviceGyroPoseProvider : IPoseProvider
    {
        private Quaternion _anchorInverse = Quaternion.identity;
        private Vector3 _lastEuler;
        private double _lastTimestamp;
        private bool _hasLast;

        public string ProviderName => "DeviceGyro";

        public bool IsAvailable => SystemInfo.supportsGyroscope;

        public void Initialize()
        {
            if (!IsAvailable)
            {
                return;
            }

            Input.gyro.enabled = true;
            // 100Hz。真机上部分机型会向下取整到自身上限。
            Input.gyro.updateInterval = 0.01f;
            _hasLast = false;
            Recenter();
        }

        public PoseSample Sample(double timestamp)
        {
            if (!IsAvailable)
            {
                return PoseSample.Identity(timestamp);
            }

            Quaternion device = ToUnitySpace(Input.gyro.attitude);
            Quaternion relative = _anchorInverse * device;

            Vector3 euler = relative.eulerAngles;
            float dt = _hasLast ? (float)(timestamp - _lastTimestamp) : 0f;

            Vector3 angularVelocity;
            if (_hasLast && dt > 0f)
            {
                angularVelocity = new Vector3(
                    Mathf.DeltaAngle(_lastEuler.x, euler.x),
                    Mathf.DeltaAngle(_lastEuler.y, euler.y),
                    Mathf.DeltaAngle(_lastEuler.z, euler.z)) / dt;
            }
            else
            {
                // 传感器自带角速度更准，但单位是弧度/秒且在设备坐标系下。
                angularVelocity = Input.gyro.rotationRateUnbiased * Mathf.Rad2Deg;
            }

            _lastEuler = euler;
            _lastTimestamp = timestamp;
            _hasLast = true;

            return new PoseSample
            {
                Timestamp = timestamp,
                Rotation = relative,
                AngularVelocity = angularVelocity,
                Quality = 1f,
                Position = null
            };
        }

        public void Recenter()
        {
            if (!IsAvailable)
            {
                return;
            }

            _anchorInverse = Quaternion.Inverse(ToUnitySpace(Input.gyro.attitude));
            _hasLast = false;
        }

        public void Shutdown()
        {
            if (IsAvailable)
            {
                Input.gyro.enabled = false;
            }
        }

        /// <summary>
        /// CoreMotion 右手系 → Unity 左手系，并补上横屏握持的基座旋转。
        /// </summary>
        private static Quaternion ToUnitySpace(Quaternion attitude)
        {
            Quaternion converted = new Quaternion(attitude.x, attitude.y, -attitude.z, -attitude.w);
            // 手机横持、屏幕朝向玩家时，设备 -Z 指向前方。
            return Quaternion.Euler(90f, 0f, 0f) * converted;
        }
    }
}
