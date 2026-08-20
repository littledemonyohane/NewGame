using UnityEngine;

namespace SpatialRhythm.Tracking
{
    /// <summary>
    /// 一次姿态采样。对应设计文档 §7.6 的 PoseSample 契约。
    /// Rotation 始终是【物理姿态】（未经视角增益 G 放大）。
    /// </summary>
    public struct PoseSample
    {
        /// <summary>采样时刻，位于 <see cref="Core.AppClock"/> 的时间轴上（秒）。</summary>
        public double Timestamp;

        /// <summary>设备姿态，相对开局锚点。</summary>
        public Quaternion Rotation;

        /// <summary>角速度，单位 度/秒，物理角。用于 One Euro 自适应与预测外推。</summary>
        public Vector3 AngularVelocity;

        /// <summary>追踪质量 0..1。3DoF 下恒为 1，为未来 6DoF 预留。</summary>
        public float Quality;

        /// <summary>首版 3DoF 不提供位置。为未来 6DoF 预留，不参与判定。</summary>
        public Vector3? Position;

        public static PoseSample Identity(double timestamp) => new PoseSample
        {
            Timestamp = timestamp,
            Rotation = Quaternion.identity,
            AngularVelocity = Vector3.zero,
            Quality = 1f,
            Position = null
        };
    }
}
