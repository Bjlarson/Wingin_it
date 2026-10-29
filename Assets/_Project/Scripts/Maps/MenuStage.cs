using UnityEngine;

namespace WingingIt.Maps
{
    /// <summary>
    /// The main menu scene's own backdrop: a hangar on an apron under its own light, where the player's plane is parked
    /// for the menus before any map is loaded. Hidden once a map scene loads (the map has its own sun and ground).
    /// </summary>
    public class MenuStage : MonoBehaviour
    {
        [SerializeField] Transform hangarSpot;

        static MenuStage s_current;

        /// <summary>The menu stage that's showing, or null (none in the level, or a map is loaded).</summary>
        public static MenuStage Current => s_current != null && s_current.isActiveAndEnabled ? s_current : null;

        /// <summary>Where the plane parks, nose to the hangar doors.</summary>
        public Transform HangarSpot => hangarSpot;

        public void Configure(Transform spot) => hangarSpot = spot;

        void OnEnable() => s_current = this;

        void OnDisable()
        {
            if (s_current == this)
                s_current = null;
        }

        /// <summary>Shows or hides the whole stage, its light and colliders included.</summary>
        public void Show(bool show) => gameObject.SetActive(show);
    }
}
