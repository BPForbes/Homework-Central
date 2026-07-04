namespace HomeworkCentral.Api.Captcha.ArrowMatch;

/// <summary>A 3x3 grid of arrow tiles. Each tile has its own random target orientation — solving
/// isn't "rotate back to a fixed direction," it's rotating every tile to match its own
/// <see cref="TileDto.TargetRotationSteps"/>.</summary>
public sealed record TileRotateDto(TileDto[] Tiles);

/// <summary>One arrow tile. Both fields are steps of 45° (0–7, one of 8 compass positions).
/// <paramref name="InitialRotationSteps"/> is where it starts; <paramref name="TargetRotationSteps"/>
/// is where the player must rotate it to — never equal to the initial position, and not fixed to
/// any one direction across tiles or challenges.</summary>
public sealed record TileDto(int InitialRotationSteps, int TargetRotationSteps);
