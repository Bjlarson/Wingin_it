using UnityEngine;
using WingingIt.Flight;

namespace WingingIt.Editor
{
    /// <summary>
    /// The weapons pack (ArtSource/Weapons/build_weapons.py → Art/Models/Weapons/Weapons.fbx): one object per weapon,
    /// nose down +Z, origin mid-length, on the shared plane materials plus Weapon_Glow. Every model is unset when the FBX
    /// hasn't been generated, and the weapons keep their placeholder boxes.
    /// </summary>
    internal class WeaponModels
    {
        static string ModelPath => $"{ClassModels.ModelsFolder}/Weapons/Weapons.fbx";

        public ProjectileVisuals.WeaponModel Rocket, Missile, Bomb, Tracer, FlakShell, MuzzleFlash;

        public static WeaponModels Load(ClassModels.PlaneMaterials materials)
        {
            var models = new WeaponModels();
            var source = ClassModels.LoadModel(ModelPath, materials);
            if (source == null)
                return models;
            models.Rocket = Get(source, "Rocket");
            models.Missile = Get(source, "Missile");
            models.Bomb = Get(source, "Bomb");
            models.Tracer = Get(source, "Tracer");
            models.FlakShell = Get(source, "FlakShell");
            models.MuzzleFlash = Get(source, "MuzzleFlash");
            return models;
        }

        static ProjectileVisuals.WeaponModel Get(GameObject source, string name)
        {
            var child = source.transform.Find(name) ?? throw new System.ArgumentException($"{source.name} has no object '{name}'");
            return new ProjectileVisuals.WeaponModel
            {
                mesh = child.GetComponent<MeshFilter>().sharedMesh,
                materials = child.GetComponent<MeshRenderer>().sharedMaterials,
            };
        }

        /// <summary>Sets a <see cref="ProjectileVisuals.WeaponModel"/> field on a component.</summary>
        public static void Set(Object target, string field, ProjectileVisuals.WeaponModel model)
        {
            var so = new UnityEditor.SerializedObject(target);
            var prop = so.FindProperty(field) ?? throw new System.ArgumentException($"{target.GetType().Name} has no field '{field}'");
            prop.FindPropertyRelative("mesh").objectReferenceValue = model.mesh;
            var materials = prop.FindPropertyRelative("materials");
            var values = model.materials ?? System.Array.Empty<Material>();
            materials.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                materials.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
