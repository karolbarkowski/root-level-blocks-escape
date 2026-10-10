using System;
using UnityEngine;

namespace BlocksEscape.Levels
{
    // Minimal JsonUtility mirror of the block-puzzle/v1 format (see LevelGenerator/FORMAT.md).
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

        public static LevelData Parse(string json) => JsonUtility.FromJson<LevelData>(json);
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
        public string color;
        public int line; // enclosed mode only: column for left/right, row for top/bottom
    }

    [Serializable]
    public class BlockData
    {
        public string id;
        public CellData[] shape;
        public CellData position;
        public string[] colors; // outside to inside; empty = permanent gray
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
