using BlocksEscape.Levels;
using UnityEngine;
using UnityEngine.Serialization;

namespace BlocksEscape
{
    // One cell of a gate, spawned from its block type's gate prefab (see BlockTypeModels).
    // Local +Z is the gate's outward (exit) direction.
    [DisallowMultipleComponent]
    public sealed class GateCell : MonoBehaviour
    {
        [SerializeField] string gateId;
        [SerializeField, FormerlySerializedAs("colorName")] string blockType;

        public string GateId => gateId;
        public string BlockTypeId => blockType;

        public void Setup(string id, BlockType type)
        {
            gateId = id;
            blockType = type.Id;
        }
    }
}
