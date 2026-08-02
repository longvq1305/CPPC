using PolygonAiBuilder.Integrations;

namespace PolygonAiBuilder.UnitTests;

public sealed class PolygonSignatureTests
{
    [Fact]
    public void Create_SortsFormEncodesAndHashesRawSignedParameters()
    {
        var signed = PolygonSignature.Create(
            "problems.list",
            [new KeyValuePair<string, string>("name", "a b")],
            "test-key",
            "test-secret",
            1_722_600_000,
            "abc123");

        Assert.Equal("apiKey=test-key&name=a+b&time=1722600000", signed.FormEncodedQuery);
        Assert.Equal(
            "abc123b787dbb84fa4f48d9f0c7810041387652746ae07287b0fdeae52c65f2079d63bec344bff3bf06610f2adfbc0d29b337200ac2923d290693522214edd3a676664",
            signed.ApiSignature);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("bad-12")]
    [InlineData("toolong")]
    public void Create_RejectsInvalidRandomPrefix(string prefix)
    {
        Assert.Throws<ArgumentException>(() => PolygonSignature.Create(
            "problems.list",
            [],
            "key",
            "secret",
            1,
            prefix));
    }
}
