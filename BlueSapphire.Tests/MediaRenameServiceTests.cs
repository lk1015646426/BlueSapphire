using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public class MediaRenameServiceTests
{
    private readonly MediaRenameService _service = new();

    [Theory]
    [InlineData("IMG_20240305_102233.jpg", 2024, 3, 5, 10, 22, 33)]
    [InlineData("旅行-2024年3月5日.png", 2024, 3, 5, 0, 0, 0)]
    [InlineData("2024-03-05 102233.webp", 2024, 3, 5, 10, 22, 33)]
    public void ParseTimestampFromFileName_ExtractsExpectedDate(
        string fileName,
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second)
    {
        var result = _service.ParseTimestampFromFileName(fileName);

        Assert.Equal(year, result.Year);
        Assert.Equal(month, result.Month);
        Assert.Equal(day, result.Day);
        Assert.Equal(hour, result.Hour);
        Assert.Equal(minute, result.Minute);
        Assert.Equal(second, result.Second);
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("2024-99-99.jpg")]
    [InlineData("")]
    public void ParseTimestampFromFileName_ReturnsMinValueForInvalidNames(string fileName)
    {
        Assert.Equal(DateTimeOffset.MinValue, _service.ParseTimestampFromFileName(fileName));
    }
}
