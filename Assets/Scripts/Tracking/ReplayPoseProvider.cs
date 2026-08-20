using System.Collections.Generic;
using UnityEngine;

namespace SpatialRhythm.Tracking
{
    /// <summary>
    /// 回放录制好的姿态流。
    ///
    /// 这是整个 Demo 里性价比最高的组件：真机录一次姿态，Editor 里反复回放调参，
    /// 不用反复出包。iOS 出一次包十几分钟，靠它基本抵消掉 Stage 4 的迭代成本。
    /// 副作用是调参有了可复现的基准——同一段姿态、不同参数，CSV 可以直接对比。
    /// </summary>
    public sealed class ReplayPoseProvider : IPoseProvider
    {
        private readonly List<PoseSample> _samples;
        private double _startTimestamp;
        private int _cursor;

        public string ProviderName => $"Replay({_samples.Count})";

        public bool IsAvailable => _samples != null && _samples.Count > 1;

        public bool Loop { get; set; } = true;

        /// <summary>回放是否已跑完（Loop 关闭时有意义）。</summary>
        public bool Finished { get; private set; }

        public ReplayPoseProvider(List<PoseSample> samples)
        {
            _samples = samples ?? new List<PoseSample>();
        }

        public void Initialize()
        {
            _cursor = 0;
            _startTimestamp = -1d;
            Finished = false;
        }

        public PoseSample Sample(double timestamp)
        {
            if (!IsAvailable)
            {
                return PoseSample.Identity(timestamp);
            }

            if (_startTimestamp < 0d)
            {
                _startTimestamp = timestamp;
            }

            double duration = _samples[_samples.Count - 1].Timestamp - _samples[0].Timestamp;
            double elapsed = timestamp - _startTimestamp;

            if (duration > 0d && elapsed > duration)
            {
                if (Loop)
                {
                    elapsed %= duration;
                    _cursor = 0;
                }
                else
                {
                    Finished = true;
                    elapsed = duration;
                }
            }

            double target = _samples[0].Timestamp + elapsed;

            while (_cursor < _samples.Count - 2 && _samples[_cursor + 1].Timestamp < target)
            {
                _cursor++;
            }

            PoseSample a = _samples[_cursor];
            PoseSample b = _samples[Mathf.Min(_cursor + 1, _samples.Count - 1)];

            double span = b.Timestamp - a.Timestamp;
            float t = span <= 0d ? 0f : (float)((target - a.Timestamp) / span);

            return new PoseSample
            {
                Timestamp = timestamp,
                Rotation = Quaternion.Slerp(a.Rotation, b.Rotation, t),
                AngularVelocity = Vector3.Lerp(a.AngularVelocity, b.AngularVelocity, t),
                Quality = a.Quality,
                Position = null
            };
        }

        public void Recenter()
        {
            _startTimestamp = -1d;
            _cursor = 0;
        }

        public void Shutdown()
        {
        }
    }
}
