using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Captcha;
using HomeworkCentral.Api.Captcha.Maze;
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
  public async Task Register_with_correct_captcha_and_human_like_behavior_grants_verified_user_instead_of_guest()
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
      Captcha = BuildCorrectSubmission(challenge!),
    };

    HttpResponseMessage registerResponse = await client.PostAsJsonAsync("/api/auth/register", register);
    Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

    AuthResponse? registered = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();
    Assert.NotNull(registered);
    Assert.Contains("VerifiedUser", registered!.User.Roles);
    Assert.DoesNotContain("Guest", registered.User.Roles);
  }

  [SkippableFact]
  public async Task Register_with_correct_captcha_but_bot_like_behavior_still_only_grants_guest()
  {
    Skip.IfNot(fixture.IsDatabaseAvailable, fixture.SkipReason);
    HttpClient client = fixture.RequireClient();
    string suffix = Guid.NewGuid().ToString("N")[..8];

    HttpResponseMessage challengeResponse = await client.GetAsync("/api/captcha/challenge");
    Assert.Equal(HttpStatusCode.OK, challengeResponse.StatusCode);
    CaptchaChallengeDto? challenge = await challengeResponse.Content.ReadFromJsonAsync<CaptchaChallengeDto>();
    Assert.NotNull(challenge);

    CaptchaSubmissionDto submission = BuildCorrectSubmission(challenge!);
    submission.Behavior = new CaptchaBehaviorDto
    {
      MouseSamples = null,
      KeyIntervalsMs = null,
      TotalDurationMs = 150,
      WebdriverFlag = true,
      InteractionCount = 0,
    };

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

  /// <summary>Builds a submission that correctly solves whichever challenge type was issued, paired
  /// with telemetry engineered to comfortably clear the dynamic risk threshold for a first-time,
  /// IP-matched signup attempt (base 0.65 + at most a small new-identity/failure adjustment).</summary>
  private static CaptchaSubmissionDto BuildCorrectSubmission(CaptchaChallengeDto challenge)
  {
    CaptchaSubmissionDto submission = new()
    {
      ChallengeId = challenge.ChallengeId,
      Behavior = GoodBehavior(),
    };

    switch (challenge.Type)
    {
      case "maze" when HasPath(challenge.Maze!):
        submission.MazePath = SolveMaze(challenge.Maze!);
        break;
      case "maze":
        // Some maze challenges are deliberately generated with no path from A to B at all;
        // correctly recognizing that is itself the correct answer.
        submission.MazeUnsolvableClaim = true;
        break;
      case "tileRotate":
        submission.TileRotationClicks = challenge.TileRotate!.Tiles
          .Select(tile => (tile.TargetRotationSteps - tile.InitialRotationSteps + 8) % 8)
          .ToList();
        break;
      default:
        submission.Answer = SolveText(challenge.Content!);
        submission.Behavior!.KeyIntervalsMs = [120, 95, 180, 60, 140, 110, 200, 85];
        break;
    }

    return submission;
  }

  private static string SolveText(string content)
  {
    System.Text.RegularExpressions.Match arithmetic =
      System.Text.RegularExpressions.Regex.Match(content, @"^(\d+) \+ (\d+)$");
    if (arithmetic.Success)
    {
      int a = int.Parse(arithmetic.Groups[1].Value);
      int b = int.Parse(arithmetic.Groups[2].Value);
      return (a + b).ToString();
    }

    // Code challenges: the content is the answer itself.
    return content;
  }

  private static bool HasPath(MazeDto maze)
  {
    if (maze.StartIndex == maze.EndIndex)
      return true;

    bool[] visited = new bool[maze.CellWalls.Length];
    Queue<int> queue = new();
    queue.Enqueue(maze.StartIndex);
    visited[maze.StartIndex] = true;

    while (queue.Count > 0)
    {
      int current = queue.Dequeue();
      if (current == maze.EndIndex)
        return true;

      int walls = maze.CellWalls[current];
      int x = current % maze.Width;
      int y = current / maze.Width;
      if ((walls & 1) != 0 && y > 0 && !visited[current - maze.Width]) { visited[current - maze.Width] = true; queue.Enqueue(current - maze.Width); }
      if ((walls & 2) != 0 && x < maze.Width - 1 && !visited[current + 1]) { visited[current + 1] = true; queue.Enqueue(current + 1); }
      if ((walls & 4) != 0 && y < maze.Height - 1 && !visited[current + maze.Width]) { visited[current + maze.Width] = true; queue.Enqueue(current + maze.Width); }
      if ((walls & 8) != 0 && x > 0 && !visited[current - 1]) { visited[current - 1] = true; queue.Enqueue(current - 1); }
    }

    return false;
  }

  private static List<int> SolveMaze(MazeDto maze)
  {
    int cellCount = maze.Width * maze.Height;
    int[] previous = new int[cellCount];
    Array.Fill(previous, -1);
    bool[] visited = new bool[cellCount];
    Queue<int> queue = new();
    queue.Enqueue(maze.StartIndex);
    visited[maze.StartIndex] = true;

    while (queue.Count > 0)
    {
      int current = queue.Dequeue();
      if (current == maze.EndIndex)
        break;

      int walls = maze.CellWalls[current];
      int x = current % maze.Width;
      int y = current / maze.Width;
      List<int> neighbors = new();
      if ((walls & 1) != 0 && y > 0) neighbors.Add(current - maze.Width);
      if ((walls & 2) != 0 && x < maze.Width - 1) neighbors.Add(current + 1);
      if ((walls & 4) != 0 && y < maze.Height - 1) neighbors.Add(current + maze.Width);
      if ((walls & 8) != 0 && x > 0) neighbors.Add(current - 1);

      foreach (int neighbor in neighbors)
      {
        if (visited[neighbor]) continue;
        visited[neighbor] = true;
        previous[neighbor] = current;
        queue.Enqueue(neighbor);
      }
    }

    List<int> reversed = new();
    int step = maze.EndIndex;
    while (step != maze.StartIndex)
    {
      reversed.Add(step);
      step = previous[step];
    }
    reversed.Add(maze.StartIndex);
    reversed.Reverse();
    return reversed;
  }

  private static CaptchaBehaviorDto GoodBehavior()
  {
    int[] dxs = [3, 15, -2, 20, -1, 18, 2, 16, -3, 14];
    int[] dys = [2, -10, 3, -12, 2, 9, -1, -11, 4, 8];
    int[] dts = [80, 20, 90, 15, 85, 18, 95, 16, 88, 22];

    List<MouseSampleDto> mouseSamples = new();
    int x = 10, y = 10, t = 0;
    for (int i = 0; i < dxs.Length; i++)
    {
      x += dxs[i];
      y += dys[i];
      t += dts[i];
      mouseSamples.Add(new MouseSampleDto { X = x, Y = y, TMs = t });
    }

    return new CaptchaBehaviorDto
    {
      MouseSamples = mouseSamples,
      TotalDurationMs = 4000,
      WebdriverFlag = false,
      InteractionCount = 5,
    };
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
