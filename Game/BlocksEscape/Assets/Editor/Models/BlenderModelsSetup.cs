using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksEscape.Levels;
using UnityEditor;
using UnityEngine;

namespace BlocksEscape.EditorTools
{
    // Turns the models exported by Game/BlocksEscape/Blender/animals.py into prefabs and registers them in BlockTypeModels:
    // Prefabs/Block/<Animal>/<Animal>_<Shape>.fbx + <Animal>_Albedo.png,
    // Prefabs/Gate/<Animal>/<Animal>Gate_Tile.fbx + <Animal>Gate_Arch.fbx + <Animal>Gate_Albedo.png, and the wall fence
    // Prefabs/Wall/Fence/Fence_Post.fbx + Fence_Rail.fbx + Fence_RailDiag.fbx + Fence_Albedo.png into Prefabs/Wall/Wall.prefab.
    static class BlenderModelsSetup
    {
        const string BlockRoot = "Assets/Prefabs/Block";
        const string GateRoot = "Assets/Prefabs/Gate";
        const string ModelsPath = "Assets/Prefabs/BlockTypeModels.asset";
        const string FenceDir = "Assets/Prefabs/Wall/Fence";
        const string WallPath = "Assets/Prefabs/Wall/Wall.prefab";

        // Model folder -> block type id it is used for; null is the neutral (typeless) blocker.
        static readonly (string animal, string type)[] Animals =
        {
            ("Cow", "cow"), ("Pig", "pig"), ("Chicken", "chicken"), ("Sheep", "sheep"),
            ("Horse", "horse"), ("Cat", "cat"), ("Dog", "dog"), ("HayBale", null),
        };

        [MenuItem("Blocks Escape/Build Models From Blender")]
        static void BuildAll()
        {
            BuildFence();
            var models = AssetDatabase.LoadAssetAtPath<BlockTypeModels>(ModelsPath);
            var so = new SerializedObject(models);
            var entries = so.FindProperty("entries");
            entries.arraySize = 0;
            foreach (var (animal, type) in Animals)
            {
                var blocks = BuildBlocks(animal);
                if (type == null)
                {
                    SetArray(so.FindProperty("neutralBlocks"), blocks);
                    continue;
                }
                entries.arraySize++;
                var entry = entries.GetArrayElementAtIndex(entries.arraySize - 1);
                entry.FindPropertyRelative("type").stringValue = type;
                SetArray(entry.FindPropertyRelative("blocks"), blocks);
                entry.FindPropertyRelative("gate").objectReferenceValue = BuildGate(animal);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log($"Built block and gate prefabs for {Animals.Length} block types.", models);
        }

        static void SetArray(SerializedProperty array, IReadOnlyList<Object> values)
        {
            array.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        static List<Object> BuildBlocks(string animal)
        {
            string dir = $"{BlockRoot}/{animal}";
            var material = MakeMaterial($"{dir}/{animal}.mat", $"{dir}/{animal}_Albedo.png");
            var blocks = new List<Object>();
            foreach (var path in Directory.GetFiles(dir, $"{animal}_*.fbx").Select(p => p.Replace('\\', '/')).OrderBy(p => p))
            {
                var instance = Instantiate(path, material);
                var block = instance.AddComponent<Block>();
                block.DetectFootprint();
                if (block.Footprint.Count == 0) Debug.LogError($"{path}: the mesh covers no cell.");
                blocks.Add(Save(instance, Path.ChangeExtension(path, ".prefab")).GetComponent<Block>());
            }
            return blocks;
        }

        static GateCell BuildGate(string animal)
        {
            string dir = $"{GateRoot}/{animal}";
            var material = MakeMaterial($"{dir}/{animal}Gate.mat", $"{dir}/{animal}Gate_Albedo.png");
            var instance = Instantiate($"{dir}/{animal}Gate_Tile.fbx", material);
            instance.name = $"{animal}Gate";
            Instantiate($"{dir}/{animal}Gate_Arch.fbx", material).transform.SetParent(instance.transform, false);
            instance.AddComponent<GateCell>();
            return Save(instance, $"{dir}/{animal}Gate.prefab").GetComponent<GateCell>();
        }

        // Wall prefab: a post plus eight rail arms (clockwise from +Z), enabled per cell by WallCell.Connect.
        static void BuildFence()
        {
            var material = MakeMaterial($"{FenceDir}/Fence.mat", $"{FenceDir}/Fence_Albedo.png");
            var root = new GameObject("Wall");
            Instantiate($"{FenceDir}/Fence_Post.fbx", material).transform.SetParent(root.transform, false);
            var arms = new List<Object>();
            for (int i = 0; i < 8; i++)
            {
                var arm = Instantiate($"{FenceDir}/{(i % 2 == 0 ? "Fence_Rail" : "Fence_RailDiag")}.fbx", material);
                arm.name = $"Arm {i * 45}";
                arm.transform.SetParent(root.transform, false);
                arm.transform.localRotation = Quaternion.Euler(0f, i * 45f, 0f);
                arms.Add(arm);
            }
            var so = new SerializedObject(root.AddComponent<WallCell>());
            SetArray(so.FindProperty("arms"), arms);
            so.ApplyModifiedPropertiesWithoutUndo();
            Save(root, WallPath);
        }

        static Material MakeMaterial(string path, string texturePath)
        {
            if (AssetImporter.GetAtPath(texturePath) is TextureImporter textures && textures.maxTextureSize != 2048)
            {
                textures.maxTextureSize = 2048; // plenty for blocks a phone shows at ~100 px per cell
                textures.SaveAndReimport();
            }
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            material.SetFloat("_Smoothness", 0.3f);
            EditorUtility.SetDirty(material);
            return material;
        }

        // Instance of an imported model (with its material) to build a prefab variant from.
        static GameObject Instantiate(string modelPath, Material material)
        {
            if (AssetImporter.GetAtPath(modelPath) is ModelImporter importer &&
                importer.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.animationType = ModelImporterAnimationType.None;
                importer.SaveAndReimport();
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
            instance.GetComponent<MeshRenderer>().sharedMaterial = material;
            return instance;
        }

        static GameObject Save(GameObject instance, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(instance, path);
            Object.DestroyImmediate(instance);
            return prefab;
        }
    }
}
