using System.Collections.Generic;
using UnityEngine;

namespace BlocksEscape
{
    // One wall cell, drawn as a fence: a post on the cell centre and rail arms that lead to the neighbouring
    // barriers (walls and gate cells), so a level's outline reads as one continuous fence. A diagonal arm closes
    // a step in the outline where no straight neighbour already does; a line end continues straight through its cell.
    [DisallowMultipleComponent]
    public sealed class WallCell : MonoBehaviour
    {
        // Level directions (x right, y down) of the arms, clockwise from the top (+Z): N, NE, E, SE, S, SW, W, NW.
        static readonly Vector2Int[] Directions =
        {
            new(0, -1), new(1, -1), new(1, 0), new(1, 1), new(0, 1), new(-1, 1), new(-1, 0), new(-1, -1),
        };

        [SerializeField] GameObject[] arms = new GameObject[8]; // in Directions order

        public void Connect(Vector2Int cell, ICollection<Vector2Int> barriers)
        {
            var on = new bool[8];
            int count = 0;
            for (int i = 0; i < 8; i++)
            {
                var d = Directions[i];
                if (!barriers.Contains(cell + d)) continue;
                bool diagonal = d.x != 0 && d.y != 0;
                if (diagonal && (barriers.Contains(cell + new Vector2Int(d.x, 0)) || barriers.Contains(cell + new Vector2Int(0, d.y))))
                    continue; // the straight arms already close this corner
                on[i] = true;
                count++;
            }
            if (count == 0) on[2] = on[6] = true; // a lone wall: a short east-west fence
            else if (count == 1) on[(System.Array.IndexOf(on, true) + 4) % 8] = true;

            for (int i = 0; i < 8; i++)
            {
                if (arms[i] == null) continue;
                arms[i].SetActive(on[i]);
                Board.RecordPrefabInstanceChanges(arms[i]);
            }
        }
    }
}
