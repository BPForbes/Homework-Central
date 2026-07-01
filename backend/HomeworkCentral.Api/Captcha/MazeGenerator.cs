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
/// Generates a "perfect" maze via randomized depth-first backtracking: a spanning tree over the
/// grid, so there is exactly one simple path between the start (cell 0) and end (last cell) —
/// guaranteed solvable, no loops, no isolated regions.
/// </summary>
internal static class MazeGenerator
{
    public static MazeDto Generate(int width, int height)
    {
        int cellCount = width * height;
        int[] walls = new int[cellCount];
        bool[] visited = new bool[cellCount];
        Stack<int> stack = new();

        int start = 0;
        visited[start] = true;
        stack.Push(start);

        while (stack.Count > 0)
        {
            int current = stack.Peek();
            List<(int Neighbor, int DirBit, int OppositeBit)> options = UnvisitedNeighbors(current, width, height, visited);

            if (options.Count == 0)
            {
                stack.Pop();
                continue;
            }

            (int neighbor, int dirBit, int oppositeBit) = options[Random.Shared.Next(options.Count)];
            walls[current] |= dirBit;
            walls[neighbor] |= oppositeBit;
            visited[neighbor] = true;
            stack.Push(neighbor);
        }

        return new MazeDto(width, height, walls, StartIndex: start, EndIndex: cellCount - 1);
    }

    private static List<(int Neighbor, int DirBit, int OppositeBit)> UnvisitedNeighbors(
        int cell, int width, int height, bool[] visited)
    {
        List<(int, int, int)> options = new(4);
        int x = cell % width;
        int y = cell / width;

        if (y > 0 && !visited[cell - width])
            options.Add((cell - width, MazeDirections.North, MazeDirections.South));
        if (x < width - 1 && !visited[cell + 1])
            options.Add((cell + 1, MazeDirections.East, MazeDirections.West));
        if (y < height - 1 && !visited[cell + width])
            options.Add((cell + width, MazeDirections.South, MazeDirections.North));
        if (x > 0 && !visited[cell - 1])
            options.Add((cell - 1, MazeDirections.West, MazeDirections.East));

        return options;
    }
}
