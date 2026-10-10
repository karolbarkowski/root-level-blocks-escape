using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlocksEscape.Levels
{
    // Minimal JsonUtility mirror of the block-puzzle/v2 format (see LevelGenerator/FORMAT.md).
    // Only the fields the game and editor tools need; unknown fields are ignored.
    [Serializable]
    public class LevelData
    {
        public string format;
        public int seed;
        public BoardData board;
        public BlockData[] blocks;
        public MoveData[] solution;
        public AnalysisData analysis;

        public bool IsEnclosed => board != null && board.boundaryMode == "enclosed";

        // Cells a gate occupies. Enclosed gates sit on their drawn line; legacy rectangular gates
        // belong to the board edge and lie one cell outside it.
        public IEnumerable<Vector2Int> GateCells(GateData gate)
        {
            bool enclosed = IsEnclosed;
            for (int n = gate.start; n < gate.start + gate.length; n++)
            {
                switch (gate.side)
                {
                    case "top": yield return new Vector2Int(n, enclosed ? gate.line : -1); break;
                    case "bottom": yield return new Vector2Int(n, enclosed ? gate.line : board.height); break;
                    case "left": yield return new Vector2Int(enclosed ? gate.line : -1, n); break;
                    default: yield return new Vector2Int(enclosed ? gate.line : board.width, n); break;
                }
            }
        }

        public const string CurrentFormat = "block-puzzle/v2";

        public static LevelData Parse(string json)
        {
            var level = JsonUtility.FromJson<LevelData>(json);
            if (level?.format != CurrentFormat)
                throw new ArgumentException(level?.format == "block-puzzle/v1"
                    ? "This is a block-puzzle/v1 level with colors; convert it with LevelGenerator.Tools migrate."
                    : $"Unsupported level format '{level?.format}'.");
            return level;
        }
    }

    [Serializable]
    public class CellData
    {
        public int x;
        public int y;
    }

    [Serializable]
    public class BoardData
    {
        public int width;
        public int height;
        public CellData[] walls;
        public GateData[] gates;
        public string boundaryMode;
    }

    [Serializable]
    public class GateData
    {
        public string id;
        public string side; // outward direction: top, right, bottom, left
        public int start;
        public int length;
        public string type; // block type id (see BlockTypeSet)
        public int line; // enclosed mode only: column for left/right, row for top/bottom
    }

    [Serializable]
    public class BlockData
    {
        public string id;
        public CellData[] shape;
        public CellData position;
        public string[] types; // block type ids, outside to inside; empty = neutral blocker
        public string axis;     // free, horizontal, vertical
        public int frozenFor;
    }

    [Serializable]
    public class MoveData
    {
        public string block;
        public CellData to;
    }

    [Serializable]
    public class AnalysisData
    {
        public int difficulty;
        public int solutionMoves;
        public string solvability;
    }
}
