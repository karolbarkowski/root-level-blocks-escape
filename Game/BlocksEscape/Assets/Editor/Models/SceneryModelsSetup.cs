using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BlocksEscape.EditorTools
{
    // Turns the models exported by Game/BlocksEscape/Blender/scenery.py (Prefabs/Scenery/*.fbx + Scenery_Palette.png)
    // into prefabs that share one URP Lit material. The palette texture is a grid of flat colour swatches the meshes are
    // UV-mapped into, so it is imported without filtering or mipmaps to keep the swatches from bleeding.
    // The back button gets a collider and a WorldButton; the sign gets a LevelSign wired to the digit prefabs.
    static class SceneryModelsSetup
    {
        const string Dir = "Assets/Prefabs/Scenery";
        const string PalettePath = Dir + "/Scenery_Palette.png";
        const string MaterialPath = Dir + "/Scenery.mat";

        [MenuItem("Blocks Escape/Build Scenery From Blender")]
        static void Build()
        {
            if (AssetImporter.GetAtPath(PalettePath) is TextureImporter palette)
            {
                palette.filterMode = FilterMode.Point;
                palette.mipmapEnabled = false;
                palette.textureCompression = TextureImporterCompression.Uncompressed;
                palette.SaveAndReimport();
            }
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(PalettePath));
            material.SetFloat("_Smoothness", 0.15f);
            EditorUtility.SetDirty(material);

            var digits = new GameObject[10];
            foreach (var path in Directory.GetFiles(Dir, "*.fbx").Select(p => p.Replace('\\', '/')).OrderBy(p => p))
            {
                if (AssetImporter.GetAtPath(path) is ModelImporter importer &&
                    importer.materialImportMode != ModelImporterMaterialImportMode.None)
                {
                    importer.materialImportMode = ModelImporterMaterialImportMode.None;
                    importer.animationType = ModelImporterAnimationType.None;
                    importer.SaveAndReimport();
                }
                string name = Path.GetFileNameWithoutExtension(path);
                string prefabPath = $"{Dir}/{name}.prefab";
                // Each prefab is a root with the model as its child, so scripts can animate the model (a button's squash)
                // while the root keeps the position and scale ScreenAnchor gives it. An existing prefab is updated in
                // place, only its model child replaced, so its root components keep the overrides scene instances have.
                bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;
                var root = exists ? PrefabUtility.LoadPrefabContents(prefabPath) : new GameObject(name);
                for (int i = root.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                model.transform.SetParent(root.transform, false);
                foreach (var r in model.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = material;

                if (name == "BackButton")
                {
                    var collider = model.AddComponent<BoxCollider>();
                    collider.size += new Vector3(0.1f, 0.3f, 0.1f); // a little forgiving for fingers
                    if (root.GetComponent<WorldButton>() == null) root.AddComponent<WorldButton>();
                }
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                if (exists) PrefabUtility.UnloadPrefabContents(root);
                else Object.DestroyImmediate(root);
                if (name.StartsWith("Digit_") && int.TryParse(name.Substring(6), out int d) && d is >= 0 and <= 9) digits[d] = prefab;
            }

            // The sign needs the digits, so wire it last.
            var signPath = $"{Dir}/Sign.prefab";
            var sign = PrefabUtility.LoadPrefabContents(signPath);
            var levelSign = sign.GetComponent<LevelSign>();
            if (levelSign == null) levelSign = sign.AddComponent<LevelSign>();
            var so = new SerializedObject(levelSign);
            var array = so.FindProperty("digits");
            array.arraySize = 10;
            for (int i = 0; i < 10; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = digits[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(sign, signPath);
            PrefabUtility.UnloadPrefabContents(sign);

            AssetDatabase.SaveAssets();
            Debug.Log("Built scenery prefabs.", material);
        }
    }
}
