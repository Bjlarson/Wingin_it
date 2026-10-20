using UnityEngine;
using WingingIt.Flight;

namespace WingingIt.World
{
    /// <summary>
    /// The cloth of one team's banner and its cable (Design/01-Game-Design/match-modes.md#capture-the-banner-prototype).
    /// It flies from its stand's pole while home, trails on a cable behind the plane that carries it (a streamer everyone can
    /// see), and falls to the ground when the carrier goes down, where it lies until it's picked up or goes home. Purely
    /// visual: <see cref="Match.BannerBoard"/> holds the rules, <see cref="Match.BannerWorld"/> moves this between states.
    /// The cloth is the stand's own (<see cref="BannerStand.Cloth"/>), taken off the pole while it's away.
    /// </summary>
    public class TowedBanner : MonoBehaviour
    {
        /// <summary>Where the banner is, visually.</summary>
        public enum Place { Home, Towed, Lying }

        const float CableLength = 40f;
        const float FallSpeed = 45f;
        const float Follow = 7f; // 1/s: how fast the cloth catches up with the point behind the plane

        BannerStand _stand;
        Transform _cloth, _homeParent;
        Vector3 _homeLocalPos, _homeLocalScale;
        Quaternion _homeLocalRot;
        LineRenderer _cable;
        PlaneController _carrier;
        Vector3 _restPoint, _cableDir = Vector3.back;
        bool _madeCloth;

        public Place Where { get; private set; }
        public int Team => _stand != null ? _stand.Team : -1;
        /// <summary>The cloth's centre.</summary>
        public Vector3 Position => _cloth != null ? _cloth.position : transform.position;
        public PlaneController Carrier => _carrier;

        /// <summary>A banner for <paramref name="stand"/>'s team, flying from its pole.</summary>
        public static TowedBanner Create(BannerStand stand)
        {
            var go = new GameObject($"Banner Cloth Team{stand.Team}");
            var banner = go.AddComponent<TowedBanner>();
            banner.Bind(stand);
            return banner;
        }

        void Bind(BannerStand stand)
        {
            _stand = stand;
            _cloth = stand.Cloth;
            if (_cloth == null)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(cube.GetComponent<Collider>());
                cube.name = "Banner";
                cube.transform.SetParent(stand.transform, false);
                cube.transform.localPosition = (stand.FlagMount != null ? stand.FlagMount.localPosition : Vector3.up * 40f) + new Vector3(0f, -6f, 5f);
                cube.transform.localScale = new Vector3(0.3f, 10f, 10f);
                _cloth = cube.transform;
                _madeCloth = true;
            }
            _homeParent = _cloth.parent;
            _homeLocalPos = _cloth.localPosition;
            _homeLocalRot = _cloth.localRotation;
            _homeLocalScale = _cloth.localScale;

            _cable = gameObject.AddComponent<LineRenderer>();
            _cable.positionCount = 2;
            _cable.widthMultiplier = 0.25f;
            _cable.numCapVertices = 2;
            _cable.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var material = _cloth.TryGetComponent<Renderer>(out var r) ? r.sharedMaterial : null;
            if (material != null)
                _cable.sharedMaterial = material;
            _cable.enabled = false;
        }

        void OnDestroy()
        {
            if (_madeCloth && _cloth != null)
                Destroy(_cloth.gameObject);
        }

        /// <summary>Flies from the pole again (and drops any carrier).</summary>
        public void GoHome()
        {
            _carrier = null;
            Where = Place.Home;
            _cable.enabled = false;
            if (_cloth == null)
                return;
            _cloth.SetParent(_homeParent, false);
            _cloth.localPosition = _homeLocalPos;
            _cloth.localRotation = _homeLocalRot;
            _cloth.localScale = _homeLocalScale;
        }

        /// <summary>Trails behind <paramref name="carrier"/> on its cable.</summary>
        public void Tow(PlaneController carrier)
        {
            _carrier = carrier;
            Where = Place.Towed;
            _cloth.SetParent(null, true); // keeps its place: it comes off the pole (or up off the ground) and trails in
            _cloth.localScale = _homeLocalScale;
            _cable.enabled = true;
        }

        /// <summary>The carrier went down: falls to <paramref name="groundPoint"/> and lies there.</summary>
        public void Drop(Vector3 groundPoint)
        {
            if (Where != Place.Towed)
                return;
            _carrier = null;
            Where = Place.Lying;
            _restPoint = groundPoint + Vector3.up * 5f;
        }

        void LateUpdate()
        {
            switch (Where)
            {
                case Place.Towed:
                    if (_carrier == null)
                        return;
                    float scale = _carrier.Stats != null ? _carrier.Stats.visualScale : 1f;
                    Vector3 tail = _carrier.transform.position - _carrier.transform.forward * (4f * scale);
                    Vector3 target = tail - _carrier.transform.forward * CableLength;
                    _cloth.position = Vector3.Lerp(_cloth.position, target, 1f - Mathf.Exp(-Follow * Time.deltaTime));
                    Vector3 toCloth = _cloth.position - tail;
                    // Never let the cable slacken into a ball: the cloth always hangs the full length behind.
                    if (toCloth.sqrMagnitude > 0.01f)
                    {
                        _cableDir = toCloth.normalized;
                        _cloth.position = tail + _cableDir * CableLength;
                    }
                    FaceAlong(_cableDir);
                    DrawCable(tail, _cloth.position);
                    break;
                case Place.Lying:
                    _cloth.position = Vector3.MoveTowards(_cloth.position, _restPoint, FallSpeed * Time.deltaTime);
                    FaceAlong(FlatDir());
                    // The cable lies out behind it along the ground.
                    DrawCable(_cloth.position, _cloth.position - FlatDir() * (CableLength * 0.5f));
                    _cable.enabled = true;
                    break;
            }
        }

        Vector3 FlatDir()
        {
            Vector3 flat = new Vector3(_cableDir.x, 0f, _cableDir.z);
            return flat.sqrMagnitude > 0.01f ? flat.normalized : Vector3.back;
        }

        /// <summary>The cloth's long side lies along the cable, upright, like a streamer.</summary>
        void FaceAlong(Vector3 dir) => _cloth.rotation = Quaternion.LookRotation(dir, Vector3.up);

        void DrawCable(Vector3 a, Vector3 b)
        {
            _cable.SetPosition(0, a);
            _cable.SetPosition(1, b);
        }
    }
}
