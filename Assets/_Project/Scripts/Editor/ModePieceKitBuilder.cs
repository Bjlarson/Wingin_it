using UnityEditor;
using UnityEngine;
using WingingIt.Maps;
using WingingIt.World;
using B = WingingIt.Editor.FlightTestSceneBuilder;

namespace WingingIt.Editor
{
    /// <summary>
    /// Writes <c>Maps/ModePieceKit.asset</c>, the assets <see cref="ModePieceBuilder"/> spawns mode pieces from: the
    /// airfield prefab, the light AA tuning, the markers pack's gate pylon (greybox without it) and the banner stand's
    /// materials. Run by the scene builders before they place a <see cref="ModePieceBuilder"/>.
    /// </summary>
    internal static class ModePieceKitBuilder
    {
        internal const string KitPath = B.Root + "/Maps/ModePieceKit.asset";

        public static ModePieceKit Ensure()
        {
            B.EnsureFolder(B.Root + "/Maps");
            var kit = AssetDatabase.LoadAssetAtPath<ModePieceKit>(KitPath);
            if (kit == null)
            {
                kit = ScriptableObject.CreateInstance<ModePieceKit>();
                AssetDatabase.CreateAsset(kit, KitPath);
            }
            kit.airfieldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(B.AirfieldPrefabPath);
            kit.lightAA = LightAAStats();

            var markers = MarkerModels.Load();
            KitMesh Part(string name) => markers != null ? new KitMesh(markers.Mesh(name), markers.Materials(name)) : null;
            kit.pylonBase = Part("PylonBase");
            kit.pylonTop = Part("PylonTop");
            kit.pylonPole = Part("PylonPole");
            kit.laneGlow = markers != null ? MarkerModels.LaneMaterial("LaneBase", Color.white) : null;
            kit.greyboxPole = B.Materials.Mat("Greybox_LanePole", new Color(0.85f, 0.85f, 0.82f));
            kit.greyboxLane = B.Materials.UnlitMat("Greybox_LaneBase", Color.white);

            kit.bannerPole = B.Materials.Mat("Greybox_BannerPole", new Color(0.82f, 0.82f, 0.85f));
            kit.bannerCloth = new[]
            {
                B.Materials.UnlitMat("Greybox_Banner_Team0", new Color(0.35f, 0.6f, 1f)),
                B.Materials.UnlitMat("Greybox_Banner_Team1", new Color(1f, 0.38f, 0.32f)),
            };
            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();
            return kit;
        }

        /// <summary>Capture fields' light AA: shorter reach, slower and less accurate than a home field's guns.</summary>
        static AAStats LightAAStats()
        {
            string path = $"{B.WeaponsFolder}/LightAAGuns.asset";
            var stats = AssetDatabase.LoadAssetAtPath<AAStats>(path);
            if (stats != null)
                return stats; // keep hand tuning
            stats = ScriptableObject.CreateInstance<AAStats>();
            stats.range = 600f;
            stats.traverseRate = 40f;
            stats.damage = 1.5f;
            stats.roundsPerSecond = 3f;
            stats.startSpread = 5f;
            stats.settledSpread = 1.4f;
            AssetDatabase.CreateAsset(stats, path);
            return stats;
        }
    }
}
