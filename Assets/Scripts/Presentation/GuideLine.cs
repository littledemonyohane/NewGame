using System;
using System.Collections.Generic;
using UnityEngine;
using SpatialRhythm.Audio;
using SpatialRhythm.Chart;
using SpatialRhythm.Core;
using SpatialRhythm.Judging;

namespace SpatialRhythm.Presentation
{
    /// <summary>
    /// 引导线：把设计文档 §7.2 的"视线编舞曲线"从编辑器辅助线变成玩家可见的游戏元素。
    ///
    /// 它解决的是这套玩法的根本矛盾：音符钉在空间的固定坐标上，玩家转过去才能看到，
    /// 但玩家不知道该往哪转。让音符自己飞到眼前会毁掉"寻找"这件事——
    /// 而寻找恰恰是这套输入方式唯一不可替代的乐趣。
    ///
    /// 所以引导的是【路径】而不是【音符】：
    /// 一条贴在球面上、穿过后续音符的弧线，加一个按 dspTime 沿线前进的光点。
    /// 玩家的任务变成"用准星跟住那个光点"，音符会在他到达时正好长到实际大小。
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public sealed class GuideLine : MonoBehaviour
    {
        private const int SampleCount = 72;

        [SerializeField] private ChartPlayer _chartPlayer;
        [SerializeField] private Conductor _conductor;

        [Header("显示窗口（拍）")]
        [Tooltip("向前显示多少拍的路径。太短来不及转，太长会糊成一团。")]
        [SerializeField] private float _lookAheadBeats = 4f;

        [Tooltip("身后保留多少拍，给出'我刚从哪来'的连续感。")]
        [SerializeField] private float _tailBeats = 1f;

        [Header("外观（桌面尺度，米）")]
        [SerializeField] private float _width = 0.006f;

        [SerializeField] private Color _color = new Color(0.45f, 0.85f, 1f);
        [SerializeField] private float _playheadScale = 0.025f;

        private LineRenderer _line;
        private Transform _playhead;
        private int[] _order;
        private readonly List<Vector3> _points = new List<Vector3>(SampleCount);

        /// <summary>
        /// 向前显示多少拍。这是新设计的核心旋钮：
        /// 太短来不及转过去，太长则路径糊成一团、玩家不知道该跟哪一段。
        /// P0 要测的正是这个值与音符预告时间的比例关系。
        /// </summary>
        public float LookAheadBeats
        {
            get => _lookAheadBeats;
            set
            {
                _lookAheadBeats = Mathf.Clamp(value, 1f, 12f);
                RefreshGradient();
            }
        }

        public float TailBeats
        {
            get => _tailBeats;
            set
            {
                _tailBeats = Mathf.Clamp(value, 0f, 4f);
                RefreshGradient();
            }
        }

        private void Awake()
        {
            _chartPlayer = _chartPlayer != null ? _chartPlayer : FindObjectOfType<ChartPlayer>();
            _conductor = _conductor != null ? _conductor : FindObjectOfType<Conductor>();

            BuildLine();
            BuildPlayhead();
        }

        private void Start()
        {
            BuildOrder();
        }

        /// <summary>按拍排序的音符索引。谱面通常已是升序，但不该假设。</summary>
        private void BuildOrder()
        {
            if (_chartPlayer == null)
            {
                _order = Array.Empty<int>();
                return;
            }

            IReadOnlyList<ChartPlayer.NoteRuntime> runtimes = _chartPlayer.Runtimes;
            _order = new int[runtimes.Count];
            for (int i = 0; i < runtimes.Count; i++)
            {
                _order[i] = i;
            }

            Array.Sort(_order, (a, b) => runtimes[a].Note.Beat.CompareTo(runtimes[b].Note.Beat));
        }

        private void Update()
        {
            if (_chartPlayer == null || _conductor == null || _order == null || _order.Length < 2)
            {
                return;
            }

            double currentBeat = _conductor.SongPositionBeats;
            double from = currentBeat - _tailBeats;
            double to = currentBeat + _lookAheadBeats;

            _points.Clear();
            for (int i = 0; i < SampleCount; i++)
            {
                double beat = from + (to - from) * i / (SampleCount - 1);
                _points.Add(PositionAtBeat(beat));
            }

            _line.positionCount = _points.Count;
            _line.SetPositions(_points.ToArray());

            if (_playhead != null)
            {
                Vector3 head = PositionAtBeat(currentBeat);
                _playhead.position = head;
                _playhead.rotation = Quaternion.LookRotation(
                    head.sqrMagnitude > 1e-6f ? head.normalized : Vector3.forward);

                // 每拍轻微脉动，让玩家不看数字也能感到节奏。
                float phase = (float)(currentBeat - Math.Floor(currentBeat));
                float pulse = 1f + 0.35f * Mathf.Exp(-phase * 6f);
                _playhead.localScale = Vector3.one * (_playheadScale * pulse);
            }
        }

        /// <summary>
        /// 路径在给定拍上的世界坐标。
        ///
        /// 方向沿球面大圆插值、距离线性插值——玩家跟着走的是一条真正的
        /// "转头 + 移动"轨迹，而不是屏幕上的直线。桌面尺度下距离变化很显著，
        /// 所以这条线同时在告诉玩家"该往前凑还是往后退"。
        /// </summary>
        private Vector3 PositionAtBeat(double beat)
        {
            NoteAt(0, out double firstBeat, out Vector3 firstDirection, out float firstDistance);
            if (beat <= firstBeat)
            {
                return firstDirection * firstDistance;
            }

            int last = _order.Length - 1;
            NoteAt(last, out double lastBeat, out Vector3 lastDirection, out float lastDistance);
            if (beat >= lastBeat)
            {
                return lastDirection * lastDistance;
            }

            // 音符数量在灰盒规模（几十个）下线性扫描足够，且天然按拍有序。
            for (int i = 0; i < last; i++)
            {
                NoteAt(i, out double beatA, out Vector3 directionA, out float distanceA);
                NoteAt(i + 1, out double beatB, out Vector3 directionB, out float distanceB);

                if (beat < beatA || beat > beatB)
                {
                    continue;
                }

                double span = beatB - beatA;
                if (span <= 0d)
                {
                    return directionB * distanceB;
                }

                float t = (float)((beat - beatA) / span);
                t = t * t * (3f - 2f * t);
                return Vector3.Slerp(directionA, directionB, t) * Mathf.Lerp(distanceA, distanceB, t);
            }

            return lastDirection * lastDistance;
        }

        private void NoteAt(int orderIndex, out double beat, out Vector3 direction, out float distance)
        {
            NoteEvent note = _chartPlayer.Runtimes[_order[orderIndex]].Note;
            beat = note.Beat;
            direction = note.Direction;
            distance = Mathf.Max(0.05f, note.DistanceMeters);
        }

        private void BuildLine()
        {
            var node = new GameObject("GuideLineRenderer");
            node.transform.SetParent(transform, false);

            _line = node.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.widthMultiplier = _width;
            _line.numCapVertices = 2;
            _line.alignment = LineAlignment.View;
            _line.textureMode = LineTextureMode.Stretch;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.sharedMaterial = StageMaterials.CreateLine(Color.white);

            RefreshGradient();
        }

        /// <summary>
        /// 身后暗、当前亮、远处渐隐——玩家一眼能看出该往哪个方向走。
        /// 前瞻拍数变化时必须重算，否则高亮点会跑偏。
        /// </summary>
        private void RefreshGradient()
        {
            if (_line == null)
            {
                return;
            }

            float headPosition = _tailBeats / Mathf.Max(0.01f, _tailBeats + _lookAheadBeats);

            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(_color * 0.45f, 0f),
                    new GradientColorKey(_color, headPosition),
                    new GradientColorKey(_color * 0.8f, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.35f, headPosition * 0.6f),
                    new GradientAlphaKey(0.95f, headPosition),
                    new GradientAlphaKey(0.15f, 1f)
                });
            _line.colorGradient = gradient;

            // 远端收细，进一步强化"这一头是现在"的方向感。
            _line.widthCurve = new AnimationCurve(
                new Keyframe(0f, 0.35f),
                new Keyframe(headPosition, 1f),
                new Keyframe(1f, 0.45f));
        }

        private void BuildPlayhead()
        {
            var node = GameObject.CreatePrimitive(PrimitiveType.Cube);
            node.name = "GuidePlayhead";
            node.transform.SetParent(transform, false);

            Collider collider = node.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            var renderer = node.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = StageMaterials.Create(new Color(1f, 0.95f, 0.65f));
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            _playhead = node.transform;
        }
    }
}
