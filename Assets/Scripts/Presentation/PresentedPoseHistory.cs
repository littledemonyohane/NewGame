using UnityEngine;

namespace SpatialRhythm.Presentation
{
    /// <summary>
    /// 呈现姿态的环形缓冲。对应设计文档 §7.6 的 PresentedPoseHistory。
    ///
    /// 这是"所见即所判"的核心：判定时不使用当前帧的姿态，而是用触摸事件的
    /// OS 时间戳回溯查询——取出玩家手指落下那一刻【屏幕上实际显示】的姿态。
    ///
    /// 因此写入这里的必须是最终呈现姿态（已滤波、已增益、已预测外推），
    /// 而不是原始传感器姿态。原始姿态只用于诊断。
    /// </summary>
    public sealed class PresentedPoseHistory
    {
        public struct Entry
        {
            public double PresentationTime;
            public Quaternion Rotation;

            /// <summary>相机位置（米）。桌面尺度下位置参与判定，必须一并回溯。</summary>
            public Vector3 Position;

            public Vector3 VirtualAngularVelocity;
        }

        private readonly Entry[] _buffer;
        private int _head = -1;
        private int _count;

        public PresentedPoseHistory(int capacity = 512)
        {
            _buffer = new Entry[Mathf.Max(8, capacity)];
        }

        public int Count => _count;

        public bool HasData => _count > 0;

        public Entry Latest => _count > 0 ? _buffer[_head] : default;

        public void Clear()
        {
            _head = -1;
            _count = 0;
        }

        public void Push(double presentationTime, Quaternion rotation, Vector3 position, Vector3 virtualAngularVelocity)
        {
            _head = (_head + 1) % _buffer.Length;
            _buffer[_head] = new Entry
            {
                PresentationTime = presentationTime,
                Rotation = rotation,
                Position = position,
                VirtualAngularVelocity = virtualAngularVelocity
            };

            if (_count < _buffer.Length)
            {
                _count++;
            }
        }

        /// <summary>
        /// 取出给定呈现时刻的完整位姿（旋转 + 位置）。缓冲区之外的查询会钳到最旧/最新一条，
        /// 并通过 <paramref name="exact"/> 报告是否落在有效区间内——
        /// 判定日志需要记录这一点，否则无法区分"玩家打偏"和"历史不够长"。
        /// </summary>
        public Entry SampleAt(double presentationTime, out bool exact)
        {
            exact = false;

            if (_count == 0)
            {
                return new Entry { Rotation = Quaternion.identity, Position = Vector3.zero };
            }

            if (_count == 1)
            {
                return _buffer[_head];
            }

            int oldestIndex = (_head - _count + 1 + _buffer.Length) % _buffer.Length;
            Entry oldest = _buffer[oldestIndex];
            Entry newest = _buffer[_head];

            if (presentationTime <= oldest.PresentationTime)
            {
                return oldest;
            }

            if (presentationTime >= newest.PresentationTime)
            {
                return newest;
            }

            exact = true;

            // 从最新往回线性扫描：判定查询总是落在最近几帧，扫描比二分更快。
            for (int i = 0; i < _count - 1; i++)
            {
                int currentIndex = (_head - i + _buffer.Length) % _buffer.Length;
                int previousIndex = (_head - i - 1 + _buffer.Length) % _buffer.Length;

                Entry current = _buffer[currentIndex];
                Entry previous = _buffer[previousIndex];

                if (presentationTime >= previous.PresentationTime && presentationTime <= current.PresentationTime)
                {
                    double span = current.PresentationTime - previous.PresentationTime;
                    float t = span <= 0d ? 1f : (float)((presentationTime - previous.PresentationTime) / span);

                    return new Entry
                    {
                        PresentationTime = presentationTime,
                        Rotation = Quaternion.Slerp(previous.Rotation, current.Rotation, t),
                        Position = Vector3.Lerp(previous.Position, current.Position, t),
                        VirtualAngularVelocity = Vector3.Lerp(
                            previous.VirtualAngularVelocity, current.VirtualAngularVelocity, t)
                    };
                }
            }

            return newest;
        }
    }
}
