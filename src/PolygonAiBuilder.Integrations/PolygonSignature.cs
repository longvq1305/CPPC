using System.Security.Cryptography;
using System.Text;

namespace PolygonAiBuilder.Integrations;

public static class PolygonSignature
{
    public static SignedPolygonRequest Create(
        string methodName,
        IEnumerable<KeyValuePair<string, string>> parameters,
        string apiKey,
        string apiSecret,
        long unixTimeSeconds,
        string randomPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiSecret);
        if (randomPrefix.Length != 6 || randomPrefix.Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            throw new ArgumentException("Polygon signature prefix must contain six ASCII letters or digits.", nameof(randomPrefix));
        }

        var signedParameters = parameters
            .Append(new("apiKey", apiKey))
            .Append(new("time", unixTimeSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .OrderBy(parameter => parameter.Key, StringComparer.Ordinal)
            .ThenBy(parameter => parameter.Value, StringComparer.Ordinal)
            .ToArray();
        var canonicalQuery = string.Join(
            "&",
            signedParameters.Select(parameter => $"{Encode(parameter.Key)}={Encode(parameter.Value)}"));
        var signatureSource = $"{randomPrefix}/{methodName}?{canonicalQuery}#{apiSecret}";
        var signatureHash = Convert.ToHexStringLower(
            SHA512.HashData(Encoding.UTF8.GetBytes(signatureSource)));

        return new(canonicalQuery, randomPrefix + signatureHash);
    }

    public static string CreateRandomPrefix() =>
        Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(3));

    public static string Encode(string value) => Uri.EscapeDataString(value).Replace("%20", "+", StringComparison.Ordinal);
}

public sealed record SignedPolygonRequest(
    string CanonicalQuery,
    string ApiSignature);
