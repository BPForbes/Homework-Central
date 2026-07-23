namespace HomeworkCentral.Api.Assessment;

public sealed record SyntheticCommunityResolution(
    float FinalApproval,
    float VoteConfidence,
    int Upvotes,
    int Downvotes,
    float ResolvedEvidence,
    SyntheticVoteEvaluationTrace Evaluation,
    SyntheticVoteSamplingTrace Sampling);

/// <summary>Deterministically resolves an LLM 1 proposal against LLM 2's blind estimate.</summary>
public static class SyntheticCommunitySignalResolver
{
    public static SyntheticCommunityResolution Resolve(
        SyntheticCommunityIntent proposal,
        float evaluatorApproval,
        float evaluatorConfidence,
        float evaluatorEvidence,
        float channelRelevance,
        int seed)
    {
        float difference = MathF.Abs(proposal.ProposedApproval - evaluatorApproval);
        float confidence = Math.Clamp(evaluatorConfidence, 0, 1);
        float approval = difference <= .15f
            ? .2f * proposal.ProposedApproval + .8f * evaluatorApproval
            : evaluatorApproval;
        if (difference > .15f) confidence *= .75f;
        if (difference > .35f) confidence = Math.Min(confidence, .5f);
        approval = Math.Clamp(approval, 0, 1);
        int voters = Math.Clamp(proposal.ProposedVoterCount, 1, 200);
        int upvotes = DrawBinomial(voters, approval, seed);
        int downvotes = voters - upvotes;
        float voteSignal = ((float)upvotes - downvotes) / voters * confidence;
        float multiplier = .5f + .5f * Math.Clamp(channelRelevance, 0, 1);
        float resolvedEvidence = Math.Clamp(evaluatorEvidence + Math.Clamp(.10f * voteSignal * multiplier, -.10f, .10f), 0, 1);
        SyntheticVoteEvaluationTrace evaluation = new(evaluatorApproval, evaluatorConfidence, [], difference <= .15f, difference, "community-evaluator-v1");
        SyntheticVoteSamplingTrace sampling = new(approval, voters, upvotes, downvotes, "binomial", "community-sampling-v1", "hc-xoshiro256ss-v1", seed);
        return new SyntheticCommunityResolution(approval, confidence, upvotes, downvotes, resolvedEvidence, evaluation, sampling);
    }

    private static int DrawBinomial(int count, float probability, int seed)
    {
        Xoshiro random = new((ulong)(uint)seed);
        int successes = 0;
        for (int index = 0; index < count; index++) if (random.NextUnit() < probability) successes++;
        return successes;
    }

    private struct Xoshiro
    {
        private ulong s0, s1, s2, s3;
        public Xoshiro(ulong seed) { s0 = Split(ref seed); s1 = Split(ref seed); s2 = Split(ref seed); s3 = Split(ref seed); }
        public float NextUnit() => (Next() >> 40) * (1f / (1UL << 24));
        private ulong Next() { ulong result = Rotate(s1 * 5, 7) * 9; ulong t = s1 << 17; s2 ^= s0; s3 ^= s1; s1 ^= s2; s0 ^= s3; s2 ^= t; s3 = Rotate(s3, 45); return result; }
        private static ulong Split(ref ulong value) { value += 0x9E3779B97F4A7C15UL; ulong z = value; z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL; return z ^ (z >> 31); }
        private static ulong Rotate(ulong value, int shift) => (value << shift) | (value >> (64 - shift));
    }
}