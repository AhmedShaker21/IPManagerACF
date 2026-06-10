namespace IpManager.Core.Util;

/// <summary>
/// MACs arrive as AA:BB:CC:11:22:33, aa-bb-cc-11-22-33, aabb.cc11.2233 ...
/// We store a normalized (hex-only, upper) copy and search against that, so partial
/// search works regardless of the separators the source used.
/// </summary>
public static class MacHelper
{
    public static string Normalize(string? mac) =>
        new string((mac ?? string.Empty).Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();

    public static string ToDisplay(string normalized)
    {
        var hex = Normalize(normalized);
        if (hex.Length < 2) return hex;
        return string.Join(":", Enumerable.Range(0, hex.Length / 2)
                                          .Select(i => hex.Substring(i * 2, 2)));
    }
}
