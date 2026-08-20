namespace SpatialRhythm.Judging
{
    public enum TimingGrade
    {
        Miss = 0,
        Good = 1,
        Great = 2,
        Perfect = 3
    }

    public enum FailureReason
    {
        None = 0,

        /// <summary>没进入激活锥——空间失败。红色 "Out of Frame"。</summary>
        OutOfFrame = 1,

        /// <summary>进了锥但时间偏差超窗——节奏失败。</summary>
        BadTiming = 2,

        /// <summary>完全没有触发。</summary>
        NoInput = 3
    }

    /// <summary>
    /// 一次判定的完整结果。对应设计文档 §7.6 的 JudgeResult 契约。
    ///
    /// 两种失败必须能被清晰归因：空间失败给"没套进"，时间失败给评级。
    /// 如果玩家分不清自己是打早了还是没瞄准，技能就无法成长。
    /// </summary>
    public struct JudgeResult
    {
        /// <summary>是否越过 θ_activate（含辅助扩大与滞回）。</summary>
        public bool Activated;

        public TimingGrade Grade;

        public FailureReason Failure;

        /// <summary>0..1。基于【未经辅助修正】的真实呈现偏差，取触发瞬间的值。</summary>
        public float SpatialQuality;

        /// <summary>触发瞬间的角度偏差（虚拟角，度）。</summary>
        public float AngularErrorVirtual;

        /// <summary>时间偏差（毫秒），正值 = 打晚了。</summary>
        public float TimingOffsetMs;

        /// <summary>本次判定是否吃到了辅助瞄准的扩锥。</summary>
        public bool AssistApplied;

        /// <summary>是否靠滞回窗口通过（而非触发瞬间真的在锥内）。</summary>
        public bool ViaHysteresis;

        /// <summary>姿态历史是否覆盖了该时间戳。false 说明取到的是边界值，数据需打折。</summary>
        public bool HistoryExact;

        /// <summary>触摸时间戳是否为真实 OS 时间戳。false 表示退化为帧时间。</summary>
        public bool OsTimestamp;

        public float RhythmScore => Grade switch
        {
            TimingGrade.Perfect => 1f,
            TimingGrade.Great => 0.7f,
            TimingGrade.Good => 0.4f,
            _ => 0f
        };

        /// <summary>TotalScore = RhythmScore * 0.8 + SpatialPerformance * 0.2</summary>
        public float TotalScore => Activated ? RhythmScore * 0.8f + SpatialQuality * 0.2f : 0f;
    }
}
