using NUnit.Framework;
using WingingIt.Settings;

namespace WingingIt.Tests
{
    public class GameSettingsTests
    {
        sealed class MemoryStore : ISettingsStore
        {
            public string Json;
            public int Saves;
            public string Load() => Json;
            public void Save(string json)
            {
                Json = json;
                Saves++;
            }
        }

        [Test]
        public void Defaults_DependOnThePlatform()
        {
            var pc = new GameSettings(false, new MemoryStore()).Data;
            var phone = new GameSettings(true, new MemoryStore()).Data;
            Assert.AreEqual(144, pc.frameRateCap);
            Assert.AreEqual(1f, pc.renderScale);
            Assert.AreEqual(60, phone.frameRateCap);
            Assert.AreEqual(0.8f, phone.renderScale, 1e-5f);
            Assert.AreEqual(0.8f, pc.masterVolume, 1e-5f);
            Assert.AreEqual(60f, pc.fieldOfView);
            Assert.IsTrue(pc.mouseAim);
            Assert.AreEqual(TouchControlsMode.Auto, pc.touchControls);
            Assert.IsTrue(pc.hdr);
            Assert.IsFalse(phone.hdr, "HDR costs phones bandwidth for little gain");
            Assert.IsFalse(phone.showFps);
            Assert.AreEqual(1f, phone.touchSensitivity);
        }

        [Test]
        public void Shadows_HighOnPc_MediumOnPhones_AndAnUnknownLevelResets()
        {
            Assert.AreEqual(ShadowDetail.High, new GameSettings(false, new MemoryStore()).Data.shadows);
            var phone = new GameSettings(true, new MemoryStore());
            Assert.AreEqual(ShadowDetail.Medium, phone.Data.shadows);
            phone.Set(s => s.shadows = (ShadowDetail)42);
            Assert.AreEqual(ShadowDetail.High, phone.Data.shadows);
            phone.Set(s => s.shadows = ShadowDetail.Off);
            phone.ResetTab(SettingsTab.Graphics);
            Assert.AreEqual(ShadowDetail.Medium, phone.Data.shadows);
        }

        [Test]
        public void ShadowLevels_ReachFurtherAndFilterMore_AsTheyGoUp()
        {
            Assert.IsFalse(ShadowProfile.For(ShadowDetail.Off).Enabled);
            var low = ShadowProfile.For(ShadowDetail.Low);
            var medium = ShadowProfile.For(ShadowDetail.Medium);
            var high = ShadowProfile.For(ShadowDetail.High);
            Assert.IsTrue(low.Enabled && medium.Enabled && high.Enabled);
            Assert.Less(low.Distance, medium.Distance);
            Assert.Less(medium.Distance, high.Distance);
            Assert.LessOrEqual(low.Resolution, medium.Resolution);
            Assert.LessOrEqual(medium.Resolution, high.Resolution);
            Assert.Less(low.Cascades, medium.Cascades);
            Assert.Less(medium.Cascades, high.Cascades);
            Assert.Less(low.SoftQuality, medium.SoftQuality);
            Assert.Less(medium.SoftQuality, high.SoftQuality);
            Assert.That(high.Splits.x, Is.LessThan(high.Splits.y).And.GreaterThan(0f));
            Assert.That(high.Splits.y, Is.LessThan(high.Splits.z));
            Assert.That(high.Splits.z, Is.LessThan(1f));
        }

        [Test]
        public void Phones_Offer90Fps_AndTouchSensitivityClamps()
        {
            var phone = new GameSettings(true, new MemoryStore());
            phone.Set(s => s.frameRateCap = 90);
            Assert.AreEqual(90, phone.Data.frameRateCap);
            phone.Set(s => s.touchSensitivity = 10f);
            Assert.AreEqual(GameSettings.MaxSensitivity, phone.Data.touchSensitivity);
        }

        [Test]
        public void SetFollowPointer_OnPhone_HidesTouchControlsAndAutoThrottles_OffBringsControlsBack()
        {
            var phone = new GameSettings(true, new MemoryStore());
            phone.Set(s => s.autoThrottle = false);
            phone.Set(s => s.SetFollowPointer(true, true));
            Assert.IsTrue(phone.Data.followPointer);
            Assert.IsTrue(phone.Data.touchGesturesOnly);
            Assert.IsTrue(phone.Data.autoThrottle);

            phone.Set(s => s.autoThrottle = false);
            phone.Set(s => s.SetFollowPointer(false, true));
            Assert.IsFalse(phone.Data.followPointer);
            Assert.IsFalse(phone.Data.touchGesturesOnly);
            Assert.IsFalse(phone.Data.autoThrottle, "leaving follow keeps the throttle setting");
        }

        [Test]
        public void SetFollowPointer_OnDesktop_LeavesTouchSettingsAlone()
        {
            var desktop = new GameSettings(false, new MemoryStore());
            desktop.Set(s => s.autoThrottle = false);
            desktop.Set(s => s.SetFollowPointer(true, false));
            Assert.IsTrue(desktop.Data.followPointer);
            Assert.IsFalse(desktop.Data.touchGesturesOnly);
            Assert.IsFalse(desktop.Data.autoThrottle);
        }

        [Test]
        public void Set_ClampsSavesAndRaisesChanged()
        {
            var store = new MemoryStore();
            var settings = new GameSettings(false, store);
            int changed = 0;
            settings.Changed += () => changed++;
            settings.Set(s =>
            {
                s.mouseAimSensitivity = 10f;
                s.fieldOfView = 20f;
                s.masterVolume = -1f;
                s.hudScale = 2f;
                s.renderScale = 0.1f;
            });
            Assert.AreEqual(1, changed);
            Assert.AreEqual(1, store.Saves);
            Assert.AreEqual(GameSettings.MaxSensitivity, settings.Data.mouseAimSensitivity);
            Assert.AreEqual(GameSettings.MinFov, settings.Data.fieldOfView);
            Assert.AreEqual(0f, settings.Data.masterVolume);
            Assert.AreEqual(GameSettings.MaxHudScale, settings.Data.hudScale);
            Assert.AreEqual(GameSettings.MinRenderScale, settings.Data.renderScale);
        }

        [Test]
        public void SaveAndLoad_RoundTrips()
        {
            var store = new MemoryStore();
            new GameSettings(false, store).Set(s =>
            {
                s.invertPitch = true;
                s.musicVolume = 0.25f;
                s.teamColors = TeamColorMode.HighContrast;
                s.windowMode = WindowMode.Borderless;
            });
            var loaded = new GameSettings(false, store).Data;
            Assert.IsTrue(loaded.invertPitch);
            Assert.AreEqual(0.25f, loaded.musicVolume, 1e-5f);
            Assert.AreEqual(TeamColorMode.HighContrast, loaded.teamColors);
            Assert.AreEqual(WindowMode.Borderless, loaded.windowMode);
        }

        [Test]
        public void ControlStyle_DefaultsToAutoLevel_RoundTrips_AndRejectsGarbage()
        {
            var store = new MemoryStore();
            Assert.AreEqual(WingingIt.Flight.ControlStyle.AutoLevel, new GameSettings(false, store).Data.controlStyle);
            var settings = new GameSettings(false, store);
            settings.Set(s => s.controlStyle = WingingIt.Flight.ControlStyle.Bank);
            Assert.AreEqual(WingingIt.Flight.ControlStyle.Bank, new GameSettings(false, store).Data.controlStyle);
            settings.Set(s => s.controlStyle = (WingingIt.Flight.ControlStyle)42);
            Assert.AreEqual(WingingIt.Flight.ControlStyle.AutoLevel, settings.Data.controlStyle);
        }

        [Test]
        public void Load_KeepsDefaultsForMissingFields_AndSurvivesGarbage()
        {
            var store = new MemoryStore { Json = "{\"invertPitch\":true}" };
            var phone = new GameSettings(true, store).Data;
            Assert.IsTrue(phone.invertPitch);
            Assert.AreEqual(60, phone.frameRateCap, "a missing field keeps the platform default");

            store.Json = "not json at all {";
            var pc = new GameSettings(false, store).Data;
            Assert.AreEqual(144, pc.frameRateCap);
        }

        [Test]
        public void FrameRate_SnapsToAnOfferedOne()
        {
            var pc = new GameSettings(false, new MemoryStore());
            pc.Set(s => s.frameRateCap = 100);
            Assert.AreEqual(120, pc.Data.frameRateCap);
            pc.Set(s => s.frameRateCap = 0);
            Assert.AreEqual(0, pc.Data.frameRateCap, "unlimited on PC");

            var phone = new GameSettings(true, new MemoryStore());
            phone.Set(s => s.frameRateCap = 0);
            Assert.AreEqual(120, phone.Data.frameRateCap, "phones have no unlimited: the top one");
            phone.Set(s => s.frameRateCap = 144);
            Assert.AreEqual(120, phone.Data.frameRateCap);
        }

        [Test]
        public void ResetTab_OnlyResetsThatTab()
        {
            var settings = new GameSettings(true, new MemoryStore());
            settings.Set(s =>
            {
                s.invertPitch = true;
                s.renderScale = 0.5f;
                s.masterVolume = 0.1f;
                s.reduceFlashing = true;
                s.minimapHeadingUp = true;
            });
            settings.ResetTab(SettingsTab.Graphics);
            Assert.AreEqual(0.8f, settings.Data.renderScale, 1e-5f, "back to the phone default");
            Assert.IsTrue(settings.Data.invertPitch);
            Assert.AreEqual(0.1f, settings.Data.masterVolume, 1e-5f);
            Assert.IsTrue(settings.Data.reduceFlashing);

            settings.ResetTab(SettingsTab.Controls);
            settings.ResetTab(SettingsTab.Audio);
            settings.ResetTab(SettingsTab.Hud);
            Assert.IsFalse(settings.Data.invertPitch);
            Assert.AreEqual(0.8f, settings.Data.masterVolume, 1e-5f);
            Assert.IsFalse(settings.Data.reduceFlashing);
            Assert.IsFalse(settings.Data.minimapHeadingUp, "north up by default");
        }
    }
}
