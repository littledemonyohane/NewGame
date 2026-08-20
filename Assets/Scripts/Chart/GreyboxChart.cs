using System.Collections.Generic;
using UnityEngine;
using SpatialRhythm.Judging;

namespace SpatialRhythm.Chart
{
    /// <summary>
    /// 灰盒谱面 —— 桌面尺度。刻意做成程序化生成而非资源文件，
    /// 谱面格式与编辑器明确不在 P0 范围内。
    ///
    /// 可玩体积约 70cm 宽 × 44cm 高 × 40cm 深（坐在桌前手臂能覆盖的范围），
    /// 音符距原点 0.30–0.85m。这个尺度下【位移是有意义的操作】：
    /// 挪动 10cm 就能让一个 0.35m 处的音符偏移 16°，远超 θ_activate 的 7°。
    ///
    /// 两个设计目标：
    /// 1. 扫过三档角速度，找"多快开始靠运气"的拐点
    /// 2. 制造必须【靠平移而非旋转】才能对准的段落，验证位移玩法是否成立
    /// </summary>
    [CreateAssetMenu(fileName = "GreyboxChart", menuName = "SpatialRhythm/Greybox Chart")]
    public sealed class GreyboxChart : ScriptableObject
    {
        /// <summary>桌面可玩体积的半尺寸（米）。与 EditorMousePoseProvider 的模拟范围一致。</summary>
        public static readonly Vector3 DeskVolumeHalfExtents = new Vector3(0.35f, 0.22f, 0.20f);

        private const float NearDistance = 0.32f;
        private const float MidDistance = 0.55f;
        private const float FarDistance = 0.85f;

        [SerializeField] private List<NoteEvent> _notes = new List<NoteEvent>();

        public IReadOnlyList<NoteEvent> Notes => _notes;

        public double LastBeat
        {
            get
            {
                double last = 0d;
                for (int i = 0; i < _notes.Count; i++)
                {
                    if (_notes[i].Beat > last)
                    {
                        last = _notes[i].Beat;
                    }
                }

                return last;
            }
        }

        public static GreyboxChart CreateDefault()
        {
            var chart = CreateInstance<GreyboxChart>();
            chart.Generate();
            return chart;
        }

        [ContextMenu("Generate")]
        public void Generate()
        {
            _notes.Clear();

            double beat = 4d;

            // ── 段 A：慢速热身。中距离、小角度，先建立"跟住光点"的基本节奏。
            // Δθ 约 36° / 0.5s → 约 40°/s 物理角。含 Point 音符。
            for (int i = 0; i < 16; i++)
            {
                float azimuth = (i % 2 == 0) ? -18f : 18f;
                float elevation = (i % 4 < 2) ? 6f : -6f;
                // Point 只放在低角速度段——直接点触与高速转向不能并发。
                NoteType type = (i % 4 == 3) ? NoteType.Point : NoteType.Pulse;
                Add(beat, type, azimuth, elevation, MidDistance, 2f);
                beat += 1d;
            }

            // ── 段 B：深度交替。近远来回，桌面尺度下这会产生很强的视差摆动，
            //     是"移动改变了什么"最直观的一段。
            for (int i = 0; i < 16; i++)
            {
                float azimuth = (i % 2 == 0) ? -30f : 30f;
                float elevation = (i % 4 < 2) ? 14f : -10f;
                float distance = (i % 2 == 0) ? NearDistance : FarDistance;
                Add(beat, NoteType.Pulse, azimuth, elevation, distance, 2f);
                beat += 1d;
            }

            // ── 段 C：高速八分音符。约 190°/s 物理角，预期开始掉分的地方。
            for (int i = 0; i < 16; i++)
            {
                float azimuth = (i % 2 == 0) ? -42f : 42f;
                float elevation = (i % 4 < 2) ? 24f : -18f;
                Add(beat, NoteType.Pulse, azimuth, elevation, MidDistance, 2.5f);
                beat += 0.5d;
            }

            // ── 段 D：平移段。★ 这一段是位移玩法的核心验证。
            //     音符全部集中在很窄的角度范围内、但距离与横向偏移不同，
            //     光靠转动手机对不准——必须真的把设备挪过去。
            beat += 2d;
            for (int i = 0; i < 10; i++)
            {
                // 角度很小（±8°），但近距离下这点角度对应几厘米的横向位移。
                float azimuth = Mathf.Lerp(-8f, 8f, i / 9f);
                float elevation = (i % 2 == 0) ? 5f : -5f;
                float distance = Mathf.Lerp(NearDistance, FarDistance, Mathf.PingPong(i / 3f, 1f));
                Add(beat, NoteType.Pulse, azimuth, elevation, distance, 2f);
                beat += 1d;
            }

            // ── 段 E：静止段。体力曲线的"张弛"，验证恢复是否真的有效。
            beat += 2d;
            for (int i = 0; i < 6; i++)
            {
                float azimuth = (i % 2 == 0) ? -8f : 8f;
                Add(beat, i % 3 == 2 ? NoteType.Point : NoteType.Pulse, azimuth, 4f, MidDistance, 3f);
                beat += 2d;
            }

            // ── 段 F：边界压力测试，顶到角度笼和可玩体积的边上。
            beat += 2d;
            float[] extremeAzimuth = { -60f, 60f, -60f, 60f, 0f, -55f, 55f, 0f };
            float[] extremeElevation = { 34f, -34f, -34f, 34f, 42f, 0f, 0f, -38f };
            float[] extremeDistance = { FarDistance, NearDistance, MidDistance, FarDistance,
                                        NearDistance, MidDistance, FarDistance, NearDistance };
            for (int i = 0; i < extremeAzimuth.Length; i++)
            {
                Add(beat, NoteType.Pulse, extremeAzimuth[i], extremeElevation[i], extremeDistance[i], 3f);
                beat += 1d;
            }
        }

        private void Add(double beat, NoteType type, float azimuth, float elevation, float distance, float previewBeats)
        {
            _notes.Add(new NoteEvent
            {
                Beat = beat,
                Type = type,
                AzimuthDeg = azimuth,
                ElevationDeg = elevation,
                DistanceMeters = distance,
                PreviewBeats = previewBeats
            });
        }
    }
}
