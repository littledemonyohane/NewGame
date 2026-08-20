using UnityEngine;
using SpatialRhythm.Audio;
using SpatialRhythm.Chart;
using SpatialRhythm.Core;
using SpatialRhythm.InputLayer;
using SpatialRhythm.Judging;
using SpatialRhythm.Presentation;

namespace SpatialRhythm.Diagnostics
{
    /// <summary>
    /// 实时指标面板。P0 的核心交付物——Demo 好不好玩是主观的，
    /// 但"传感器造成了多少非玩家失误"必须是客观数字。
    ///
    /// F1 显示/隐藏。
    /// </summary>
    public sealed class MetricsHud : MonoBehaviour
    {
        private const int TimingBinCount = 13;
        private const float TimingBinWidthMs = 20f;

        [SerializeField] private ChartPlayer _chartPlayer;
        [SerializeField] private JudgeService _judge;
        [SerializeField] private TouchRouter _touchRouter;
        [SerializeField] private bool _visible = true;

        private readonly RollingWindow _frameTimes = new RollingWindow(600);
        private readonly RollingWindow _timingOffsets = new RollingWindow(256);
        private readonly RollingWindow _spatialQualities = new RollingWindow(256);
        private readonly StaticJitterMeter _jitter = new StaticJitterMeter(512);
        private readonly int[] _timingHistogram = new int[TimingBinCount];

        private int _perfect;
        private int _great;
        private int _good;
        private int _missOutOfFrame;
        private int _missTiming;
        private int _missNoInput;
        private int _dropouts;
        private int _hysteresisSaves;
        private int _assistUsed;

        private GUIStyle _labelStyle;
        private GUIStyle _headerStyle;
        private Texture2D _barTexture;
        private Texture2D _panelTexture;

        public bool Visible
        {
            get => _visible;
            set => _visible = value;
        }

        private void Awake()
        {
            _chartPlayer = _chartPlayer != null ? _chartPlayer : FindObjectOfType<ChartPlayer>();
            _judge = _judge != null ? _judge : FindObjectOfType<JudgeService>();
            _touchRouter = _touchRouter != null ? _touchRouter : FindObjectOfType<TouchRouter>();

            _barTexture = MakeTexture(new Color(0.35f, 0.8f, 1f, 0.9f));
            _panelTexture = MakeTexture(new Color(0f, 0f, 0f, 0.62f));
        }

        private void OnEnable()
        {
            if (_chartPlayer != null)
            {
                _chartPlayer.OnNoteJudged += HandleNoteJudged;
            }
        }

        private void OnDisable()
        {
            if (_chartPlayer != null)
            {
                _chartPlayer.OnNoteJudged -= HandleNoteJudged;
            }
        }

        public void ResetCounters()
        {
            _perfect = _great = _good = 0;
            _missOutOfFrame = _missTiming = _missNoInput = 0;
            _dropouts = _hysteresisSaves = _assistUsed = 0;
            _timingOffsets.Clear();
            _spatialQualities.Clear();
            System.Array.Clear(_timingHistogram, 0, _timingHistogram.Length);
        }

        private void Update()
        {
            _frameTimes.Add(Time.unscaledDeltaTime * 1000f);

            PosePipeline pipeline = PosePipeline.Instance;
            if (pipeline != null)
            {
                _jitter.Tick(
                    AppClock.Now,
                    pipeline.PresentedRotation,
                    pipeline.LatestRawSample.AngularVelocity,
                    Time.unscaledDeltaTime);
            }

            if (Input.GetKeyDown(KeyCode.F1))
            {
                _visible = !_visible;
            }
        }

        private void HandleNoteJudged(ChartPlayer.NoteRuntime runtime, JudgeResult result)
        {
            if (result.Activated)
            {
                switch (result.Grade)
                {
                    case TimingGrade.Perfect: _perfect++; break;
                    case TimingGrade.Great: _great++; break;
                    case TimingGrade.Good: _good++; break;
                    default: _missTiming++; break;
                }

                _spatialQualities.Add(result.SpatialQuality);

                if (!float.IsNaN(result.TimingOffsetMs))
                {
                    _timingOffsets.Add(result.TimingOffsetMs);
                    AddToHistogram(result.TimingOffsetMs);
                }

                if (result.ViaHysteresis)
                {
                    _hysteresisSaves++;
                }

                if (result.AssistApplied)
                {
                    _assistUsed++;
                }
            }
            else if (result.Failure == FailureReason.NoInput)
            {
                _missNoInput++;
            }
            else
            {
                _missOutOfFrame++;
            }

            // 脱锁：接近时进过锥，触发瞬间却没通过。这是"传感器造成的非玩家失误"的主要来源。
            if (runtime.EverInCone && !result.Activated)
            {
                _dropouts++;
            }
        }

        private void AddToHistogram(float offsetMs)
        {
            int center = TimingBinCount / 2;
            int bin = center + Mathf.RoundToInt(offsetMs / TimingBinWidthMs);
            bin = Mathf.Clamp(bin, 0, TimingBinCount - 1);
            _timingHistogram[bin]++;
        }

        private void OnGUI()
        {
            if (!_visible)
            {
                return;
            }

            EnsureStyles();

            const float Width = 430f;
            var area = new Rect(12f, 12f, Width, 500f);
            GUI.DrawTexture(area, _panelTexture);

            GUILayout.BeginArea(new Rect(area.x + 12f, area.y + 10f, area.width - 24f, area.height - 20f));

            PosePipeline pipeline = PosePipeline.Instance;

            GUILayout.Label("空间音游 · P0 灰盒诊断    [F1] 隐藏  [F2] 调参", _headerStyle);
            GUILayout.Space(4f);

            // ── 帧率
            float frameP95 = _frameTimes.Percentile(0.95f);
            Label($"FPS {1000f / Mathf.Max(0.01f, _frameTimes.Mean):F0}    帧时间 均值 {_frameTimes.Mean:F1} ms  P95 {frameP95:F1} ms");

            // ── 姿态：物理角与虚拟角必须并列，否则调 G 时看不懂数据
            if (pipeline != null)
            {
                Vector3 phys = pipeline.Gain.LastPhysicalEuler;
                Vector3 virt = pipeline.Gain.LastVirtualEuler;
                Label($"物理角  yaw {phys.y,7:F1}°  pitch {phys.x,6:F1}°     G = {pipeline.Gain.Gain:F2}");
                Label($"虚拟角  yaw {virt.y,7:F1}°  pitch {virt.x,6:F1}°     源 = {pipeline.Provider?.ProviderName}");
                Label($"隐形重定心累计 {pipeline.Gain.AccumulatedRecenterDeg:F1}°   外推 {pipeline.PredictionMs:F0} ms");

                Vector3 pos = pipeline.PresentedPosition;
                Label($"相机位置 x{pos.x * 100f:F0} y{pos.y * 100f:F0} z{pos.z * 100f:F0} cm" +
                      (pipeline.ParallaxEnabled ? $"   杠杆 r={pipeline.ArmRadius:F2}m ×{pipeline.ParallaxScale:F1}" : "   杠杆关"));
            }

            GUILayout.Space(6f);

            // ── 抖动（P0 指标 1）
            if (_jitter.IsStatic)
            {
                Label($"静止抖动  P95 {_jitter.JitterP95Deg:F3}°  均值 {_jitter.JitterMeanDeg:F3}°   " +
                      $"已静止 {_jitter.StaticSeconds:F1}s", JitterColor());
            }
            else
            {
                Label("静止抖动  ——（运动中，读数无意义）", new Color(0.7f, 0.7f, 0.7f));
            }

            GUILayout.Space(6f);

            // ── 判定统计
            int total = _perfect + _great + _good + _missOutOfFrame + _missTiming + _missNoInput;
            Label($"判定 {total}/{(_chartPlayer != null ? _chartPlayer.TotalNotes : 0)}    " +
                  $"P {_perfect}  G {_great}  Gd {_good}");
            Label($"Miss  空间(没套进) {_missOutOfFrame}   节奏 {_missTiming}   未触发 {_missNoInput}",
                  new Color(1f, 0.6f, 0.6f));

            float dropoutRate = total > 0 ? (float)_dropouts / total : 0f;
            Label($"脱锁率 {dropoutRate * 100f:F1}%  ({_dropouts})   ← P0 指标：< 2%",
                  dropoutRate <= 0.02f ? new Color(0.5f, 1f, 0.6f) : new Color(1f, 0.55f, 0.4f));

            Label($"滞回救回 {_hysteresisSaves}   吃到辅助 {_assistUsed}");
            Label($"空间表现分 均值 {_spatialQualities.Mean:F3}   (辅助{(_judge != null && _judge.AssistEnabled ? "开" : "关")})");

            GUILayout.Space(6f);

            // ── 时间偏差
            if (_timingOffsets.Count > 0)
            {
                Label($"时间偏差 均值 {_timingOffsets.Mean:F1} ms   P95 |偏差| {_timingOffsets.Percentile(0.95f):F1} ms");
                DrawHistogram();
            }
            else
            {
                Label("时间偏差 —— 尚无数据");
            }

            GUILayout.Space(4f);

            if (_touchRouter != null && !_touchRouter.UsingOsTimestamp)
            {
                Label("⚠ 触摸时间戳为帧时间，非 OS 时间戳（Stage 4 待验证）", new Color(1f, 0.8f, 0.35f));
            }

            GUILayout.EndArea();
        }

        private Color JitterColor()
        {
            if (_jitter.StaticSeconds < 10f)
            {
                return new Color(0.85f, 0.85f, 0.85f);
            }

            return _jitter.JitterP95Deg <= 0.6f
                ? new Color(0.5f, 1f, 0.6f)
                : new Color(1f, 0.55f, 0.4f);
        }

        private void DrawHistogram()
        {
            Rect row = GUILayoutUtility.GetRect(400f, 62f);

            int max = 1;
            for (int i = 0; i < _timingHistogram.Length; i++)
            {
                max = Mathf.Max(max, _timingHistogram[i]);
            }

            float binWidth = row.width / TimingBinCount;

            for (int i = 0; i < TimingBinCount; i++)
            {
                float height = (float)_timingHistogram[i] / max * (row.height - 14f);
                var bar = new Rect(
                    row.x + i * binWidth + 1f,
                    row.y + (row.height - 14f) - height,
                    binWidth - 2f,
                    height);

                GUI.color = i == TimingBinCount / 2 ? new Color(0.5f, 1f, 0.6f) : Color.white;
                GUI.DrawTexture(bar, _barTexture);
            }

            GUI.color = Color.white;
            GUI.Label(new Rect(row.x, row.y + row.height - 15f, row.width, 16f),
                $"早 {-TimingBinWidthMs * (TimingBinCount / 2):F0}ms" +
                new string(' ', 28) + "0" + new string(' ', 28) +
                $"+{TimingBinWidthMs * (TimingBinCount / 2):F0}ms 晚",
                _labelStyle);
        }

        private void Label(string text)
        {
            Label(text, Color.white);
        }

        private void Label(string text, Color color)
        {
            Color previous = _labelStyle.normal.textColor;
            _labelStyle.normal.textColor = color;
            GUILayout.Label(text, _labelStyle);
            _labelStyle.normal.textColor = previous;
        }

        private void EnsureStyles()
        {
            if (_labelStyle != null)
            {
                return;
            }

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                richText = false
            };
            _labelStyle.normal.textColor = Color.white;

            _headerStyle = new GUIStyle(_labelStyle)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold
            };
            _headerStyle.normal.textColor = new Color(0.6f, 0.9f, 1f);
        }

        private static Texture2D MakeTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }
    }
}
