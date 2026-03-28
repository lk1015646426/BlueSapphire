using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public class AudioConversionServiceTests
{
    private readonly AudioConversionService _service = new();

    [Theory]
    [InlineData("track.mp3", AudioConversionTarget.Wav)]
    [InlineData("track.wav", AudioConversionTarget.Mp3)]
    [InlineData("track.flac", AudioConversionTarget.M4a)]
    [InlineData("track.aac", AudioConversionTarget.Mp3)]
    public void CanConvertToTarget_ReturnsTrueForSupportedAudioInputs(string fileName, AudioConversionTarget target)
    {
        Assert.True(_service.CanConvertToTarget(fileName, target));
    }

    [Theory]
    [InlineData("track.zip", AudioConversionTarget.Mp3)]
    [InlineData("", AudioConversionTarget.Wav)]
    [InlineData(null, AudioConversionTarget.M4a)]
    public void CanConvertToTarget_ReturnsFalseForUnsupportedInputs(string? fileName, AudioConversionTarget target)
    {
        Assert.False(_service.CanConvertToTarget(fileName, target));
    }

    [Theory]
    [InlineData("Mp3", AudioConversionTarget.Mp3)]
    [InlineData("Wav", AudioConversionTarget.Wav)]
    [InlineData("M4a", AudioConversionTarget.M4a)]
    public void TryParseTarget_ParsesKnownTargets(string key, AudioConversionTarget expected)
    {
        Assert.True(_service.TryParseTarget(key, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("track.mp3", ".mp3")]
    [InlineData("track.wav", ".wav")]
    [InlineData("track.m4a", ".m4a")]
    [InlineData("track.aac", ".m4a")]
    [InlineData("track.flac", ".wav")]
    public void GetTrimOutputExtension_ReturnsExpectedExtension(string fileName, string expectedExtension)
    {
        Assert.Equal(expectedExtension, _service.GetTrimOutputExtension(fileName));
    }

    [Theory]
    [InlineData("track.mp3", true)]
    [InlineData("track.flac", true)]
    [InlineData("track.zip", false)]
    public void CanTrim_ReturnsExpectedValue(string fileName, bool expected)
    {
        Assert.Equal(expected, _service.CanTrim(fileName));
    }
}
