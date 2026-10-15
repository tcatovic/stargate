using System.Text.Json;

namespace StargateAPI.Tests;

/// <summary>Assertions on 400 ValidationProblemDetails bodies, written against literals rather than the code's constants.</summary>
internal static class ProblemAssert
{
    public const string NameRuleMessage =
        "Use only English letters (A-Z), digits, spaces, hyphens, apostrophes and periods, with at least one letter or digit (at most 100 characters).";

    public const string RequiredMessage = "The Name field is required.";

    /// <summary>errors[key] (key compared ignoring case) is exactly one message, equal to <paramref name="expected"/>.</summary>
    public static void HasOnlyNameError(JsonElement problem, string key, string expected) =>
        HasNameErrors(problem, key, expected);

    /// <summary>errors[key] (key compared ignoring case) is exactly the given messages, in order.</summary>
    public static void HasNameErrors(JsonElement problem, string key, params string[] expected)
    {
        var errors = problem.GetProperty("errors").EnumerateObject().ToList();
        var entry = Assert.Single(errors, e => e.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(expected, entry.Value.EnumerateArray().Select(m => m.GetString()).ToArray());
    }

    /// <summary>
    /// For a request body that carried a rejected name: errors.name is exactly the expected literal message (the rule
    /// message for a non-blank string, "required" for a missing, null or blank one) and nothing decoded echoes the value.
    /// </summary>
    public static void NameRejected(JsonElement problem, string requestBody)
    {
        using var submitted = JsonDocument.Parse(requestBody);
        string? name = submitted.RootElement.TryGetProperty("name", out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
        if (name is null)
        {
            HasNameErrors(problem, "name", RequiredMessage);
        }
        else if (string.IsNullOrWhiteSpace(name))
        {
            HasNameErrors(problem, "name", RequiredMessage, NameRuleMessage);
        }
        else
        {
            HasNameErrors(problem, "name", NameRuleMessage);
        }

        NothingDecodedContains(problem, name ?? string.Empty);
    }

    /// <summary>
    /// The body is parsed, so JSON escapes (', é, ...) are decoded first: no property name and no string value
    /// may contain any of the submitted values. A raw-text check cannot catch an echo that the encoder escaped.
    /// </summary>
    public static void NothingDecodedContains(JsonElement json, params string[] submitted)
    {
        // Only where a message could echo: errors, title, detail ("type" and "traceId" are generated). A value that is
        // itself a substring of the fixed messages (a lone "." or "-") cannot be told apart from them, so it is skipped.
        var texts = new[] { "errors", "title", "detail" }
            .Where(k => json.TryGetProperty(k, out _))
            .SelectMany(k => Strings(json.GetProperty(k)));
        foreach (var text in texts)
        {
            foreach (var value in submitted.Where(v => v.Trim().Length > 0
                && !NameRuleMessage.Contains(v.Trim(), StringComparison.Ordinal) && !RequiredMessage.Contains(v.Trim(), StringComparison.Ordinal)))
            {
                Assert.DoesNotContain(value, text, StringComparison.Ordinal);
                Assert.DoesNotContain(value.Trim(), text, StringComparison.Ordinal);
            }
        }
    }

    public static IEnumerable<string> Strings(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                yield return element.GetString()!;
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    yield return property.Name;
                    foreach (var inner in Strings(property.Value))
                    {
                        yield return inner;
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var inner in Strings(item))
                    {
                        yield return inner;
                    }
                }
                break;
        }
    }

    /// <summary>
    /// A malformed-JSON 400 must not carry parser internals: CLR type names, the JSON path, line and byte position.
    /// Every key and string value of the whole body is checked after decoding. The error KEYS "$" and "$.name" are the
    /// framework's model-state path of the offending member (the public property name), so "$" itself is allowed.
    /// </summary>
    public static void NoParserInternals(JsonElement problem)
    {
        foreach (var text in Strings(problem))
        {
            foreach (var forbidden in new[] { "StargateAPI", "System.", "Path", "LineNumber", "BytePosition", "converted" })
            {
                Assert.DoesNotContain(forbidden, text, StringComparison.Ordinal);
            }
        }
    }
}
