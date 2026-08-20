using UnityEngine;

namespace SpatialRhythm.Core
{
    /// <summary>
    /// 全局参考时钟。所有姿态采样、呈现时间戳、触摸时间戳都必须换算到这条时间轴上，
    /// 否则 "所见即所判" 的回溯查询会取到错误的姿态。
    ///
    /// 注意：节奏判定的主时钟是 <see cref="UnityEngine.AudioSettings.dspTime"/>（见 Conductor），
    /// 本时钟只负责姿态/输入这条链路的对齐。两者的偏移在 <see cref="DspOffset"/> 中标定。
    /// </summary>
    public static class AppClock
    {
        /// <summary>参考时间轴的当前值（秒）。</summary>
        public static double Now => Time.realtimeSinceStartupAsDouble;

        /// <summary>
        /// dspTime 与本参考时钟的偏移：dspTime ≈ AppClock.Now + DspOffset。
        /// 每帧由 Conductor 更新，用于把音频时间换算到姿态时间轴。
        /// </summary>
        public static double DspOffset { get; internal set; }

        public static double ToAppTime(double dspTime) => dspTime - DspOffset;

        public static double ToDspTime(double appTime) => appTime + DspOffset;
    }
}
