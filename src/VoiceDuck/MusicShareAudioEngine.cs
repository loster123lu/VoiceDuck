using System;
using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VoiceDuck
{
    internal sealed class MusicShareStartOptions
    {
        public string MicrophoneEndpointId { get; set; }
        public string MonitorEndpointId { get; set; }
        public string MusicFilePath { get; set; }
        public float MicrophoneGain { get; set; }
        public float MusicGain { get; set; }
    }

    internal sealed class MusicShareStatus
    {
        public bool Running { get; set; }
        public bool Paused { get; set; }
        public bool TrackEnded { get; set; }
        public string TrackName { get; set; }
        public string LastError { get; set; }
        public float MicrophonePeak { get; set; }
        public float MusicPeak { get; set; }
        public TimeSpan Position { get; set; }
        public TimeSpan Duration { get; set; }
    }

    internal sealed class MusicShareAudioEngine : IDisposable
    {
        private static readonly WaveFormat TargetFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        private readonly object _sync = new object();

        private MMDevice _microphoneDevice;
        private MMDevice _monitorDevice;
        private MMDevice _cableRenderDevice;
        private WasapiCapture _microphoneCapture;
        private BufferedWaveProvider _microphoneBuffer;
        private WasapiOut _monitorOutput;
        private WasapiOut _cableOutput;
        private MediaFoundationResampler _microphoneResampler;
        private MediaFoundationReader _monitorReader;
        private MediaFoundationReader _shareReader;
        private MediaFoundationResampler _monitorResampler;
        private MediaFoundationResampler _shareResampler;
        private PauseSampleProvider _monitorGate;
        private PauseSampleProvider _shareGate;
        private GainSampleProvider _microphoneGain;
        private GainSampleProvider _monitorMusicGain;
        private GainSampleProvider _shareMusicGain;
        private PeakSampleProvider _microphoneMeter;
        private PeakSampleProvider _musicMeter;
        private bool _running;
        private bool _paused;
        private bool _trackEnded;
        private bool _stopping;
        private string _trackName = String.Empty;
        private string _lastError = String.Empty;

        public void Start(MusicShareStartOptions options)
        {
            if (options == null) throw new ArgumentNullException("options");
            if (!File.Exists(options.MusicFilePath)) throw new FileNotFoundException("找不到要分享的音乐文件。", options.MusicFilePath);
            if (!MusicShareCore.IsSupportedAudioFile(options.MusicFilePath))
                throw new NotSupportedException("暂不支持这个音频格式。请选择 MP3、WAV、M4A、AAC、WMA 或 FLAC。");

            lock (_sync)
            {
                StopInternal();
                _stopping = false;
                _lastError = String.Empty;
                _trackEnded = false;
                _paused = false;
                _trackName = Path.GetFileName(options.MusicFilePath);
                try
                {
                    VirtualCableStatus cable = AudioEndpointCatalog.GetVirtualCableStatus();
                    if (!cable.Ready) throw new InvalidOperationException(cable.Message);
                    _microphoneDevice = AudioEndpointCatalog.OpenDevice(options.MicrophoneEndpointId);
                    _monitorDevice = AudioEndpointCatalog.OpenDevice(options.MonitorEndpointId);
                    _cableRenderDevice = AudioEndpointCatalog.OpenDevice(cable.RenderId);
                    if (MusicShareCore.IsVirtualCableName(_microphoneDevice.FriendlyName) ||
                        MusicShareCore.IsVirtualCableName(_monitorDevice.FriendlyName))
                        throw new InvalidOperationException("麦克风和本地监听必须选择真实硬件设备，不能选择虚拟线缆。");

                    _microphoneCapture = new WasapiCapture(_microphoneDevice, true, 60);
                    _microphoneBuffer = new BufferedWaveProvider(_microphoneCapture.WaveFormat)
                    {
                        BufferDuration = TimeSpan.FromMilliseconds(600),
                        DiscardOnBufferOverflow = true,
                        ReadFully = true
                    };
                    _microphoneCapture.DataAvailable += MicrophoneDataAvailable;
                    _microphoneCapture.RecordingStopped += MicrophoneRecordingStopped;

                    // Capture in the hardware's native shared-mode format. Physical
                    // microphones are often mono, so normalize sample rate and channel
                    // count before mixing with the stereo music stream.
                    _microphoneResampler = new MediaFoundationResampler(_microphoneBuffer, TargetFormat)
                    {
                        ResamplerQuality = 60
                    };

                    _monitorReader = OpenReader(options.MusicFilePath);
                    _shareReader = OpenReader(options.MusicFilePath);
                    _monitorResampler = new MediaFoundationResampler(_monitorReader, TargetFormat) { ResamplerQuality = 60 };
                    _shareResampler = new MediaFoundationResampler(_shareReader, TargetFormat) { ResamplerQuality = 60 };

                    _monitorGate = new PauseSampleProvider(_monitorResampler.ToSampleProvider());
                    _shareGate = new PauseSampleProvider(_shareResampler.ToSampleProvider());
                    _monitorMusicGain = new GainSampleProvider(_monitorGate, options.MusicGain);
                    _shareMusicGain = new GainSampleProvider(_shareGate, options.MusicGain);
                    _microphoneGain = new GainSampleProvider(_microphoneResampler.ToSampleProvider(), options.MicrophoneGain);
                    _microphoneMeter = new PeakSampleProvider(_microphoneGain);
                    _musicMeter = new PeakSampleProvider(_shareMusicGain);

                    var outgoingMixer = new MixingSampleProvider(TargetFormat) { ReadFully = true };
                    outgoingMixer.AddMixerInput(_microphoneMeter);
                    outgoingMixer.AddMixerInput(_musicMeter);
                    var limiter = new HardLimiterSampleProvider(outgoingMixer, 0.96f);

                    _cableOutput = new WasapiOut(_cableRenderDevice, AudioClientShareMode.Shared, true, 60);
                    _cableOutput.PlaybackStopped += CablePlaybackStopped;
                    _cableOutput.Init(limiter.ToWaveProvider());

                    _monitorOutput = new WasapiOut(_monitorDevice, AudioClientShareMode.Shared, true, 60);
                    _monitorOutput.PlaybackStopped += MonitorPlaybackStopped;
                    _monitorOutput.Init(_monitorMusicGain.ToWaveProvider());

                    _microphoneCapture.StartRecording();
                    _cableOutput.Play();
                    _monitorOutput.Play();
                    _running = true;
                }
                catch
                {
                    StopInternal();
                    throw;
                }
            }
        }

        public void TogglePause()
        {
            lock (_sync)
            {
                if (!_running || _trackEnded) return;
                _paused = !_paused;
                _monitorGate.Paused = _paused;
                _shareGate.Paused = _paused;
                if (_paused) _monitorOutput.Pause();
                else _monitorOutput.Play();
            }
        }

        public void UpdateGains(float microphoneGain, float musicGain)
        {
            lock (_sync)
            {
                if (_microphoneGain != null) _microphoneGain.Gain = microphoneGain;
                if (_monitorMusicGain != null) _monitorMusicGain.Gain = musicGain;
                if (_shareMusicGain != null) _shareMusicGain.Gain = musicGain;
            }
        }

        public MusicShareStatus GetStatus()
        {
            lock (_sync)
            {
                TimeSpan position = TimeSpan.Zero;
                TimeSpan duration = TimeSpan.Zero;
                try
                {
                    if (_monitorReader != null)
                    {
                        position = _monitorReader.CurrentTime;
                        duration = _monitorReader.TotalTime;
                    }
                }
                catch { }
                return new MusicShareStatus
                {
                    Running = _running,
                    Paused = _paused,
                    TrackEnded = _trackEnded,
                    TrackName = _trackName,
                    LastError = _lastError,
                    MicrophonePeak = _microphoneMeter == null ? 0.0f : _microphoneMeter.Peak,
                    MusicPeak = _musicMeter == null ? 0.0f : _musicMeter.Peak,
                    Position = position,
                    Duration = duration
                };
            }
        }

        public void Stop()
        {
            lock (_sync) StopInternal();
        }

        public void Dispose()
        {
            Stop();
        }

        private static MediaFoundationReader OpenReader(string path)
        {
            var settings = new MediaFoundationReader.MediaFoundationReaderSettings
            {
                RequestFloatOutput = true,
                RepositionInRead = false,
                SingleReaderObject = true
            };
            return new MediaFoundationReader(path, settings);
        }

        private void MicrophoneDataAvailable(object sender, WaveInEventArgs eventArgs)
        {
            try
            {
                BufferedWaveProvider buffer = _microphoneBuffer;
                if (buffer != null && eventArgs.BytesRecorded > 0)
                    buffer.AddSamples(eventArgs.Buffer, 0, eventArgs.BytesRecorded);
            }
            catch (Exception exception)
            {
                lock (_sync) _lastError = "麦克风缓冲失败：" + exception.Message;
            }
        }

        private void MicrophoneRecordingStopped(object sender, StoppedEventArgs eventArgs)
        {
            if (eventArgs.Exception == null) return;
            lock (_sync)
            {
                if (!_stopping) _lastError = "麦克风已停止：" + eventArgs.Exception.Message;
            }
        }

        private void MonitorPlaybackStopped(object sender, StoppedEventArgs eventArgs)
        {
            lock (_sync)
            {
                if (_stopping) return;
                if (eventArgs.Exception != null)
                {
                    _lastError = "本地音乐播放停止：" + eventArgs.Exception.Message;
                    return;
                }
                _trackEnded = true;
                _paused = true;
                if (_shareGate != null) _shareGate.Paused = true;
            }
        }

        private void CablePlaybackStopped(object sender, StoppedEventArgs eventArgs)
        {
            if (eventArgs.Exception == null) return;
            lock (_sync)
            {
                if (!_stopping) _lastError = "虚拟麦克风输出停止：" + eventArgs.Exception.Message;
            }
        }

        private void StopInternal()
        {
            _stopping = true;
            _running = false;
            _paused = false;
            _trackEnded = false;

            if (_microphoneCapture != null)
            {
                _microphoneCapture.DataAvailable -= MicrophoneDataAvailable;
                _microphoneCapture.RecordingStopped -= MicrophoneRecordingStopped;
                try { _microphoneCapture.StopRecording(); } catch { }
            }
            if (_monitorOutput != null)
            {
                _monitorOutput.PlaybackStopped -= MonitorPlaybackStopped;
                try { _monitorOutput.Stop(); } catch { }
            }
            if (_cableOutput != null)
            {
                _cableOutput.PlaybackStopped -= CablePlaybackStopped;
                try { _cableOutput.Stop(); } catch { }
            }

            DisposeAndClear(ref _microphoneCapture);
            DisposeAndClear(ref _monitorOutput);
            DisposeAndClear(ref _cableOutput);
            DisposeAndClear(ref _microphoneResampler);
            DisposeAndClear(ref _monitorResampler);
            DisposeAndClear(ref _shareResampler);
            DisposeAndClear(ref _monitorReader);
            DisposeAndClear(ref _shareReader);
            DisposeAndClear(ref _microphoneDevice);
            DisposeAndClear(ref _monitorDevice);
            DisposeAndClear(ref _cableRenderDevice);
            _microphoneBuffer = null;
            _monitorGate = null;
            _shareGate = null;
            _microphoneGain = null;
            _monitorMusicGain = null;
            _shareMusicGain = null;
            _microphoneMeter = null;
            _musicMeter = null;
        }

        private static void DisposeAndClear<T>(ref T value) where T : class, IDisposable
        {
            T disposable = value;
            value = null;
            if (disposable == null) return;
            try { disposable.Dispose(); } catch { }
        }
    }

    internal sealed class GainSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private volatile float _gain;

        public GainSampleProvider(ISampleProvider source, float gain)
        {
            if (source == null) throw new ArgumentNullException("source");
            _source = source;
            Gain = gain;
        }

        public WaveFormat WaveFormat { get { return _source.WaveFormat; } }
        public float Gain
        {
            get { return _gain; }
            set { _gain = MusicShareCore.ClampGain(value); }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            float gain = _gain;
            for (int index = 0; index < read; index++) buffer[offset + index] *= gain;
            return read;
        }
    }

    internal sealed class PauseSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private volatile bool _paused;

        public PauseSampleProvider(ISampleProvider source)
        {
            if (source == null) throw new ArgumentNullException("source");
            _source = source;
        }

        public WaveFormat WaveFormat { get { return _source.WaveFormat; } }
        public bool Paused { get { return _paused; } set { _paused = value; } }

        public int Read(float[] buffer, int offset, int count)
        {
            if (!_paused) return _source.Read(buffer, offset, count);
            Array.Clear(buffer, offset, count);
            return count;
        }
    }

    internal sealed class PeakSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private volatile float _peak;

        public PeakSampleProvider(ISampleProvider source)
        {
            if (source == null) throw new ArgumentNullException("source");
            _source = source;
        }

        public WaveFormat WaveFormat { get { return _source.WaveFormat; } }
        public float Peak { get { return _peak; } }

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            float peak = 0.0f;
            for (int index = 0; index < read; index++)
                peak = Math.Max(peak, Math.Abs(buffer[offset + index]));
            _peak = peak;
            return read;
        }
    }

    internal sealed class HardLimiterSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly float _ceiling;

        public HardLimiterSampleProvider(ISampleProvider source, float ceiling)
        {
            if (source == null) throw new ArgumentNullException("source");
            _source = source;
            _ceiling = Math.Max(0.1f, Math.Min(1.0f, ceiling));
        }

        public WaveFormat WaveFormat { get { return _source.WaveFormat; } }

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            for (int index = 0; index < read; index++)
            {
                float sample = buffer[offset + index];
                if (sample > _ceiling) sample = _ceiling;
                else if (sample < -_ceiling) sample = -_ceiling;
                buffer[offset + index] = sample;
            }
            return read;
        }
    }
}
