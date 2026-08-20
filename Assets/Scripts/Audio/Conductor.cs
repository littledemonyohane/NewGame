using UnityEngine;
using SpatialRhythm.Core;

namespace SpatialRhythm.Audio
{
    /// <summary>
    /// 音频 DSP 时钟 —— 全曲唯一主时钟（设计文档不可妥协原则 #3）。
    ///
    /// 所有音符时间、判定窗口、动画进度都从 <see cref="AudioSettings.dspTime"/> 派生，
    /// 绝不使用 Time.time 或帧计数，否则帧率抖动会直接污染节奏。
    ///
    /// 灰盒阶段没有版权曲目：用程序化生成的静音底轨驱动 dspTime，
    /// 拍点用程序化生成的节拍器点击音标记。
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class Conductor : MonoBehaviour
    {
        public static Conductor Instance { get; private set; }

        [Header("节奏")]
        [SerializeField] private float _bpm = 120f;

        [Tooltip("开始前的空拍，给玩家准备时间。")]
        [SerializeField] private float _leadInSeconds = 3f;

        [Header("节拍器")]
        [SerializeField] private bool _metronomeEnabled = true;
        [SerializeField] private float _metronomeVolume = 0.35f;

        private AudioSource _bedSource;
        private AudioSource _clickSource;
        private AudioClip _clickClip;

        private double _songStartDsp;
        private bool _running;
        private int _lastClickedBeat = -1;
        private bool _offsetInitialized;

        public float Bpm => _bpm;

        public bool IsRunning => _running;

        /// <summary>曲目位置（秒）。负值表示仍在 lead-in 阶段。</summary>
        public double SongPositionSeconds => AudioSettings.dspTime - _songStartDsp;

        /// <summary>曲目位置（拍）。</summary>
        public double SongPositionBeats => SongPositionSeconds * _bpm / 60d;

        public double SecondsPerBeat => 60d / _bpm;

        /// <summary>曲目零点在 dspTime 轴上的位置。</summary>
        public double SongStartDsp => _songStartDsp;

        /// <summary>把拍数换算到姿态/输入所用的 AppClock 时间轴。判定用的就是这个。</summary>
        public double BeatToAppTime(double beat)
        {
            return AppClock.ToAppTime(_songStartDsp + beat * SecondsPerBeat);
        }

        private void Awake()
        {
            Instance = this;
            BuildAudioSources();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Start()
        {
            StartSong();
        }

        public void StartSong()
        {
            _songStartDsp = AudioSettings.dspTime + _leadInSeconds;
            _lastClickedBeat = -1;
            _running = true;

            _bedSource.Stop();
            _bedSource.PlayScheduled(_songStartDsp);
        }

        public void Restart()
        {
            StartSong();
        }

        private void Update()
        {
            UpdateClockOffset();

            if (!_running || !_metronomeEnabled)
            {
                return;
            }

            double beats = SongPositionBeats;
            if (beats < 0d)
            {
                return;
            }

            int beat = (int)beats;
            if (beat != _lastClickedBeat)
            {
                _lastClickedBeat = beat;
                // 每 4 拍的第一拍加重，给玩家小节感。
                _clickSource.pitch = beat % 4 == 0 ? 1.5f : 1f;
                _clickSource.PlayOneShot(_clickClip, _metronomeVolume);
            }
        }

        /// <summary>
        /// 标定 dspTime 与姿态时间轴的偏移。
        ///
        /// dspTime 按音频缓冲块前进（而非按帧），直接相减会有一个缓冲区大小的锯齿，
        /// 所以用 EMA 平滑。这个偏移是"所见即所判"能跨时钟工作的前提。
        /// </summary>
        private void UpdateClockOffset()
        {
            double instant = AudioSettings.dspTime - AppClock.Now;

            if (!_offsetInitialized)
            {
                AppClock.DspOffset = instant;
                _offsetInitialized = true;
                return;
            }

            const double Alpha = 0.02d;
            AppClock.DspOffset += (instant - AppClock.DspOffset) * Alpha;
        }

        private void BuildAudioSources()
        {
            _bedSource = gameObject.AddComponent<AudioSource>();
            _bedSource.clip = CreateSilentClip(120f);
            _bedSource.loop = false;
            _bedSource.playOnAwake = false;
            _bedSource.volume = 0f;

            _clickSource = gameObject.AddComponent<AudioSource>();
            _clickSource.playOnAwake = false;
            _clickClip = CreateClickClip();
        }

        private static AudioClip CreateSilentClip(float seconds)
        {
            int sampleRate = AudioSettings.outputSampleRate;
            int samples = Mathf.CeilToInt(sampleRate * seconds);
            var clip = AudioClip.Create("SilentBed", samples, 1, sampleRate, false);
            clip.SetData(new float[samples], 0);
            return clip;
        }

        private static AudioClip CreateClickClip()
        {
            int sampleRate = AudioSettings.outputSampleRate;
            int samples = Mathf.CeilToInt(sampleRate * 0.03f);
            var data = new float[samples];

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sampleRate;
                float envelope = Mathf.Exp(-t * 180f);
                data[i] = Mathf.Sin(2f * Mathf.PI * 1600f * t) * envelope;
            }

            var clip = AudioClip.Create("MetronomeClick", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
