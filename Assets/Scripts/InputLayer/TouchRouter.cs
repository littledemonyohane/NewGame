using System;
using System.Collections.Generic;
using UnityEngine;
using SpatialRhythm.Core;

namespace SpatialRhythm.InputLayer
{
    /// <summary>
    /// 把触摸/鼠标输入归一化成带时间戳的 <see cref="TouchEvent"/>，并划分横屏触发区。
    ///
    /// 布局（横持双手，双拇指常驻两侧）：
    ///   左侧 28% = LeftTrigger   右侧 28% = RightTrigger   中间 = Center
    ///
    /// 关于时间戳：当前使用帧时间，并把 IsOsTimestamp 标为 false。
    /// Stage 4 的第一件事就是验证 Unity 能否拿到真实 OS 触摸时间戳
    /// （这是整条"所见即所判"链路唯一的落地阻塞点）——
    /// 拿得到就在这里替换，拿不到则接受一帧误差并在 CSV 里标记。
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public sealed class TouchRouter : MonoBehaviour
    {
        [Range(0.1f, 0.45f)]
        [SerializeField] private float _sideZoneWidth = 0.28f;

        private readonly List<TouchEvent> _frameEvents = new List<TouchEvent>(8);

        /// <summary>本帧发生的所有触发（按时间顺序）。</summary>
        public IReadOnlyList<TouchEvent> FrameEvents => _frameEvents;

        /// <summary>每次触发时抛出，供判定与诊断订阅。</summary>
        public event Action<TouchEvent> OnTrigger;

        /// <summary>本次会话是否拿到了真实 OS 时间戳。写入 CSV 表头。</summary>
        public bool UsingOsTimestamp { get; private set; }

        private void Update()
        {
            _frameEvents.Clear();

            CollectTouches();
            CollectMouse();

            for (int i = 0; i < _frameEvents.Count; i++)
            {
                OnTrigger?.Invoke(_frameEvents[i]);
            }
        }

        private void CollectTouches()
        {
            int count = Input.touchCount;
            for (int i = 0; i < count; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.phase != TouchPhase.Began)
                {
                    continue;
                }

                Emit(touch.position, touch.fingerId);
            }
        }

        private void CollectMouse()
        {
            if (!Application.isEditor && Application.platform != RuntimePlatform.WindowsPlayer)
            {
                return;
            }

            // Editor 映射：左键 = 右拇指击发区，右键 = 左拇指区。
            // 之所以不按鼠标位置分区，是因为鼠标位置同时用于控制姿态。
            if (Input.GetMouseButtonDown(0))
            {
                Emit(new Vector2(Screen.width * 0.9f, Screen.height * 0.5f), -1);
            }

            if (Input.GetMouseButtonDown(1))
            {
                Emit(new Vector2(Screen.width * 0.1f, Screen.height * 0.5f), -2);
            }
        }

        private void Emit(Vector2 screenPosition, int fingerId)
        {
            _frameEvents.Add(new TouchEvent
            {
                Timestamp = AppClock.Now,
                IsOsTimestamp = false,
                ScreenPosition = screenPosition,
                Zone = ClassifyZone(screenPosition),
                FingerId = fingerId
            });
        }

        private TriggerZone ClassifyZone(Vector2 screenPosition)
        {
            float normalizedX = screenPosition.x / Mathf.Max(1, Screen.width);

            if (normalizedX <= _sideZoneWidth)
            {
                return TriggerZone.LeftTrigger;
            }

            if (normalizedX >= 1f - _sideZoneWidth)
            {
                return TriggerZone.RightTrigger;
            }

            return TriggerZone.Center;
        }
    }
}
