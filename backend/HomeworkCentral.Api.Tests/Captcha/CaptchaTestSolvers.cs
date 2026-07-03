using System.Text.RegularExpressions;
using HomeworkCentral.Api.Captcha;
using HomeworkCentral.Api.Captcha.ArrowMatch;
using HomeworkCentral.Api.Captcha.Maze;

namespace HomeworkCentral.Api.Tests.Captcha;

/// <summary>Shared helpers for building correct captcha submissions in unit and integration tests.</summary>
internal static class CaptchaTestSolvers
{
    public static CaptchaSubmissionDto BuildCorrectSubmission(CaptchaChallengeDto challenge, string fCaptchaToken = "token")
    {
        CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            FCaptchaToken = fCaptchaToken,
        };

        switch (challenge.Type)
        {
            case "maze" when HasPath(challenge.Maze!):
                submission.MazePath = SolveMaze(challenge.Maze!);
                break;
            case "maze":
                submission.MazeUnsolvableClaim = true;
                break;
            case "tileRotate":
                submission.TileRotationClicks = SolveTileRotate(challenge.TileRotate!);
                break;
            default:
                submission.Answer = SolveText(challenge.Content!);
                break;
        }

        return submission;
    }

    public static bool HasPath(MazeDto maze)
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

            foreach (int neighbor in MazeNeighbors(maze, current))
            {
                if (visited[neighbor])
                    continue;

                visited[neighbor] = true;
                queue.Enqueue(neighbor);
            }
        }

        return false;
    }

    public static string SolveText(string content)
    {
        Match arithmetic = Regex.Match(content, @"^(\d+) \+ (\d+)$");
        if (arithmetic.Success)
        {
            int a = int.Parse(arithmetic.Groups[1].Value);
            int b = int.Parse(arithmetic.Groups[2].Value);
            return (a + b).ToString();
        }

        return content;
    }

    public static List<int> SolveMaze(MazeDto maze)
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

            foreach (int neighbor in MazeNeighbors(maze, current))
            {
                if (visited[neighbor])
                    continue;

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

    public static List<int> SolveTileRotate(TileRotateDto tileRotate) =>
        tileRotate.Tiles.Select(tile => (tile.TargetRotationSteps - tile.InitialRotationSteps + 8) % 8).ToList();

    private static IEnumerable<int> MazeNeighbors(MazeDto maze, int cell)
    {
        int walls = maze.CellWalls[cell];
        int x = cell % maze.Width;
        int y = cell / maze.Width;

        if ((walls & 1) != 0 && y > 0) yield return cell - maze.Width;
        if ((walls & 2) != 0 && x < maze.Width - 1) yield return cell + 1;
        if ((walls & 4) != 0 && y < maze.Height - 1) yield return cell + maze.Width;
        if ((walls & 8) != 0 && x > 0) yield return cell - 1;
    }
}
