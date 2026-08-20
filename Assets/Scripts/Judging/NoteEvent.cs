using UnityEngine;

namespace SpatialRhythm.Judging
{
    public enum NoteType
    {
        /// <summary>捕获点：套进中央捕获区 + 任一触发区点击。首版基础音符。</summary>
        Pulse,

        /// <summary>直触点：直接点击音符的屏幕投影。只能出现在低角速度段落。</summary>
        Point
    }

    /// <summary>
    /// 一个音符。
    ///
    /// 桌面尺度下音符有【真实的三维位置】：方位角 + 仰角 + 距离。
    /// 判定用的是"从相机当前位置看向音符"的方向，所以玩家在桌面体积内平移
    /// 会真实地改变对准关系——这正是位移玩法成立的地方。
    ///
    /// 注意这与纯 3DoF 首版的区别：无摄像头时位置只能由肩关节杠杆从旋转派生，
    /// 与朝向刚性耦合；真正的独立位移需要 6DoF VIO。
    /// </summary>
    [System.Serializable]
    public struct NoteEvent
    {
        [Tooltip("判定拍。")]
        public double Beat;

        public NoteType Type;

        [Tooltip("方位角（度）。0 = 正前，正值向右。")]
        public float AzimuthDeg;

        [Tooltip("仰角（度）。0 = 水平，正值向上。")]
        public float ElevationDeg;

        [Tooltip("距原点的距离（米）。桌面尺度约 0.30–0.85。")]
        public float DistanceMeters;

        [Tooltip("提前多少拍出现。屏外音符需要 ≥800ms 的预告。")]
        public float PreviewBeats;

        /// <summary>音符在舞台坐标系中的单位方向向量（从原点看）。</summary>
        public Vector3 Direction => DirectionOf(AzimuthDeg, ElevationDeg);

        /// <summary>音符的世界坐标。渲染与判定都以它为准。</summary>
        public Vector3 WorldPosition => Direction * Mathf.Max(0.05f, DistanceMeters);

        public static Vector3 DirectionOf(float azimuthDeg, float elevationDeg)
        {
            // Unity 的 pitch 正方向是"向下看"，所以仰角取负。
            return Quaternion.Euler(-elevationDeg, azimuthDeg, 0f) * Vector3.forward;
        }
    }
}
