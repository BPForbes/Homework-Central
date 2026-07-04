namespace HomeworkCentral.Api.Captcha.Text;

/// <summary>
/// Retype-a-code or solve-a-simple-expression challenges — a lightweight puzzle type alongside the
/// maze (see <c>HomeworkCentral.Api.Captcha.Maze</c>) and arrow-match
/// (see <c>HomeworkCentral.Api.Captcha.ArrowMatch</c>) puzzles.
/// </summary>
internal static class TextChallenge
{
    public const string TypeName = "text";

    // Excludes visually ambiguous characters (0/O, 1/I/L) since the code is typed back by hand.
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public static (string Label, string Content, string Answer) Generate() =>
        Random.Shared.Next(2) == 0 ? BuildArithmeticChallenge() : BuildCodeChallenge();

    public static bool Validate(string? expected, string? answer)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(answer))
            return false;

        return string.Equals(expected.Trim(), answer.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static (string Label, string Content, string Answer) BuildArithmeticChallenge()
    {
        int a = Random.Shared.Next(2, 10);
        int b = Random.Shared.Next(2, 10);
        return ("To prove you're human, solve:", $"{a} + {b}", (a + b).ToString());
    }

    private static (string Label, string Content, string Answer) BuildCodeChallenge()
    {
        char[] code = new char[6];
        for (int i = 0; i < code.Length; i++)
            code[i] = CodeAlphabet[Random.Shared.Next(CodeAlphabet.Length)];

        string codeText = new(code);
        return ("Retype this verification code exactly:", codeText, codeText);
    }
}
