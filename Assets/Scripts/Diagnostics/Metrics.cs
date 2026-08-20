using System;
using UnityEngine;

namespace SpatialRhythm.Diagnostics
{
    /// <summary>固定容量的滚动窗口，支持均值与分位数。</summary>
    public sealed class RollingWindow
    {
        private readonly float[] _buffer;
        private readonly float[] _scratch;
        private int _head = -1;
        private int _count;
        private double _sum;

        public RollingWindow(int capacity)
        {
            _buffer = new float[Mathf.Max(4, capacity)];
            _scratch = new float[_buffer.Length];
        }

        public int Count => _count;

        public bool IsFull => _count == _buffer.Length;

        public float Mean => _count == 0 ? 0f : (float)(_sum / _count);

        public void Clear()
        {
            _head = -1;
            _count = 0;
            _sum = 0d;
        }

        public void Add(float value)
        {
            if (_count == _buffer.Length)
            {
                int tail = (_head + 1) % _buffer.Length;
                _sum -= _buffer[tail];
            }

            _head = (_head + 1) % _buffer.Length;
            _buffer[_head] = value;
            _sum += value;

            if (_count < _buffer.Length)
            {
                _count++;
            }
        }

        /// <summary>
        /// 分位数。P95 而非均值，是因为音游里毁掉一局的是尾部——
        /// 平均延迟 40ms 但每隔几秒抖到 120ms，体感是"这游戏判定有问题"。
        /// </summary>
        public float Percentile(float p)
        {
            if (_count == 0)
            {
                return 0f;
            }

            Array.Copy(_buffer, _scratch, _count);
            Array.Sort(_scratch, 0, _count);

            int index = Mathf.Clamp(Mathf.RoundToInt(p * (_count - 1)), 0, _count - 1);
            return _scratch[index];
        }
    }

    /// <summary>
    /// 静止呈现抖动测量。
    ///
    /// 定义（P0 验收指标 1 的口径，必须写死，否则三个人测会得到三个结论）：
    ///   在【物理角速度低于静止阈值】的滑动窗口内，
    ///   呈现前向向量相对窗口平均方向的夹角，取 P95，单位【虚拟角度】。
    ///
    /// 之所以量呈现姿态而非原始姿态：玩家看到的是呈现姿态，
    /// 而视角增益 G 会把物理抖动放大 G 倍——这正是抖动指标必须标明 G 的原因。
    /// </summary>
    public sealed class StaticJitterMeter
    {
        private const float StaticThresholdDegPerSecond = 3f;
        private const float SampleIntervalSeconds = 0.02f;

        private readonly Vector3[] _forwards;
        private int _head = -1;
        private int _count;
        private double _lastSampleTime;
        private float _staticSeconds;

        private readonly RollingWindow _deviations;

        public StaticJitterMeter(int capacity = 512)
        {
            _forwards = new Vector3[Mathf.Max(16, capacity)];
            _deviations = new RollingWindow(_forwards.Length);
        }

        /// <summary>当前是否满足静止条件。不满足时抖动读数无意义。</summary>
        public bool IsStatic { get; private set; }

        /// <summary>已连续静止的秒数。指标要求"握持静止 10 秒"。</summary>
        public float StaticSeconds => _staticSeconds;

        public float JitterP95Deg => _deviations.Percentile(0.95f);

        public float JitterMeanDeg => _deviations.Mean;

        public void Tick(double now, Quaternion presentedRotation, Vector3 physicalAngularVelocity, float deltaTime)
        {
            IsStatic = physicalAngularVelocity.magnitude <= StaticThresholdDegPerSecond;

            if (!IsStatic)
            {
                _staticSeconds = 0f;
                _head = -1;
                _count = 0;
                _deviations.Clear();
                return;
            }

            _staticSeconds += deltaTime;

            if (now - _lastSampleTime < SampleIntervalSeconds)
            {
                return;
            }

            _lastSampleTime = now;

            _head = (_head + 1) % _forwards.Length;
            _forwards[_head] = presentedRotation * Vector3.forward;
            if (_count < _forwards.Length)
            {
                _count++;
            }

            if (_count < 8)
            {
                return;
            }

            Vector3 mean = Vector3.zero;
            for (int i = 0; i < _count; i++)
            {
                int index = (_head - i + _forwards.Length) % _forwards.Length;
                mean += _forwards[index];
            }

            mean.Normalize();

            _deviations.Clear();
            for (int i = 0; i < _count; i++)
            {
                int index = (_head - i + _forwards.Length) % _forwards.Length;
                _deviations.Add(Vector3.Angle(_forwards[index], mean));
            }
        }
    }
}
