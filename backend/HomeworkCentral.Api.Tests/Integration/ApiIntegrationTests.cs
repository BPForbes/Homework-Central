using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Captcha;
using HomeworkCentral.Api.Dev;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
  public async Task Register_with_correct_captcha_grants_verified_user_instead_of_guest()
  {
    Skip.IfNot(fixture.IsDatabaseAvailable, fixture.SkipReason);
    HttpClient client = fixture.RequireClient();
    string suffix = Guid.NewGuid().ToString("N")[..8];

    HttpResponseMessage challengeResponse = await client.GetAsync("/api/captcha/challenge");
    Assert.Equal(HttpStatusCode.OK, challengeResponse.StatusCode);
    CaptchaChallengeDto? challenge = await challengeResponse.Content.ReadFromJsonAsync<CaptchaChallengeDto>();
    Assert.NotNull(challenge);

    RegisterRequest register = new()
    {
      Email = $"ci-verified-{suffix}@example.com",
      Username = $"civerified{suffix}",
      Password = "Password123!",
      CaptchaChallengeId = challenge!.ChallengeId,
      CaptchaAnswer = SolveChallenge(challenge.Prompt),
    };

    HttpResponseMessage registerResponse = await client.PostAsJsonAsync("/api/auth/register", register);
    Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

    AuthResponse? registered = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();
    Assert.NotNull(registered);
    Assert.Contains("VerifiedUser", registered!.User.Roles);
    Assert.DoesNotContain("Guest", registered.User.Roles);
  }

  private static string SolveChallenge(string prompt)
  {
    System.Text.RegularExpressions.Match arithmetic =
      System.Text.RegularExpressions.Regex.Match(prompt, @"what is (\d+) \+ (\d+)\?");
    if (arithmetic.Success)
    {
      int a = int.Parse(arithmetic.Groups[1].Value);
      int b = int.Parse(arithmetic.Groups[2].Value);
      return (a + b).ToString();
    }

    System.Text.RegularExpressions.Match code =
      System.Text.RegularExpressions.Regex.Match(prompt, @"exactly: (\w+)");
    if (code.Success)
      return code.Groups[1].Value;

    throw new InvalidOperationException($"Unrecognized captcha prompt format: {prompt}");
  }

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

  public IntegrationTestFixture()
  {
    _connectionString = Environment.GetEnvironmentVariable("TEST_DATABASE_URL")
      ?? "Host=localhost;Port=5432;Database=homework_central_test;Username=postgres;Password=postgres";
    _adminConnectionString = Environment.GetEnvironmentVariable("TEST_POSTGRES_ADMIN_URL")
      ?? new NpgsqlConnectionStringBuilder(_connectionString) { Database = "postgres" }.ConnectionString;

    IsDatabaseAvailable = CanConnectToDatabase(_connectionString);
    SkipReason = "Integration tests require Postgres at TEST_DATABASE_URL.";
  }

  public HttpClient RequireClient() => _client ??= CreateClient();

  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    builder.UseEnvironment(Environments.Development);
    builder.UseSetting("ConnectionStrings:MasterConnection", _connectionString);
    builder.UseSetting("ConnectionStrings:PostgresAdmin", _adminConnectionString);
    builder.UseSetting("Tenancy:ClusterEnvironment", "dev");
    builder.UseSetting("Jwt:Secret", "integration-test-jwt-secret-key-32chars!");
    builder.UseSetting(DevBypass.EnvVarName, "0");
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
