using UnityEngine;

namespace BlocksEscape
{
    // A tile in the level picker's grid: shows a level number (WorldLabel) and, once that level is solved, the solved
    // model (green top with a check) instead of the plain one. Both meshes come from scenery.py (LevelTile and
    // LevelTileDone), wired by "Blocks Escape/Build Scenery From Blender".
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WorldLabel))]
    public sealed class LevelTile : MonoBehaviour
    {
        [SerializeField] Mesh plainMesh;
        [SerializeField] Mesh solvedMesh;

        WorldLabel _label;
        MeshFilter _model;

        // Catalog index of the level the tile opens; -1 when unused.
        public int Level { get; private set; } = -1;

        public void Show(int level, bool solved)
        {
            if (_label == null) _label = GetComponent<WorldLabel>();
            if (_model == null) _model = GetComponentInChildren<MeshFilter>(true);
            Level = level;
            _label.Text = (level + 1).ToString();
            var mesh = solved ? solvedMesh : plainMesh;
            if (_model != null && mesh != null) _model.sharedMesh = mesh;
        }
    }
}
