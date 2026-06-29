namespace HomeworkCentral.Api.Dev;

public static class DevRootPage
{
    public static IResult ForbiddenDirectoryPage() =>
        Results.Content(ForbiddenHtml, "text/html; charset=utf-8", statusCode: 403);

    public static IResult ApiErrorPage(string errors) =>
        Results.Content(BuildApiErrorHtml(errors), "text/html; charset=utf-8", statusCode: 500);

    private const string ForbiddenHtml = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="UTF-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1.0" />
          <title>403 - Forbidden</title>
          <style>
            body { margin: 0; font-family: "Segoe UI", Tahoma, Geneva, Verdana, sans-serif; }
            .server-error { background: #808080; color: #ffffff; padding: 0.5rem 1rem; font-size: 1.25rem; font-weight: 600; }
            .forbidden { background: #ffffff; color: #cc0000; padding: 0.5rem 1rem; font-size: 1.1rem; font-weight: 600; }
            .detail { background: #ffffff; color: #000000; padding: 0.5rem 1rem; font-size: 1rem; }
          </style>
        </head>
        <body>
          <div class="server-error">Server Error</div>
          <div class="forbidden">403 - Forbidden Access is denied.</div>
          <div class="detail">You do not have access to this directory or page while using the credentials you supplied.</div>
        </body>
        </html>
        """;

    private const string ApiErrorHtmlPrefix = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="UTF-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1.0" />
          <title>API Error</title>
          <style>
            body { margin: 0; font-family: "Segoe UI", Tahoma, Geneva, Verdana, sans-serif; background: #f5f5f5; }
            .header { background: #808080; color: #ffffff; padding: 0.5rem 1rem; font-size: 1.25rem; font-weight: 600; }
            pre { margin: 0; padding: 1rem; background: #ffffff; color: #000000; white-space: pre-wrap; word-break: break-word; font-size: 0.9rem; line-height: 1.4; }
          </style>
        </head>
        <body>
          <div class="header">API Errors</div>
          <pre>
        """;

    private const string ApiErrorHtmlSuffix = """
        </pre>
        </body>
        </html>
        """;

    private static string BuildApiErrorHtml(string errors)
    {
        string encoded = System.Net.WebUtility.HtmlEncode(errors);
        return ApiErrorHtmlPrefix + encoded + ApiErrorHtmlSuffix;
    }
}
