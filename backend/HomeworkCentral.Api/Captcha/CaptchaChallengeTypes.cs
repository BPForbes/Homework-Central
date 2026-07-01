namespace HomeworkCentral.Api.Captcha;

/// <summary>String discriminator values for <see cref="CaptchaChallengeDto.Type"/> — plain
/// strings (not a JSON-serialized enum) so the wire format matches the frontend's TS union type
/// (`'text' | 'maze' | 'tileRotate'`) without a converter.</summary>
internal static class CaptchaChallengeTypes
{
    public const string Text = "text";
    public const string Maze = "maze";
    public const string TileRotate = "tileRotate";
}
