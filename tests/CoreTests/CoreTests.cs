using System;
using System.Collections.Generic;
using NAudio.Wave;

namespace VoiceDuck
{
    internal sealed class FakeSession : IDuckableSession
    {
        public string Id { get; set; }
        public string ProcessName { get; set; }
        public bool IsSystemSounds { get; set; }
        public float Peak { get; set; }
        public float Volume { get; set; }
        public float ReadPeak() { return Peak; }
        public float ReadVolume() { return Volume; }
        public void WriteVolume(float volume) { Volume = volume; }
    }

    internal static class CoreTests
    {
        private static int _passed;

        private static void Main()
        {
            TestNotificationPulseIsRejected();
            TestSpeechDucksOnlySelectedTarget();
            TestHoldBridgesShortPause();
            TestDisableRestoresExactBaseline();
            TestMultipleSessionsOfSameProcessAreControlled();
            TestMusicShareLoopbackNames();
            TestMusicShareRoutingHelpers();
            TestShareSettingsMigration();
            TestShareSampleProviders();
            Console.WriteLine("PASS " + _passed + " core tests");
        }

        private static AppSettings Settings()
        {
            var settings = AppSettings.CreateDefault();
            settings.Enabled = true;
            settings.ThresholdDb = -40;
            settings.TriggerDelayMs = 200;
            settings.HoldMs = 200;
            settings.AttackMs = 100;
            settings.ReleaseMs = 200;
            settings.DuckRatio = 0.25f;
            settings.TriggerApps = new List<string> { "wechat" };
            settings.TargetApps = new List<string> { "spotify" };
            return settings;
        }

        private static void TestNotificationPulseIsRejected()
        {
            var trigger = new FakeSession { Id = "t", ProcessName = "WeChat.exe", Peak = 0.2f, Volume = 1.0f };
            var music = new FakeSession { Id = "m", ProcessName = "Spotify.exe", Volume = 0.8f };
            var sessions = new List<IDuckableSession> { trigger, music };
            var engine = new DuckingCoordinator();
            AppSettings settings = Settings();

            engine.Tick(sessions, settings, 50);
            engine.Tick(sessions, settings, 50);
            trigger.Peak = 0;
            engine.Tick(sessions, settings, 50);

            Assert(!engine.IsDucking, "short notification must not open gate");
            AssertNear(0.8f, music.Volume, 0.001f, "short notification must not change music");
        }

        private static void TestSpeechDucksOnlySelectedTarget()
        {
            var trigger = new FakeSession { Id = "t", ProcessName = "wechat", Peak = 0.2f, Volume = 1.0f };
            var music = new FakeSession { Id = "m", ProcessName = "spotify", Volume = 0.8f };
            var browser = new FakeSession { Id = "b", ProcessName = "chrome", Volume = 0.7f };
            var sessions = new List<IDuckableSession> { trigger, music, browser };
            var engine = new DuckingCoordinator();
            AppSettings settings = Settings();

            for (int i = 0; i < 8; i++) engine.Tick(sessions, settings, 50);

            Assert(engine.IsDucking, "continuous speech must open gate");
            Assert(music.Volume < 0.35f, "selected music must be ducked");
            AssertNear(0.7f, browser.Volume, 0.001f, "unselected browser must be untouched");
        }

        private static void TestHoldBridgesShortPause()
        {
            var trigger = new FakeSession { Id = "t", ProcessName = "wechat", Peak = 0.2f, Volume = 1.0f };
            var music = new FakeSession { Id = "m", ProcessName = "spotify", Volume = 0.8f };
            var sessions = new List<IDuckableSession> { trigger, music };
            var engine = new DuckingCoordinator();
            AppSettings settings = Settings();

            for (int i = 0; i < 4; i++) engine.Tick(sessions, settings, 50);
            trigger.Peak = 0;
            for (int i = 0; i < 3; i++) engine.Tick(sessions, settings, 50);
            Assert(engine.IsDucking, "hold must bridge short gaps between words");
            engine.Tick(sessions, settings, 50);
            Assert(!engine.IsDucking, "gate must close after hold expires");
        }

        private static void TestDisableRestoresExactBaseline()
        {
            var trigger = new FakeSession { Id = "t", ProcessName = "wechat", Peak = 0.2f, Volume = 1.0f };
            var music = new FakeSession { Id = "m", ProcessName = "spotify", Volume = 0.63f };
            var sessions = new List<IDuckableSession> { trigger, music };
            var engine = new DuckingCoordinator();
            AppSettings settings = Settings();

            for (int i = 0; i < 8; i++) engine.Tick(sessions, settings, 50);
            settings.Enabled = false;
            engine.Tick(sessions, settings, 50);
            AssertNear(0.63f, music.Volume, 0.001f, "disabling must restore exact baseline");
        }

        private static void TestMultipleSessionsOfSameProcessAreControlled()
        {
            var trigger = new FakeSession { Id = "t", ProcessName = "wechat", Peak = 0.2f, Volume = 1.0f };
            var musicOne = new FakeSession { Id = "m1", ProcessName = "spotify", Volume = 0.8f };
            var musicTwo = new FakeSession { Id = "m2", ProcessName = "spotify", Volume = 0.5f };
            var sessions = new List<IDuckableSession> { trigger, musicOne, musicTwo };
            var engine = new DuckingCoordinator();
            AppSettings settings = Settings();

            for (int i = 0; i < 8; i++) engine.Tick(sessions, settings, 50);
            Assert(musicOne.Volume < 0.35f && musicTwo.Volume < 0.25f,
                "every audio session of one process must be controlled");
        }

        private static void TestMusicShareLoopbackNames()
        {
            Assert(MusicShareCore.IsLoopbackCaptureName("立体声混音 (Realtek Audio)"),
                "Chinese Stereo Mix name must be detected");
            Assert(MusicShareCore.IsLoopbackCaptureName("What U Hear"),
                "vendor loopback alias must be detected");
            Assert(!MusicShareCore.IsLoopbackCaptureName("Microphone Array"),
                "physical microphone must not be treated as loopback");
            Assert(MusicShareCore.IsVirtualCableName("CABLE Output (VB-Audio Virtual Cable)"),
                "VB-CABLE endpoint must be detected");
            Assert(MusicShareCore.IsVirtualCableName("VoiceMeeter Input"),
                "VoiceMeeter endpoint must be detected");
            Assert(!MusicShareCore.IsVirtualCableName("Realtek Speakers"),
                "physical output must not be treated as a virtual cable");
        }

        private static void TestMusicShareRoutingHelpers()
        {
            Assert(MusicShareCore.IsVbCableRenderName("CABLE Input (VB-Audio Virtual Cable)"),
                "VB-CABLE render endpoint must be recognized");
            Assert(MusicShareCore.IsVbCableCaptureName("CABLE Output (VB-Audio Virtual Cable)"),
                "VB-CABLE capture endpoint must be recognized");
            Assert(!MusicShareCore.IsVbCableCaptureName("VoiceMeeter Output"),
                "other virtual endpoints must not be mistaken for the managed cable");
            Assert(MusicShareCore.IsSupportedAudioFile(@"C:\Music\track.MP3"),
                "MP3 extension matching must be case insensitive");
            Assert(!MusicShareCore.IsSupportedAudioFile(@"C:\Music\notes.txt"),
                "non-audio files must be rejected");
            AssertNear(1.5f, MusicShareCore.ClampGain(8.0f), 0.001f,
                "share gain must stay inside the safe range");

            var devices = new List<AudioEndpointChoice>
            {
                new AudioEndpointChoice { Id = "mic-a", Name = "Microphone", IsDefault = false },
                new AudioEndpointChoice { Id = "mic-b", Name = "Headset Mic", IsDefault = true }
            };
            Assert(MusicShareCore.FindPreferredEndpointIndex(devices, "mic-a") == 0,
                "saved endpoint ID must beat the current default");
            Assert(MusicShareCore.FindPreferredEndpointIndex(devices, String.Empty) == 1,
                "current default must be selected when no preference is saved");
            Assert(MusicShareCore.LooksLikeCallProcess("Weixin.exe", new List<string>()),
                "known call processes must activate the driver safety guard");
        }

        private static void TestShareSampleProviders()
        {
            var gain = new GainSampleProvider(new ArraySampleProvider(0.8f, -0.8f), 0.5f);
            float[] buffer = new float[2];
            Assert(gain.Read(buffer, 0, buffer.Length) == 2 &&
                   Math.Abs(buffer[0] - 0.4f) < 0.001f && Math.Abs(buffer[1] + 0.4f) < 0.001f,
                "share gain provider must scale every sample");

            var pause = new PauseSampleProvider(new ArraySampleProvider(0.6f));
            pause.Paused = true;
            buffer = new float[1];
            Assert(pause.Read(buffer, 0, 1) == 1 && Math.Abs(buffer[0]) < 0.0001f,
                "paused music must emit silence while keeping the outgoing microphone alive");
            pause.Paused = false;
            Assert(pause.Read(buffer, 0, 1) == 1 && Math.Abs(buffer[0] - 0.6f) < 0.001f,
                "unpausing must continue from the same music position");

            var limiter = new HardLimiterSampleProvider(new ArraySampleProvider(2.0f, -2.0f), 0.96f);
            buffer = new float[2];
            limiter.Read(buffer, 0, 2);
            AssertNear(0.96f, buffer[0], 0.001f, "positive mixed peaks must be limited");
            AssertNear(-0.96f, buffer[1], 0.001f, "negative mixed peaks must be limited");
        }

        private static void TestShareSettingsMigration()
        {
            var oldSettings = new AppSettings
            {
                SettingsVersion = 0,
                TriggerApps = new List<string>(),
                TargetApps = new List<string>()
            };
            oldSettings.Normalize();
            Assert(oldSettings.SettingsVersion == 2,
                "old settings must be migrated to the current schema");
            AssertNear(0.65f, oldSettings.ShareMicrophoneGain, 0.001f,
                "upgrading from 1.0 must not silently mute the shared microphone");
            AssertNear(0.55f, oldSettings.ShareMusicGain, 0.001f,
                "upgrading from 1.0 must initialize the music mix level");

            AppSettings current = AppSettings.CreateDefault();
            current.ShareMicrophoneGain = 0.0f;
            current.ShareMusicGain = 0.0f;
            current.Normalize();
            AssertNear(0.0f, current.ShareMicrophoneGain, 0.001f,
                "an explicit microphone mute must survive normalization");
            AssertNear(0.0f, current.ShareMusicGain, 0.001f,
                "an explicit music mute must survive normalization");
        }

        private static void Assert(bool value, string message)
        {
            if (!value) throw new Exception("FAIL: " + message);
            _passed++;
        }

        private static void AssertNear(float expected, float actual, float tolerance, string message)
        {
            Assert(Math.Abs(expected - actual) <= tolerance,
                message + " (expected " + expected + ", actual " + actual + ")");
        }
    }

    internal sealed class ArraySampleProvider : ISampleProvider
    {
        private readonly float[] _samples;
        private int _position;

        public ArraySampleProvider(params float[] samples)
        {
            _samples = samples;
        }

        public WaveFormat WaveFormat
        {
            get { return WaveFormat.CreateIeeeFloatWaveFormat(48000, 2); }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int available = Math.Min(count, _samples.Length - _position);
            Array.Copy(_samples, _position, buffer, offset, available);
            _position += available;
            return available;
        }
    }
}
