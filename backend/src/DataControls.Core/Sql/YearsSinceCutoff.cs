namespace DataControls.Core.Sql;

/// <summary>
/// Supports the generic <c>YearsSince</c> field transform (whole years since a date column: age from a birth date,
/// seniority from a hire date, ...). Rewrites "years since d ≥ N" into a date range on the column itself, so the
/// database can use an index on it instead of computing the value for every row.
///
/// Years since d at today t - the same definition as the SQL display expression:
/// <c>DATEDIFF(YEAR, d, t) - (DATEADD(YEAR, DATEDIFF(YEAR, d, t), d) &gt; t ? 1 : 0)</c>,
/// i.e. years(d, t) ≥ N  ⇔  d + N years ≤ t   (DATEADD rule: 29-Feb + 1 year = 28-Feb).
///
/// The cutoff is the LATEST date d satisfying that: start from t − N years and step one day forward if that day
/// also qualifies (29-Feb turns N years on 28-Feb in non-leap years). Proven against <see cref="YearsSince"/> for every
/// date over 25 years in <c>YearsSinceCutoffTests</c>, and against the SQL expression on real data in the integration tests.
/// </summary>
public static class YearsSinceCutoff
{
    /// <summary>Latest date d for which at least <paramref name="years"/> whole years have passed by <paramref name="today"/>.</summary>
    public static DateOnly LatestDateForYearsSince(DateOnly today, long years)
    {
        var y = (int)Math.Clamp(years, -9000, 9000);
        try
        {
            var cutoff = today.AddYears(-y);
            var next = cutoff.AddDays(1);
            return next.AddYears(y) <= today ? next : cutoff;
        }
        catch (ArgumentOutOfRangeException)
        {
            // Far beyond the calendar: nobody (or everybody) qualifies.
            return y > 0 ? DateOnly.MinValue : DateOnly.MaxValue;
        }
    }

    /// <summary>Whole years from <paramref name="date"/> to <paramref name="today"/> - the C# twin of the SQL display expression.</summary>
    public static int YearsSince(DateOnly date, DateOnly today)
    {
        var years = today.Year - date.Year;
        return date.AddYears(years) > today ? years - 1 : years;
    }
}
