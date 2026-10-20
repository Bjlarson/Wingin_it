using System.Collections.Generic;
using UnityEngine;

namespace WingingIt.Effects
{
    /// <summary>
    /// Plays the VFX pack's effects from a pool: <see cref="Play(VfxKind, Vector3, Quaternion, float)"/> for one-shots
    /// (they recycle themselves), <see cref="Attach"/> for looping emitters that follow something until
    /// <see cref="VfxEffect.Stop"/>. Everything is a no-op without a <see cref="VfxLibrary"/> or outside play mode, so
    /// gameplay code can call it unconditionally.
    /// </summary>
    public static class Vfx
    {
        /// <summary>Most copies of one effect alive at once; more are skipped (a hail of bullet hits stays cheap).</summary>
        public const int MaxPerKind = 48;

        static VfxLibrary _library;
        static bool _searched;
        static Transform _root;
        static readonly Dictionary<VfxKind, Stack<VfxEffect>> Pools = new();
        static readonly List<VfxEffect> Playing = new();

        /// <summary>The pack, from Resources/<see cref="VfxLibrary.ResourcePath"/> unless set (tests can swap it).</summary>
        public static VfxLibrary Library
        {
            get
            {
                if (!_searched)
                {
                    _searched = true;
                    _library = Resources.Load<VfxLibrary>(VfxLibrary.ResourcePath);
                }
                return _library;
            }
            set
            {
                _library = value;
                _searched = true;
                Pools.Clear();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _library = null;
            _searched = false;
            _root = null;
            Pools.Clear();
            Playing.Clear();
        }

        /// <summary>Parent of idle (and detached, finishing) effects. Lives in the active scene, so it goes with it.</summary>
        internal static Transform PoolRoot
        {
            get
            {
                if (_root == null)
                {
                    _root = new GameObject("VFX").transform;
                    Pools.Clear(); // the old pool went with its scene
                }
                return _root;
            }
        }

        public static VfxEffect Play(VfxKind kind, Vector3 position, float scale = 1f) => Play(kind, position, Quaternion.identity, scale);

        public static VfxEffect Play(VfxKind kind, Vector3 position, Quaternion rotation, float scale = 1f)
        {
            var effect = Get(kind);
            if (effect == null)
                return null;
            effect.transform.SetParent(PoolRoot, false);
            effect.transform.SetPositionAndRotation(position, rotation);
            Start(effect, scale);
            return effect;
        }

        /// <summary>A looping effect riding on <paramref name="parent"/> (its puffs stay in the world). Stop it when done.</summary>
        public static VfxEffect Attach(VfxKind kind, Transform parent, Vector3 localPosition, float scale = 1f)
        {
            if (parent == null)
                return null;
            var effect = Get(kind);
            if (effect == null)
                return null;
            effect.transform.SetParent(parent, false);
            effect.transform.SetLocalPositionAndRotation(localPosition, Quaternion.identity);
            Start(effect, scale);
            return effect;
        }

        /// <summary>How many of <paramref name="kind"/> are playing (or finishing) right now.</summary>
        public static int PlayingCount(VfxKind kind)
        {
            int n = 0;
            foreach (var e in Playing)
                if (e != null && e.Kind == kind)
                    n++;
            return n;
        }

        static void Start(VfxEffect effect, float scale)
        {
            effect.gameObject.SetActive(true);
            effect.Play(Mathf.Max(0.01f, scale));
            Playing.Add(effect);
        }

        static VfxEffect Get(VfxKind kind)
        {
            if (!Application.isPlaying)
                return null;
            var prefab = Library != null ? Library.Get(kind) : null;
            if (prefab == null)
                return null;
            Playing.RemoveAll(e => e == null);
            if (PlayingCount(kind) >= MaxPerKind)
                return null;

            var root = PoolRoot;
            if (Pools.TryGetValue(kind, out var pool))
                while (pool.Count > 0)
                {
                    var pooled = pool.Pop();
                    if (pooled != null)
                        return pooled;
                }
            var effect = Object.Instantiate(prefab, root);
            effect.name = prefab.name;
            effect.Kind = kind;
            return effect;
        }

        /// <summary>Called by an effect when its last particle is gone.</summary>
        internal static void Release(VfxEffect effect)
        {
            Playing.Remove(effect);
            effect.Recycle();
            effect.transform.SetParent(PoolRoot, false);
            if (!Pools.TryGetValue(effect.Kind, out var pool))
                Pools[effect.Kind] = pool = new Stack<VfxEffect>();
            pool.Push(effect);
        }
    }
}
