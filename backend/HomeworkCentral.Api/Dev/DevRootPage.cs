namespace HomeworkCentral.Api.Dev;

public static class DevRootPage
{
    public const string FaviconPath = "/favicon.svg";

    public static IResult ForbiddenDirectoryPage() =>
        Results.Content(ForbiddenHtml, "text/html; charset=utf-8", statusCode: 403);

    public static IResult ApiErrorPage(string errors) =>
        Results.Content(BuildApiErrorHtml(errors), "text/html; charset=utf-8", statusCode: 500);

    public static IResult Favicon()
    {
        string faviconPath = Path.Combine(AppContext.BaseDirectory, "Dev", "favicon.svg");
        if (!File.Exists(faviconPath))
            return Results.NotFound();

        return Results.File(faviconPath, "image/svg+xml");
    }

    private const string ForbiddenHtml = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="UTF-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1.0" />
          <title>403 - Forbidden</title>
          <link rel="icon" type="image/svg+xml" href="/favicon.svg" />
          <link rel="preconnect" href="https://fonts.googleapis.com" />
          <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
          <link href="https://fonts.googleapis.com/css2?family=Lato:wght@400;700&display=swap" rel="stylesheet" />
          <style>
            * {
              position: relative;
              margin: 0;
              padding: 0;
              box-sizing: border-box;
              font-family: 'Lato', sans-serif;
            }

            body {
              min-height: 100vh;
              display: flex;
              flex-direction: column;
              justify-content: center;
              align-items: center;
              background: linear-gradient(to bottom right, #eef2f7, #b8c4d4);
              padding: 2rem 1rem;
            }

            .page {
              display: flex;
              flex-direction: column;
              align-items: center;
              width: min(100%, 32rem);
            }

            .lock {
              border-radius: 5px;
              width: 55px;
              height: 45px;
              background-color: #334155;
              animation: dip 1s;
              animation-delay: 1.5s;
            }

            .lock::before,
            .lock::after {
              content: '';
              position: absolute;
              border-left: 5px solid #334155;
              height: 20px;
              width: 15px;
              left: calc(50% - 12.5px);
            }

            .lock::before {
              top: -30px;
              border: 5px solid #334155;
              border-bottom-color: transparent;
              border-radius: 15px 15px 0 0;
              height: 30px;
              animation: lock 2s, spin 2s;
            }

            .lock::after {
              top: -10px;
              border-right: 5px solid transparent;
              animation: spin 2s;
            }

            .message {
              margin-top: 2.5rem;
              width: 100%;
              background: #ffffff;
              border-radius: 12px;
              box-shadow: 0 10px 30px rgba(15, 23, 42, 0.12);
              overflow: hidden;
              text-align: center;
            }

            .server-error {
              background: #808080;
              color: #ffffff;
              padding: 0.65rem 1rem;
              font-size: 1.1rem;
              font-weight: 700;
              letter-spacing: 0.02em;
            }

            .message-body {
              padding: 1.75rem 1.5rem 2rem;
            }

            .forbidden {
              color: #cc0000;
              font-size: 1.35rem;
              font-weight: 700;
              line-height: 1.35;
              margin-bottom: 1rem;
            }

            .detail {
              color: #111827;
              font-size: 1rem;
              line-height: 1.55;
            }

            .hint {
              margin-top: 1.25rem;
              color: #64748b;
              font-size: 0.9rem;
            }

            @keyframes lock {
              0% { top: -45px; }
              65% { top: -45px; }
              100% { top: -30px; }
            }

            @keyframes spin {
              0% {
                transform: scaleX(-1);
                left: calc(50% - 30px);
              }
              65% {
                transform: scaleX(1);
                left: calc(50% - 12.5px);
              }
            }

            @keyframes dip {
              0% { transform: translateY(0); }
              50% { transform: translateY(10px); }
              100% { transform: translateY(0); }
            }
          </style>
        </head>
        <body>
          <main class="page">
            <div class="lock" aria-hidden="true"></div>
            <div class="message">
              <div class="server-error">Server Error</div>
              <div class="message-body">
                <h1 class="forbidden">403 - Forbidden Access is denied.</h1>
                <p class="detail">You do not have access to this directory or page while using the credentials you supplied.</p>
                <p class="hint">Homework Central API is running. Use the frontend or API routes instead.</p>
              </div>
            </div>
          </main>
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
          <link rel="icon" type="image/svg+xml" href="/favicon.svg" />
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
