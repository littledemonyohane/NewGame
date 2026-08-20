using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using SpatialRhythm.Presentation;
using SpatialRhythm.Tracking;

namespace SpatialRhythm.Diagnostics
{
    /// <summary>
    /// 录制原始物理姿态流，供 <see cref="ReplayPoseProvider"/> 回放。
    ///
    /// 录的是【原始】姿态而非呈现姿态——回放时要重新过一遍滤波与增益，
    /// 这样才能用同一段真人动作去对比不同的参数组合。
    /// </summary>
    public sealed class PoseRecorder : MonoBehaviour
    {
        private const string FilePrefix = "pose_";
        private const string FileExtension = ".csv";

        [SerializeField] private bool _recordOnStart;

        private readonly List<PoseSample> _samples = new List<PoseSample>(20000);

        public bool IsRecording { get; private set; }

        public int SampleCount => _samples.Count;

        public string LastSavedPath { get; private set; }

        private void Start()
        {
            if (_recordOnStart)
            {
                StartRecording();
            }
        }

        private void Update()
        {
            if (!IsRecording)
            {
                return;
            }

            PosePipeline pipeline = PosePipeline.Instance;
            if (pipeline == null)
            {
                return;
            }

            _samples.Add(pipeline.LatestRawSample);
        }

        public void StartRecording()
        {
            _samples.Clear();
            IsRecording = true;
        }

        public string StopAndSave()
        {
            IsRecording = false;

            if (_samples.Count == 0)
            {
                return null;
            }

            string path = Path.Combine(
                Application.persistentDataPath,
                FilePrefix + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + FileExtension);

            var builder = new StringBuilder(_samples.Count * 64);
            builder.AppendLine("timestamp,qx,qy,qz,qw,avx,avy,avz,quality");

            for (int i = 0; i < _samples.Count; i++)
            {
                PoseSample s = _samples[i];
                builder.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:F6},{1:F6},{2:F6},{3:F6},{4:F6},{5:F3},{6:F3},{7:F3},{8:F2}",
                    s.Timestamp,
                    s.Rotation.x, s.Rotation.y, s.Rotation.z, s.Rotation.w,
                    s.AngularVelocity.x, s.AngularVelocity.y, s.AngularVelocity.z,
                    s.Quality));
            }

            File.WriteAllText(path, builder.ToString());
            LastSavedPath = path;
            Debug.Log($"[PoseRecorder] 已保存 {_samples.Count} 条姿态到 {path}");
            return path;
        }

        /// <summary>加载最近一次录制并切换到回放模式。</summary>
        public bool LoadLatestAndReplay()
        {
            string path = FindLatestRecording();
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning("[PoseRecorder] 没有找到任何录制文件。");
                return false;
            }

            List<PoseSample> samples = Load(path);
            if (samples.Count < 2)
            {
                Debug.LogWarning($"[PoseRecorder] {path} 样本不足，无法回放。");
                return false;
            }

            PosePipeline pipeline = PosePipeline.Instance;
            if (pipeline == null)
            {
                return false;
            }

            pipeline.SetProvider(new ReplayPoseProvider(samples));
            Debug.Log($"[PoseRecorder] 回放 {path}，共 {samples.Count} 条。");
            return true;
        }

        public static string FindLatestRecording()
        {
            if (!Directory.Exists(Application.persistentDataPath))
            {
                return null;
            }

            string[] files = Directory.GetFiles(Application.persistentDataPath, FilePrefix + "*" + FileExtension);
            if (files.Length == 0)
            {
                return null;
            }

            Array.Sort(files, StringComparer.Ordinal);
            return files[files.Length - 1];
        }

        public static List<PoseSample> Load(string path)
        {
            var result = new List<PoseSample>();
            string[] lines = File.ReadAllLines(path);

            for (int i = 1; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split(',');
                if (parts.Length < 9)
                {
                    continue;
                }

                result.Add(new PoseSample
                {
                    Timestamp = ParseDouble(parts[0]),
                    Rotation = new Quaternion(
                        ParseFloat(parts[1]), ParseFloat(parts[2]),
                        ParseFloat(parts[3]), ParseFloat(parts[4])),
                    AngularVelocity = new Vector3(
                        ParseFloat(parts[5]), ParseFloat(parts[6]), ParseFloat(parts[7])),
                    Quality = ParseFloat(parts[8]),
                    Position = null
                });
            }

            return result;
        }

        private static double ParseDouble(string value) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double r) ? r : 0d;

        private static float ParseFloat(string value) =>
            float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float r) ? r : 0f;
    }
}
