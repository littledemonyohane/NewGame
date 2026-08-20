using UnityEngine;
using SpatialRhythm.Chart;
using SpatialRhythm.Judging;
using SpatialRhythm.Presentation;

namespace SpatialRhythm.Diagnostics
{
    /// <summary>
    /// 屏幕叠加层：中央捕获区准星 + 屏外音符方向指示器。
    ///
    /// 准星的像素半径由 θ_activate 与相机 FOV 反算，不是画个好看的圈——
    /// 玩家看到的圈必须就是判定用的锥，否则"所见即所判"在视觉层面就先破了。
    /// </summary>
    public sealed class StageOverlay : MonoBehaviour
    {
        [SerializeField] private ChartPlayer _chartPlayer;
        [SerializeField] private JudgeService _judge;

        [Tooltip("屏外指示器提前多少秒开始显示。设计文档要求屏外音符 ≥800ms 预告。")]
        [SerializeField] private float _indicatorLeadSeconds = 1.2f;

        [SerializeField] private bool _visible = true;

        private Camera _camera;
        private Texture2D _ringActivate;
        private Texture2D _ringPerfect;
        private Texture2D _dot;

        public bool Visible
        {
            get => _visible;
            set => _visible = value;
        }

        private void Awake()
        {
            _camera = Camera.main;
            _chartPlayer = _chartPlayer != null ? _chartPlayer : FindObjectOfType<ChartPlayer>();
            _judge = _judge != null ? _judge : FindObjectOfType<JudgeService>();

            _ringActivate = CreateRing(128, 2, new Color(0.55f, 0.85f, 1f, 0.75f));
            _ringPerfect = CreateRing(128, 2, new Color(1f, 1f, 1f, 0.35f));
            _dot = CreateSolid(new Color(1f, 0.85f, 0.3f, 0.9f));
        }

        private void OnGUI()
        {
            if (!_visible || _camera == null || _judge == null)
            {
                return;
            }

            DrawReticle();
            DrawOffScreenIndicators();
            DrawCursorHint();
        }

        /// <summary>
        /// 未锁定光标时给出提示。否则"鼠标转不动视角"很容易被误判成滤波或判定的问题。
        /// </summary>
        private void DrawCursorHint()
        {
            if (!Application.isEditor && Application.platform != RuntimePlatform.WindowsPlayer)
            {
                return;
            }

            if (Tracking.EditorMousePoseProvider.IsLocked)
            {
                return;
            }

            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 15
            };
            style.normal.textColor = new Color(1f, 0.9f, 0.5f);

            GUI.Label(
                new Rect(0f, Screen.height * 0.62f, Screen.width, 30f),
                "点击画面锁定鼠标 · 鼠标转向 / WASD+空格+Ctrl 平移 · Esc 释放 · F2 调参",
                style);
        }

        private void DrawReticle()
        {
            float activateRadius = ConeToPixels(_judge.ThetaActivate);
            float perfectRadius = ConeToPixels(_judge.ThetaPerfect);

            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;

            GUI.DrawTexture(
                new Rect(cx - activateRadius, cy - activateRadius, activateRadius * 2f, activateRadius * 2f),
                _ringActivate);

            GUI.DrawTexture(
                new Rect(cx - perfectRadius, cy - perfectRadius, perfectRadius * 2f, perfectRadius * 2f),
                _ringPerfect);
        }

        /// <summary>把锥半角换算成屏幕像素半径。</summary>
        private float ConeToPixels(float coneDeg)
        {
            float halfFov = _camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float ratio = Mathf.Tan(coneDeg * Mathf.Deg2Rad) / Mathf.Tan(halfFov);
            return ratio * Screen.height * 0.5f;
        }

        private void DrawOffScreenIndicators()
        {
            if (_chartPlayer == null)
            {
                return;
            }

            double now = Core.AppClock.Now;
            const float Margin = 34f;

            var runtimes = _chartPlayer.Runtimes;
            for (int i = 0; i < runtimes.Count; i++)
            {
                ChartPlayer.NoteRuntime runtime = runtimes[i];
                if (runtime.Judged || runtime.Visual == null)
                {
                    continue;
                }

                double remaining = runtime.HitTimeApp - now;
                if (remaining < 0d || remaining > _indicatorLeadSeconds)
                {
                    continue;
                }

                Vector3 screenPoint = _camera.WorldToScreenPoint(runtime.Visual.transform.position);
                bool onScreen = screenPoint.z > 0f &&
                                screenPoint.x >= 0f && screenPoint.x <= Screen.width &&
                                screenPoint.y >= 0f && screenPoint.y <= Screen.height;

                if (onScreen)
                {
                    continue;
                }

                // 屏后的点要翻转，否则指示器会指向相反方向。
                Vector2 direction = new Vector2(screenPoint.x - Screen.width * 0.5f,
                                                screenPoint.y - Screen.height * 0.5f);
                if (screenPoint.z < 0f)
                {
                    direction = -direction;
                }

                if (direction.sqrMagnitude < 0.001f)
                {
                    continue;
                }

                direction.Normalize();

                float halfW = Screen.width * 0.5f - Margin;
                float halfH = Screen.height * 0.5f - Margin;
                float scale = Mathf.Min(
                    halfW / Mathf.Max(0.0001f, Mathf.Abs(direction.x)),
                    halfH / Mathf.Max(0.0001f, Mathf.Abs(direction.y)));

                float px = Screen.width * 0.5f + direction.x * scale;
                // GUI 坐标 y 向下，屏幕坐标 y 向上。
                float py = Screen.height - (Screen.height * 0.5f + direction.y * scale);

                float urgency = 1f - Mathf.Clamp01((float)(remaining / _indicatorLeadSeconds));
                float size = Mathf.Lerp(10f, 22f, urgency);

                GUI.DrawTexture(new Rect(px - size * 0.5f, py - size * 0.5f, size, size), _dot);
            }
        }

        private static Texture2D CreateRing(int size, int thickness, Color color)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color[size * size];
            float center = (size - 1) * 0.5f;
            float outer = center;
            float inner = center - thickness;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                    bool onRing = distance <= outer && distance >= inner;
                    pixels[y * size + x] = onRing ? color : Color.clear;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static Texture2D CreateSolid(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }
    }
}
