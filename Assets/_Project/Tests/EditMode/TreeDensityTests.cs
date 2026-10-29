using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using WingingIt.Settings;

namespace WingingIt.Tests
{
    public class TreeDensityTests
    {
        const string IslandV2Terrain = "Assets/_Project/Terrain/IslandV2_MapTerrain.asset";

        static TreeInstance[] Trees(params int[] countPerKind)
        {
            var trees = new System.Collections.Generic.List<TreeInstance>();
            for (int kind = 0; kind < countPerKind.Length; kind++)
                for (int i = 0; i < countPerKind[kind]; i++)
                    trees.Add(new TreeInstance { prototypeIndex = kind, position = new Vector3(i * 0.001f, 0f, kind * 0.1f) });
            return trees.ToArray();
        }

        static int CountOf(TreeInstance[] trees, int kind) => System.Array.FindAll(trees, t => t.prototypeIndex == kind).Length;

        [Test]
        public void EachLevel_KeepsItsShareOfEveryCommonKind()
        {
            var trees = Trees(300, 90);
            var low = TreeDensity.Thin(trees, 2, TreeDetail.Low);
            var medium = TreeDensity.Thin(trees, 2, TreeDetail.Medium);
            Assert.AreEqual(100, CountOf(low, 0));
            Assert.AreEqual(30, CountOf(low, 1));
            Assert.AreEqual(200, CountOf(medium, 0));
            Assert.AreEqual(60, CountOf(medium, 1));
            Assert.AreSame(trees, TreeDensity.Thin(trees, 2, TreeDetail.High));
        }

        [Test]
        public void RareKinds_AreAlwaysKept()
        {
            var trees = Trees(300, TreeDensity.RareCount - 1);
            var low = TreeDensity.Thin(trees, 2, TreeDetail.Low);
            Assert.AreEqual(TreeDensity.RareCount - 1, CountOf(low, 1));
        }

        [Test]
        public void Thinning_SpreadsOverTheMap_NotOneCorner()
        {
            var trees = Trees(300);
            var low = TreeDensity.Thin(trees, 1, TreeDetail.Low);
            float first = low[0].position.x, last = low[^1].position.x;
            Assert.Less(first, 0.003f);
            Assert.Greater(last, 0.29f);
        }

        [Test]
        public void ThinnedTerrain_DrawsFromACopy_AndHighPutsTheMapsOwnDataBack()
        {
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(IslandV2Terrain);
            Assume.That(data, Is.Not.Null, "IslandV2 terrain not built");
            int all = data.treeInstanceCount;
            var go = new GameObject("Terrain");
            try
            {
                var terrain = go.AddComponent<Terrain>();
                terrain.terrainData = data;
                terrain.treeDistance = 2000f;
                var scenery = TerrainScenery.For(terrain);

                scenery.Apply(TreeDetail.Low, RenderDistance.High);
                Assert.AreNotSame(data, terrain.terrainData);
                Assert.AreEqual(all, data.treeInstanceCount, "the map's own trees are untouched");
                Assert.That(terrain.terrainData.treeInstanceCount, Is.InRange(all / 3, all / 2));
                Assert.AreEqual(data.size, terrain.terrainData.size);

                scenery.Apply(TreeDetail.Medium, RenderDistance.Low);
                Assert.AreEqual(1000f, terrain.treeDistance, 1e-3f, "Low draws trees half as far as the map was built");
                Assert.That(terrain.terrainData.treeInstanceCount, Is.InRange(all * 2 / 3, all * 3 / 4));

                scenery.Apply(TreeDetail.High, RenderDistance.High);
                Assert.AreEqual(2000f, terrain.treeDistance, 1e-3f);
                Assert.AreSame(data, terrain.terrainData);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
            Assert.AreEqual(all, data.treeInstanceCount);
        }
    }
}
