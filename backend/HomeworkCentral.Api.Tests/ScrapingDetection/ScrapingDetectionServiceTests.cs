using HomeworkCentral.Api.ScrapingDetection;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace HomeworkCentral.Api.Tests.ScrapingDetection;

public class ScrapingDetectionServiceTests
{
    private readonly ScrapingDetectionService _service = new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public void Handful_of_requests_is_never_assessed()
    {
        // Under the minimum sample count, there isn't enough signal to say anything either way.
        ScrapingAssessment? last = null;
        DateTime start = DateTime.UtcNow;
        for (int i = 0; i < 5; i++)
            last = _service.RecordRequest(new ScrapingRequestSample("user:u1", $"/api/chat/rooms/room{i}/messages", "GET", start.AddSeconds(i * 5)));

        Assert.NotNull(last);
        Assert.Equal(0, last!.SuspicionScore);
        Assert.False(last.ShouldBlock);
    }

    [Fact]
    public void Normal_chatty_human_usage_is_not_blocked()
    {
        // A handful of endpoints, a real mix of reads and writes, irregular timing over two minutes.
        (string Path, string Method, double OffsetSeconds)[] session =
        [
            ("/api/chat/nav", "GET", 0),
            ("/api/chat/rooms/general:lobby/messages", "GET", 2),
            ("/api/chat/rooms/general:lobby/messages", "POST", 8),
            ("/api/chat/rooms/general:lobby/messages", "GET", 15),
            ("/api/subjects/general", "GET", 22),
            ("/api/subjects/claim", "POST", 30),
            ("/api/chat/rooms/general:lobby/messages", "GET", 40),
            ("/api/chat/rooms/general:lobby/messages", "POST", 55),
            ("/api/chat/rooms/general:lobby/messages", "GET", 70),
            ("/api/chat/rooms/general:lobby/messages", "GET", 95),
            ("/api/chat/rooms/general:lobby/messages", "POST", 110),
        ];

        DateTime start = DateTime.UtcNow;
        ScrapingAssessment? last = null;
        foreach ((string path, string method, double offsetSeconds) in session)
            last = _service.RecordRequest(new ScrapingRequestSample("user:human1", path, method, start.AddSeconds(offsetSeconds)));

        Assert.NotNull(last);
        Assert.False(last!.ShouldBlock);
    }

    [Fact]
    public void Fast_uniform_read_only_enumeration_of_many_distinct_resources_is_blocked()
    {
        DateTime start = DateTime.UtcNow;
        ScrapingAssessment? last = null;

        for (int i = 0; i < 150; i++)
        {
            last = _service.RecordRequest(new ScrapingRequestSample(
                "ip:203.0.113.5",
                $"/api/chat/rooms/room{i}/messages",
                "GET",
                start.AddMilliseconds(i * 300)));
        }

        Assert.NotNull(last);
        Assert.True(last!.ShouldBlock);
        Assert.True(last.SuspicionScore >= 0.75);
        Assert.NotNull(last.Reason);
    }

    [Fact]
    public void High_volume_alone_without_other_signals_does_not_reach_the_block_threshold()
    {
        // Many requests, but to the same handful of resources with a real write mix and irregular
        // timing — should accumulate at most the volume signal, not enough alone to block.
        DateTime start = DateTime.UtcNow;
        string[] paths = ["/api/chat/rooms/general:lobby/messages", "/api/chat/nav"];
        Random rng = new(42);
        double offset = 0;
        ScrapingAssessment? last = null;

        for (int i = 0; i < 100; i++)
        {
            offset += 100 + rng.Next(0, 400); // irregular gaps
            string method = i % 3 == 0 ? "POST" : "GET";
            last = _service.RecordRequest(new ScrapingRequestSample(
                "user:activehuman", paths[i % paths.Length], method, start.AddMilliseconds(offset)));
        }

        Assert.NotNull(last);
        Assert.False(last!.ShouldBlock);
    }

    [Fact]
    public void Different_identities_are_tracked_independently()
    {
        DateTime start = DateTime.UtcNow;

        for (int i = 0; i < 150; i++)
        {
            _service.RecordRequest(new ScrapingRequestSample(
                "ip:198.51.100.9", $"/api/chat/rooms/room{i}/messages", "GET", start.AddMilliseconds(i * 300)));
        }

        // A different identity making a single request should be completely unaffected by the
        // scraper above sharing the same time range.
        ScrapingAssessment assessment = _service.RecordRequest(
            new ScrapingRequestSample("user:innocent", "/api/chat/nav", "GET", start));

        Assert.False(assessment.ShouldBlock);
        Assert.Equal(0, assessment.SuspicionScore);
    }
}
