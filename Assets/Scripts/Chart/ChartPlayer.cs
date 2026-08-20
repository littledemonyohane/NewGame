using System;
using System.Collections.Generic;
using UnityEngine;
using SpatialRhythm.Audio;
using SpatialRhythm.Core;
using SpatialRhythm.InputLayer;
using SpatialRhythm.Judging;
using SpatialRhythm.Presentation;

namespace SpatialRhythm.Chart
{
    /// <summary>
    /// 谱面播放与音符生命周期管理。
    ///
    /// 所有时间都从 <see cref="Conductor"/> 的 dspTime 派生，再换算到 AppClock 时间轴，
    /// 因为判定要和触摸/姿态的时间戳比较。
    /// </summary>
    public sealed class ChartPlayer : MonoBehaviour
    {
        public sealed class NoteRuntime
        {
            public int Index;
            public NoteEvent Note;

            /// <summary>判定时刻，AppClock 时间轴（秒）。每帧刷新以跟随时钟偏移估计。</summary>
            public double HitTimeApp;

            public GameObject Visual;
            public MeshRenderer Renderer;

            public double SpawnTime;

            /// <summary>最近一次落在激活锥内的时刻。滞回判定用。</summary>
            public double LastInConeTime;

            /// <summary>接近过程中是否曾经进过锥。用于统计"脱锁率"。</summary>
            public bool EverInCone;

            public bool Judged;
            public JudgeResult Result;
        }

        [Header("引用")]
        [SerializeField] private Conductor _conductor;
        [SerializeField] private TouchRouter _touchRouter;
        [SerializeField] private JudgeService _judge;
        [SerializeField] private GreyboxChart _chartAsset;

        [Header("舞台")]
        [Tooltip("音符边长（米）。桌面尺度下约 5cm——同样大小的音符，远的看起来小，深度就有了线索。")]
        [SerializeField] private float _noteScale = 0.05f;

        [Header("出现表现")]
        [Tooltip("音符在固定坐标原地生成，从小长到实际大小。玩家必须主动转过去才能看到——这是核心乐趣所在。")]
        [Range(0.02f, 0.6f)]
        [SerializeField] private float _materializeSeconds = 0.18f;

        [Tooltip("刚生成时的相对尺寸。判定时刻长到 1.0。")]
        [Range(0.05f, 0.8f)]
        [SerializeField] private float _spawnScaleRatio = 0.22f;

        [Header("Editor 降级")]
        [Tooltip("Editor 里只有一个鼠标，既要瞄准又要点击，Point 无法真实测试——退化为 Pulse 语义。")]
        [SerializeField] private bool _treatPointAsPulseInEditor = true;

        [Tooltip("真机上 Point 音符的屏幕命中半径（像素）。")]
        [SerializeField] private float _pointTouchRadiusPixels = 110f;

        private readonly List<NoteRuntime> _runtimes = new List<NoteRuntime>();
        private readonly List<NoteRuntime> _active = new List<NoteRuntime>();
        private readonly Stack<GameObject> _pool = new Stack<GameObject>();

        private Material _matIdle;
        private Material _matLocked;
        private Material _matPoint;
        private Camera _stageCamera;

        private int _nextSpawnIndex;

        public event Action<NoteRuntime, JudgeResult> OnNoteJudged;

        public IReadOnlyList<NoteRuntime> Runtimes => _runtimes;

        public int TotalNotes => _runtimes.Count;

        public int JudgedCount { get; private set; }

        public bool IsFinished => JudgedCount >= _runtimes.Count && _runtimes.Count > 0;

        private void Awake()
        {
            _conductor = _conductor != null ? _conductor : FindObjectOfType<Conductor>();
            _touchRouter = _touchRouter != null ? _touchRouter : FindObjectOfType<TouchRouter>();
            _judge = _judge != null ? _judge : FindObjectOfType<JudgeService>();
            _stageCamera = Camera.main;

            BuildMaterials();

            if (_chartAsset == null)
            {
                _chartAsset = GreyboxChart.CreateDefault();
            }

            BuildRuntimes();
        }

        private void OnEnable()
        {
            if (_touchRouter != null)
            {
                _touchRouter.OnTrigger += HandleTrigger;
            }
        }

        private void OnDisable()
        {
            if (_touchRouter != null)
            {
                _touchRouter.OnTrigger -= HandleTrigger;
            }
        }

        public void Restart()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                Despawn(_active[i]);
            }

            _active.Clear();
            _nextSpawnIndex = 0;
            JudgedCount = 0;
            BuildRuntimes();
            _conductor.Restart();
        }

        private void BuildRuntimes()
        {
            _runtimes.Clear();

            IReadOnlyList<NoteEvent> notes = _chartAsset.Notes;
            for (int i = 0; i < notes.Count; i++)
            {
                _runtimes.Add(new NoteRuntime
                {
                    Index = i,
                    Note = notes[i]
                });
            }
        }

        private void Update()
        {
            if (_conductor == null || !_conductor.IsRunning || _judge == null)
            {
                return;
            }

            double now = AppClock.Now;

            RefreshHitTimes();
            SpawnDueNotes(now);
            UpdateActiveNotes(now);
        }

        /// <summary>
        /// 每帧重算判定时刻。AppClock.DspOffset 由 Conductor 用 EMA 持续修正，
        /// 重算比缓存更准，成本也只有一次乘加。
        /// </summary>
        private void RefreshHitTimes()
        {
            for (int i = 0; i < _runtimes.Count; i++)
            {
                NoteRuntime runtime = _runtimes[i];
                runtime.HitTimeApp = _conductor.BeatToAppTime(runtime.Note.Beat);
            }
        }

        private void SpawnDueNotes(double now)
        {
            while (_nextSpawnIndex < _runtimes.Count)
            {
                NoteRuntime runtime = _runtimes[_nextSpawnIndex];
                double previewSeconds = runtime.Note.PreviewBeats * _conductor.SecondsPerBeat;

                if (runtime.HitTimeApp - now > previewSeconds)
                {
                    break;
                }

                Spawn(runtime, now);
                _nextSpawnIndex++;
            }
        }

        private void UpdateActiveNotes(double now)
        {
            PosePipeline pipeline = PosePipeline.Instance;
            var pose = new PresentedPoseHistory.Entry
            {
                Rotation = pipeline != null ? pipeline.PresentedRotation : Quaternion.identity,
                Position = pipeline != null ? pipeline.PresentedPosition : Vector3.zero
            };

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                NoteRuntime runtime = _active[i];
                double previewSeconds = runtime.Note.PreviewBeats * _conductor.SecondsPerBeat;
                double remaining = runtime.HitTimeApp - now;

                UpdateVisual(runtime, now, remaining, previewSeconds);
                TrackConeLock(runtime, now, pose);

                if (!runtime.Judged && remaining < -_judge.GoodWindowMs * 0.001d)
                {
                    ApplyAutoMiss(runtime);
                }

                // 判定完再留 0.25 秒播反馈。
                if (runtime.Judged && remaining < -0.25d)
                {
                    Despawn(runtime);
                    _active.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 追踪音符是否落在激活锥内。滞回与"脱锁率"统计都依赖这里。
        /// 使用与判定一致的有效锥（含辅助扩大），否则视觉锁定和实际判定会对不上。
        /// </summary>
        private void TrackConeLock(NoteRuntime runtime, double now, in PresentedPoseHistory.Entry pose)
        {
            float offsetMs = (float)((now - runtime.HitTimeApp) * 1000d);
            float error = JudgeService.AngularError(runtime.Note.WorldPosition, pose.Position, pose.Rotation);
            float cone = _judge.EffectiveActivateCone(offsetMs);

            if (error <= cone)
            {
                runtime.LastInConeTime = now;
                runtime.EverInCone = true;
            }
        }

        private void UpdateVisual(NoteRuntime runtime, double now, double remaining, double previewSeconds)
        {
            if (runtime.Visual == null)
            {
                return;
            }

            // 音符钉在世界固定坐标上，全程不移动。
            // 玩家看不到它，只能是因为还没转过去或还没走到位置——引导线负责告诉他往哪去。
            //
            // 渲染与判定都用同一个世界坐标 + 同一个相机位姿，
            // 所以"所见即所判"自动成立，不需要额外的对齐技巧。
            Vector3 worldPosition = runtime.Note.WorldPosition;

            runtime.Visual.transform.position = worldPosition;
            runtime.Visual.transform.rotation = Quaternion.LookRotation(runtime.Note.Direction) *
                                                (runtime.Note.Type == NoteType.Point
                                                    ? Quaternion.Euler(0f, 0f, 45f)
                                                    : Quaternion.identity);

            // 从小到大：生成时小，判定时刻长到实际大小。
            float normalizedRemaining = previewSeconds > 0d
                ? Mathf.Clamp01((float)(remaining / previewSeconds))
                : 0f;
            float approach = Mathf.Lerp(1f, _spawnScaleRatio, normalizedRemaining);

            // 生成瞬间再叠一个短促的弹出，让"突然出现"这件事有明确的视觉起点。
            double sinceSpawn = now - runtime.SpawnTime;
            float pop = _materializeSeconds > 0.001f
                ? Mathf.Clamp01((float)(sinceSpawn / _materializeSeconds))
                : 1f;
            pop = pop * pop * (3f - 2f * pop);

            runtime.Visual.transform.localScale = Vector3.one * (_noteScale * approach * pop);

            if (!runtime.Judged)
            {
                bool locked = runtime.LastInConeTime > 0d &&
                              (now - runtime.LastInConeTime) <= _judge.HysteresisSeconds;
                Material target = runtime.Note.Type == NoteType.Point ? _matPoint : (locked ? _matLocked : _matIdle);
                if (runtime.Renderer.sharedMaterial != target)
                {
                    runtime.Renderer.sharedMaterial = target;
                }
            }
        }

        private void HandleTrigger(TouchEvent touch)
        {
            if (_judge == null || _active.Count == 0)
            {
                return;
            }

            NoteRuntime best = SelectCandidate(touch);
            if (best == null)
            {
                return;
            }

            PosePipeline pipeline = PosePipeline.Instance;
            var pose = new PresentedPoseHistory.Entry { Rotation = Quaternion.identity, Position = Vector3.zero };
            bool exact = false;

            if (pipeline != null)
            {
                // "所见即所判"的落点：按触摸时间戳回溯，取出手指落下那一刻屏幕上的位姿。
                pose = pipeline.History.SampleAt(touch.Timestamp, out exact);
            }

            JudgeResult result = _judge.Evaluate(
                best.Note.WorldPosition,
                best.HitTimeApp,
                touch,
                pose,
                best.LastInConeTime,
                exact);

            best.Judged = true;
            best.Result = result;
            JudgedCount++;

            ApplyJudgeVisual(best, result);
            OnNoteJudged?.Invoke(best, result);
        }

        /// <summary>
        /// 候选选择：先按时间窗筛，再优先已激活的音符，最后取时间偏差最小的。
        ///
        /// 只按时间选是不够的——3D 下两个时间相近的音符可能相隔 80°，
        /// 只按时间会把判定发给玩家根本没看的那一个。
        /// </summary>
        private NoteRuntime SelectCandidate(in TouchEvent touch)
        {
            PosePipeline pipeline = PosePipeline.Instance;
            PresentedPoseHistory.Entry pose = pipeline != null
                ? pipeline.History.SampleAt(touch.Timestamp, out _)
                : new PresentedPoseHistory.Entry { Rotation = Quaternion.identity, Position = Vector3.zero };

            double window = (_judge.GoodWindowMs + _judge.AssistWindowMs) * 0.001d;

            NoteRuntime best = null;
            float bestScore = float.MaxValue;

            for (int i = 0; i < _active.Count; i++)
            {
                NoteRuntime runtime = _active[i];
                if (runtime.Judged)
                {
                    continue;
                }

                double offset = touch.Timestamp - runtime.HitTimeApp;
                if (Math.Abs(offset) > window)
                {
                    continue;
                }

                if (!MatchesTriggerRules(runtime, touch))
                {
                    continue;
                }

                float offsetMs = (float)(offset * 1000d);
                float error = JudgeService.AngularError(runtime.Note.WorldPosition, pose.Position, pose.Rotation);
                bool activated = error <= _judge.EffectiveActivateCone(offsetMs);

                // 已激活的一律优先于未激活的。
                float score = Mathf.Abs(offsetMs) + (activated ? 0f : 10000f);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = runtime;
                }
            }

            return best;
        }

        private bool MatchesTriggerRules(NoteRuntime runtime, in TouchEvent touch)
        {
            if (runtime.Note.Type == NoteType.Pulse)
            {
                // 捕获音符：任一触发区点击即可，不要求手指追着移动目标跑。
                return true;
            }

            if (Application.isEditor && _treatPointAsPulseInEditor)
            {
                return true;
            }

            if (_stageCamera == null || runtime.Visual == null)
            {
                return true;
            }

            Vector3 screenPoint = _stageCamera.WorldToScreenPoint(runtime.Visual.transform.position);
            if (screenPoint.z <= 0f)
            {
                return false;
            }

            return Vector2.Distance(touch.ScreenPosition, screenPoint) <= _pointTouchRadiusPixels;
        }

        private void ApplyAutoMiss(NoteRuntime runtime)
        {
            var result = new JudgeResult
            {
                Activated = false,
                Grade = TimingGrade.Miss,
                Failure = FailureReason.NoInput,
                SpatialQuality = 0f,
                AngularErrorVirtual = float.NaN,
                TimingOffsetMs = float.NaN,
                HistoryExact = true,
                OsTimestamp = false
            };

            runtime.Judged = true;
            runtime.Result = result;
            JudgedCount++;

            ApplyJudgeVisual(runtime, result);
            OnNoteJudged?.Invoke(runtime, result);
        }

        private void ApplyJudgeVisual(NoteRuntime runtime, JudgeResult result)
        {
            if (runtime.Renderer == null)
            {
                return;
            }

            runtime.Renderer.sharedMaterial = ResolveFeedbackMaterial(result);
        }

        private Material ResolveFeedbackMaterial(JudgeResult result)
        {
            if (!result.Activated)
            {
                return _matMiss;
            }

            return result.Grade switch
            {
                TimingGrade.Perfect => _matPerfect,
                TimingGrade.Great => _matGreat,
                TimingGrade.Good => _matGood,
                _ => _matMiss
            };
        }

        private void Spawn(NoteRuntime runtime, double now)
        {
            GameObject visual = _pool.Count > 0 ? _pool.Pop() : CreateVisual();
            visual.SetActive(true);

            runtime.Visual = visual;
            runtime.Renderer = visual.GetComponent<MeshRenderer>();
            runtime.SpawnTime = now;
            runtime.LastInConeTime = 0d;
            runtime.EverInCone = false;
            runtime.Judged = false;

            runtime.Renderer.sharedMaterial = runtime.Note.Type == NoteType.Point ? _matPoint : _matIdle;
            _active.Add(runtime);
        }

        private void Despawn(NoteRuntime runtime)
        {
            if (runtime.Visual == null)
            {
                return;
            }

            runtime.Visual.SetActive(false);
            _pool.Push(runtime.Visual);
            runtime.Visual = null;
            runtime.Renderer = null;
        }

        private GameObject CreateVisual()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Note";
            go.transform.SetParent(transform, false);

            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            return go;
        }

        private Material _matPerfect;
        private Material _matGreat;
        private Material _matGood;
        private Material _matMiss;

        private void BuildMaterials()
        {
            _matIdle = StageMaterials.Create(new Color(0.25f, 0.42f, 0.85f));
            _matLocked = StageMaterials.Create(new Color(0.30f, 0.95f, 0.95f));
            _matPoint = StageMaterials.Create(new Color(0.95f, 0.75f, 0.25f));
            _matPerfect = StageMaterials.Create(new Color(0.30f, 1.00f, 0.45f));
            _matGreat = StageMaterials.Create(new Color(0.85f, 0.95f, 0.35f));
            _matGood = StageMaterials.Create(new Color(0.95f, 0.60f, 0.25f));
            _matMiss = StageMaterials.Create(new Color(0.90f, 0.20f, 0.25f));
        }
    }
}
