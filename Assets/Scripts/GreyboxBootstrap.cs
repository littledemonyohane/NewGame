using UnityEngine;
using SpatialRhythm.Audio;
using SpatialRhythm.Chart;
using SpatialRhythm.Core;
using SpatialRhythm.Diagnostics;
using SpatialRhythm.InputLayer;
using SpatialRhythm.Judging;
using SpatialRhythm.Presentation;
using SpatialRhythm.Tuning;

namespace SpatialRhythm
{
    /// <summary>
    /// 灰盒场景的唯一入口：场景里只需要一个挂了本组件的空物体，
    /// 其余全部在运行时按依赖顺序构建。
    ///
    /// 这样做是为了让场景文件保持"几乎为空"——灰盒阶段会频繁改结构，
    /// 用代码装配比用场景引用装配更容易 diff、更不容易出现丢引用。
    /// </summary>
    public sealed class GreyboxBootstrap : MonoBehaviour
    {
        [Header("星空")]
        [Tooltip("没有静态视觉参照物就感知不到旋转——星点不是装饰，是必需的空间参照。")]
        [SerializeField] private int _starCount = 420;

        [SerializeField] private float _starFieldRadius = 70f;
        [SerializeField] private int _randomSeed = 20260819;

        private void Awake()
        {
            // 依赖顺序：AddComponent 会立即触发 Awake，所以先建被依赖的。
            var conductor = CreateNode("Conductor").AddComponent<Conductor>();
            var pipeline = CreateNode("PosePipeline").AddComponent<PosePipeline>();

            BuildCamera();
            BuildStarField();

            var systems = CreateNode("Systems");
            systems.AddComponent<JudgeService>();
            systems.AddComponent<TouchRouter>();

            CreateNode("ChartPlayer").AddComponent<ChartPlayer>();

            // 必须在 ChartPlayer 之后：引导线要读谱面才能构建路径。
            CreateNode("GuideLine").AddComponent<GuideLine>();

            var diagnostics = CreateNode("Diagnostics");
            diagnostics.AddComponent<PoseRecorder>();
            diagnostics.AddComponent<SessionLogger>();
            diagnostics.AddComponent<MetricsHud>();
            diagnostics.AddComponent<StageOverlay>();
            diagnostics.AddComponent<TuningPanel>();

            Debug.Log($"[GreyboxBootstrap] 就绪。姿态源 = {pipeline.Provider?.ProviderName}，" +
                      $"BPM = {conductor.Bpm}，日志目录 = {Application.persistentDataPath}");
        }

        private void BuildCamera()
        {
            GameObject node = CreateNode("StageCamera");
            var camera = node.AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.015f, 0.02f, 0.045f);
            // 桌面尺度：音符最近在 0.32m，近裁剪面必须远小于它。
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 200f;
            camera.allowHDR = false;
            camera.allowMSAA = false;

            node.AddComponent<AudioListener>();
            node.AddComponent<StageCamera>();
        }

        private void BuildStarField()
        {
            GameObject root = CreateNode("StarField");

            var dim = StageMaterials.Create(new Color(0.55f, 0.62f, 0.80f));
            var bright = StageMaterials.Create(new Color(0.95f, 0.97f, 1f));

            var random = new System.Random(_randomSeed);
            var mesh = BuildQuadMesh();

            for (int i = 0; i < _starCount; i++)
            {
                // 球面均匀采样。
                double u = random.NextDouble() * 2d - 1d;
                double theta = random.NextDouble() * System.Math.PI * 2d;
                double r = System.Math.Sqrt(1d - u * u);

                var direction = new Vector3(
                    (float)(r * System.Math.Cos(theta)),
                    (float)u,
                    (float)(r * System.Math.Sin(theta)));

                var star = new GameObject("Star");
                star.transform.SetParent(root.transform, false);

                // 距离拉开一个区间，让近星和远星产生【差异视差】。
                // 全部放在同一球面上的话，相机平移只会让整片星空整体平移，
                // 那不构成深度线索。近星起点略大于音符半径(12)，避免遮挡音符。
                float distance = Mathf.Lerp(14f, _starFieldRadius, (float)random.NextDouble());
                star.transform.position = direction * distance;
                star.transform.rotation = Quaternion.LookRotation(-direction);

                // 尺寸随距离等比放大，视角大小才一致。
                float scale = Mathf.Lerp(0.10f, 0.34f, (float)random.NextDouble()) * (distance / _starFieldRadius);
                star.transform.localScale = Vector3.one * scale;

                var filter = star.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;

                var renderer = star.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = random.NextDouble() > 0.82d ? bright : dim;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        private static Mesh BuildQuadMesh()
        {
            var mesh = new Mesh { name = "StarQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f)
            };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private GameObject CreateNode(string nodeName)
        {
            var node = new GameObject(nodeName);
            node.transform.SetParent(transform, false);
            return node;
        }
    }
}
