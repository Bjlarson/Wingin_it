using UnityEngine;

namespace WingingIt.Input
{
    /// <summary>
    /// Reads one steering finger as a weapon trigger for button-free touch play: hold = guns, quick tap = secondary
    /// weapon, double tap = ability. A tap is held back for <see cref="DoubleTapGap"/> so a double tap never also fires
    /// the single-tap weapon. <see cref="Tap"/> and <see cref="DoubleTap"/> are true for the one update they happen in.
    /// </summary>
    public sealed class TouchGestures
    {
        public const float TapMaxTime = 0.22f;
        public const float DoubleTapGap = 0.28f;

        public bool Holding { get; private set; }
        public bool Tap { get; private set; }
        public bool DoubleTap { get; private set; }

        bool _down, _moved, _secondPress;
        float _downTime, _pending = -1f;
        Vector2 _start;

        public void Reset()
        {
            Holding = Tap = DoubleTap = _down = _moved = _secondPress = false;
            _pending = -1f;
        }

        /// <param name="moveLimit">How far (pixels) a finger can travel and still count as a tap.</param>
        public void Update(float dt, bool down, Vector2 position, float moveLimit)
        {
            Tap = DoubleTap = false;

            if (down && !_down)
            {
                _downTime = 0f;
                _moved = false;
                _start = position;
                _secondPress = _pending >= 0f;
                if (_secondPress)
                    DoubleTap = true;
                _pending = -1f;
            }
            else if (!down && _down)
            {
                if (!_secondPress && !_moved && _downTime <= TapMaxTime)
                    _pending = DoubleTapGap;
                Holding = false;
            }
            else if (!down && _pending >= 0f)
            {
                _pending -= dt;
                if (_pending < 0f)
                {
                    _pending = -1f;
                    Tap = true;
                }
            }

            if (down)
            {
                _downTime += dt;
                if ((position - _start).sqrMagnitude > moveLimit * moveLimit)
                    _moved = true;
                Holding = _downTime > TapMaxTime;
            }
            _down = down;
        }
    }
}
