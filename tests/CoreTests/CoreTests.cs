using System;
using System.Collections.Generic;

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
}
