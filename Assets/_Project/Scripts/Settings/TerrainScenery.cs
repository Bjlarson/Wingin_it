using UnityEngine;

namespace WingingIt.Settings
{
    /// <summary>
    /// A terrain's trees and grass at the player's Trees (<see cref="TreeDensity"/>) and Render Distance
    /// (<see cref="RenderDistanceProfile"/>) settings. The terrain's data is a project asset, and changing its trees in
    /// the Editor's play mode would change the map for good, so a thinned terrain draws from a copy made the first time
    /// it's thinned; High puts the original back. The collider keeps the original (same heights; tree prefabs have no
    /// colliders). Distances are shares of what the terrain was built with. Added by <see cref="SettingsApplier"/> on
    /// every scene load and change.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TerrainScenery : MonoBehaviour
    {
        Terrain _terrain;
        TerrainData _original, _copy;
        TreeDetail? _trees;
        float _treeDistance, _detailDistance;

        /// <summary>Every active terrain at <paramref name="trees"/> and <paramref name="distance"/>.</summary>
        public static void ApplyAll(TreeDetail trees, RenderDistance distance)
        {
            foreach (var terrain in Terrain.activeTerrains)
                if (terrain != null)
                    For(terrain).Apply(trees, distance);
        }

        public static TerrainScenery For(Terrain terrain)
        {
            if (terrain.TryGetComponent(out TerrainScenery scenery))
                return scenery;
            scenery = terrain.gameObject.AddComponent<TerrainScenery>();
            scenery.hideFlags = HideFlags.DontSave;
            scenery._terrain = terrain;
            scenery._original = terrain.terrainData;
            scenery._treeDistance = terrain.treeDistance;
            scenery._detailDistance = terrain.detailObjectDistance;
            return scenery;
        }

        /// <summary>The terrain's own data, as the map was built.</summary>
        public TerrainData Original => _original;

        public void Apply(TreeDetail trees, RenderDistance distance)
        {
            var profile = RenderDistanceProfile.For(distance);
            _terrain.treeDistance = _treeDistance * profile.Trees;
            _terrain.treeBillboardDistance = _terrain.treeDistance; // the prefabs' own billboards, not the terrain's
            _terrain.detailObjectDistance = _detailDistance * profile.Details;
            ThinTrees(trees);
        }

        void ThinTrees(TreeDetail detail)
        {
            if (_trees == detail || _original == null)
                return;
            _trees = detail;
            if (TreeDensity.KeptOfThree(detail) >= 3 || _original.treeInstanceCount == 0)
            {
                _terrain.terrainData = _original;
                return;
            }
            if (_copy == null)
            {
                _copy = Instantiate(_original);
                _copy.name = _original.name + " (thinned)";
                _copy.hideFlags = HideFlags.DontSave;
            }
            _copy.SetTreeInstances(TreeDensity.Thin(_original.treeInstances, _original.treePrototypes.Length, detail), false);
            _terrain.terrainData = _copy;
        }

        void OnDestroy()
        {
            if (_terrain != null)
            {
                _terrain.treeDistance = _treeDistance;
                _terrain.treeBillboardDistance = _treeDistance;
                _terrain.detailObjectDistance = _detailDistance;
                if (_terrain.terrainData == _copy)
                    _terrain.terrainData = _original;
            }
            if (_copy == null)
                return;
            if (Application.isPlaying)
                Destroy(_copy);
            else
                DestroyImmediate(_copy);
        }
    }
}
