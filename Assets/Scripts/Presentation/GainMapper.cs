using UnityEngine;

namespace SpatialRhythm.Presentation
{
    /// <summary>
    /// 物理角 → 虚拟角的映射，外加角度笼与隐形重定心。
    ///
    /// 设计文档最关键的一处口径：θ_virt = G · θ_phys。
    /// - 角度笼、判定容差、抖动指标 → 全部用【虚拟角】
    /// - 体力预算、角速度分档     → 全部用【物理角】
    /// 两者混用会让实测数据无法解释，所以本类同时输出两套值。
    /// </summary>
    [System.Serializable]
    public sealed class GainMapper
    {
        [Header("视角增益 G")]
        [Tooltip("yaw/pitch 的增益。1.6–2.0 为推荐区间：物理 ±35° 覆盖虚拟 ±60–70°，手腕即可完成。")]
        [SerializeField] private float _gain = 1.8f;

        [Tooltip("roll 一般不放大——放大 roll 只会加剧眩晕，不会降低体力消耗。")]
        [SerializeField] private float _rollGain = 1f;

        [Header("角度笼（虚拟角，度）")]
        [SerializeField] private float _yawLimit = 75f;
        [SerializeField] private float _pitchMin = -50f;
        [SerializeField] private float _pitchMax = 55f;

        [Tooltip("软边界宽度。进入这段后按 tanh 压缩，永远不硬停。")]
        [SerializeField] private float _softKnee = 15f;

        [Header("隐形重定心")]
        [Tooltip("低于人类视觉运动感知阈值的速率，玩家察觉不到，但 3 分钟能修正 100° 以上的累积偏差。")]
        [SerializeField] private float _recenterDegPerSecond = 0.8f;

        [Tooltip("玩家平均朝向偏离超过该角度（物理角）才启动重定心。")]
        [SerializeField] private float _recenterDeadZone = 8f;

        [SerializeField] private bool _recenterEnabled = true;

        private float _anchorYaw;
        private float _averagedYaw;
        private bool _initialized;

        public float Gain
        {
            get => _gain;
            set => _gain = Mathf.Clamp(value, 0.5f, 4f);
        }

        public bool RecenterEnabled
        {
            get => _recenterEnabled;
            set => _recenterEnabled = value;
        }

        /// <summary>最近一次映射的物理欧拉角（pitch, yaw, roll）。</summary>
        public Vector3 LastPhysicalEuler { get; private set; }

        /// <summary>最近一次映射的虚拟欧拉角（pitch, yaw, roll）。</summary>
        public Vector3 LastVirtualEuler { get; private set; }

        /// <summary>累计的隐形重定心量（物理角，度）。用于诊断"肌肉记忆是否被破坏"。</summary>
        public float AccumulatedRecenterDeg { get; private set; }

        public void Reset()
        {
            _anchorYaw = 0f;
            _averagedYaw = 0f;
            _initialized = false;
            AccumulatedRecenterDeg = 0f;
        }

        /// <summary>把已滤波的物理姿态映射为呈现用的虚拟姿态。</summary>
        public Quaternion Map(Quaternion physicalRotation, float deltaTime)
        {
            Vector3 euler = physicalRotation.eulerAngles;
            float physYaw = Mathf.DeltaAngle(0f, euler.y);
            float physPitch = Mathf.DeltaAngle(0f, euler.x);
            float physRoll = Mathf.DeltaAngle(0f, euler.z);

            if (!_initialized)
            {
                _averagedYaw = physYaw;
                _anchorYaw = 0f;
                _initialized = true;
            }

            UpdateInvisibleRecenter(physYaw, deltaTime);

            float relativeYaw = Mathf.DeltaAngle(_anchorYaw, physYaw);
            LastPhysicalEuler = new Vector3(physPitch, relativeYaw, physRoll);

            float virtualYaw = SoftClamp(relativeYaw * _gain, -_yawLimit, _yawLimit, _softKnee);
            float virtualPitch = SoftClamp(physPitch * _gain, _pitchMin, _pitchMax, _softKnee);
            float virtualRoll = physRoll * _rollGain;

            LastVirtualEuler = new Vector3(virtualPitch, virtualYaw, virtualRoll);
            return Quaternion.Euler(virtualPitch, virtualYaw, virtualRoll);
        }

        /// <summary>把物理角速度换算成虚拟角速度（可读性预算用的就是这个）。</summary>
        public Vector3 ToVirtualAngularVelocity(Vector3 physicalAngularVelocity)
        {
            return new Vector3(
                physicalAngularVelocity.x * _gain,
                physicalAngularVelocity.y * _gain,
                physicalAngularVelocity.z * _rollGain);
        }

        /// <summary>显式重定心：把当前朝向直接记为正前方。</summary>
        public void RecenterNow()
        {
            _anchorYaw = _averagedYaw;
        }

        private void UpdateInvisibleRecenter(float physYaw, float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            // 长时间平均朝向，时间常数约 4 秒。
            float alpha = 1f - Mathf.Exp(-deltaTime / 4f);
            _averagedYaw += Mathf.DeltaAngle(_averagedYaw, physYaw) * alpha;

            if (!_recenterEnabled)
            {
                return;
            }

            float offset = Mathf.DeltaAngle(_anchorYaw, _averagedYaw);
            if (Mathf.Abs(offset) <= _recenterDeadZone)
            {
                return;
            }

            float step = Mathf.Clamp(offset, -_recenterDegPerSecond * deltaTime, _recenterDegPerSecond * deltaTime);
            _anchorYaw += step;
            AccumulatedRecenterDeg += Mathf.Abs(step);
        }

        /// <summary>
        /// 软边界：线性区之外按 tanh 压缩并渐进至上限，永远不硬停。
        /// 硬停会产生"撞墙"顿挫，也会让玩家误以为是判定失灵。
        /// </summary>
        private static float SoftClamp(float value, float min, float max, float knee)
        {
            if (knee <= 0.01f)
            {
                return Mathf.Clamp(value, min, max);
            }

            if (value > max - knee)
            {
                float over = value - (max - knee);
                return (max - knee) + knee * (float)System.Math.Tanh(over / knee);
            }

            if (value < min + knee)
            {
                float over = (min + knee) - value;
                return (min + knee) - knee * (float)System.Math.Tanh(over / knee);
            }

            return value;
        }
    }
}
