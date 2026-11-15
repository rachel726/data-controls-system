using DataControls.Core.Sql;
using Shouldly;

namespace DataControls.Core.Tests.Sql;

public class YearsSinceCutoffTests
{
    public static TheoryData<string> Todays => new()
    {
        "2026-11-12",
        "2026-02-28",   // non-leap year, the day leap-day babies "turn" a year
        "2026-03-01",
        "2028-02-28",
        "2028-02-29",   // leap day itself
        "2028-03-01",
        "2026-12-31",
        "2027-01-01",
    };

    /// <summary>
    /// The rewrite is only safe if it is EXACTLY equivalent to the age expression shown to the user.
    /// Checked for every birth date over 25 years (incl. all 29-Feb birthdays) and several ages.
    /// </summary>
    [Theory]
    [MemberData(nameof(Todays))]
    public void Birth_date_cutoff_is_exactly_equivalent_to_age_at_least_N(string todayText)
    {
        var today = DateOnly.Parse(todayText, System.Globalization.CultureInfo.InvariantCulture);
        int[] ages = [0, 1, 15, 17, 18, 19, 21, 65];
        var mismatches = new List<string>();

        for (var birth = new DateOnly(1999, 1, 1); birth <= new DateOnly(2024, 12, 31); birth = birth.AddDays(1))
        {
            var age = YearsSinceCutoff.YearsSince(birth, today);
            foreach (var n in ages)
            {
                if (age >= n != birth <= YearsSinceCutoff.LatestDateForYearsSince(today, n))
                {
                    mismatches.Add($"born {birth:yyyy-MM-dd}, age {age}, N={n}");
                }
            }
        }

        mismatches.ShouldBeEmpty();
    }

    [Fact]
    public void Leap_day_birthday_follows_the_DATEADD_rule()
    {
        var leapBaby = new DateOnly(2008, 2, 29);

        YearsSinceCutoff.YearsSince(leapBaby, new DateOnly(2026, 2, 27)).ShouldBe(17);
        YearsSinceCutoff.YearsSince(leapBaby, new DateOnly(2026, 2, 28)).ShouldBe(18);
        YearsSinceCutoff.LatestDateForYearsSince(new DateOnly(2026, 2, 28), 18).ShouldBe(new DateOnly(2008, 2, 29));
    }

    [Fact]
    public void Regression_day_before_a_leap_day()
    {
        // Found by the equivalence test: on 28-Feb-2028 someone born 28-Feb-2010 turns 18
        // (an earlier "+1 day / −1 day" formula wrongly answered 17).
        YearsSinceCutoff.YearsSince(new DateOnly(2010, 2, 28), new DateOnly(2028, 2, 28)).ShouldBe(18);
        YearsSinceCutoff.LatestDateForYearsSince(new DateOnly(2028, 2, 28), 18).ShouldBe(new DateOnly(2010, 2, 28));
    }

    [Theory]
    [InlineData(1_000_000)]
    [InlineData(-1_000_000)]
    [InlineData(long.MaxValue)]
    public void Absurd_ages_do_not_crash(long years)
    {
        var cutoff = YearsSinceCutoff.LatestDateForYearsSince(new DateOnly(2026, 11, 12), years);

        cutoff.ShouldBe(years > 0 ? DateOnly.MinValue : DateOnly.MaxValue);
    }
}
