using UnityEngine;
using SpatialRhythm.InputLayer;
using SpatialRhythm.Presentation;

namespace SpatialRhythm.Judging
{
    /// <summary>
    /// 判定核心。实现设计文档的双锥模型：
    ///
    ///   θ_activate  二元门槛，进了才进入时间评级
    ///   θ_perfect   空间表现满分区
    ///   SpatialQuality = smoothstep(θ_activate → θ_perfect)
    ///
    /// 三条不可让步的规则：
    /// 1. 空间是二元门槛，节奏评级完全由时间偏差决定（保证失误可归因）
    /// 2. 辅助瞄准【只】扩大 θ_activate，绝不影响 SpatialQuality 的评分基准
    ///    ——否则 20% 的空间分是系统送的，不同机型/设置还会不公平
    /// 3. SpatialQuality 取【触发瞬间】的偏差，不取窗口均值
    ///    ——否则玩家可以提前锁定并僵住手臂刷分，那奖励的是"不动"而非"准确"
    /// </summary>
    public sealed class JudgeService : MonoBehaviour
    {
        public static JudgeService Instance { get; private set; }

        [Header("空间双锥（虚拟角，度）")]
        [SerializeField] private float _thetaActivate = 7.0f;
        [SerializeField] private float _thetaPerfect = 2.5f;

        [Header("时间窗（毫秒）")]
        [SerializeField] private float _perfectMs = 35f;
        [SerializeField] private float _greatMs = 70f;
        [SerializeField] private float _goodMs = 110f;

        [Header("辅助瞄准")]
        [Tooltip("这不是作弊，是必需的可用性设计——它比任何滤波都更有效地解决手抖，且不引入延迟。")]
        [SerializeField] private bool _assistEnabled = true;

        [Tooltip("判定时刻前后多少毫秒内启用扩锥。")]
        [SerializeField] private float _assistWindowMs = 80f;

        [Tooltip("θ_activate 的扩大比例。0.4 = 扩大 40%。")]
        [SerializeField] private float _assistExpansion = 0.4f;

        [Header("滞回")]
        [Tooltip("音符激活后保留的锁定时长，避免自然手抖在拍点前造成反复锁定/脱锁。")]
        [SerializeField] private float _hysteresisMs = 120f;

        public float ThetaActivate
        {
            get => _thetaActivate;
            set => _thetaActivate = Mathf.Clamp(value, 1f, 30f);
        }

        public float ThetaPerfect
        {
            get => _thetaPerfect;
            set => _thetaPerfect = Mathf.Clamp(value, 0.5f, _thetaActivate);
        }

        public bool AssistEnabled
        {
            get => _assistEnabled;
            set => _assistEnabled = value;
        }

        public float AssistWindowMs => _assistWindowMs;

        public float HysteresisSeconds => _hysteresisMs * 0.001f;

        public float GoodWindowMs => _goodMs;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>触发时刻生效的激活锥半角（含辅助扩大）。</summary>
        public float EffectiveActivateCone(float timingOffsetMs)
        {
            bool assist = _assistEnabled && Mathf.Abs(timingOffsetMs) <= _assistWindowMs;
            return assist ? _thetaActivate * (1f + _assistExpansion) : _thetaActivate;
        }

        /// <summary>
        /// 计算"从相机位置看向音符"的方向与相机前向的夹角（度）。
        ///
        /// 判定就是一次点积——不要用物理射线检测。
        /// 桌面尺度下必须带上相机位置：位置变化会真实改变这个角度，
        /// 移动才成为有意义的操作。渲染和判定用的是同一个位姿，
        /// 所以"所见即所判"自动成立，不需要额外的对齐技巧。
        /// </summary>
        public static float AngularError(Vector3 noteWorldPosition, Vector3 cameraPosition, Quaternion cameraRotation)
        {
            Vector3 toNote = noteWorldPosition - cameraPosition;
            if (toNote.sqrMagnitude < 1e-8f)
            {
                return 0f;
            }

            Vector3 forward = cameraRotation * Vector3.forward;
            float dot = Mathf.Clamp(Vector3.Dot(toNote.normalized, forward), -1f, 1f);
            return Mathf.Acos(dot) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// 主判定入口。
        /// </summary>
        /// <param name="noteWorldPosition">音符的世界坐标。</param>
        /// <param name="noteHitTime">音符判定时刻，AppClock 时间轴（秒）。</param>
        /// <param name="touch">触发事件。</param>
        /// <param name="cameraPose">
        /// 从 PresentedPoseHistory 按 <paramref name="touch"/> 的时间戳回溯取出的呈现位姿。
        /// 这是"所见即所判"的落点——绝不能传入当前帧的位姿。
        /// </param>
        /// <param name="lastInConeTime">音符最近一次落在激活锥内的时刻，用于滞回。</param>
        /// <param name="historyExact">历史缓冲是否真的覆盖了该时间戳。</param>
        public JudgeResult Evaluate(
            Vector3 noteWorldPosition,
            double noteHitTime,
            in TouchEvent touch,
            in PresentedPoseHistory.Entry cameraPose,
            double lastInConeTime,
            bool historyExact)
        {
            float offsetMs = (float)((touch.Timestamp - noteHitTime) * 1000d);
            float error = AngularError(noteWorldPosition, cameraPose.Position, cameraPose.Rotation);

            bool assist = _assistEnabled && Mathf.Abs(offsetMs) <= _assistWindowMs;
            float cone = assist ? _thetaActivate * (1f + _assistExpansion) : _thetaActivate;

            bool inConeNow = error <= cone;
            bool viaHysteresis = !inConeNow
                                 && lastInConeTime > 0d
                                 && (touch.Timestamp - lastInConeTime) <= HysteresisSeconds;

            bool activated = inConeNow || viaHysteresis;

            var result = new JudgeResult
            {
                Activated = activated,
                AngularErrorVirtual = error,
                TimingOffsetMs = offsetMs,
                AssistApplied = assist,
                ViaHysteresis = viaHysteresis,
                HistoryExact = historyExact,
                OsTimestamp = touch.IsOsTimestamp,
                // 关键：用未经辅助修正的 θ_activate 作为基准。
                SpatialQuality = ComputeSpatialQuality(error)
            };

            if (!activated)
            {
                result.Grade = TimingGrade.Miss;
                result.Failure = FailureReason.OutOfFrame;
                return result;
            }

            result.Grade = GradeOf(Mathf.Abs(offsetMs));
            result.Failure = result.Grade == TimingGrade.Miss ? FailureReason.BadTiming : FailureReason.None;
            return result;
        }

        /// <summary>
        /// 空间表现分：θ_perfect 内为 1，θ_activate 处为 0，中间 smoothstep 过渡。
        /// 基准恒为未扩大的 θ_activate。
        /// </summary>
        public float ComputeSpatialQuality(float angularError)
        {
            float t = Mathf.InverseLerp(_thetaActivate, _thetaPerfect, angularError);
            return t * t * (3f - 2f * t);
        }

        private TimingGrade GradeOf(float absOffsetMs)
        {
            if (absOffsetMs <= _perfectMs)
            {
                return TimingGrade.Perfect;
            }

            if (absOffsetMs <= _greatMs)
            {
                return TimingGrade.Great;
            }

            if (absOffsetMs <= _goodMs)
            {
                return TimingGrade.Good;
            }

            return TimingGrade.Miss;
        }
    }
}
