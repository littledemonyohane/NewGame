using UnityEngine;
using SpatialRhythm.Chart;
using SpatialRhythm.Judging;
using SpatialRhythm.Presentation;

namespace SpatialRhythm.Tuning
{
    /// <summary>
    /// 运行时调参面板（F2）。
    ///
    /// 存在的理由：P0 要回答的不是"这套参数好不好"，而是"参数敏感度有多高"。
    /// 如果 G 从 1.4 调到 2.0 就让脱锁率翻三倍，那说明这个玩法对个体差异过于敏感，
    /// 这本身就是一条重要结论。所以调参必须能在同一次运行内热切换。
    /// </summary>
    public sealed class TuningPanel : MonoBehaviour
    {
        [SerializeField] private bool _visible;

        private JudgeService _judge;
        private ChartPlayer _chartPlayer;
        private Diagnostics.PoseRecorder _recorder;
        private Diagnostics.SessionLogger _logger;
        private Diagnostics.MetricsHud _hud;
        private Diagnostics.StageOverlay _overlay;
        private Presentation.GuideLine _guideLine;

        private GUIStyle _labelStyle;
        private Texture2D _panelTexture;

        private void Awake()
        {
            _judge = FindObjectOfType<JudgeService>();
            _chartPlayer = FindObjectOfType<ChartPlayer>();
            _recorder = FindObjectOfType<Diagnostics.PoseRecorder>();
            _logger = FindObjectOfType<Diagnostics.SessionLogger>();
            _hud = FindObjectOfType<Diagnostics.MetricsHud>();
            _overlay = FindObjectOfType<Diagnostics.StageOverlay>();
            _guideLine = FindObjectOfType<Presentation.GuideLine>();

            _panelTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _panelTexture.SetPixel(0, 0, new Color(0.02f, 0.05f, 0.09f, 0.88f));
            _panelTexture.Apply();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F2))
            {
                _visible = !_visible;

                // 面板是 IMGUI，光标锁定时点不到控件。
                if (_visible)
                {
                    Tracking.EditorMousePoseProvider.Unlock();
                }
            }

            if (Input.GetKeyDown(KeyCode.F5) && _chartPlayer != null)
            {
                RestartRun();
            }
        }

        private void OnGUI()
        {
            if (!_visible)
            {
                return;
            }

            EnsureStyles();

            PosePipeline pipeline = PosePipeline.Instance;
            if (pipeline == null || _judge == null)
            {
                return;
            }

            const float Width = 380f;
            var area = new Rect(Screen.width - Width - 12f, 12f, Width, 700f);
            GUI.DrawTexture(area, _panelTexture);

            GUILayout.BeginArea(new Rect(area.x + 12f, area.y + 10f, area.width - 24f, area.height - 20f));

            GUILayout.Label("调参面板  [F2] 关闭   [F5] 重开一局", _labelStyle);
            GUILayout.Space(6f);

            // ── 视角增益
            GUILayout.Label($"视角增益 G = {pipeline.Gain.Gain:F2}", _labelStyle);
            pipeline.Gain.Gain = GUILayout.HorizontalSlider(pipeline.Gain.Gain, 1.0f, 2.5f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("1.0")) { pipeline.Gain.Gain = 1.0f; }
            if (GUILayout.Button("1.4")) { pipeline.Gain.Gain = 1.4f; }
            if (GUILayout.Button("1.8")) { pipeline.Gain.Gain = 1.8f; }
            if (GUILayout.Button("2.2")) { pipeline.Gain.Gain = 2.2f; }
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);

            // ── One Euro
            bool filterEnabled = GUILayout.Toggle(pipeline.FilterEnabled, " One Euro 滤波", _labelStyle);
            if (filterEnabled != pipeline.FilterEnabled)
            {
                pipeline.FilterEnabled = filterEnabled;
            }

            float minCutoff = pipeline.MinCutoff;
            float beta = pipeline.Beta;
            GUILayout.Label($"mincutoff {minCutoff:F2} Hz  （静止平滑强度）", _labelStyle);
            minCutoff = GUILayout.HorizontalSlider(minCutoff, 0.2f, 4f);
            GUILayout.Label($"beta {beta:F3}  （速度自适应强度）", _labelStyle);
            beta = GUILayout.HorizontalSlider(beta, 0f, 0.15f);

            if (!Mathf.Approximately(minCutoff, pipeline.MinCutoff) || !Mathf.Approximately(beta, pipeline.Beta))
            {
                pipeline.SetFilterParameters(minCutoff, beta, pipeline.DCutoff);
            }

            GUILayout.Label($"预测外推 {pipeline.PredictionMs:F0} ms  （>30ms 急停会过冲）", _labelStyle);
            pipeline.PredictionMs = GUILayout.HorizontalSlider(pipeline.PredictionMs, 0f, 40f);

            GUILayout.Space(6f);

            // ── 双锥
            GUILayout.Label($"θ_activate {_judge.ThetaActivate:F1}°  （二元门槛）", _labelStyle);
            _judge.ThetaActivate = GUILayout.HorizontalSlider(_judge.ThetaActivate, 2f, 14f);
            GUILayout.Label($"θ_perfect  {_judge.ThetaPerfect:F1}°  （空间满分区）", _labelStyle);
            _judge.ThetaPerfect = GUILayout.HorizontalSlider(_judge.ThetaPerfect, 0.5f, _judge.ThetaActivate);

            _judge.AssistEnabled = GUILayout.Toggle(_judge.AssistEnabled,
                " 辅助瞄准（只扩激活锥，不影响空间分）", _labelStyle);

            GUILayout.Space(6f);

            // ── 模拟手抖
            pipeline.Jitter.Enabled = GUILayout.Toggle(pipeline.Jitter.Enabled, " 注入模拟手抖", _labelStyle);
            GUILayout.Label($"幅度 {pipeline.Jitter.AmplitudeDeg:F2}°  频率 {pipeline.Jitter.FrequencyHz:F1} Hz", _labelStyle);
            pipeline.Jitter.AmplitudeDeg = GUILayout.HorizontalSlider(pipeline.Jitter.AmplitudeDeg, 0f, 1.0f);
            pipeline.Jitter.FrequencyHz = GUILayout.HorizontalSlider(pipeline.Jitter.FrequencyHz, 1f, 10f);

            pipeline.Gain.RecenterEnabled = GUILayout.Toggle(pipeline.Gain.RecenterEnabled,
                " 隐形重定心", _labelStyle);

            GUILayout.Space(6f);

            // ── 肩关节杠杆视差（不参与判定）
            pipeline.ParallaxEnabled = GUILayout.Toggle(pipeline.ParallaxEnabled,
                " 肩关节杠杆视差（只作用于星空）", _labelStyle);
            GUILayout.Label($"臂长 r = {pipeline.ArmRadius:F2} m   幅度 ×{pipeline.ParallaxScale:F1}" +
                            (pipeline.ParallaxScale > 1.05f ? "  ⚠ 已夸张" : ""), _labelStyle);
            pipeline.ArmRadius = GUILayout.HorizontalSlider(pipeline.ArmRadius, 0.1f, 0.9f);
            pipeline.ParallaxScale = GUILayout.HorizontalSlider(pipeline.ParallaxScale, 0f, 4f);

            GUILayout.Space(6f);

            // ── 引导线：新设计的核心旋钮
            if (_guideLine != null)
            {
                GUILayout.Label($"引导线前瞻 {_guideLine.LookAheadBeats:F1} 拍  （音符只提前 2 拍出现）", _labelStyle);
                _guideLine.LookAheadBeats = GUILayout.HorizontalSlider(_guideLine.LookAheadBeats, 1f, 10f);
                GUILayout.Label($"身后保留 {_guideLine.TailBeats:F1} 拍", _labelStyle);
                _guideLine.TailBeats = GUILayout.HorizontalSlider(_guideLine.TailBeats, 0f, 4f);
            }

            GUILayout.Space(8f);

            // ── 操作
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("重开(F5)")) { RestartRun(); }
            if (GUILayout.Button("重定心(R)")) { pipeline.Recenter(); }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (_recorder != null)
            {
                if (!_recorder.IsRecording)
                {
                    if (GUILayout.Button("开始录姿态")) { _recorder.StartRecording(); }
                }
                else
                {
                    if (GUILayout.Button($"停止并保存 ({_recorder.SampleCount})")) { _recorder.StopAndSave(); }
                }

                if (GUILayout.Button("回放最近一段")) { _recorder.LoadLatestAndReplay(); }
            }

            GUILayout.EndHorizontal();

            if (_logger != null && GUILayout.Button($"写出 CSV ({_logger.RowCount} 行)"))
            {
                _logger.Flush();
            }

            if (_overlay != null)
            {
                _overlay.Visible = GUILayout.Toggle(_overlay.Visible, " 显示准星与屏外指示", _labelStyle);
            }

            GUILayout.EndArea();
        }

        private void RestartRun()
        {
            _chartPlayer?.Restart();
            _hud?.ResetCounters();
            _logger?.BeginSession();
        }

        private void EnsureStyles()
        {
            if (_labelStyle != null)
            {
                return;
            }

            _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            _labelStyle.normal.textColor = Color.white;
        }
    }
}
