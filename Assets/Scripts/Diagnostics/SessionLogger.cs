using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using SpatialRhythm.Chart;
using SpatialRhythm.Judging;
using SpatialRhythm.Presentation;

namespace SpatialRhythm.Diagnostics
{
    /// <summary>
    /// 把每一次判定写成 CSV 一行。这是 P0 真正的交付物——
    /// Demo 的目的不是"做出一个能玩的东西"，而是产出一份能回答
    /// "手抖是否让成绩不可复现"的可量化数据。
    ///
    /// 每行都带上当时的参数快照（G、滤波、辅助开关），
    /// 否则换了参数之后旧数据就无法解释了。
    /// </summary>
    public sealed class SessionLogger : MonoBehaviour
    {
        [SerializeField] private ChartPlayer _chartPlayer;
        [SerializeField] private bool _autoFlushOnFinish = true;

        private StringBuilder _builder;
        private string _path;
        private bool _headerWritten;

        public string Path => _path;

        public int RowCount { get; private set; }

        private void Awake()
        {
            _chartPlayer = _chartPlayer != null ? _chartPlayer : FindObjectOfType<ChartPlayer>();
            _builder = new StringBuilder(64 * 1024);
            BeginSession();
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

        private void OnApplicationQuit()
        {
            Flush();
        }

        public void BeginSession()
        {
            _builder.Clear();
            RowCount = 0;
            _headerWritten = false;

            _path = System.IO.Path.Combine(
                Application.persistentDataPath,
                "session_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".csv");

            WriteHeader();
        }

        private void WriteHeader()
        {
            if (_headerWritten)
            {
                return;
            }

            _builder.AppendLine(string.Join(",",
                "noteIndex", "noteType", "beat",
                "azimuthDeg", "elevationDeg",
                "activated", "grade", "failure",
                "timingOffsetMs",
                "angularErrorVirtualDeg", "angularErrorPhysicalDeg",
                "spatialQuality", "totalScore",
                "assistApplied", "viaHysteresis", "everInCone",
                "historyExact", "osTimestamp",
                "gain", "filterEnabled", "minCutoff", "beta", "dCutoff",
                "predictionMs", "jitterEnabled", "jitterAmplitudeDeg",
                "provider"));

            _headerWritten = true;
        }

        private void HandleNoteJudged(ChartPlayer.NoteRuntime runtime, JudgeResult result)
        {
            PosePipeline pipeline = PosePipeline.Instance;
            float gain = pipeline != null ? pipeline.Gain.Gain : 1f;

            // 物理角误差 = 虚拟角误差 / G。两者必须同时记录：
            // 体力/角速度分析看物理角，判定与抖动分析看虚拟角。
            float errorVirtual = result.AngularErrorVirtual;
            float errorPhysical = float.IsNaN(errorVirtual) ? float.NaN : errorVirtual / Mathf.Max(0.001f, gain);

            // 脱锁：接近过程中曾经进过锥，但触发瞬间没通过激活门槛。
            bool dropout = runtime.EverInCone && !result.Activated;

            _builder.AppendLine(string.Join(",",
                runtime.Index.ToString(CultureInfo.InvariantCulture),
                runtime.Note.Type.ToString(),
                runtime.Note.Beat.ToString("F3", CultureInfo.InvariantCulture),
                runtime.Note.AzimuthDeg.ToString("F2", CultureInfo.InvariantCulture),
                runtime.Note.ElevationDeg.ToString("F2", CultureInfo.InvariantCulture),
                result.Activated ? "1" : "0",
                result.Grade.ToString(),
                result.Failure.ToString(),
                Fmt(result.TimingOffsetMs),
                Fmt(errorVirtual),
                Fmt(errorPhysical),
                result.SpatialQuality.ToString("F4", CultureInfo.InvariantCulture),
                result.TotalScore.ToString("F4", CultureInfo.InvariantCulture),
                result.AssistApplied ? "1" : "0",
                result.ViaHysteresis ? "1" : "0",
                dropout ? "1" : "0",
                result.HistoryExact ? "1" : "0",
                result.OsTimestamp ? "1" : "0",
                gain.ToString("F2", CultureInfo.InvariantCulture),
                pipeline != null && pipeline.FilterEnabled ? "1" : "0",
                pipeline != null ? pipeline.MinCutoff.ToString("F3", CultureInfo.InvariantCulture) : "",
                pipeline != null ? pipeline.Beta.ToString("F4", CultureInfo.InvariantCulture) : "",
                pipeline != null ? pipeline.DCutoff.ToString("F3", CultureInfo.InvariantCulture) : "",
                pipeline != null ? pipeline.PredictionMs.ToString("F1", CultureInfo.InvariantCulture) : "",
                pipeline != null && pipeline.Jitter.Enabled ? "1" : "0",
                pipeline != null ? pipeline.Jitter.AmplitudeDeg.ToString("F3", CultureInfo.InvariantCulture) : "",
                pipeline?.Provider?.ProviderName ?? "none"));

            RowCount++;

            if (_autoFlushOnFinish && _chartPlayer != null && _chartPlayer.IsFinished)
            {
                Flush();
            }
        }

        public void Flush()
        {
            if (RowCount == 0 || string.IsNullOrEmpty(_path))
            {
                return;
            }

            File.WriteAllText(_path, _builder.ToString());
            Debug.Log($"[SessionLogger] 已写入 {RowCount} 行到 {_path}");
        }

        private static string Fmt(float value)
        {
            return float.IsNaN(value) ? "" : value.ToString("F3", CultureInfo.InvariantCulture);
        }
    }
}
