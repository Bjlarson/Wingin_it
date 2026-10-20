using UnityEngine;
using WingingIt.Settings;

namespace WingingIt.UI
{
    /// <summary>
    /// The Show frame rate setting: a small readout in the top left corner with the frame rate, the CPU and GPU time per
    /// frame (from <see cref="FrameTimingManager"/>, where the device reports them), the graphics API and the size the
    /// game renders at. Refreshed twice a second. Created on its own before the first scene loads and kept across scenes.
    /// </summary>
    public sealed class PerfOverlay : MonoBehaviour
    {
        const float RefreshInterval = 0.5f;
        const float ReferenceHeight = 1080f;

        static PerfOverlay s_instance;

        readonly FrameTiming[] _timings = new FrameTiming[1];
        GUIStyle _style;
        string _text = "";
        float _windowStart;
        int _frames;
        float _worstFrame;
        double _cpuMs, _gpuMs;
        int _timingSamples;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Create()
        {
            if (s_instance != null)
                return;
            var go = new GameObject("Perf Overlay") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<PerfOverlay>();
        }

        void Awake() => useGUILayout = false;

        void Update()
        {
            if (!GameSettings.Current.Data.showFps)
                return;

            _frames++;
            _worstFrame = Mathf.Max(_worstFrame, Time.unscaledDeltaTime);
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _timings) > 0)
            {
                _cpuMs += _timings[0].cpuFrameTime;
                _gpuMs += _timings[0].gpuFrameTime;
                _timingSamples++;
            }

            float now = Time.unscaledTime;
            float elapsed = now - _windowStart;
            if (elapsed < RefreshInterval)
                return;

            float fps = _frames / elapsed;
            string timing = _timingSamples > 0
                ? $"CPU {_cpuMs / _timingSamples:0.0} ms  GPU {(_gpuMs > 0 ? $"{_gpuMs / _timingSamples:0.0} ms" : "n/a")}"
                : "CPU/GPU n/a";
            var api = SystemInfo.graphicsDeviceType;
            int w = Mathf.RoundToInt(Screen.width * ScalableBufferManager.widthScaleFactor * RenderScale());
            int h = Mathf.RoundToInt(Screen.height * ScalableBufferManager.heightScaleFactor * RenderScale());
            _text = $"{fps:0} FPS  (worst {_worstFrame * 1000f:0} ms)\n{timing}\n{api}  {w}×{h}";

            _windowStart = now;
            _frames = 0;
            _worstFrame = 0f;
            _cpuMs = _gpuMs = 0;
            _timingSamples = 0;
        }

        static float RenderScale() =>
            UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp
                ? urp.renderScale
                : 1f;

        void OnGUI()
        {
            if (!GameSettings.Current.Data.showFps || Event.current.type != EventType.Repaint || _text.Length == 0)
                return;
            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, wordWrap = false };

            GUI.depth = -100; // over everything
            float scale = Screen.height / ReferenceHeight;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            var safe = Screen.safeArea;
            var r = new Rect(safe.x / scale + 12f, (Screen.height - safe.yMax) / scale + 8f, 520f, 90f);

            _style.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), _text, _style);
            _style.normal.textColor = Color.white;
            GUI.Label(r, _text, _style);
        }
    }
}
