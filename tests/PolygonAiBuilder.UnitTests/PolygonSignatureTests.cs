using PolygonAiBuilder.Integrations;

namespace PolygonAiBuilder.UnitTests;

public sealed class PolygonSignatureTests
{
    [Fact]
    public void Create_SortsEncodesAndHashesAllSignedParameters()
    {
        var signed = PolygonSignature.Create(
            "problems.list",
            [new KeyValuePair<string, string>("name", "a b")],
            "test-key",
            "test-secret",
            1_722_600_000,
            "abc123");

        Assert.Equal("apiKey=test-key&name=a+b&time=1722600000", signed.CanonicalQuery);
        Assert.Equal(
            "abc1238af0cad3610c7bbabbda12d01afed6185eea4a63ed3e0373e60e26c88e1f54154621d57f977742e4561c9a73f33a66e9d9afc0d1a108007f7489d827e1997dc5",
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
