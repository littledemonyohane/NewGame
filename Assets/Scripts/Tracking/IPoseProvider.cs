namespace SpatialRhythm.Tracking
{
    /// <summary>
    /// 姿态来源抽象。对应设计文档 §7.6 的 ITrackingProvider。
    ///
    /// 谱面与判定逻辑不得直接读取平台传感器，必须经由本接口，
    /// 以便 Editor 模拟 / 真机陀螺 / 回放三种来源可互换。
    /// </summary>
    public interface IPoseProvider
    {
        string ProviderName { get; }

        bool IsAvailable { get; }

        void Initialize();

        /// <summary>
        /// 推进到给定时刻并返回最新的【原始物理姿态】。
        /// 不做滤波、不做增益、不做角度笼——那些属于呈现链路。
        /// </summary>
        PoseSample Sample(double timestamp);

        /// <summary>把当前朝向记为新的正前方（开局锚定 / 显式重定心）。</summary>
        void Recenter();

        void Shutdown();
    }
}
