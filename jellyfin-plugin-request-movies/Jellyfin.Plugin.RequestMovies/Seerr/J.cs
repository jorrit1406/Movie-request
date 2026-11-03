using System.Text.Json;

namespace Jellyfin.Plugin.RequestMovies.Seerr;

/// <summary>Accès tolérant au JSON de Seerr (les champs absents ou nuls renvoient null).</summary>
internal static class J
{
    public static JsonElement? Get(this JsonElement? e, string name)
        => e is { ValueKind: JsonValueKind.Object } v && v.TryGetProperty(name, out var p) && p.ValueKind != JsonValueKind.Null
            ? p
            : null;

    public static JsonElement? Get(this JsonElement e, string name) => ((JsonElement?)e).Get(name);

    public static int? Int(this JsonElement? e)
        => e is { ValueKind: JsonValueKind.Number } v && v.TryGetInt32(out var i) ? i : null;

    public static string? Str(this JsonElement? e)
        => e is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    public static bool Bool(this JsonElement? e)
        => e is { ValueKind: JsonValueKind.True };
}
