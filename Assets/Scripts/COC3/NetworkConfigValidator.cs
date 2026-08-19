using System.Text.RegularExpressions;

/// <summary>
/// A plain (non-MonoBehaviour) utility class with static helper methods for
/// checking whether text typed into a TMP_InputField is a validly-formatted
/// IPv4 value, and whether it matches the expected answer for a step.
///
/// Because these methods are static, you can call them from anywhere without
/// needing a reference to a GameObject, e.g. NetworkConfigValidator.IsValidFormat("...").
/// </summary>
public static class NetworkConfigValidator
{
    // Matches a standard IPv4 address like 192.168.1.10 (each number 0-255).
    private static readonly Regex IPv4Pattern = new Regex(
        @"^((25[0-5]|2[0-4][0-9]|1[0-9][0-9]|[1-9]?[0-9])\.){3}(25[0-5]|2[0-4][0-9]|1[0-9][0-9]|[1-9]?[0-9])$"
    );

    /// <summary>Returns true if the text is a well-formed IPv4-style value (e.g. "192.168.1.1").</summary>
    public static bool IsValidFormat(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return IPv4Pattern.IsMatch(value.Trim());
    }

    /// <summary>
    /// Returns true only if the input is validly formatted AND exactly matches
    /// the expected value. Used for IP address, subnet mask, and gateway checks -
    /// they all follow the same "dotted quad" format, so one method covers all three.
    /// </summary>
    public static bool ValidateExactMatch(string input, string expected)
    {
        if (!IsValidFormat(input)) return false;
        return input.Trim() == expected.Trim();
    }
}