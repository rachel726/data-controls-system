using DataControls.Core.Catalog;
using DataControls.Core.Definitions;
using DataControls.Core.Operators;
using Shouldly;
using static DataControls.Core.Catalog.DataType;
using static DataControls.Core.Definitions.ConditionOperator;

namespace DataControls.Core.Tests.Operators;

public class OperatorRegistryTests
{
    [Fact]
    public void Every_operator_of_the_contract_is_registered()
    {
        // Guards future additions: a new ConditionOperator must be described here before it can be used.
        OperatorRegistry.All.Select(o => o.Operator).ShouldBe(System.Enum.GetValues<ConditionOperator>(), ignoreOrder: true);
    }

    /// <summary>The full type ⟷ operator matrix of PLAN §5.2, row by row.</summary>
    public static TheoryData<DataType, ConditionOperator[]> Matrix => new()
    {
        { Text,    [Eq, Neq, In, Contains, IsEmpty, IsNotEmpty] },
        { Integer, [Eq, Neq, Gt, Gte, Lt, Lte, Between, In, IsEmpty, IsNotEmpty] },
        { DataType.Decimal, [Eq, Neq, Gt, Gte, Lt, Lte, Between, IsEmpty, IsNotEmpty] },
        { Date,    [Eq, Neq, Gt, Gte, Lt, Lte, Between, IsEmpty, IsNotEmpty] },
        { DataType.Boolean, [Eq, Neq, IsEmpty, IsNotEmpty] },
        { DataType.Enum, [Eq, Neq, In, IsEmpty, IsNotEmpty] },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void Offers_exactly_the_operators_that_make_sense_for_each_type(DataType type, ConditionOperator[] expected)
    {
        OperatorRegistry.ForType(type).Select(o => o.Operator).ShouldBe(expected, ignoreOrder: true);
    }

    [Theory]
    [InlineData(Contains, Integer)]   // "contains" on a number
    [InlineData(Gt, Text)]            // "greater than" on text
    [InlineData(Gt, DataType.Boolean)]
    [InlineData(Between, DataType.Enum)]
    [InlineData(In, Date)]
    public void Rejects_meaningless_combinations(ConditionOperator op, DataType type)
    {
        OperatorRegistry.Get(op).IsAllowedFor(type).ShouldBeFalse();
    }

    [Theory]
    [InlineData(Gt, "גדול מ", "אחרי")]
    [InlineData(Gte, "גדול או שווה ל", "בתאריך או אחרי")]
    [InlineData(Lt, "קטן מ", "לפני")]
    [InlineData(Lte, "קטן או שווה ל", "בתאריך או לפני")]
    [InlineData(Eq, "שווה ל", "שווה ל")]
    public void Uses_date_wording_for_dates_only(ConditionOperator op, string numberLabel, string dateLabel)
    {
        var info = OperatorRegistry.Get(op);

        info.LabelFor(Integer).ShouldBe(numberLabel);
        info.LabelFor(DataType.Decimal).ShouldBe(numberLabel);
        info.LabelFor(Date).ShouldBe(dateLabel);
    }

    [Theory]
    [InlineData(IsEmpty, OperandShape.None)]
    [InlineData(IsNotEmpty, OperandShape.None)]
    [InlineData(Between, OperandShape.Range)]
    [InlineData(In, OperandShape.List)]
    [InlineData(Contains, OperandShape.Value)]
    [InlineData(Lt, OperandShape.Value)]
    public void Declares_the_operand_shape(ConditionOperator op, OperandShape shape)
    {
        OperatorRegistry.Get(op).Shape.ShouldBe(shape);
    }

    [Fact]
    public void Field_to_field_comparison_is_allowed_only_for_single_value_comparisons()
    {
        OperatorRegistry.All.Where(o => o.AllowsFieldOperand).Select(o => o.Operator)
            .ShouldBe([Eq, Neq, Gt, Gte, Lt, Lte], ignoreOrder: true);
    }

    [Fact]
    public void Keys_match_the_JSON_contract_and_labels_are_Hebrew()
    {
        OperatorRegistry.Get(IsNotEmpty).Key.ShouldBe("isNotEmpty");
        OperatorRegistry.Get(Eq).Key.ShouldBe("eq");

        foreach (var info in OperatorRegistry.All)
        {
            info.Label.ShouldNotBeNullOrWhiteSpace();
            info.Label.Any(c => c is >= 'א' and <= 'ת').ShouldBeTrue(info.Key);
        }
    }

    [Fact]
    public void Unknown_operator_value_fails_loudly() =>
        Should.Throw<ArgumentOutOfRangeException>(() => OperatorRegistry.Get((ConditionOperator)999));
}
