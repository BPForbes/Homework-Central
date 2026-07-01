namespace HomeworkCentral.Api.Security;

public interface IContentSanitizer
{
    string Sanitize(string rawContent);
}
