using System.Collections.Generic;
using UnityEngine;

namespace WingingIt.World
{
    /// <summary>
    /// A capture field's defenses in Airfield Domination (Design/01-Game-Design/match-modes.md#airfield-domination-prototype):
    /// a control tower and two light AA guns, all <see cref="GroundTarget"/>s that rebuild themselves (tower 45 s, AA
    /// 30 s). While the field is owned and its tower stands, the AA fires and enemies capture it slower. A bombed-out
    /// tower shuts off the field's AA and repair (<see cref="Airfield.TowerDown"/>) and opens it to a full-speed capture.
    /// Dormant outside a Domination match (<see cref="SetActive"/>): nothing can be hit, the AA stays quiet and the
    /// field is as built. A wreck stays a wreck when the field changes hands and rebuilds on its own timer.
    /// </summary>
    [RequireComponent(typeof(Airfield))]
    public class FieldDefenses : MonoBehaviour
    {
        static readonly List<FieldDefenses> s_all = new();

        [SerializeField] GroundTarget tower;
        [SerializeField] AAGun[] aaGuns = { };

        Airfield _airfield;

        public static IReadOnlyList<FieldDefenses> All => s_all;

        public Airfield Airfield => _airfield != null ? _airfield : _airfield = GetComponent<Airfield>();
        public GroundTarget Tower => tower;
        public IReadOnlyList<AAGun> AAGuns => aaGuns;
        /// <summary>Switched on for the match in progress.</summary>
        public bool IsActive { get; private set; }
        /// <summary>The control tower stands (what <c>FieldBoard.Field.TowerUp</c> wants). Always true while dormant.</summary>
        public bool TowerUp => !IsActive || tower == null || !tower.IsDestroyed;

        public void Configure(GroundTarget controlTower, AAGun[] guns)
        {
            tower = controlTower;
            aaGuns = guns ?? new AAGun[0];
        }

        void Awake() => SetTargetsEnabled(false);
        void OnEnable() => s_all.Add(this);

        void OnDisable()
        {
            s_all.Remove(this);
            SetActive(false);
        }

        /// <summary>Switches the defenses on (rebuilt, hittable) or off (rebuilt, dormant).</summary>
        public void SetActive(bool active)
        {
            RebuildAll();
            IsActive = active;
            SetTargetsEnabled(active);
            Airfield.SetTowerDown(false);
        }

        /// <summary>Everything back to intact.</summary>
        public void RebuildAll()
        {
            if (tower != null && tower.IsDestroyed)
                tower.Rebuild();
            foreach (var gun in aaGuns)
                if (gun != null && gun.TryGetComponent<GroundTarget>(out var body) && body.IsDestroyed)
                    body.Rebuild();
        }

        void SetTargetsEnabled(bool on)
        {
            if (tower != null)
                tower.enabled = on;
            foreach (var gun in aaGuns)
                if (gun != null && gun.TryGetComponent<GroundTarget>(out var body))
                    body.enabled = on;
        }

        void Update()
        {
            if (IsActive)
                Airfield.SetTowerDown(!TowerUp);
        }
    }
}
