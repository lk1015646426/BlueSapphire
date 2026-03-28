using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public class AudioPreviewServiceTests
{
    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(65, "01:05")]
    [InlineData(3723, "01:02:03")]
    public void FormatTimestamp_ReturnsExpectedText(int totalSeconds, string expected)
    {
        Assert.Equal(expected, AudioPreviewService.FormatTimestamp(TimeSpan.FromSeconds(totalSeconds)));
    }

    [Theory]
    [InlineData(-5, 100, 0)]
    [InlineData(15, 100, 15)]
    [InlineData(120, 100, 100)]
    [InlineData(15, 0, 15)]
    public void Clamp_ReturnsExpectedPosition(int seconds, int durationSeconds, int expectedSeconds)
    {
        TimeSpan actual = AudioPreviewService.Clamp(
            TimeSpan.FromSeconds(seconds),
            TimeSpan.FromSeconds(durationSeconds));

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), actual);
    }

    [Theory]
    [InlineData(0, 5, 1, false, 1)]
    [InlineData(4, 5, 1, false, -1)]
    [InlineData(4, 5, 1, true, 0)]
    [InlineData(0, 5, -1, true, 4)]
    [InlineData(0, 5, -1, false, -1)]
    public void ResolveAdjacentIndex_ReturnsExpectedIndex(int currentIndex, int count, int offset, bool allowWrap, int expectedIndex)
    {
        int actual = AudioPreviewService.ResolveAdjacentIndex(currentIndex, count, offset, allowWrap);

        Assert.Equal(expectedIndex, actual);
    }
}
