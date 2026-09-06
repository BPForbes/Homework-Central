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
    public void Loaded_lru_follows_client_walk_d_on_a_b_c()
    {
        if (!RustKernels.HasLru)
            return;

        Assert.True(RustKernels.TryLruCreate(3, out nint handle));
        try
        {
            byte[] a = "A"u8.ToArray();
            byte[] b = "B"u8.ToArray();
            byte[] c = "C"u8.ToArray();
            byte[] d = "D"u8.ToArray();
            Assert.True(RustKernels.TryLruPut(handle, a, [1]));
            Assert.True(RustKernels.TryLruPut(handle, b, [2]));
            Assert.True(RustKernels.TryLruPut(handle, c, [3]));
            byte[] dest = new byte[1];
            Assert.Equal(0, RustKernels.TryLruGet(handle, a, dest, out _));
            Assert.True(RustKernels.TryLruPut(handle, d, [4]));
            Assert.Equal(0, RustKernels.TryLruGet(handle, d, dest, out _));
            Assert.Equal(4, dest[0]);
            Assert.Equal(0, RustKernels.TryLruGet(handle, a, dest, out _));
            Assert.Equal(1, dest[0]);
            Assert.Equal(0, RustKernels.TryLruGet(handle, c, dest, out _));
            Assert.Equal(3, dest[0]);
            Assert.Equal(1, RustKernels.TryLruGet(handle, b, dest, out _));
        }
        finally
        {
            RustKernels.LruFree(handle);
        }
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

        float[] weights = [1f, 3f, 2f, 4f];
        float[] source = [1f, 0f];
        float[] biases = [0.5f, -0.5f];
        float[] rustDestination = new float[2];
        float[] managedDestination = new float[2];
        Assert.True(RustKernels.TryMultiplyBias(weights, 2, 2, source, biases, rustDestination));
        NeuralNetwork.MultiplyBiasManaged(weights, 2, 2, source, biases, managedDestination);
        Assert.Equal(managedDestination, rustDestination);

        float[] rustTranspose = new float[2];
        float[] managedTranspose = new float[2];
        Assert.True(RustKernels.TryMultiplyTranspose(weights, 2, 2, [1f, 1f], rustTranspose));
        NeuralNetwork.MultiplyTransposeManaged(weights, 2, 2, [1f, 1f], managedTranspose);
        Assert.Equal(managedTranspose, rustTranspose);

        Assert.True(RustKernels.TryHashEmbed("offline", out float[] rustEmbed));
        Assert.Equal(LlmClient.HashEmbedManaged("offline"), rustEmbed);

        float[] rustExpertise = new float[TutoringSubjectContextRouter.InputSize];
        float[] managedExpertise = new float[TutoringSubjectContextRouter.InputSize];
        Assert.True(RustKernels.TryAddExpertiseHash(
            rustExpertise,
            "  Rust  ",
            TutoringSubjectContextRouter.BaseInputSize,
            TutoringSubjectContextRouter.ExpertiseHashBins));
        TutoringSubjectContextRouter.AddExpertiseHashManaged(managedExpertise, "  Rust  ");
        Assert.Equal(managedExpertise, rustExpertise);

        Assert.True(RustKernels.TrySupportCosine([1f, 0f], [-1f, 0f], out double supportScore));
        Assert.Equal(0d, supportScore);
        Assert.Equal(-1d, VectorDocumentStore.Cosine([1f, 0f], [-1f, 0f]), 12);

        Assert.True(RustKernels.TryBatchCosineJson(
            [1f, 0f],
            ["[1,0]", "[0,1]", "null"],
            out double[] batchScores));
        Assert.Equal(3, batchScores.Length);
        Assert.Equal(1d, batchScores[0], 12);
        Assert.Equal(0d, batchScores[1], 12);
        Assert.Equal(0d, batchScores[2], 12);

        if (RustKernels.HasTopKAbs)
        {
            Assert.True(RustKernels.TryTopKAbs([0.1f, -2f, 0f, 3f, 1e-8f], [10, 11, 12, 13, 14], 2, out int[] topK));
            Assert.Equal([13, 11], topK);
        }

        if (RustKernels.TryShouldSpill(70, 70, 1, 100, out bool spill))
            Assert.True(spill);
    }

    [Fact]
    public void Hash_embed_and_expertise_match_managed()
    {
        Assert.Equal(LlmClient.HashEmbedManaged("offline"), LlmClient.HashEmbed("offline"));
        Assert.Equal(LlmClient.HashEmbedManaged(""), LlmClient.HashEmbed(""));

        float[] viaManaged = new float[TutoringSubjectContextRouter.InputSize];
        TutoringSubjectContextRouter.AddExpertiseHashManaged(viaManaged, "biology");
        SubjectSignalSnapshot snapshot = ChatMonitoringSubjectSignals.Resolve(
            [],
            channelGeneral: null,
            appliedExpertise: ["biology"]);
        float[] viaPublic = TutoringSubjectContextRouter.BuildRouterInput(snapshot);
        Assert.Equal(
            viaManaged.AsSpan(TutoringSubjectContextRouter.BaseInputSize).ToArray(),
            viaPublic.AsSpan(TutoringSubjectContextRouter.BaseInputSize).ToArray());
    }

    [Fact]
    public void Support_cosine_clamps_and_does_not_match_store_cosine()
    {
        float[] left = [1f, 0f];
        float[] right = [-1f, 0f];
        Assert.Equal(0d, ChatMonitoringNeuralModelHashedMlp.CosineManaged(left, right));
        Assert.Equal(0d, ChatMonitoringNeuralModelHashedMlp.Cosine(left, right));
        Assert.Equal(-1d, VectorDocumentStore.Cosine(left, right), 12);
    }

    [Fact]
    public void Gemv_managed_skips_zero_sources()
    {
        float[] weights = [1f, 3f, 2f, 4f];
        float[] destination = new float[2];
        NeuralNetwork.MultiplyBiasManaged(weights, 2, 2, [1f, 0f], [0.5f, -0.5f], destination);
        Assert.Equal([1.5f, 2.5f], destination);
    }
}
