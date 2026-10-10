namespace BlocksEscape
{
    // Base unit for everything placed on the board.
    // One cell is one world unit, matching Unity's default editor grid and snap increment,
    // so a board placed at whole coordinates has its tile edges on the scene grid lines.
    public static class GridUnits
    {
        public const float CellSize = 1f;
    }
}
