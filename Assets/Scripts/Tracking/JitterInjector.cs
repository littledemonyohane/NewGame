using UnityEngine;

namespace SpatialRhythm.Tracking
{
    /// <summary>
    /// 向姿态注入可配置的模拟手抖与低频漂移。
    ///
    /// 存在的理由：Editor 里一切都完美、到真机全崩，是这类项目最常见的失败模式。
    /// 有了它，One Euro 参数和双锥半径在 Editor 阶段就能被真实地调，
    /// 而不是等出包后才发现判定锥根本压不住噪声。
    ///
    /// 使用 Perlin 噪声而非白噪声：手抖是带限的、连续的，白噪声不像手抖，
    /// 而且 Perlin 可复现，便于 A/B 对比。
    /// </summary>
    [System.Serializable]
    public sealed class JitterInjector
    {
        [Header("手抖")]
        [Tooltip("单轴抖动幅度（度，物理角）。真机静止握持典型值 0.1–0.5。")]
        [SerializeField] private float _amplitudeDeg = 0.3f;

        [Tooltip("抖动主频（Hz）。生理性震颤集中在 4–8 Hz。")]
        [SerializeField] private float _frequencyHz = 5f;

        [Header("低频漂移")]
        [Tooltip("陀螺仪 yaw 漂移速率（度/分钟）。真机典型值 2–10。")]
        [SerializeField] private float _yawDriftDegPerMinute = 4f;

        [SerializeField] private bool _enabled;

        private const float SeedYaw = 11.37f;
        private const float SeedPitch = 53.11f;
        private const float SeedRoll = 97.53f;

        public bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        public float AmplitudeDeg
        {
            get => _amplitudeDeg;
            set => _amplitudeDeg = Mathf.Max(0f, value);
        }

        public float FrequencyHz
        {
            get => _frequencyHz;
            set => _frequencyHz = Mathf.Max(0.01f, value);
        }

        public float YawDriftDegPerMinute
        {
            get => _yawDriftDegPerMinute;
            set => _yawDriftDegPerMinute = value;
        }

        public PoseSample Apply(PoseSample sample)
        {
            if (!_enabled)
            {
                return sample;
            }

            float t = (float)sample.Timestamp;

            float jitterYaw = Noise(SeedYaw, t) * _amplitudeDeg;
            float jitterPitch = Noise(SeedPitch, t) * _amplitudeDeg;
            // roll 的生理抖动通常小于 yaw/pitch。
            float jitterRoll = Noise(SeedRoll, t) * _amplitudeDeg * 0.6f;

            float drift = _yawDriftDegPerMinute * t / 60f;

            sample.Rotation = sample.Rotation * Quaternion.Euler(jitterPitch, jitterYaw + drift, jitterRoll);
            return sample;
        }

        /// <summary>返回 [-1, 1] 的带限连续噪声。</summary>
        private float Noise(float seed, float time)
        {
            return (Mathf.PerlinNoise(seed, time * _frequencyHz) - 0.5f) * 2f;
        }
    }
}
