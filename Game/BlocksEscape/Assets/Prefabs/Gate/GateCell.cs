using BlocksEscape.Levels;
using UnityEngine;
using UnityEngine.Serialization;

namespace BlocksEscape
{
    // One cell of a gate, spawned from its block type's gate prefab (see BlockTypeModels): a floor tile with the
    // animal's face. While a block that fits it is held, it glows (Shaders/GateGlow.shader).
    [DisallowMultipleComponent]
    public sealed class GateCell : MonoBehaviour
    {
        const string GlowName = "Glow";
        const float GlowHeight = 0.7f; // in cells
        const float GlowFlare = 0.2f;  // how far the walls lean out at the top, in cells: a halo from the camera above
        static readonly int GlowId = Shader.PropertyToID("_Glow");
        static Mesh _glowMesh;

        [SerializeField] string gateId;
        [SerializeField, FormerlySerializedAs("colorName")] string blockType;

        MeshRenderer _glow;
        MaterialPropertyBlock _glowBlock;
        float _shownGlow;

        public string GateId => gateId;
        public string BlockTypeId => blockType;

        public void Setup(string id, BlockType type)
        {
            gateId = id;
            blockType = type.Id;
        }

        // amount 0..1; the glow object is made on first use and hidden at 0.
        public void SetGlow(float amount, Material material)
        {
            if (material == null || Mathf.Approximately(amount, _shownGlow)) return;
            _shownGlow = amount;
            if (_glow == null)
            {
                if (amount <= 0f) return;
                var go = new GameObject(GlowName, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(transform, false);
                go.GetComponent<MeshFilter>().sharedMesh = GlowMesh();
                _glow = go.GetComponent<MeshRenderer>();
                _glow.sharedMaterial = material;
                _glow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _glow.receiveShadows = false;
                _glowBlock = new MaterialPropertyBlock();
            }
            _glow.enabled = amount > 0f;
            _glowBlock.SetFloat(GlowId, amount);
            _glow.SetPropertyBlock(_glowBlock);
        }

        // A floor quad over the cell plus four walls rising from its edge and leaning outwards, one cell across; uv as
        // GateGlow.shader expects.
        static Mesh GlowMesh()
        {
            if (_glowMesh != null) return _glowMesh;
            float h = GridUnits.CellSize * 0.5f, top = GlowHeight * GridUnits.CellSize;
            float lift = 0.07f * GridUnits.CellSize; // the floor line lies just above the tile, which is 0.06 cells thick
            float flare = 1f + GlowFlare / 0.5f;
            var corners = new[] { new Vector3(-h, 0f, -h), new Vector3(h, 0f, -h), new Vector3(h, 0f, h), new Vector3(-h, 0f, h) };
            var vertices = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector3>();
            var triangles = new System.Collections.Generic.List<int>();

            foreach (var c in corners)
            {
                vertices.Add(c + Vector3.up * lift);
                uvs.Add(new Vector3(c.x / (2f * h) + 0.5f, c.z / (2f * h) + 0.5f, 0f));
            }
            triangles.AddRange(new[] { 0, 2, 1, 0, 3, 2 });

            for (int i = 0; i < 4; i++)
            {
                var a = corners[i];
                var b = corners[(i + 1) % 4];
                int first = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(b * flare + Vector3.up * top); vertices.Add(a * flare + Vector3.up * top);
                uvs.Add(new Vector3(i, 0f, 1f)); uvs.Add(new Vector3(i + 1, 0f, 1f));
                uvs.Add(new Vector3(i + 1, 1f, 1f)); uvs.Add(new Vector3(i, 1f, 1f));
                triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
            }

            _glowMesh = new Mesh { name = "Gate Glow" };
            _glowMesh.SetVertices(vertices);
            _glowMesh.SetUVs(0, uvs);
            _glowMesh.SetTriangles(triangles, 0);
            _glowMesh.RecalculateBounds();
            return _glowMesh;
        }
    }
}
