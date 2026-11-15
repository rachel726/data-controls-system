using System.Diagnostics.CodeAnalysis;
using DataControls.Core.Catalog;

namespace DataControls.Core.Paths;

/// <summary>
/// A field path resolved against the catalog: the field, the entity that owns it and the chain of
/// many-to-one relations leading to it from the root entity (empty for a field of the root itself).
/// <see cref="DisplayName"/> is its business wording - the same in sentences, messages, result columns and the UI.
/// </summary>
public sealed record ResolvedField(
    string Path,
    FieldMeta Field,
    EntityMeta Entity,
    IReadOnlyList<RelationMeta> RelationChain,
    string DisplayName)
{
    /// <summary>The relation part of the path ("" / "department" / "manager.department") - identifies the join chain.</summary>
    public string RelationPathKey { get; } = string.Join('.', RelationChain.Select(r => r.Key));

    /// <summary>The record that owns the field, nearest relation first: "עובד", "מנהל ישיר", "מחלקה של מנהל ישיר".</summary>
    public string Owner { get; } = FieldPathResolver.OwnerOf(Entity, RelationChain);
}

public enum PathErrorCode
{
    UnknownEntity,
    EmptyPath,
    InvalidFormat,
    UnknownRelation,
    OneToManyInPath,
    TooManyRelations,
    RelationWithoutField,
    UnknownField,
}

/// <summary>Why a path could not be resolved. <see cref="Message"/> is user-facing Hebrew in business terms.</summary>
public sealed record PathError(PathErrorCode Code, string Message);

/// <summary>
/// Resolves dot-separated field paths ("age", "department.isActive", "manager.salary") against the catalog.
/// Only catalog keys can ever be resolved, so a path is either a known field or an error - never raw SQL.
/// </summary>
public static class FieldPathResolver
{
    /// <param name="maxRelationHops">
    /// The limit to enforce - the validator and the planner pass <c>ValidationLimits.MaxRelationHops</c>. Without it
    /// nothing is limited: the wording of an already validated definition must never fail on a configured limit.
    /// </param>
    public static bool TryResolve(
        MetadataCatalog catalog,
        string rootEntityKey,
        string? path,
        [NotNullWhen(true)] out ResolvedField? resolved,
        [NotNullWhen(false)] out PathError? error,
        int maxRelationHops = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        resolved = null;

        if (!catalog.TryGetEntity(rootEntityKey, out var entity))
        {
            error = new(PathErrorCode.UnknownEntity, $"סוג הנתונים '{rootEntityKey}' אינו קיים.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            error = new(PathErrorCode.EmptyPath, "לא נבחר שדה.");
            return false;
        }

        var segments = path.Split('.');
        if (!segments.All(IsValidKey))
        {
            error = new(PathErrorCode.InvalidFormat, $"השדה '{path}' אינו תקין.");
            return false;
        }

        var chain = new List<RelationMeta>(segments.Length - 1);
        foreach (var segment in segments[..^1])
        {
            if (!catalog.TryGetRelation(entity.Key, segment, out var relation))
            {
                error = catalog.TryGetField(entity.Key, segment, out var field)
                    ? new(PathErrorCode.InvalidFormat, $"'{field.DisplayName}' הוא שדה ולא קשר לנתונים אחרים.")
                    : new(PathErrorCode.UnknownRelation, $"ל{entity.DisplayName} אין קשר בשם '{segment}'.");
                return false;
            }

            if (relation.Cardinality == Cardinality.OneToMany)
            {
                error = new(PathErrorCode.OneToManyInPath,
                    $"ל{entity.DisplayName} יכולים להיות {relation.DisplayName} רבים - השתמשו בתנאי 'יש / אין'.");
                return false;
            }

            chain.Add(relation);
            if (chain.Count > maxRelationHops)
            {
                // in business words, the record nearest first: "מנהל ישיר של מנהל ישיר"
                var owner = string.Join(" של ", Enumerable.Reverse(chain).Select(r => r.DisplayName));
                var allowed = maxRelationHops == 1 ? "דרך קשר אחד בלבד" : $"דרך {maxRelationHops} קשרים לכל היותר";
                error = new(PathErrorCode.TooManyRelations,
                    $"לא ניתן להשתמש בשדה של {owner} - אפשר לבחור שדות {allowed}, למשל שדות של {chain[0].DisplayName}.");
                return false;
            }

            entity = catalog.GetEntity(relation.ToEntityKey);
        }

        var fieldKey = segments[^1];
        if (!catalog.TryGetField(entity.Key, fieldKey, out var target))
        {
            error = catalog.TryGetRelation(entity.Key, fieldKey, out var relation)
                ? new(PathErrorCode.RelationWithoutField, $"יש לבחור שדה של {relation.DisplayName}.")
                : new(PathErrorCode.UnknownField, $"ל{entity.DisplayName} אין שדה בשם '{fieldKey}'.");
            return false;
        }

        resolved = new ResolvedField(path, target, entity, chain, DisplayNameOf(catalog, catalog.GetEntity(rootEntityKey), entity, target, chain));
        error = null;
        return true;
    }

    /// <summary>
    /// Every field a definition on <paramref name="rootEntityKey"/> may use - its own, then those reached through
    /// many-to-one relations up to <paramref name="maxRelationHops"/> - resolved like any path, so the UI offers exactly
    /// what validation accepts, under the same names. Listed record by record, the root first.
    /// </summary>
    public static IReadOnlyList<ResolvedField> ReachableFields(MetadataCatalog catalog, string rootEntityKey, int maxRelationHops)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var fields = new List<ResolvedField>();

        void Walk(EntityMeta entity, string prefix, int hops)
        {
            foreach (var field in entity.Fields.OrderBy(f => f.SortOrder))
            {
                if (TryResolve(catalog, rootEntityKey, prefix + field.Key, out var resolved, out _, maxRelationHops))
                {
                    fields.Add(resolved);
                }
            }

            if (hops < maxRelationHops)
            {
                foreach (var relation in entity.Relations.Where(r => r.Cardinality == Cardinality.ManyToOne))
                {
                    Walk(catalog.GetEntity(relation.ToEntityKey), $"{prefix}{relation.Key}.", hops + 1);
                }
            }
        }

        Walk(catalog.GetEntity(rootEntityKey), "", 0);
        return fields;
    }

    /// <summary>The record that owns a field: the entity itself, or the relations leading to it, nearest first.</summary>
    internal static string OwnerOf(EntityMeta entity, IReadOnlyList<RelationMeta> chain) =>
        chain.Count == 0 ? entity.DisplayName : string.Join(" של ", chain.Reverse().Select(r => r.DisplayName));

    /// <summary>
    /// "שם המחלקה" when the name says it all; "שכר חודשי של מנהל ישיר" when the same name is also another field near
    /// the root (its own "שכר חודשי"); always with the owner beyond one relation: "שם המחלקה של מחלקה של מנהל ישיר".
    /// </summary>
    private static string DisplayNameOf(MetadataCatalog catalog, EntityMeta root, EntityMeta owner, FieldMeta field, List<RelationMeta> chain) =>
        chain.Count == 0 || (chain.Count == 1 && !IsAmbiguous(catalog, root, field))
            ? field.DisplayName
            : $"{field.DisplayName} של {OwnerOf(owner, chain)}";

    /// <summary>Another field with the same name on the root or on a record one many-to-one relation away.</summary>
    private static bool IsAmbiguous(MetadataCatalog catalog, EntityMeta root, FieldMeta field) =>
        root.Fields
            .Concat(root.Relations.Where(r => r.Cardinality == Cardinality.ManyToOne).SelectMany(r => catalog.GetEntity(r.ToEntityKey).Fields))
            .Count(f => f.DisplayName == field.DisplayName) > 1;

    /// <summary>Catalog keys are ASCII letters/digits starting with a lowercase letter (same rule as the DB CHECK).</summary>
    private static bool IsValidKey(string segment) =>
        segment.Length is > 0 and <= 50
        && char.IsAsciiLetterLower(segment[0])
        && segment.All(char.IsAsciiLetterOrDigit);
}
