using System;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VoiceDuck
{
    internal sealed class MusicShareStartOptions
    {
        public string MicrophoneEndpointId { get; set; }
        public string MonitorEndpointId { get; set; }
        public float MicrophoneGain { get; set; }
        public float MusicGain { get; set; }
    }

    internal sealed class MusicShareStatus
    {
        public bool Running { get; set; }
        public bool Paused { get; set; }
        public bool RemoteAudioBlocked { get; set; }
        public string SourceName { get; set; }
        public string LastError { get; set; }
        public float MicrophonePeak { get; set; }
        public float MusicPeak { get; set; }
        public int DelayMilliseconds { get; set; }
    }

    internal sealed class MusicShareAudioEngine : IDisposable
    {
        internal const int PlaybackDelayMilliseconds = 350;
        internal const int EchoProtectionHoldMilliseconds = 700;
        internal const float RemoteSpeechThresholdDb = -58.0f;

        private static readonly WaveFormat TargetFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        private readonly object _sync = new object();
        private readonly Func<CallAudioActivity> _callActivityProvider;

        private MMDevice _microphoneDevice;
        private MMDevice _monitorDevice;
        private MMDevice _cableRenderDevice;
        private WasapiCapture _microphoneCapture;
        private WasapiLoopbackCapture _playbackCapture;
        private BufferedWaveProvider _microphoneBuffer;
        private BufferedWaveProvider _playbackBuffer;
        private MediaFoundationResampler _microphoneResampler;
        private MediaFoundationResampler _playbackResampler;
        private WasapiOut _cableOutput;
        private LiveAudioGateSampleProvider _playbackGate;
        private GainSampleProvider _microphoneGain;
        private GainSampleProvider _playbackGain;
        private PeakSampleProvider _microphoneMeter;
        private PeakSampleProvider _playbackMeter;
        private Thread _echoProtectionThread;
        private volatile bool _echoProtectionStopRequested;
        private volatile bool _remoteAudioBlocked;
        private long _remoteBlockUntilTicks;
        private bool _running;
        private bool _paused;
        private bool _stopping;
        private string _sourceName = String.Empty;
        private string _lastError = String.Empty;

        public MusicShareAudioEngine(Func<CallAudioActivity> callActivityProvider = null)
        {
            _callActivityProvider = callActivityProvider;
        }

        public void Start(MusicShareStartOptions options)
        {
            if (options == null) throw new ArgumentNullException("options");
            if (_callActivityProvider == null)
                throw new InvalidOperationException("通话声音检测器尚未就绪，无法安全启动实时分享。");

            lock (_sync)
            {
                StopInternal();
                _stopping = false;
                _lastError = String.Empty;
                _paused = false;
                _remoteAudioBlocked = false;
                Interlocked.Exchange(ref _remoteBlockUntilTicks, 0L);
                try
                {
                    VirtualCableStatus cable = AudioEndpointCatalog.GetVirtualCableStatus();
                    if (!cable.Ready) throw new InvalidOperationException(cable.Message);

                    _microphoneDevice = AudioEndpointCatalog.OpenDevice(options.MicrophoneEndpointId);
                    _monitorDevice = AudioEndpointCatalog.OpenDevice(options.MonitorEndpointId);
                    _cableRenderDevice = AudioEndpointCatalog.OpenDevice(cable.RenderId);
                    if (MusicShareCore.IsVirtualCableName(_microphoneDevice.FriendlyName) ||
                        MusicShareCore.IsVirtualCableName(_monitorDevice.FriendlyName))
                        throw new InvalidOperationException("麦克风和正在播放的设备必须选择真实硬件，不能选择虚拟线缆。");

                    _sourceName = _monitorDevice.FriendlyName ?? "当前播放设备";

                    _microphoneCapture = new WasapiCapture(_microphoneDevice, true, 60);
                    _microphoneBuffer = CreateCaptureBuffer(_microphoneCapture.WaveFormat, 900);
                    _microphoneCapture.DataAvailable += MicrophoneDataAvailable;
                    _microphoneCapture.RecordingStopped += MicrophoneRecordingStopped;
                    _microphoneResampler = new MediaFoundationResampler(_microphoneBuffer, TargetFormat)
                    {
                        ResamplerQuality = 60
                    };

                    _playbackCapture = new WasapiLoopbackCapture(_monitorDevice);
                    _playbackBuffer = CreateCaptureBuffer(_playbackCapture.WaveFormat, 2000);
                    _playbackCapture.DataAvailable += PlaybackDataAvailable;
                    _playbackCapture.RecordingStopped += PlaybackRecordingStopped;
                    _playbackResampler = new MediaFoundationResampler(_playbackBuffer, TargetFormat)
                    {
                        ResamplerQuality = 60
                    };

                    var delayedPlayback = new InitialDelaySampleProvider(
                        _playbackResampler.ToSampleProvider(),
                        PlaybackDelayMilliseconds);
                    _playbackGate = new LiveAudioGateSampleProvider(delayedPlayback);
                    _microphoneGain = new GainSampleProvider(
                        _microphoneResampler.ToSampleProvider(),
                        options.MicrophoneGain);
                    _playbackGain = new GainSampleProvider(_playbackGate, options.MusicGain);
                    _microphoneMeter = new PeakSampleProvider(_microphoneGain);
                    _playbackMeter = new PeakSampleProvider(_playbackGain);

                    var outgoingMixer = new MixingSampleProvider(TargetFormat) { ReadFully = true };
                    outgoingMixer.AddMixerInput(_microphoneMeter);
                    outgoingMixer.AddMixerInput(_playbackMeter);
                    var limiter = new HardLimiterSampleProvider(outgoingMixer, 0.96f);

                    _cableOutput = new WasapiOut(
                        _cableRenderDevice,
                        AudioClientShareMode.Shared,
                        true,
                        60);
                    _cableOutput.PlaybackStopped += CablePlaybackStopped;
                    _cableOutput.Init(limiter.ToWaveProvider());

                    _microphoneCapture.StartRecording();
                    _playbackCapture.StartRecording();
                    StartEchoProtection();
                    _cableOutput.Play();
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
                if (!_running) return;
                _paused = !_paused;
                if (_playbackGate != null) _playbackGate.UserPaused = _paused;
            }
        }

        public void UpdateGains(float microphoneGain, float musicGain)
        {
            lock (_sync)
            {
                if (_microphoneGain != null) _microphoneGain.Gain = microphoneGain;
                if (_playbackGain != null) _playbackGain.Gain = musicGain;
            }
        }

        public MusicShareStatus GetStatus()
        {
            lock (_sync)
            {
                return new MusicShareStatus
                {
                    Running = _running,
                    Paused = _paused,
                    RemoteAudioBlocked = _remoteAudioBlocked,
                    SourceName = _sourceName,
                    LastError = _lastError,
                    MicrophonePeak = _microphoneMeter == null ? 0.0f : _microphoneMeter.Peak,
                    MusicPeak = _playbackMeter == null ? 0.0f : _playbackMeter.Peak,
                    DelayMilliseconds = PlaybackDelayMilliseconds
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

        private static BufferedWaveProvider CreateCaptureBuffer(WaveFormat format, int milliseconds)
        {
            return new BufferedWaveProvider(format)
            {
                BufferDuration = TimeSpan.FromMilliseconds(milliseconds),
                DiscardOnBufferOverflow = true,
                ReadFully = true
            };
        }

        private void StartEchoProtection()
        {
            _echoProtectionStopRequested = false;
            _echoProtectionThread = new Thread(EchoProtectionWorker);
            _echoProtectionThread.Name = "VoiceDuck Echo Protection";
            _echoProtectionThread.IsBackground = true;
            _echoProtectionThread.Start();
        }

        private void EchoProtectionWorker()
        {
            while (!_echoProtectionStopRequested)
            {
                long now = DateTime.UtcNow.Ticks;
                try
                {
                    CallAudioActivity activity = _callActivityProvider();
                    if (activity != null &&
                        !String.IsNullOrWhiteSpace(activity.ProcessName) &&
                        activity.PeakDb >= RemoteSpeechThresholdDb)
                    {
                        long holdUntil = now + TimeSpan.FromMilliseconds(
                            PlaybackDelayMilliseconds + EchoProtectionHoldMilliseconds).Ticks;
                        Interlocked.Exchange(ref _remoteBlockUntilTicks, holdUntil);
                    }
                }
                catch
                {
                    // Fail closed for one protection window: a detector failure must
                    // never send the remote party's voice back into the call.
                    long holdUntil = now + TimeSpan.FromMilliseconds(
                        PlaybackDelayMilliseconds + EchoProtectionHoldMilliseconds).Ticks;
                    Interlocked.Exchange(ref _remoteBlockUntilTicks, holdUntil);
                }

                bool blocked = now < Interlocked.Read(ref _remoteBlockUntilTicks);
                _remoteAudioBlocked = blocked;
                LiveAudioGateSampleProvider gate = _playbackGate;
                if (gate != null) gate.RemoteAudioBlocked = blocked;
                Thread.Sleep(25);
            }

            _remoteAudioBlocked = false;
            LiveAudioGateSampleProvider finalGate = _playbackGate;
            if (finalGate != null) finalGate.RemoteAudioBlocked = false;
        }

        private void MicrophoneDataAvailable(object sender, WaveInEventArgs eventArgs)
        {
            AddCaptureData(_microphoneBuffer, eventArgs, "麦克风");
        }

        private void PlaybackDataAvailable(object sender, WaveInEventArgs eventArgs)
        {
            AddCaptureData(_playbackBuffer, eventArgs, "播放设备");
        }

        private void AddCaptureData(BufferedWaveProvider buffer, WaveInEventArgs eventArgs, string source)
        {
            try
            {
                if (buffer != null && eventArgs.BytesRecorded > 0)
                    buffer.AddSamples(eventArgs.Buffer, 0, eventArgs.BytesRecorded);
            }
            catch (Exception exception)
            {
                lock (_sync) _lastError = source + "缓冲失败：" + exception.Message;
            }
        }

        private void MicrophoneRecordingStopped(object sender, StoppedEventArgs eventArgs)
        {
            SetCaptureError("麦克风已停止：", eventArgs);
        }

        private void PlaybackRecordingStopped(object sender, StoppedEventArgs eventArgs)
        {
            SetCaptureError("播放设备监听已停止：", eventArgs);
        }

        private void SetCaptureError(string prefix, StoppedEventArgs eventArgs)
        {
            if (eventArgs.Exception == null) return;
            lock (_sync)
            {
                if (!_stopping) _lastError = prefix + eventArgs.Exception.Message;
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
            _echoProtectionStopRequested = true;
            Thread echoThread = _echoProtectionThread;
            _echoProtectionThread = null;
            if (echoThread != null && echoThread != Thread.CurrentThread)
                echoThread.Join(1200);

            if (_microphoneCapture != null)
            {
                _microphoneCapture.DataAvailable -= MicrophoneDataAvailable;
                _microphoneCapture.RecordingStopped -= MicrophoneRecordingStopped;
                try { _microphoneCapture.StopRecording(); } catch { }
            }
            if (_playbackCapture != null)
            {
                _playbackCapture.DataAvailable -= PlaybackDataAvailable;
                _playbackCapture.RecordingStopped -= PlaybackRecordingStopped;
                try { _playbackCapture.StopRecording(); } catch { }
            }
            if (_cableOutput != null)
            {
                _cableOutput.PlaybackStopped -= CablePlaybackStopped;
                try { _cableOutput.Stop(); } catch { }
            }

            DisposeAndClear(ref _microphoneCapture);
            DisposeAndClear(ref _playbackCapture);
            DisposeAndClear(ref _cableOutput);
            DisposeAndClear(ref _microphoneResampler);
            DisposeAndClear(ref _playbackResampler);
            DisposeAndClear(ref _microphoneDevice);
            DisposeAndClear(ref _monitorDevice);
            DisposeAndClear(ref _cableRenderDevice);
            _microphoneBuffer = null;
            _playbackBuffer = null;
            _playbackGate = null;
            _microphoneGain = null;
            _playbackGain = null;
            _microphoneMeter = null;
            _playbackMeter = null;
            _remoteAudioBlocked = false;
            Interlocked.Exchange(ref _remoteBlockUntilTicks, 0L);
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

    internal sealed class InitialDelaySampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private long _remainingSamples;

        public InitialDelaySampleProvider(ISampleProvider source, int delayMilliseconds)
        {
            if (source == null) throw new ArgumentNullException("source");
            _source = source;
            int safeDelay = Math.Max(0, delayMilliseconds);
            _remainingSamples = (long)source.WaveFormat.SampleRate *
                                source.WaveFormat.Channels *
                                safeDelay / 1000L;
        }

        public WaveFormat WaveFormat { get { return _source.WaveFormat; } }

        public int Read(float[] buffer, int offset, int count)
        {
            int silentSamples = (int)Math.Min((long)count, _remainingSamples);
            if (silentSamples > 0)
            {
                Array.Clear(buffer, offset, silentSamples);
                _remainingSamples -= silentSamples;
            }
            if (silentSamples == count) return count;
            int read = _source.Read(buffer, offset + silentSamples, count - silentSamples);
            return silentSamples + read;
        }
    }

    internal sealed class LiveAudioGateSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private volatile bool _userPaused;
        private volatile bool _remoteAudioBlocked;

        public LiveAudioGateSampleProvider(ISampleProvider source)
        {
            if (source == null) throw new ArgumentNullException("source");
            _source = source;
        }

        public WaveFormat WaveFormat { get { return _source.WaveFormat; } }
        public bool UserPaused { get { return _userPaused; } set { _userPaused = value; } }
        public bool RemoteAudioBlocked
        {
            get { return _remoteAudioBlocked; }
            set { _remoteAudioBlocked = value; }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            if (_userPaused || _remoteAudioBlocked) Array.Clear(buffer, offset, read);
            return read;
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
