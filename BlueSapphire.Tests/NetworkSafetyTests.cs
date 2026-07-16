using System.Net.Http;
using System.Text;
using BlueSapphire.Helpers;

namespace BlueSapphire.Tests;

public class NetworkSafetyTests
{
    [Theory]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://10.0.0.1/")]
    [InlineData("https://169.254.1.1/")]
    [InlineData("https://192.168.1.1/")]
    [InlineData("https://[::1]/")]
    [InlineData("https://203.0.113.10/")]
    public async Task ValidatePublicUriAsync_RejectsNonPublicTargets(string value)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NetworkSafety.ValidatePublicUriAsync(new Uri(value), requireHttps: true));
    }

    [Fact]
    public async Task ValidatePublicUriAsync_AcceptsPublicHttpsLiteral()
    {
        await NetworkSafety.ValidatePublicUriAsync(
            new Uri("https://1.1.1.1/"),
            requireHttps: true);
    }

    [Fact]
    public async Task ValidatePublicUriAsync_RejectsHttpWhenHttpsIsRequired()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NetworkSafety.ValidatePublicUriAsync(
                new Uri("http://1.1.1.1/"),
                requireHttps: true));
    }

    [Fact]
    public async Task ReadContentAsStringAsync_EnforcesByteLimit()
    {
        using var content = new ByteArrayContent(Encoding.UTF8.GetBytes(new string('x', 1025)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NetworkSafety.ReadContentAsStringAsync(content, 1024));
    }
}
