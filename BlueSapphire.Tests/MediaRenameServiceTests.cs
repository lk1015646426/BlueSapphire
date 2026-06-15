using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public class MediaRenameServiceTests
{
    private readonly MediaRenameService _service = new();

    // ================================================================
    // Test 1: HasUsableTimestamp 拒绝 MinValue
    // ================================================================
    [Fact]
    public void HasUsableTimestamp_ReturnsFalseForMinValue()
    {
        Assert.False(_service.HasUsableTimestamp(DateTimeOffset.MinValue));
    }

    // ================================================================
    // Test 2: HasUsableTimestamp 接受正常时间戳
    // ================================================================
    [Fact]
    public void HasUsableTimestamp_ReturnsTrueForValidTimestamp()
    {
        Assert.True(_service.HasUsableTimestamp(new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.Zero)));
    }

    // ================================================================
    // Test 3: HasUsableTimestamp 拒绝 1900 年之前的时间戳
    // ================================================================
    [Fact]
    public void HasUsableTimestamp_ReturnsFalseForYearBefore1900()
    {
        Assert.False(_service.HasUsableTimestamp(new DateTimeOffset(1899, 1, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    // ================================================================
    // Test 4: ParseTimestampFromFileName 解析完整日期时间文件名
    // ================================================================
    [Fact]
    public void ParseTimestampFromFileName_ParsesFullDateTimeWithSeparators()
    {
        DateTimeOffset result = _service.ParseTimestampFromFileName("IMG_20240615_143000.jpg");

        Assert.Equal(2024, result.Year);
        Assert.Equal(6, result.Month);
        Assert.Equal(15, result.Day);
        Assert.Equal(14, result.Hour);
        Assert.Equal(30, result.Minute);
    }

    // ================================================================
    // Test 5: ParseTimestampFromFileName 解析中文日期格式
    // ================================================================
    [Fact]
    public void ParseTimestampFromFileName_ParsesChineseDateFormat()
    {
        DateTimeOffset result = _service.ParseTimestampFromFileName("2024年6月15日-143000.jpg");

        Assert.Equal(2024, result.Year);
        Assert.Equal(6, result.Month);
        Assert.Equal(15, result.Day);
    }

    // ================================================================
    // Test 6: ParseTimestampFromFileName 无日期时返回 MinValue
    // ================================================================
    [Fact]
    public void ParseTimestampFromFileName_ReturnsMinValueForNonDateName()
    {
        DateTimeOffset result = _service.ParseTimestampFromFileName("screenshot.png");

        Assert.Equal(DateTimeOffset.MinValue, result);
    }

    // ================================================================
    // Test 7: ParseTimestampFromFileName 空文件名安全处理
    // ================================================================
    [Fact]
    public void ParseTimestampFromFileName_HandlesEmptyString()
    {
        DateTimeOffset result = _service.ParseTimestampFromFileName("");

        Assert.Equal(DateTimeOffset.MinValue, result);
    }
}
