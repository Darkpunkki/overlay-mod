using System.Text.Json;
using System.Text.Json.Serialization;

namespace OverlayMod.Engine.Tracking;

/// <summary>
/// Reads a challenge name without ever throwing, because the files carrying one
/// are hand-edited and outlive the version that wrote them.
///
/// Strictness here is expensive in a way that is easy to miss: a route file whose
/// challenge fails to parse is skipped entirely by <see cref="Persistence.RouteStore"/>,
/// so an unrecognised name does not fall back — it silently removes the route
/// from the picker. Version 0.1.0 wrote <c>AnyPercent</c> and <c>AllBosses</c>,
/// both since removed, and both present in every install that predates 0.2.0.
/// Those map onto Speedrun, which is what they were ranked by.
///
/// <c>NoHit</c> is deliberately *not* remapped even though its meaning narrowed
/// in 0.2.0 — it used to count fall damage, which is now No Damage. Keeping the
/// name pointing at the challenge that still bears it means an existing selection
/// lands on the stricter reading rather than silently on a differently-named one;
/// the release notes say so, and No Damage is one click away.
/// </summary>
public sealed class ChallengeTypeJsonConverter : JsonConverter<ChallengeType>
{
    public override ChallengeType Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        // Numbers were never written by any released version, and the enum was
        // reordered in 0.2.0, so an ordinal cannot be trusted to mean anything.
        if (reader.TokenType != JsonTokenType.String) return ChallengeType.NoDamage;

        var raw = reader.GetString();
        if (Enum.TryParse<ChallengeType>(raw, ignoreCase: true, out var parsed)) return parsed;

        return raw?.Replace("%", "").Trim().ToLowerInvariant() switch
        {
            "any" or "anypercent" or "allbosses" => ChallengeType.Speedrun,
            _ => ChallengeType.NoDamage,
        };
    }

    public override void Write(Utf8JsonWriter writer, ChallengeType value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
