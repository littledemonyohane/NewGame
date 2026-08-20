using UnityEngine;
using SpatialRhythm.Core;
using SpatialRhythm.Tracking;

namespace SpatialRhythm.Presentation
{
    public enum PoseProviderKind
    {
        EditorMouse,
        DeviceGyro,
        Replay
    }

    /// <summary>
    /// 姿态链路的唯一入口，串起设计文档 §3.2 的整条管线：
    ///
    ///   Provider(原始物理姿态) → JitterInjector → One Euro → GainMapper(物理→虚拟)
    ///     → 预测外推 → PresentedPoseHistory → StageCamera
    ///
    /// 关键约束：写入 History 的是【最终呈现姿态】，并以【预计上屏时刻】打时间戳。
    /// 判定按触摸时间戳回溯查询时，取到的才是玩家手指落下那一刻真正看见的画面。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class PosePipeline : MonoBehaviour
    {
        public static PosePipeline Instance { get; private set; }

        [Header("姿态来源")]
        [SerializeField] private PoseProviderKind _providerKind = PoseProviderKind.EditorMouse;

        [Header("滤波（One Euro）")]
        [SerializeField] private bool _filterEnabled = true;
        [SerializeField] private float _minCutoff = 1.2f;
        [SerializeField] private float _beta = 0.02f;
        [SerializeField] private float _dCutoff = 1.0f;

        [Header("模拟手抖")]
        [SerializeField] private JitterInjector _jitter = new JitterInjector();

        [Header("增益与角度笼")]
        [SerializeField] private GainMapper _gainMapper = new GainMapper();

        [Header("延迟补偿（毫秒）")]
        [Tooltip("角速度线性外推时长。15–25ms 收益明显；超过 30ms 会在急停时过冲。")]
        [SerializeField] private float _predictionMs = 20f;

        [Tooltip("从本帧计算完成到实际点亮屏幕的估计延迟。History 的时间戳按此前移。")]
        [SerializeField] private float _presentationLatencyMs = 25f;

        [Header("肩关节杠杆视差（设计文档 §0.2，可选表现层）")]
        [Tooltip("手机绕肩关节转动，位置就在一个半径 r 的球面上。这不是测量出的真实位移，是从姿态派生的、零漂移的视觉效果。")]
        [SerializeField] private bool _parallaxEnabled = true;

        [Tooltip("肩到手机的距离（米）。用站姿/坐姿预设，不尝试通过 IMU 反推真实臂长。")]
        [SerializeField] private float _armRadius = 0.5f;

        [Tooltip("表现放大倍数。音符与引导线跟随相机原点渲染，所以放大只影响星空视差，不会破坏判定对齐。")]
        [SerializeField] private float _parallaxScale = 1f;

        private readonly PresentedPoseHistory _history = new PresentedPoseHistory(512);
        private readonly PoseFilter _poseFilter = new PoseFilter();

        private IPoseProvider _provider;
        private double _lastTimestamp;

        /// <summary>虚拟角速度的低通时间常数（秒）。</summary>
        private const float VelocitySmoothingTau = 0.04f;

        /// <summary>单帧外推的角度上限，防止速度尖峰造成跳变。</summary>
        private const float MaxExtrapolationDeg = 6f;

        private Vector3 _lastVirtualEuler;
        private Vector3 _smoothedVirtualAngularVelocity;
        private bool _hasLastVirtualEuler;

        public PresentedPoseHistory History => _history;

        public JitterInjector Jitter => _jitter;

        public GainMapper Gain => _gainMapper;

        public PoseFilter Filter => _poseFilter;

        public IPoseProvider Provider => _provider;

        /// <summary>最新的原始物理姿态（未滤波、未增益）。仅用于诊断，不得用于判定。</summary>
        public PoseSample LatestRawSample { get; private set; }

        /// <summary>最新的呈现姿态（已滤波、已增益、已外推）。</summary>
        public Quaternion PresentedRotation { get; private set; } = Quaternion.identity;

        public Vector3 VirtualAngularVelocity { get; private set; }

        /// <summary>
        /// 由肩关节杠杆模型派生的相机位移（米）。
        ///
        /// **它不参与任何判定。** 判定只用 <see cref="PresentedRotation"/> 与音符方向的夹角。
        /// 音符和引导线会跟随这个原点渲染，所以它们相对相机的方向恒等于判定方向——
        /// 屏幕位置与判定始终一致，只有星空产生视差。
        /// </summary>
        public Vector3 PresentedPosition { get; private set; }

        public bool ParallaxEnabled
        {
            get => _parallaxEnabled;
            set => _parallaxEnabled = value;
        }

        public float ParallaxScale
        {
            get => _parallaxScale;
            set => _parallaxScale = Mathf.Clamp(value, 0f, 4f);
        }

        public float ArmRadius
        {
            get => _armRadius;
            set => _armRadius = Mathf.Clamp(value, 0.1f, 0.9f);
        }

        public float PredictionMs
        {
            get => _predictionMs;
            set => _predictionMs = Mathf.Clamp(value, 0f, 40f);
        }

        public bool FilterEnabled
        {
            get => _filterEnabled;
            set
            {
                _filterEnabled = value;
                _poseFilter.Enabled = value;
            }
        }

        public void SetFilterParameters(float minCutoff, float beta, float dCutoff)
        {
            _minCutoff = minCutoff;
            _beta = beta;
            _dCutoff = dCutoff;
            _poseFilter.SetParameters(minCutoff, beta, dCutoff);
        }

        public float MinCutoff => _minCutoff;

        public float Beta => _beta;

        public float DCutoff => _dCutoff;

        private void Awake()
        {
            Instance = this;
            _poseFilter.SetParameters(_minCutoff, _beta, _dCutoff);
            _poseFilter.Enabled = _filterEnabled;
            SetProvider(CreateProvider(_providerKind));
        }

        private void OnDestroy()
        {
            _provider?.Shutdown();

            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void SetProvider(IPoseProvider provider)
        {
            _provider?.Shutdown();
            _provider = provider;
            _provider?.Initialize();

            _poseFilter.Reset();
            _gainMapper.Reset();
            _history.Clear();
            _lastTimestamp = AppClock.Now;

            _hasLastVirtualEuler = false;
            _smoothedVirtualAngularVelocity = Vector3.zero;
        }

        /// <summary>显式重定心：双指下滑手势 / Editor 里按 R。</summary>
        public void Recenter()
        {
            _provider?.Recenter();
            _gainMapper.RecenterNow();
            _poseFilter.Reset();
        }

        private void Update()
        {
            if (_provider == null)
            {
                return;
            }

            double now = AppClock.Now;
            float dt = (float)(now - _lastTimestamp);
            _lastTimestamp = now;

            PoseSample raw = _provider.Sample(now);
            raw = _jitter.Apply(raw);
            LatestRawSample = raw;

            Quaternion filtered = _poseFilter.Filter(raw.Rotation, raw.Timestamp);
            _gainMapper.Map(filtered, dt);

            // 角速度必须从【滤波后的虚拟角】求导，不能用 raw.AngularVelocity。
            // 否则 One Euro 刚把姿态磨平，外推又把未滤波的噪声乘上 20ms 加回来，
            // 滤波等于白做——这是"操控很抖"的主因。
            VirtualAngularVelocity = EstimateVirtualAngularVelocity(_gainMapper.LastVirtualEuler, dt);

            PresentedRotation = Extrapolate(
                _gainMapper.LastVirtualEuler, VirtualAngularVelocity, _predictionMs * 0.001f);

            // 杠杆位移用【物理】姿态，不用放大后的虚拟姿态——
            // 手臂是按真实角度摆的，用虚拟角会把视差夸张 G 倍。
            // 再叠加 Provider 提供的独立位移（Editor 的 WASD 模拟 / 未来的 VIO）。
            PresentedPosition = ComputeLeverPosition(filtered) + (raw.Position ?? Vector3.zero);

            _history.Push(now + _presentationLatencyMs * 0.001d,
                PresentedRotation, PresentedPosition, VirtualAngularVelocity);
        }

        /// <summary>
        /// 肩关节杠杆模型：手机绕肩关节转动，位置落在半径 r 的球面上。
        ///
        /// **支点在手机后方**（肩膀），所以杠杆向量是 +Z。设计文档写的 (0,0,-r)
        /// 是 ARKit 相机朝 -Z 的约定，转到 Unity 的 +Z 前向必须取正号——
        /// 取反的话支点会跑到手机前方，变成"绕着前方一个点公转"，
        /// 恰好抵消掉该距离上的所有视差。
        ///
        /// 只用姿态四元数派生，因此方向合理且【不会累计漂移】——
        /// 这是它相对于 IMU 双积分唯一的、也是决定性的优势。
        /// </summary>
        private Vector3 ComputeLeverPosition(Quaternion physicalRotation)
        {
            if (!_parallaxEnabled || _parallaxScale <= 0.001f)
            {
                return Vector3.zero;
            }

            var lever = new Vector3(0f, 0f, _armRadius);
            return (physicalRotation * lever - lever) * _parallaxScale;
        }

        /// <summary>
        /// 从虚拟欧拉角求导并再做一次低通。
        ///
        /// 单帧差分本身噪声很大（尤其在高帧率下，输入是台阶状的），
        /// 直接拿去外推会产生单帧过冲。时间常数 40ms 足以稳住，
        /// 又不至于让急停时的速度衰减得太慢。
        /// </summary>
        private Vector3 EstimateVirtualAngularVelocity(Vector3 virtualEuler, float dt)
        {
            if (!_hasLastVirtualEuler)
            {
                _lastVirtualEuler = virtualEuler;
                _hasLastVirtualEuler = true;
                return Vector3.zero;
            }

            if (dt > 0f)
            {
                var instant = new Vector3(
                    Mathf.DeltaAngle(_lastVirtualEuler.x, virtualEuler.x),
                    Mathf.DeltaAngle(_lastVirtualEuler.y, virtualEuler.y),
                    Mathf.DeltaAngle(_lastVirtualEuler.z, virtualEuler.z)) / dt;

                float alpha = 1f - Mathf.Exp(-dt / VelocitySmoothingTau);
                _smoothedVirtualAngularVelocity += (instant - _smoothedVirtualAngularVelocity) * alpha;
            }

            _lastVirtualEuler = virtualEuler;
            return _smoothedVirtualAngularVelocity;
        }

        /// <summary>
        /// 在欧拉角空间做线性外推。虚拟姿态本来就是由 Euler 构造的，
        /// 且角度笼把 pitch 限制在 ±55°，远离万向锁，因此安全且廉价。
        ///
        /// 外推量必须钳位：单帧速度尖峰乘上 20ms 可能产生几十度的跳变，
        /// 那比它想补偿的延迟更毁手感。
        /// </summary>
        private static Quaternion Extrapolate(Vector3 virtualEuler, Vector3 virtualAngularVelocity, float seconds)
        {
            if (seconds <= 0f)
            {
                return Quaternion.Euler(virtualEuler);
            }

            Vector3 delta = Vector3.ClampMagnitude(virtualAngularVelocity * seconds, MaxExtrapolationDeg);
            return Quaternion.Euler(virtualEuler + delta);
        }

        private IPoseProvider CreateProvider(PoseProviderKind kind)
        {
            switch (kind)
            {
                case PoseProviderKind.DeviceGyro:
                    var gyro = new DeviceGyroPoseProvider();
                    if (gyro.IsAvailable)
                    {
                        return gyro;
                    }

                    Debug.LogWarning("[PosePipeline] 设备无可用陀螺仪，回退到 EditorMouse。");
                    return new EditorMousePoseProvider();

                case PoseProviderKind.Replay:
                    Debug.LogWarning("[PosePipeline] Replay 需要由 PoseRecorder 注入，先回退到 EditorMouse。");
                    return new EditorMousePoseProvider();

                default:
                    return new EditorMousePoseProvider();
            }
        }
    }
}
