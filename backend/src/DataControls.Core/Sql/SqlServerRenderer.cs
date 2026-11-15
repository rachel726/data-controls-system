using System.Data;
using System.Globalization;
using System.Text;
using DataControls.Core.Catalog;
using DataControls.Core.Definitions;
using DataControls.Core.Planning;

namespace DataControls.Core.Sql;

/// <summary>
/// SQL Server implementation of <see cref="ISqlRenderer"/>. Security properties:
///   * only SELECT statements are produced;
///   * identifiers come from the catalog / planner and are bracket-quoted (']' doubled);
///   * every value is a typed parameter - user text never becomes SQL text;
///   * sort, filter and search keys are checked against the model's result columns (whitelist).
/// NULL semantics: comparisons are false on NULL; "≠" includes NULL; "empty" = NULL or blank text.
/// </summary>
public sealed class SqlServerRenderer : ISqlRenderer
{
    public const string IdColumn = "__id";
    public const string TitleColumn = "__title";
    public const string TotalColumn = "__total";
    public const string ScannedColumn = "scanned";
    public const string ViolationsColumn = "violations";

    /// <summary>Fixed size for text parameters, so SQL Server reuses one cached plan regardless of value length.</summary>
    private const int TextParamSize = 4000;

    /// <summary>SQL text must never depend on the server's culture settings.</summary>
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string FlagColumn(int index) => $"__c{index}";

    public RenderedQuery RenderPage(QueryModel model, ResultQueryOptions options)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);
        ValidatePaging(options);

        var w = new Writer(options.Today);
        var flagSql = new Dictionary<int, string>();
        var where = w.Group(model.Filter, flagSql);

        var select = new List<string>
        {
            $"{Col(model.Root.Alias, model.Root.PrimaryKeyColumn)} AS {SqlIdentifier.Quote(IdColumn)}",
            $"{w.Expr(model.Title)} AS {SqlIdentifier.Quote(TitleColumn)}",
        };
        select.AddRange(model.Columns.Select(c => $"{w.Expr(c.Expression)} AS {SqlIdentifier.Quote(c.Key)}"));
        select.AddRange(model.Flags.Select(f =>
            $"CASE WHEN {flagSql[f.Index]} THEN 1 ELSE 0 END AS {SqlIdentifier.Quote(FlagColumn(f.Index))}"));

        var outer = OuterConditions(model, options, w);

        var sql = new StringBuilder()
            .AppendLine("WITH v AS (")
            .Append("    SELECT ").AppendJoin(",\n           ", select).AppendLine()
            .Append("    FROM ").AppendLine(From(model.Root, model.Joins, indent: "    "))
            .Append("    WHERE ").AppendLine(where)
            .AppendLine(")")
            .AppendLine(Inv, $"SELECT *, COUNT(*) OVER () AS {SqlIdentifier.Quote(TotalColumn)}")
            .AppendLine("FROM v");

        if (outer.Count > 0)
        {
            sql.Append("WHERE ").AppendJoin("\n  AND ", outer).AppendLine();
        }

        sql.AppendLine(Inv, $"ORDER BY {OrderBy(model, options)}")
           .Append(Inv, $"OFFSET {w.Param((long)(options.Page - 1) * options.PageSize)} ROWS FETCH NEXT {w.Param((long)options.PageSize)} ROWS ONLY;");

        return new RenderedQuery(sql.ToString().ReplaceLineEndings("\n"), w.Parameters);
    }

    public RenderedQuery RenderSummary(QueryModel model, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(model);

        var w = new Writer(today);
        var where = w.Group(model.Filter, flags: null);
        var root = $"{Table(model.Root)}";

        var sql = new StringBuilder()
            .AppendLine(Inv, $"SELECT (SELECT COUNT_BIG(*) FROM {root}) AS {SqlIdentifier.Quote(ScannedColumn)},")
            .AppendLine(Inv, $"       (SELECT COUNT_BIG(*) FROM {From(model.Root, model.Joins, indent: "        ")}")
            .Append(Inv, $"        WHERE {where}) AS {SqlIdentifier.Quote(ViolationsColumn)};")
            .ToString()
            .ReplaceLineEndings("\n");

        return new RenderedQuery(sql, w.Parameters);
    }

    // ---------- outer query: search, column filters, sort ----------

    private static List<string> OuterConditions(QueryModel model, ResultQueryOptions options, Writer w)
    {
        var conditions = new List<string>();

        var search = options.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            if (search.Length > options.MaxSearchLength)
            {
                throw new QueryOptionsException($"טקסט החיפוש ארוך מדי (עד {options.MaxSearchLength} תווים).");
            }

            var pattern = w.Param(LikeContains(search));
            var searchable = model.Columns
                .Where(c => c.Field.Field.IsSearchable && c.Field.Field.DataType == DataType.Text)
                .Select(c => c.Key)
                .ToList();

            // The record title is always searchable; skip it when the same field is already a searchable column.
            if (!model.Columns.Any(c => c.Expression == model.Title && searchable.Contains(c.Key)))
            {
                searchable.Insert(0, TitleColumn);
            }
            conditions.Add("(" + string.Join(" OR ", searchable.Select(k => $"v.{SqlIdentifier.Quote(k)} LIKE {pattern} ESCAPE N'\\'")) + ")");
        }

        // One filter per column, as short as a search: bounds the query (and its parameters) whatever the client sends.
        var filtered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var filter in options.Filters)
        {
            var column = model.Columns.FirstOrDefault(c => c.Key == filter.Key)
                ?? throw new QueryOptionsException("לא ניתן לסנן לפי עמודה זו.");
            if (!filtered.Add(column.Key))
            {
                throw new QueryOptionsException($"נשלח יותר מסינון אחד לעמודה '{column.Field.DisplayName}'.");
            }
            if (string.IsNullOrWhiteSpace(filter.Value))
            {
                continue;
            }
            if (filter.Value.Trim().Length > options.MaxSearchLength)
            {
                throw new QueryOptionsException($"ערך הסינון של '{column.Field.DisplayName}' ארוך מדי (עד {options.MaxSearchLength} תווים).");
            }

            conditions.Add(ColumnFilter(column, filter.Value.Trim(), w));
        }

        return conditions;
    }

    private static string ColumnFilter(OutputColumn column, string value, Writer w)
    {
        var field = column.Field.Field;
        var target = $"v.{SqlIdentifier.Quote(column.Key)}";
        object? parsed = field.DataType switch
        {
            DataType.Text => null,
            DataType.Enum => value,
            DataType.Integer => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : null,
            DataType.Decimal => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null,
            DataType.Date => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null,
            DataType.Boolean => value.ToUpperInvariant() switch { "TRUE" or "1" => true, "FALSE" or "0" => false, _ => null },
            _ => null,
        };

        if (field.DataType == DataType.Text)
        {
            return $"{target} LIKE {w.Param(LikeContains(value))} ESCAPE N'\\'";
        }

        return parsed is null
            ? throw new QueryOptionsException($"הערך לסינון של '{column.Field.DisplayName}' אינו תקין.")
            : $"{target} = {w.Param(parsed)}";
    }

    private static string OrderBy(QueryModel model, ResultQueryOptions options)
    {
        var direction = options.SortDescending ? "DESC" : "ASC";
        if (string.IsNullOrEmpty(options.SortKey))
        {
            return $"v.{SqlIdentifier.Quote(IdColumn)} {direction}";
        }

        if (options.SortKey != TitleColumn && model.Columns.All(c => c.Key != options.SortKey))
        {
            throw new QueryOptionsException("לא ניתן למיין לפי עמודה זו.");
        }

        // __id as a tie-breaker keeps paging stable.
        return $"v.{SqlIdentifier.Quote(options.SortKey)} {direction}, v.{SqlIdentifier.Quote(IdColumn)} ASC";
    }

    private static void ValidatePaging(ResultQueryOptions options)
    {
        if (options.Page < 1)
        {
            throw new QueryOptionsException("מספר העמוד חייב להיות 1 ומעלה.");
        }

        if (options.PageSize < 1 || options.PageSize > options.MaxPageSize)
        {
            throw new QueryOptionsException($"גודל עמוד חייב להיות בין 1 ל-{options.MaxPageSize}.");
        }
    }

    // ---------- FROM / identifiers ----------

    private static string Table(TableSource t) => $"{SqlIdentifier.Quote(t.Schema)}.{SqlIdentifier.Quote(t.Table)}";

    private static string Col(string alias, string column) => $"{SqlIdentifier.Alias(alias)}.{SqlIdentifier.Quote(column)}";

    private static string From(TableSource root, IReadOnlyList<Join> joins, string indent)
    {
        var sb = new StringBuilder($"{Table(root)} AS {SqlIdentifier.Alias(root.Alias)}");
        foreach (var j in joins)
        {
            sb.Append('\n').Append(indent)
              .Append(Inv, $"LEFT JOIN {Table(j.Table)} AS {SqlIdentifier.Alias(j.Table.Alias)} ON {Col(j.Table.Alias, j.Column)} = {Col(j.ParentAlias, j.ParentColumn)}");
        }

        return sb.ToString();
    }

    /// <summary>"%value%" with LIKE wildcards escaped, so user text is matched literally.</summary>
    private static string LikeContains(string value) =>
        "%" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
                   .Replace("%", "\\%", StringComparison.Ordinal)
                   .Replace("_", "\\_", StringComparison.Ordinal)
                   .Replace("[", "\\[", StringComparison.Ordinal) + "%";

    // ---------- predicates and expressions ----------

    /// <summary>Builds predicate text and collects parameters for one statement.</summary>
    private sealed class Writer(DateOnly today)
    {
        private const string TodayParam = "@today";
        private readonly List<SqlParam> _parameters = [];
        private int _nextParam;
        private bool _todayAdded;

        public IReadOnlyList<SqlParam> Parameters => _parameters;

        public string Group(GroupPredicate group, Dictionary<int, string>? flags)
        {
            var parts = group.Children.Select(child => child switch
            {
                GroupPredicate g => Group(g, flags),
                ComparisonPredicate c => Remember(c.Flag, Comparison(c), flags),
                ExistsPredicate e => Remember(e.Flag, Exists(e), flags),
                _ => throw new NotSupportedException(child.GetType().Name),
            });

            var separator = group.Logic == LogicalOperator.And ? " AND " : " OR ";
            return "(" + string.Join(separator, parts) + ")";
        }

        public string Expr(FieldRef field)
        {
            var column = Col(field.Alias, field.Field.SourceColumn);
            return field.Field.Transform switch
            {
                FieldTransform.None => column,
                FieldTransform.FullName => $"CONCAT({column}, N' ', {Col(field.Alias, field.Field.TransformArg!)})",
                FieldTransform.YearsSince =>
                    $"(DATEDIFF(YEAR, {column}, {Today()}) - CASE WHEN DATEADD(YEAR, DATEDIFF(YEAR, {column}, {Today()}), {column}) > {Today()} THEN 1 ELSE 0 END)",
                _ => throw new NotSupportedException(field.Field.Transform.ToString()),
            };
        }

        public string Param(object value)
        {
            var name = string.Create(CultureInfo.InvariantCulture, $"@p{_nextParam++}");
            _parameters.Add(value switch
            {
                string s => new SqlParam(name, s, DbType.String, Size: TextParamSize),
                long l => new SqlParam(name, l, DbType.Int64),
                decimal d => new SqlParam(name, d, DbType.Decimal, Precision: 28, Scale: 8),
                DateOnly date => new SqlParam(name, date, DbType.Date),
                bool b => new SqlParam(name, b, DbType.Boolean),
                _ => throw new NotSupportedException($"Parameter type {value.GetType().Name}"),
            });
            return name;
        }

        private static string Remember(int? flag, string sql, Dictionary<int, string>? flags)
        {
            if (flag is int f && flags is not null)
            {
                flags[f] = sql;
            }

            return sql;
        }

        private string Today()
        {
            if (!_todayAdded)
            {
                _parameters.Add(new SqlParam(TodayParam, today, DbType.Date));
                _todayAdded = true;
            }

            return TodayParam;
        }

        private string Exists(ExistsPredicate e)
        {
            var correlation = $"{Col(e.Table.Alias, e.Column)} = {Col(e.ParentAlias, e.ParentColumn)}";
            var where = e.Where is null ? correlation : $"{correlation} AND {Group(e.Where, flags: null)}";
            return $"{(e.Negated ? "NOT " : "")}EXISTS (SELECT 1 FROM {From(e.Table, e.Joins, indent: " ")} WHERE {where})";
        }

        private string Comparison(ComparisonPredicate c)
        {
            if (c.Left.Field.Transform == FieldTransform.YearsSince && c.Right is not FieldOperandRef)
            {
                return YearsSince(c);
            }

            var left = Expr(c.Left);
            var isText = c.Left.Field.DataType == DataType.Text;

            switch (c.Operator)
            {
                case ConditionOperator.IsEmpty:
                    return isText ? $"({left} IS NULL OR TRIM({left}) = N'')" : $"{left} IS NULL";
                case ConditionOperator.IsNotEmpty:
                    return isText ? $"({left} IS NOT NULL AND TRIM({left}) <> N'')" : $"{left} IS NOT NULL";
                case ConditionOperator.Between when c.Right is RangeOperand range:
                    return $"{left} BETWEEN {Param(range.From)} AND {Param(range.To)}";
                case ConditionOperator.In when c.Right is ListOperand list:
                    return $"{left} IN ({string.Join(", ", list.Values.Select(Param))})";
                case ConditionOperator.Contains when c.Right is ValueOperand { Value: string text }:
                    return $"{left} LIKE {Param(LikeContains(text))} ESCAPE N'\\'";
            }

            var right = c.Right switch
            {
                ValueOperand v => Param(v.Value),
                FieldOperandRef f => Expr(f.Field),
                _ => throw new NotSupportedException($"Operator {c.Operator} with operand {c.Right?.GetType().Name}"),
            };

            return c.Operator switch
            {
                ConditionOperator.Eq => $"{left} = {right}",
                ConditionOperator.Neq when c.Right is FieldOperandRef =>
                    $"({left} <> {right} OR ({left} IS NULL AND {right} IS NOT NULL) OR ({left} IS NOT NULL AND {right} IS NULL))",
                ConditionOperator.Neq => $"({left} <> {right} OR {left} IS NULL)",
                ConditionOperator.Gt => $"{left} > {right}",
                ConditionOperator.Gte => $"{left} >= {right}",
                ConditionOperator.Lt => $"{left} < {right}",
                ConditionOperator.Lte => $"{left} <= {right}",
                _ => throw new NotSupportedException(c.Operator.ToString()),
            };
        }

        /// <summary>Age-style fields compared to literals: rewritten to sargable birth-date ranges (see <see cref="YearsSinceCutoff"/>).</summary>
        private string YearsSince(ComparisonPredicate c)
        {
            var b = Col(c.Left.Alias, c.Left.Field.SourceColumn);
            string AtLeast(long n) => $"{b} <= {Param(YearsSinceCutoff.LatestDateForYearsSince(today, n))}";
            string Under(long n) => $"{b} > {Param(YearsSinceCutoff.LatestDateForYearsSince(today, n))}";
            string Exactly(long n) => $"({AtLeast(n)} AND {Under(n + 1)})";

            return (c.Operator, c.Right) switch
            {
                (ConditionOperator.IsEmpty, _) => $"{b} IS NULL",
                (ConditionOperator.IsNotEmpty, _) => $"{b} IS NOT NULL",
                (ConditionOperator.Gte, ValueOperand { Value: long n }) => AtLeast(n),
                (ConditionOperator.Gt, ValueOperand { Value: long n }) => AtLeast(n + 1),
                (ConditionOperator.Lt, ValueOperand { Value: long n }) => Under(n),
                (ConditionOperator.Lte, ValueOperand { Value: long n }) => Under(n + 1),
                (ConditionOperator.Eq, ValueOperand { Value: long n }) => Exactly(n),
                (ConditionOperator.Neq, ValueOperand { Value: long n }) => $"({Under(n)} OR {AtLeast(n + 1)} OR {b} IS NULL)",
                (ConditionOperator.Between, RangeOperand { From: long from, To: long to }) => $"({AtLeast(from)} AND {Under(to + 1)})",
                (ConditionOperator.In, ListOperand list) => "(" + string.Join(" OR ", list.Values.Cast<long>().Select(Exactly)) + ")",
                _ => throw new NotSupportedException($"{c.Operator} on a years-since field"),
            };
        }
    }
}
