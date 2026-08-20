using UnityEngine;

namespace SpatialRhythm.InputLayer
{
    public enum TriggerZone
    {
        None,
        LeftTrigger,
        RightTrigger,
        Center
    }

    /// <summary>
    /// 一次触发事件。对应设计文档 §7.6 的 TouchEvent 契约。
    ///
    /// <see cref="IsOsTimestamp"/> 是 P0 阶段必须诚实记录的一位：
    /// 只有拿到真实的 OS 触摸时间戳，"所见即所判"才成立；
    /// 退化到帧时间时会引入最多一帧（8–16ms）的误差，日志里必须能区分。
    /// </summary>
    public struct TouchEvent
    {
        /// <summary>触发时刻，位于 <see cref="Core.AppClock"/> 时间轴（秒）。</summary>
        public double Timestamp;

        /// <summary>是否为真实 OS 时间戳。false 表示退化为帧时间。</summary>
        public bool IsOsTimestamp;

        public Vector2 ScreenPosition;

        public TriggerZone Zone;

        public int FingerId;
    }
}
