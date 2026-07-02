namespace HomeworkCentral.Api.Captcha;

/// <summary>Bit values in <see cref="MazeDto.CellWalls"/>: which side of a cell has an open passage.</summary>
internal static class MazeDirections
{
    public const int North = 1;
    public const int East = 2;
    public const int South = 4;
    public const int West = 8;
}

/// <summary>
/// Builds maze challenges on a <c>width</c> x <c>height</c> grid. Most of the time this produces a
/// genuinely solvable maze: a pool of independently generated candidates, each with a randomized
/// start/end cell pair, scored by how many nodes an A* search has to expand to solve it, keeping
/// the most complex candidate. The rest of the time it deliberately produces two maze regions with
/// no passage between them at all — start and end land in different regions, so there is no path,
/// and correctly recognizing that (rather than hunting forever for a route that doesn't exist) is
/// itself the intended solve; see <see cref="CaptchaSubmissionDto.MazeUnsolvableClaim"/>.
/// </summary>
internal static class MazeGenerator
{
    private const int PoolSize = 5;
    private const double UnsolvableProbability = 0.3;

    public static MazeDto Generate(int width, int height)
    {
        int minDistance = MinimumDistance(width, height);

        return Random.Shared.NextDouble() < UnsolvableProbability
            ? GenerateUnsolvable(width, height, minDistance)
            : GenerateMostComplexSolvable(width, height, minDistance);
    }

    /// <summary>True if any path connects <see cref="MazeDto.StartIndex"/> to
    /// <see cref="MazeDto.EndIndex"/>. Used both to validate a player's "no path exists" claim and,
    /// defensively, to confirm a freshly built "unsolvable" maze actually has none.</summary>
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

            foreach (int neighbor in OpenNeighbors(maze, current))
            {
                if (visited[neighbor])
                    continue;

                visited[neighbor] = true;
                queue.Enqueue(neighbor);
            }
        }

        return false;
    }

    /// <summary>Builds <see cref="PoolSize"/> independent perfect mazes, each with its own
    /// randomized (but sufficiently far apart) start/end pair, and keeps the one an A* search finds
    /// hardest to solve.</summary>
    private static MazeDto GenerateMostComplexSolvable(int width, int height, int minDistance)
    {
        List<int> allCells = RegionCells(0, width, 0, height, width);

        MazeDto? best = null;
        int bestComplexity = -1;

        for (int i = 0; i < PoolSize; i++)
        {
            int[] walls = CarveWalls(width, height, allCells);
            (int start, int end) = PickFarApartCells(allCells, allCells, width, minDistance);
            MazeDto candidate = new(width, height, walls, start, end);

            int complexity = AStarNodesExpanded(candidate);
            if (complexity > bestComplexity)
            {
                bestComplexity = complexity;
                best = candidate;
            }
        }

        return best!;
    }

    /// <summary>Splits the grid into two disjoint rectangular regions and carves each one as its
    /// own self-contained perfect maze, so no passage ever crosses the seam — genuinely
    /// disconnected, not merely hard to find. Start lands in one region, end in the other.</summary>
    private static MazeDto GenerateUnsolvable(int width, int height, int minDistance)
    {
        bool splitVertically = Random.Shared.Next(2) == 0;
        List<int> region1;
        List<int> region2;

        if (splitVertically)
        {
            int splitCol = SplitPoint(width);
            region1 = RegionCells(0, splitCol, 0, height, width);
            region2 = RegionCells(splitCol, width, 0, height, width);
        }
        else
        {
            int splitRow = SplitPoint(height);
            region1 = RegionCells(0, width, 0, splitRow, width);
            region2 = RegionCells(0, width, splitRow, height, width);
        }

        int[] walls = new int[width * height];
        CarveWallsInto(walls, width, height, region1);
        CarveWallsInto(walls, width, height, region2);

        (int start, int end) = PickFarApartCells(region1, region2, width, minDistance);
        MazeDto maze = new(width, height, walls, start, end);

        // The region split guarantees no path exists, but confirm rather than assume — if this
        // ever somehow produced a connected maze, fall back to an ordinary solvable one rather
        // than silently issuing a broken "unsolvable" challenge.
        return HasPath(maze) ? GenerateMostComplexSolvable(width, height, minDistance) : maze;
    }

    /// <summary>How far apart (Manhattan distance) start and end must be — scales with maze size so
    /// bigger mazes don't spawn a trivially adjacent A/B pair.</summary>
    private static int MinimumDistance(int width, int height) => (width + height) / 2;

    private static int SplitPoint(int dimension)
    {
        int margin = Math.Max(2, dimension / 3);
        int low = margin;
        int high = dimension - margin;
        return high <= low ? dimension / 2 : Random.Shared.Next(low, high + 1);
    }

    private static List<int> RegionCells(int xStart, int xEnd, int yStart, int yEnd, int width)
    {
        List<int> cells = new((xEnd - xStart) * (yEnd - yStart));
        for (int y = yStart; y < yEnd; y++)
            for (int x = xStart; x < xEnd; x++)
                cells.Add((y * width) + x);

        return cells;
    }

    private static (int Start, int End) PickFarApartCells(List<int> pool1, List<int> pool2, int width, int minDistance)
    {
        int bestA = pool1[0];
        int bestB = pool2[0];
        int bestDistance = -1;

        for (int attempt = 0; attempt < 200; attempt++)
        {
            int a = pool1[Random.Shared.Next(pool1.Count)];
            int b = pool2[Random.Shared.Next(pool2.Count)];
            if (a == b)
                continue;

            int distance = ManhattanDistance(a, b, width);
            if (distance > bestDistance)
            {
                bestDistance = distance;
                bestA = a;
                bestB = b;
            }

            if (distance >= minDistance)
                return (a, b);
        }

        // Never fails outright: falls back to the farthest-apart pair seen across the attempts.
        return (bestA, bestB);
    }

    private static int ManhattanDistance(int a, int b, int width)
    {
        int ax = a % width, ay = a / width;
        int bx = b % width, by = b / width;
        return Math.Abs(ax - bx) + Math.Abs(ay - by);
    }

    private static int[] CarveWalls(int width, int height, List<int> regionCells)
    {
        int[] walls = new int[width * height];
        CarveWallsInto(walls, width, height, regionCells);
        return walls;
    }

    /// <summary>Randomized depth-first backtracking restricted to <paramref name="regionCells"/>: a
    /// spanning tree over just that region, so every cell in it reaches every other cell in it by
    /// exactly one simple path, and no passage is ever carved to a cell outside the region.</summary>
    private static void CarveWallsInto(int[] walls, int width, int height, List<int> regionCells)
    {
        HashSet<int> region = [.. regionCells];
        HashSet<int> visited = new();
        Stack<int> stack = new();

        int start = regionCells[Random.Shared.Next(regionCells.Count)];
        visited.Add(start);
        stack.Push(start);

        while (stack.Count > 0)
        {
            int current = stack.Peek();
            List<(int Neighbor, int DirBit, int OppositeBit)> options = UnvisitedNeighbors(current, width, height, visited, region);

            if (options.Count == 0)
            {
                stack.Pop();
                continue;
            }

            (int neighbor, int dirBit, int oppositeBit) = options[Random.Shared.Next(options.Count)];
            walls[current] |= dirBit;
            walls[neighbor] |= oppositeBit;
            visited.Add(neighbor);
            stack.Push(neighbor);
        }
    }

    private static List<(int Neighbor, int DirBit, int OppositeBit)> UnvisitedNeighbors(
        int cell, int width, int height, HashSet<int> visited, HashSet<int> region)
    {
        List<(int, int, int)> options = new(4);
        int x = cell % width;
        int y = cell / width;

        if (y > 0 && CanVisit(cell - width, region, visited))
            options.Add((cell - width, MazeDirections.North, MazeDirections.South));
        if (x < width - 1 && CanVisit(cell + 1, region, visited))
            options.Add((cell + 1, MazeDirections.East, MazeDirections.West));
        if (y < height - 1 && CanVisit(cell + width, region, visited))
            options.Add((cell + width, MazeDirections.South, MazeDirections.North));
        if (x > 0 && CanVisit(cell - 1, region, visited))
            options.Add((cell - 1, MazeDirections.West, MazeDirections.East));

        return options;
    }

    private static bool CanVisit(int cell, HashSet<int> region, HashSet<int> visited) =>
        region.Contains(cell) && !visited.Contains(cell);

    /// <summary>Runs A* (Manhattan-distance heuristic — admissible, since every move costs exactly
    /// 1 on a cardinal grid) from start to end and returns how many cells were popped off the open
    /// set before the goal was reached. A maze whose only true path winds away from the direct line
    /// between A and B forces A* to expand and discard more misleading branches before it can
    /// confirm the real route, so this doubles as a complexity score for picking the hardest maze
    /// out of a pool of candidates.</summary>
    private static int AStarNodesExpanded(MazeDto maze)
    {
        int cellCount = maze.Width * maze.Height;
        bool[] closed = new bool[cellCount];
        int[] gScore = new int[cellCount];
        Array.Fill(gScore, int.MaxValue);
        gScore[maze.StartIndex] = 0;

        PriorityQueue<int, int> open = new();
        open.Enqueue(maze.StartIndex, Heuristic(maze.StartIndex, maze.EndIndex, maze.Width));

        int expanded = 0;

        while (open.Count > 0)
        {
            int current = open.Dequeue();
            if (closed[current])
                continue;

            closed[current] = true;
            expanded++;

            if (current == maze.EndIndex)
                break;

            foreach (int neighbor in OpenNeighbors(maze, current))
            {
                int tentativeG = gScore[current] + 1;
                if (tentativeG < gScore[neighbor])
                {
                    gScore[neighbor] = tentativeG;
                    open.Enqueue(neighbor, tentativeG + Heuristic(neighbor, maze.EndIndex, maze.Width));
                }
            }
        }

        return expanded;
    }

    private static int Heuristic(int cell, int goal, int width)
    {
        int cx = cell % width, cy = cell / width;
        int gx = goal % width, gy = goal / width;
        return Math.Abs(cx - gx) + Math.Abs(cy - gy);
    }

    private static IEnumerable<int> OpenNeighbors(MazeDto maze, int cell)
    {
        int walls = maze.CellWalls[cell];
        int x = cell % maze.Width;
        int y = cell / maze.Width;

        if ((walls & MazeDirections.North) != 0 && y > 0) yield return cell - maze.Width;
        if ((walls & MazeDirections.East) != 0 && x < maze.Width - 1) yield return cell + 1;
        if ((walls & MazeDirections.South) != 0 && y < maze.Height - 1) yield return cell + maze.Width;
        if ((walls & MazeDirections.West) != 0 && x > 0) yield return cell - 1;
    }
}
