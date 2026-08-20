using UnityEngine;

namespace SpatialRhythm.Presentation
{
    /// <summary>
    /// 把呈现姿态套到相机上。相机只负责"显示"，不持有任何判定逻辑——
    /// 判定读的是 <see cref="PresentedPoseHistory"/>，不是相机的 transform。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(-50)]
    public sealed class StageCamera : MonoBehaviour
    {
        [Tooltip("手机屏幕在臂长处只占真实视野 20–25°，但游戏相机用 60–75° 才有取景窗的感觉。")]
        [SerializeField] private float _fieldOfView = 68f;

        [Tooltip("0 = 交给平台。灰盒阶段锁 120，测不到 120 的设备会自然回落。")]
        [SerializeField] private int _targetFrameRate = 120;

        private Camera _camera;

        public Camera Camera => _camera;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _camera.fieldOfView = _fieldOfView;

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = _targetFrameRate;

            // 失焦时默认会被节流，而 dspTime 仍在走 —— 主时钟和逻辑会直接脱节。
            // 灰盒需要能无人值守跑完整段谱面，所以显式打开。
            Application.runInBackground = true;
        }

        private void LateUpdate()
        {
            PosePipeline pipeline = PosePipeline.Instance;
            if (pipeline == null)
            {
                return;
            }

            transform.localRotation = pipeline.PresentedRotation;

            // 杠杆视差：相机小幅平移。音符与引导线跟随同一个原点渲染，
            // 所以只有星空会相对移动——判定对齐不受影响。
            transform.localPosition = pipeline.PresentedPosition;
        }
    }
}
