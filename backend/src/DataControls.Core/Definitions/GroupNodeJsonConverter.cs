using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataControls.Core.Definitions;

/// <summary>
/// Reads/writes a property statically typed as <see cref="GroupNode"/> (the root filter, a related "where")
/// through the polymorphic <see cref="FilterNode"/> contract, so the JSON keeps "type": "group" like every other
/// node, and anything that is not a group is rejected. The C# model stays precise: no casts at the call sites.
/// </summary>
public sealed class GroupNodeJsonConverter : JsonConverter<GroupNode>
{
    public override GroupNode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<FilterNode>(ref reader, options) as GroupNode
            ?? throw new JsonException("Expected a node of type 'group'.");

    public override void Write(Utf8JsonWriter writer, GroupNode value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize<FilterNode>(writer, value, options);
}
