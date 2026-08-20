using UnityEngine;

namespace SpatialRhythm.Tracking
{
    /// <summary>
    /// One Euro Filter 的标量实现（Casiez et al. 2012）。
    ///
    /// 固定截止频率的低通滤波是错的：静止时平滑不够（抖），运动时延迟太大（黏）。
    /// One Euro 的截止频率随速度自适应——慢速强平滑，快速弱平滑。
    ///
    /// <see cref="IsAngle"/> 为 true 时按角度处理，内部做连续化（unwrap），
    /// 避免 ±180° 处的跳变。
    /// </summary>
    public sealed class OneEuroFilterScalar
    {
        private float _minCutoff;
        private float _beta;
        private float _dCutoff;

        private bool _initialized;
        private float _lastRawContinuous;
        private float _lastFiltered;
        private float _lastDerivative;
        private double _lastTimestamp;

        public bool IsAngle { get; set; }

        public OneEuroFilterScalar(float minCutoff = 1.2f, float beta = 0.02f, float dCutoff = 1.0f, bool isAngle = false)
        {
            SetParameters(minCutoff, beta, dCutoff);
            IsAngle = isAngle;
        }

        public void SetParameters(float minCutoff, float beta, float dCutoff)
        {
            _minCutoff = Mathf.Max(0.001f, minCutoff);
            _beta = Mathf.Max(0f, beta);
            _dCutoff = Mathf.Max(0.001f, dCutoff);
        }

        public void Reset()
        {
            _initialized = false;
        }

        public float Filter(float value, double timestamp)
        {
            if (!_initialized)
            {
                _initialized = true;
                _lastRawContinuous = value;
                _lastFiltered = value;
                _lastDerivative = 0f;
                _lastTimestamp = timestamp;
                return value;
            }

            float dt = (float)(timestamp - _lastTimestamp);
            if (dt <= 0f)
            {
                return _lastFiltered;
            }

            _lastTimestamp = timestamp;

            // 角度连续化：把 value 映射到与上一次采样相邻的分支上。
            float raw = IsAngle
                ? _lastRawContinuous + Mathf.DeltaAngle(_lastRawContinuous, value)
                : value;

            float rate = 1f / dt;

            float derivative = (raw - _lastRawContinuous) * rate;
            float filteredDerivative = LowPass(derivative, _lastDerivative, Alpha(_dCutoff, rate));
            _lastDerivative = filteredDerivative;

            // 速度自适应：动得越快，截止频率越高，延迟越低。
            float cutoff = _minCutoff + _beta * Mathf.Abs(filteredDerivative);
            float filtered = LowPass(raw, _lastFiltered, Alpha(cutoff, rate));

            _lastRawContinuous = raw;
            _lastFiltered = filtered;
            return filtered;
        }

        private static float Alpha(float cutoff, float rate)
        {
            float tau = 1f / (2f * Mathf.PI * cutoff);
            float te = 1f / rate;
            return 1f / (1f + tau / te);
        }

        private static float LowPass(float value, float previous, float alpha)
        {
            return alpha * value + (1f - alpha) * previous;
        }
    }

    /// <summary>
    /// 姿态滤波器：对 yaw / pitch / roll 分别用一个 One Euro 实例。
    ///
    /// 之所以在欧拉角而非四元数上滤波，是为了满足设计文档 §3.3 的"分轴调参"要求
    /// （roll 可以更重，因为 roll 抖动对可玩性影响最小但视觉上最晕）。
    /// 这要求 pitch 远离 ±90° 的万向锁——设计文档 §1.3 的角度笼把 pitch 限制在
    /// [-50°, +55°]，因此安全。
    /// </summary>
    public sealed class PoseFilter
    {
        private readonly OneEuroFilterScalar _yaw = new OneEuroFilterScalar(isAngle: true);
        private readonly OneEuroFilterScalar _pitch = new OneEuroFilterScalar(isAngle: true);
        private readonly OneEuroFilterScalar _roll = new OneEuroFilterScalar(isAngle: true);

        public bool Enabled { get; set; } = true;

        public void SetParameters(float minCutoff, float beta, float dCutoff, float rollCutoffScale = 0.6f)
        {
            _yaw.SetParameters(minCutoff, beta, dCutoff);
            _pitch.SetParameters(minCutoff, beta, dCutoff);
            // roll 用更低的截止频率 = 更强的平滑。
            _roll.SetParameters(minCutoff * rollCutoffScale, beta, dCutoff);
        }

        public void Reset()
        {
            _yaw.Reset();
            _pitch.Reset();
            _roll.Reset();
        }

        public Quaternion Filter(Quaternion rotation, double timestamp)
        {
            if (!Enabled)
            {
                return rotation;
            }

            Vector3 euler = rotation.eulerAngles;
            float yaw = _yaw.Filter(euler.y, timestamp);
            float pitch = _pitch.Filter(euler.x, timestamp);
            float roll = _roll.Filter(euler.z, timestamp);
            return Quaternion.Euler(pitch, yaw, roll);
        }
    }
}
