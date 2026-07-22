using System.Text;
using System.Text.RegularExpressions;

namespace HomeworkCentral.Api.Uploads;

public static partial class ShebangClassifier
{
    [GeneratedRegex(@"^#!\s*/(?:usr/bin/)?(env\s+)?(python[23]?|node|bash|sh|zsh|ruby|perl)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptShebang();

    public static bool IsScriptContent(ReadOnlySpan<byte> head)
    {
        string prefix = Encoding.UTF8.GetString(head);
        return ScriptShebang().IsMatch(prefix);
    }
}
