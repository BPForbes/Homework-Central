using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Captcha;
using HomeworkCentral.Api.Captcha.FCaptcha;
using HomeworkCentral.Api.Dev;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Tenancy;
using HomeworkCentral.Api.Tests.Captcha;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace HomeworkCentral.Api.Tests.Integration;

[Collection(nameof(IntegrationTestCollection))]
public class ApiIntegrationTests(IntegrationTestFixture fixture)
{
  [SkippableFact]
  public async Task Healthz_returns_ok()
  {
    Skip.IfNot(fixture.IsDatabaseAvailable, fixture.SkipReason);
    HttpClient client = fixture.RequireClient();
    HttpResponseMessage response = await client.GetAsync("/healthz");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Dictionary<string, string>? body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
    Assert.NotNull(body);
    Assert.Equal("healthy", body["status"]);
  }

  [SkippableFact]
  public async Task Register_and_login_round_trip_succeeds()
  {
    Skip.IfNot(fixture.IsDatabaseAvailable, fixture.SkipReason);
    HttpClient client = fixture.RequireClient();
    string suffix = Guid.NewGuid().ToString("N")[..8];
    RegisterRequest register = new()
    {
      Email = $"ci-user-{suffix}@example.com",
      Username = $"ciuser{suffix}",
      Password = "Password123!",
    };

    HttpResponseMessage registerResponse = await client.PostAsJsonAsync("/api/auth/register", register);
    Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

    AuthResponse? registered = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();
    Assert.NotNull(registered);
    Assert.False(string.IsNullOrWhiteSpace(registered.AccessToken));
    Assert.Equal(register.Email, registered.User.Email);
    Assert.Equal(AccountClass.RealAccount.ToString(), registered.User.AccountClass);
    Assert.Equal(AccountClass.RealAccount.ToString(), ReadAccountClassClaim(registered.AccessToken));

    LoginRequest login = new()
    {
      Email = register.Email,
      Password = register.Password,
    };

    HttpResponseMessage loginResponse = await client.PostAsJsonAsync("/api/auth/login", login);
    Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

    AuthResponse? loggedIn = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
    Assert.NotNull(loggedIn);
    Assert.False(string.IsNullOrWhiteSpace(loggedIn.AccessToken));
    Assert.Equal(registered.User.UserId, loggedIn.User.UserId);
    Assert.Equal(AccountClass.RealAccount.ToString(), loggedIn.User.AccountClass);
    Assert.Contains("Guest", registered.User.Roles);
    Assert.DoesNotContain("VerifiedUser", registered.User.Roles);
  }

  [SkippableFact]
  public async Task Register_with_a_confident_fcaptcha_verdict_grants_verified_user_instead_of_guest()
  {
    Skip.IfNot(fixture.IsDatabaseAvailable, fixture.SkipReason);
    HttpClient client = fixture.RequireClient();
    string suffix = Guid.NewGuid().ToString("N")[..8];

    // A confident FCaptcha verdict is sufficient on its own — no in-house puzzle needs solving.
    fixture.FCaptchaVerifier.NextResult = new FCaptchaVerification(true, 0.9);

    HttpResponseMessage challengeResponse = await client.GetAsync("/api/captcha/challenge");
    Assert.Equal(HttpStatusCode.OK, challengeResponse.StatusCode);
    CaptchaChallengeDto? challenge = await challengeResponse.Content.ReadFromJsonAsync<CaptchaChallengeDto>();
    Assert.NotNull(challenge);

    RegisterRequest register = new()
    {
      Email = $"ci-verified-{suffix}@example.com",
      Username = $"civerified{suffix}",
      Password = "Password123!",
      Captcha = new CaptchaSubmissionDto { ChallengeId = challenge!.ChallengeId, FCaptchaToken = "confident-token" },
    };

    HttpResponseMessage registerResponse = await client.PostAsJsonAsync("/api/auth/register", register);
    Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

    AuthResponse? registered = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();
    Assert.NotNull(registered);
    Assert.Contains("VerifiedUser", registered!.User.Roles);
    Assert.DoesNotContain("Guest", registered.User.Roles);
  }

  [SkippableFact]
  public async Task Register_with_uncertain_verdict_and_correctly_solved_puzzle_grants_verified_user()
  {
    Skip.IfNot(fixture.IsDatabaseAvailable, fixture.SkipReason);
    HttpClient client = fixture.RequireClient();
    string suffix = Guid.NewGuid().ToString("N")[..8];

    // Below AllowTrustScore -> falls back to requiring the puzzle too; 0.6 comfortably clears the
    // dynamic risk threshold for a first-time, IP-matched signup attempt (base 0.40 + a small
    // new-identity adjustment).
    fixture.FCaptchaVerifier.NextResult = new FCaptchaVerification(true, 0.6);

    HttpResponseMessage challengeResponse = await client.GetAsync("/api/captcha/challenge");
    Assert.Equal(HttpStatusCode.OK, challengeResponse.StatusCode);
    CaptchaChallengeDto? challenge = await challengeResponse.Content.ReadFromJsonAsync<CaptchaChallengeDto>();
    Assert.NotNull(challenge);

    CaptchaSubmissionDto submission = BuildCorrectSubmission(challenge!);
    submission.FCaptchaToken = "uncertain-token";

    RegisterRequest register = new()
    {
      Email = $"ci-puzzle-verified-{suffix}@example.com",
      Username = $"cipuzzleverified{suffix}",
      Password = "Password123!",
      Captcha = submission,
    };

    HttpResponseMessage registerResponse = await client.PostAsJsonAsync("/api/auth/register", register);
    Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

    AuthResponse? registered = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();
    Assert.NotNull(registered);
    Assert.Contains("VerifiedUser", registered!.User.Roles);
    Assert.DoesNotContain("Guest", registered.User.Roles);
  }

  [SkippableFact]
  public async Task Register_with_an_invalid_fcaptcha_token_only_grants_guest_even_with_a_correct_puzzle_answer()
  {
    Skip.IfNot(fixture.IsDatabaseAvailable, fixture.SkipReason);
    HttpClient client = fixture.RequireClient();
    string suffix = Guid.NewGuid().ToString("N")[..8];

    fixture.FCaptchaVerifier.NextResult = new FCaptchaVerification(false, 0.0);

    HttpResponseMessage challengeResponse = await client.GetAsync("/api/captcha/challenge");
    Assert.Equal(HttpStatusCode.OK, challengeResponse.StatusCode);
    CaptchaChallengeDto? challenge = await challengeResponse.Content.ReadFromJsonAsync<CaptchaChallengeDto>();
    Assert.NotNull(challenge);

    CaptchaSubmissionDto submission = BuildCorrectSubmission(challenge!);
    submission.FCaptchaToken = "invalid-token";

    RegisterRequest register = new()
    {
      Email = $"ci-botlike-{suffix}@example.com",
      Username = $"cibotlike{suffix}",
      Password = "Password123!",
      Captcha = submission,
    };

    HttpResponseMessage registerResponse = await client.PostAsJsonAsync("/api/auth/register", register);
    Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

    AuthResponse? registered = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();
    Assert.NotNull(registered);
    Assert.Contains("Guest", registered!.User.Roles);
    Assert.DoesNotContain("VerifiedUser", registered.User.Roles);
  }

  private static CaptchaSubmissionDto BuildCorrectSubmission(CaptchaChallengeDto challenge) =>
    CaptchaTestSolvers.BuildCorrectSubmission(challenge);

  private static string? ReadAccountClassClaim(string accessToken)
  {
    JwtSecurityTokenHandler handler = new();
    JwtSecurityToken jwt = handler.ReadJwtToken(accessToken);
    return jwt.Claims.FirstOrDefault(c => c.Type == TenancyConstants.AccountClassClaimName)?.Value;
  }
}

[CollectionDefinition(nameof(IntegrationTestCollection))]
public sealed class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>;

public sealed class IntegrationTestFixture : WebApplicationFactory<Program>
{
  private readonly string _connectionString;
  private readonly string _adminConnectionString;
  private HttpClient? _client;

  public bool IsDatabaseAvailable { get; }
  public string SkipReason { get; }

  /// <summary>Replaces the real (self-hosted, HTTP-calling) FCaptchaVerifier for the whole test
  /// host, so these tests never need a live FCaptcha instance reachable in CI — each test scripts
  /// the verdict it wants via <see cref="FakeFCaptchaVerifier.NextResult"/>.</summary>
  public FakeFCaptchaVerifier FCaptchaVerifier { get; } = new();

  public IntegrationTestFixture()
  {
    _connectionString = Environment.GetEnvironmentVariable("TEST_DATABASE_URL")
      ?? "Host=localhost;Port=5432;Database=homework_central_test;Username=postgres;Password=postgres";
    _adminConnectionString = Environment.GetEnvironmentVariable("TEST_POSTGRES_ADMIN_URL")
      ?? new NpgsqlConnectionStringBuilder(_connectionString) { Database = "postgres" }.ConnectionString;

    IsDatabaseAvailable = CanConnectToDatabase(_connectionString);
    SkipReason = "Integration tests require Postgres at TEST_DATABASE_URL.";
  }

  public HttpClient RequireClient()
  {
    _client ??= CreateClient();
    WaitForReady(_client);
    return _client;
  }

  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    builder.UseEnvironment(Environments.Development);
    builder.UseSetting("ConnectionStrings:MasterConnection", _connectionString);
    builder.UseSetting("ConnectionStrings:PostgresAdmin", _adminConnectionString);
    builder.UseSetting("Tenancy:ClusterEnvironment", "dev");
    builder.UseSetting("Jwt:Secret", "integration-test-jwt-secret-key-32chars!");
    builder.UseSetting("FCaptcha:Secret", "integration-test-fcaptcha-secret-key!");
    builder.UseSetting(DevBypass.EnvVarName, "0");

    builder.ConfigureTestServices(services =>
    {
      services.RemoveAll<IFCaptchaVerifier>();
      services.AddSingleton<IFCaptchaVerifier>(FCaptchaVerifier);
    });
  }

  /// <summary>
  /// Migrate/seed now runs in a BackgroundService after listen; wait until /healthz is healthy
  /// so tests do not race the warmup window.
  /// </summary>
  private static void WaitForReady(HttpClient client)
  {
    DateTime deadline = DateTime.UtcNow.AddSeconds(60);
    while (DateTime.UtcNow < deadline)
    {
      try
      {
        HttpResponseMessage response = client.GetAsync("/healthz").GetAwaiter().GetResult();
        if (response.IsSuccessStatusCode)
        {
          Dictionary<string, object>? body = response.Content
            .ReadFromJsonAsync<Dictionary<string, object>>()
            .GetAwaiter()
            .GetResult();
          if (body is not null
              && body.TryGetValue("status", out object? status)
              && string.Equals(status?.ToString(), "healthy", StringComparison.Ordinal))
          {
            return;
          }
        }
      }
      catch
      {
        // Warmup still in progress or host not accepting yet.
      }

      Thread.Sleep(100);
    }

    throw new TimeoutException("Timed out waiting for /healthz to report healthy after host start.");
  }

  private static bool CanConnectToDatabase(string connectionString)
  {
    try
    {
      using NpgsqlConnection connection = new(connectionString);
      connection.Open();
      return true;
    }
    catch
    {
      return false;
    }
  }
}
