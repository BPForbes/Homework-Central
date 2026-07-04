namespace HomeworkCentral.Api.Captcha.ArrowMatch;

/// <summary>
/// The arrow-match puzzle module: generates a 3x3 grid of arrow tiles and validates submissions
/// against it. Each tile independently gets a random target orientation (one of 8 compass
/// positions, 45° apart) and a random starting offset from it — so the "correct" direction isn't
/// always the same one, and isn't the same across tiles within a single challenge either, unlike a
/// fixed "rotate back to 0" puzzle.
/// </summary>
internal static class TileRotatePuzzleGenerator
{
    public const string TypeName = "tileRotate";

    private const int TileCount = 9;
    private const int RotationPositions = 8;

    public static TileRotateDto Generate()
    {
        TileDto[] tiles = new TileDto[TileCount];
        for (int i = 0; i < TileCount; i++)
        {
            int target = Random.Shared.Next(RotationPositions);

            // A non-zero offset (1..7) added to the target guarantees the tile never starts
            // already aligned, without needing a reject-and-retry loop.
            int offset = Random.Shared.Next(1, RotationPositions);
            int initial = (target + offset) % RotationPositions;

            tiles[i] = new TileDto(initial, target);
        }

        return new TileRotateDto(tiles);
    }

    public static bool Validate(TileRotateDto tileRotate, List<int>? clicks)
    {
        if (clicks is null || clicks.Count != tileRotate.Tiles.Length)
            return false;

        for (int i = 0; i < clicks.Count; i++)
        {
            // Sanity cap: no legitimate solve needs more than a couple of full laps around the
            // 8-position wheel per tile.
            if (clicks[i] < 0 || clicks[i] > 24)
                return false;

            int finalSteps = (tileRotate.Tiles[i].InitialRotationSteps + clicks[i]) % RotationPositions;
            if (finalSteps != tileRotate.Tiles[i].TargetRotationSteps)
                return false;
        }

        return true;
    }
}
