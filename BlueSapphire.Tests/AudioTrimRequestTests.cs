using BlueSapphire.Models;

namespace BlueSapphire.Tests;

public class AudioTrimRequestTests
{
    [Theory]
    [InlineData(0, 12.5)]
    [InlineData(3, 15)]
    public void TryCreate_ReturnsRequestForValidRange(double startSeconds, double endSeconds)
    {
        bool created = AudioTrimRequest.TryCreate(startSeconds, endSeconds, out AudioTrimRequest? request, out string validationMessage);

        Assert.True(created);
        Assert.NotNull(request);
        Assert.Equal(string.Empty, validationMessage);
        Assert.Equal(endSeconds - startSeconds, request!.Duration.TotalSeconds, 3);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(10, 10)]
    [InlineData(12, 8)]
    public void TryCreate_RejectsInvalidRange(double startSeconds, double endSeconds)
    {
        bool created = AudioTrimRequest.TryCreate(startSeconds, endSeconds, out AudioTrimRequest? request, out string validationMessage);

        Assert.False(created);
        Assert.Null(request);
        Assert.False(string.IsNullOrWhiteSpace(validationMessage));
    }

    [Theory]
    [InlineData("12.5", 12.5)]
    [InlineData("01:23", 83)]
    [InlineData("1:02:03", 3723)]
    public void TryParseTimecode_ParsesSupportedFormats(string text, double expectedSeconds)
    {
        bool parsed = AudioTrimRequest.TryParseTimecode(text, out TimeSpan value);

        Assert.True(parsed);
        Assert.Equal(expectedSeconds, value.TotalSeconds, 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1:99")]
    [InlineData("-5")]
    public void TryParseTimecode_RejectsInvalidFormats(string text)
    {
        Assert.False(AudioTrimRequest.TryParseTimecode(text, out _));
    }

    [Fact]
    public void TryCreate_FromTimecodeStrings_ReturnsExpectedRequest()
    {
        bool created = AudioTrimRequest.TryCreate("00:15", "01:20", out AudioTrimRequest? request, out string validationMessage);

        Assert.True(created);
        Assert.NotNull(request);
        Assert.Equal(string.Empty, validationMessage);
        Assert.Equal(TimeSpan.FromSeconds(15), request!.StartTime);
        Assert.Equal(TimeSpan.FromSeconds(80), request.EndTime);
    }
}
