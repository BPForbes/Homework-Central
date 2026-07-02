namespace HomeworkCentral.Api.Captcha;

/// <summary>
/// Generates a 3x3 grid of arrow tiles. Each tile independently gets a random target orientation
/// (one of 8 compass positions, 45° apart) and a random starting offset from it — so the "correct"
/// direction isn't always the same one, and isn't the same across tiles within a single challenge
/// either, unlike a fixed "rotate back to 0" puzzle.
/// </summary>
internal static class TileRotatePuzzleGenerator
{
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
}
