namespace HomeworkCentral.Api.DTOs;

public class CaptchaVerifyRequest
{
    public string ChallengeId { get; set; } = null!;
    public string Answer { get; set; } = null!;
}
