using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WingingIt.Settings
{
    /// <summary>
    /// Applies the game-wide <see cref="GameSettings"/> (quality, resolution, frame rate cap, render scale, HDR, master
    /// volume, mute in background) at startup and on every change. Created on its own before the first scene loads and
    /// kept across scenes. Per-object settings (controls, FOV, HUD) are read by the objects themselves.
    /// </summary>
    public sealed class SettingsApplier : MonoBehaviour
    {
        static SettingsApplier s_instance;
        bool _focused = true;

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
            Apply();
        }

        void OnDisable()
        {
            if (GameSettings.Current != null)
                GameSettings.Current.Changed -= Apply;
        }

        void OnApplicationFocus(bool focus)
        {
            _focused = focus;
            ApplyVolume();
        }

        void Apply()
        {
            var s = GameSettings.Current.Data;
            // Left alone until the player picks one, so the Editor keeps its own level.
            if (s.quality >= 0 && s.quality < QualitySettings.names.Length && QualitySettings.GetQualityLevel() != s.quality)
                QualitySettings.SetQualityLevel(s.quality, true);

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
            ApplyVolume();
        }

        void ApplyVolume()
        {
            var s = GameSettings.Current.Data;
            bool muted = s.muteInBackground && !_focused && !Application.isMobilePlatform;
            AudioListener.volume = muted ? 0f : s.masterVolume;
        }
    }
}
