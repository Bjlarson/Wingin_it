using UnityEngine;

namespace WingingIt.World
{
    /// <summary>
    /// Idle life for a standing pilot (menu stage): breathing (<see cref="PilotMove.Idle"/>), and every so often a fidget
    /// (a look round, a stretch, a glance at the watch, a wave), never the same one twice running. A fidget only starts
    /// from Idle, so anything else playing (the PILOT screen's <see cref="PilotMove.Risers"/>) is left alone. Runs on
    /// unscaled time, since the menus freeze the world's time scale.
    /// </summary>
    [RequireComponent(typeof(PilotFigure))]
    public class PilotIdle : MonoBehaviour
    {
        /// <summary>The fidgets, in the order <see cref="Fidget"/> walks them.</summary>
        public static readonly PilotMove[] Fidgets = { PilotMove.LookAround, PilotMove.Stretch, PilotMove.CheckWatch, PilotMove.Wave };

        [Tooltip("Shortest wait in Idle before a fidget, in seconds.")]
        [SerializeField] float minGap = 5f;
        [Tooltip("Longest wait in Idle before a fidget, in seconds.")]
        [SerializeField] float maxGap = 11f;

        PilotFigure _figure;
        float _idleTime;
        float _gap;
        int _count;

        void Awake()
        {
            _figure = GetComponent<PilotFigure>();
            if (_figure.Animator != null)
                _figure.Animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            _count = Random.Range(0, Fidgets.Length);
            _gap = Random.Range(minGap, maxGap);
        }

        void Update()
        {
            if (_figure.Current != PilotMove.Idle)
            {
                _idleTime = 0f;
                return;
            }
            _idleTime += Time.unscaledDeltaTime;
            if (_idleTime < _gap)
                return;
            _figure.Play(Fidget(_count++));
            _idleTime = 0f;
            _gap = Random.Range(minGap, maxGap);
        }

        /// <summary>
        /// The <paramref name="index"/>th fidget: steps through <see cref="Fidgets"/> three at a time (mod 4), so every one
        /// comes round within four and none follows itself.
        /// </summary>
        public static PilotMove Fidget(int index) => Fidgets[((index * 3) % Fidgets.Length + Fidgets.Length) % Fidgets.Length];
    }
}
