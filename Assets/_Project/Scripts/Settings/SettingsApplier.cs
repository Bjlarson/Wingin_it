using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using WingingIt.Flight;

namespace WingingIt.Settings
{
    /// <summary>
    /// Applies the game-wide <see cref="GameSettings"/> (quality, resolution, frame rate cap, render scale, HDR, shadows,
    /// trees, render distance, master volume, mute in background) at startup, on every change, and (shadows, trees,
    /// render distance) on every scene load. Created on its own before the first scene loads and
    /// kept across scenes. Per-object settings (controls, FOV, HUD) are read by the objects themselves.
    /// </summary>
    public sealed class SettingsApplier : MonoBehaviour
    {
        static SettingsApplier s_instance;
        bool _focused = true;
        UniversalRenderPipelineAsset _shadowAsset;
        ShadowSnapshot _originalShadows;
        // The quality level whose LOD bias Render Distance scales, and that bias as shipped (put back on leaving it).
        int _biasLevel = -1;
        float _baseLodBias;

        /// <summary>The URP asset's shadow fields as the project ships them, put back when play mode ends in the Editor.</summary>
        struct ShadowSnapshot
        {
            public int resolution, cascades;
            public float distance, split2;
            public Vector3 split4;

            public static ShadowSnapshot Of(UniversalRenderPipelineAsset a) => new()
            {
                resolution = a.mainLightShadowmapResolution, cascades = a.shadowCascadeCount, distance = a.shadowDistance,
                split2 = a.cascade2Split, split4 = a.cascade4Split,
            };

            public void ApplyTo(UniversalRenderPipelineAsset a)
            {
                a.mainLightShadowmapResolution = resolution;
                a.shadowCascadeCount = cascades;
                a.shadowDistance = distance;
                a.cascade2Split = split2;
                a.cascade4Split = split4;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Create()
        {
            if (s_instance != null)
                return;
            var go = new GameObject("Settings Applier") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<SettingsApplier>();
        }

        void OnEnable()
        {
            GameSettings.Current.Changed += Apply;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Apply();
        }

        void OnDisable()
        {
            if (GameSettings.Current != null)
                GameSettings.Current.Changed -= Apply;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            // The URP asset is a project file: the Editor would keep the player's shadow choice after play mode.
            if (Application.isEditor && _shadowAsset != null)
                _originalShadows.ApplyTo(_shadowAsset);
            RestoreLodBias();
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ApplyShadows();
            ApplyScenery();
        }

        void OnApplicationFocus(bool focus)
        {
            _focused = focus;
            ApplyVolume();
        }

        /// <summary>The playtest's combat A/B (dev builds only; a release build always plays the base tuning).</summary>
        public static void ApplyCombatTest(float health, bool bigHitboxes)
        {
            bool dev = Debug.isDebugBuild;
            float scale = dev ? Mathf.Clamp(health, GameSettings.MinCombatHealth, 1f) : 1f;
            float pad = dev && bigHitboxes ? CombatTuning.BigHitboxPad : 0f;
            if (scale != CombatTuning.HealthScale || pad != CombatTuning.HitboxPad)
                CombatTuning.ResetStats(); // a new variant starts its own time-to-kill average
            CombatTuning.HealthScale = scale;
            CombatTuning.HitboxPad = pad;
        }

        void Apply()
        {
            var s = GameSettings.Current.Data;
            // Left alone until the player picks one, so the Editor keeps its own level.
            if (s.quality >= 0 && s.quality < QualitySettings.names.Length && QualitySettings.GetQualityLevel() != s.quality)
            {
                RestoreLodBias();
                QualitySettings.SetQualityLevel(s.quality, true);
            }

            if (!Application.isMobilePlatform && !Application.isEditor)
            {
                var mode = s.windowMode switch
                {
                    WindowMode.Windowed => FullScreenMode.Windowed,
                    WindowMode.Borderless => FullScreenMode.FullScreenWindow,
                    _ => FullScreenMode.ExclusiveFullScreen,
                };
                int w = s.resolutionWidth > 0 ? s.resolutionWidth : Screen.currentResolution.width;
                int h = s.resolutionHeight > 0 ? s.resolutionHeight : Screen.currentResolution.height;
                if (Screen.width != w || Screen.height != h || Screen.fullScreenMode != mode)
                    Screen.SetResolution(w, h, mode);
            }

            QualitySettings.vSyncCount = 0; // the cap below is the limit
            Application.targetFrameRate = s.frameRateCap > 0 ? s.frameRateCap : -1;
            // The URP asset is a project file: in the Editor a change would stick after play mode, so builds only.
            if (!Application.isEditor && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.renderScale = s.renderScale;
                urp.supportsHDR = s.hdr;
            }
            ApplyShadows();
            ApplyScenery();
            ApplyVolume();
            ApplyCombatTest(s.combatHealth, s.combatBigHitboxes);
        }

        /// <summary>The Shadows setting: the shadow map on the URP asset, and on/off and edge filtering on the sun.</summary>
        void ApplyShadows()
        {
            var profile = ShadowProfile.For(GameSettings.Current.Data.shadows);
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                if (_shadowAsset != urp)
                {
                    if (Application.isEditor && _shadowAsset != null)
                        _originalShadows.ApplyTo(_shadowAsset);
                    _shadowAsset = urp;
                    _originalShadows = ShadowSnapshot.Of(urp);
                }
                if (profile.Enabled)
                {
                    urp.mainLightShadowmapResolution = profile.Resolution;
                    urp.shadowDistance = profile.Distance;
                    urp.shadowCascadeCount = profile.Cascades;
                    urp.cascade2Split = profile.Splits.x;
                    urp.cascade4Split = profile.Splits;
                }
            }

            var sun = Sun();
            if (sun == null)
                return;
            sun.shadows = profile.Enabled ? LightShadows.Soft : LightShadows.None;
            sun.GetUniversalAdditionalLightData().softShadowQuality = (SoftShadowQuality)profile.SoftQuality;
        }

        /// <summary>Trees and Render Distance: every terrain's trees and grass, and the LOD bias.</summary>
        void ApplyScenery()
        {
            var s = GameSettings.Current.Data;
            int level = QualitySettings.GetQualityLevel();
            if (level != _biasLevel)
            {
                _biasLevel = level;
                _baseLodBias = QualitySettings.lodBias;
            }
            QualitySettings.lodBias = _baseLodBias * RenderDistanceProfile.For(s.renderDistance).LodBias;
            TerrainScenery.ApplyAll(s.trees, s.renderDistance);
        }

        /// <summary>
        /// Puts the current quality level's LOD bias back as shipped: before switching level (only one level is ever
        /// scaled) and when play mode ends in the Editor, where the quality settings are a project file.
        /// </summary>
        void RestoreLodBias()
        {
            if (_biasLevel == QualitySettings.GetQualityLevel())
                QualitySettings.lodBias = _baseLodBias;
            _biasLevel = -1;
        }

        static Light Sun()
        {
            if (RenderSettings.sun != null)
                return RenderSettings.sun;
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light.type == LightType.Directional)
                    return light;
            return null;
        }

        void ApplyVolume()
        {
            var s = GameSettings.Current.Data;
            bool muted = s.muteInBackground && !_focused && !Application.isMobilePlatform;
            AudioListener.volume = muted ? 0f : s.masterVolume;
        }
    }
}
