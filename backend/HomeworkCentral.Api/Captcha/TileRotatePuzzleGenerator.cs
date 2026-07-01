namespace HomeworkCentral.Api.Captcha;

/// <summary>Generates a row of tiles each rotated a random 1–3 step (90°/180°/270°) offset out of
/// alignment; the puzzle is solved by rotating every tile back to a multiple of 4 steps (0°).</summary>
internal static class TileRotatePuzzleGenerator
{
    private const int TileCount = 5;

    public static TileRotateDto Generate()
    {
        TileDto[] tiles = new TileDto[TileCount];
        for (int i = 0; i < TileCount; i++)
            tiles[i] = new TileDto(Random.Shared.Next(1, 4)); // 1, 2, or 3 — never already aligned

        return new TileRotateDto(tiles);
    }
}
