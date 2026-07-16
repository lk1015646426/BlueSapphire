using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public class AIPrivacyServiceTests
{
    [Fact]
    public void RedactForRemoteModel_RemovesIdentityAndSecrets()
    {
        var service = new AIPrivacyService();
        string input = """
            C:\Users\alice\Documents\private.txt
            alice@example.com
            Authorization: Bearer abc.def.ghi
            api_key=super-secret
            https://example.com/?token=raw-token
            """;

        string result = service.RedactForRemoteModel(input);

        Assert.DoesNotContain("alice", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("super-secret", result);
        Assert.DoesNotContain("raw-token", result);
        Assert.Contains("<用户>", result);
        Assert.Contains("<敏感信息>", result);
    }
}
