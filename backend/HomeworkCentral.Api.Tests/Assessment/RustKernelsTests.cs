using HomeworkCentral.Api.Assessment;

namespace HomeworkCentral.Api.Tests.Assessment;

public class RustKernelsTests
{
    [Fact]
    public void Native_library_is_optional_and_bins_still_match()
    {
        IReadOnlyList<float> values = ChatMonitoringFeatureEncoder.EmbedText("anything");
        Assert.Equal(1f, values[15]);
        Assert.Equal(1d, VectorDocumentStore.Cosine([1f, 0f], [1f, 0f]));
    }

    [Fact]
    public void Candidate_library_paths_keep_the_app_directory_prefix()
    {
        string libraryFileName = RustKernels.LibraryFileName;
        string baseDirectory = AppContext.BaseDirectory;
        List<string> paths = RustKernels.CandidateLibraryPaths().ToList();
        HashSet<string> searchRoots = WalkedSearchRoots(baseDirectory);

        Assert.Contains(Path.Join(baseDirectory, libraryFileName), paths);
        Assert.Contains(Path.Join(baseDirectory, "native", libraryFileName), paths);
        Assert.Contains(Path.Join(baseDirectory, "rust", "target", "debug", libraryFileName), paths);
        Assert.Contains(Path.Join(baseDirectory, "rust", "target", "release", libraryFileName), paths);

        Assert.All(
            paths,
            path =>
            {
                Assert.EndsWith(libraryFileName, path, StringComparison.Ordinal);
                Assert.Contains(searchRoots, root => path.StartsWith(root, StringComparison.Ordinal));
            });
    }

    [Fact]
    public void Join_library_path_refuses_a_rooted_later_segment()
    {
        string rootedLater = Path.GetFullPath(Path.DirectorySeparatorChar.ToString());
        Assert.True(Path.IsPathRooted(rootedLater));
        Assert.Equal(
            string.Empty,
            RustKernels.JoinLibraryPath(AppContext.BaseDirectory, rootedLater, RustKernels.LibraryFileName));
        Assert.Equal(
            string.Empty,
            RustKernels.JoinLibraryPath(string.Empty, RustKernels.LibraryFileName));
    }

    private static HashSet<string> WalkedSearchRoots(string baseDirectory)
    {
        HashSet<string> roots = [];
        string? directory = baseDirectory;
        for (int depth = 0; depth < 8 && directory is not null; depth++)
        {
            roots.Add(directory);
            directory = Directory.GetParent(directory)?.FullName;
        }

        return roots;
    }

    [Fact]
    public void Loaded_kernels_serve_embed_text_and_store_cosine()
    {
        if (!RustKernels.IsLoaded)
            return;

        float[] bins = new float[ChatMonitoringFeatureEncoder.StructuralFeatureCount];
        Assert.True(RustKernels.TryEmbedText("anything", bins));
        Assert.Equal(1f, bins[15]);

        Assert.True(RustKernels.TryCosine([1f, 0f], [1f, 0f], out double score));
        Assert.Equal(1d, score, 12);
    }
}
