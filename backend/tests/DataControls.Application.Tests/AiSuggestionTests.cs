using System.Text.Json;
using DataControls.Application.Ai;
using DataControls.Application.Common;
using DataControls.Core.Controls;
using DataControls.Core.Definitions;
using DataControls.Core.Operators;
using DataControls.Core.Tests.Fixtures;
using DataControls.Core.Validation;
using Shouldly;

namespace DataControls.Application.Tests;

public class AiSuggestionTests
{
    private static ControlSuggestion Suggestion(string fixture) =>
        JsonSerializer.Deserialize<ControlSuggestion>(AiMockFixtures.Response(fixture), DefinitionSerializer.Options)!;

    private static string Canonical(ControlDefinition definition) => DefinitionSerializer.Serialize(definition);

    // ---------- SchemaContextBuilder ----------

    [Fact]
    public void Context_describes_the_catalog_in_business_terms()
    {
        var context = SchemaContextBuilder.Build(TestCatalog.Instance, ValidationLimits.Default);

        context.Entities.Select(e => e.Key).ShouldBe(TestCatalog.Instance.Entities.Select(e => e.Key));
        var employee = context.Entities.Single(e => e.Key == "employee");
        employee.DisplayNamePlural.ShouldBe("עובדים");
        employee.Fields.Single(f => f.Key == "salary").Unit.ShouldBe("₪");
        employee.Relations.Single(r => r.Key == "department").TargetEntity.ShouldBe("department");
        context.Entities.Single(e => e.Key == "project").Fields.Single(f => f.Key == "status").AllowedValues.ShouldNotBeEmpty();
        context.Operators.Count.ShouldBe(OperatorRegistry.All.Count);
    }

    [Fact]
    public void Context_never_reveals_physical_names()
    {
        var json = JsonSerializer.Serialize(SchemaContextBuilder.Build(TestCatalog.Instance, ValidationLimits.Default), DefinitionSerializer.Options);

        // Case-sensitive: the relation key "employees" is a business key, the table "Employees" is not.
        foreach (var entity in TestCatalog.Instance.Entities)
        {
            json.ShouldNotContain($"\"{entity.TableName}\"", Case.Sensitive);
            foreach (var field in entity.Fields.Where(f => !string.Equals(f.SourceColumn, f.Key, StringComparison.OrdinalIgnoreCase)))
            {
                json.ShouldNotContain($"\"{field.SourceColumn}\"", Case.Sensitive);
            }
        }
        json.ShouldNotContain("schemaName");
        json.ShouldNotContain("sourceColumn");
    }

    [Fact]
    public void Context_is_sent_in_the_ai_service_json_format()
    {
        var json = JsonDocument.Parse(JsonSerializer.Serialize(SchemaContextBuilder.Build(TestCatalog.Instance, ValidationLimits.Default), DefinitionSerializer.Options)).RootElement;

        var employee = json.GetProperty("entities")[0];
        employee.GetProperty("displayNamePlural").GetString().ShouldBe("עובדים");
        employee.GetProperty("relations")[0].GetProperty("cardinality").GetString().ShouldBe("manyToOne");
        employee.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("dataType").GetString()).ShouldContain("integer");
        var isActive = employee.GetProperty("fields").EnumerateArray().Single(f => f.GetProperty("key").GetString() == "isActive");
        (isActive.GetProperty("trueText").GetString(), isActive.GetProperty("falseText").GetString()).ShouldBe(("פעיל", "אינו פעיל"));
        var operators = json.GetProperty("operators").EnumerateArray().ToList();
        operators.Select(o => o.GetProperty("key").GetString()).ShouldContain("isEmpty");
        operators.Single(o => o.GetProperty("key").GetString() == "between").GetProperty("shape").GetString().ShouldBe("range");
        operators.Single(o => o.GetProperty("key").GetString() == "lt").GetProperty("allowsFieldOperand").GetBoolean().ShouldBeTrue();
        json.GetProperty("maxRelationHops").GetInt32().ShouldBe(ValidationLimits.Default.MaxRelationHops);
    }

    [Fact]
    public async Task The_AI_is_told_the_relation_path_limit_its_answer_is_validated_against()
    {
        var client = new FakeSuggestionClient(_ => Suggestion(AiMockFixtures.ActiveMinorsHighSalary));
        var service = new AiSuggestionService(client, new FakeCatalogProvider(), new FakeControlRepository(), new DefinitionValidator(new ValidationLimits { MaxRelationHops = 1 }));

        await service.SuggestAsync(new("עובדים פעילים מתחת לגיל 18"), CancellationToken.None);

        client.Requests.ShouldHaveSingleItem().Context.MaxRelationHops.ShouldBe(1);
    }

    // ---------- AiSuggestionMapper ----------

    public static TheoryData<string, string> AssignmentExamples => new()
    {
        { AiMockFixtures.MinorsHighSalary, SampleDefinitions.MinorsWithHighSalary },
        { AiMockFixtures.TerminationBeforeHire, SampleDefinitions.TerminationBeforeHire },
        { AiMockFixtures.ActiveInInactiveDepartment, SampleDefinitions.ActiveInInactiveDepartment },
        { AiMockFixtures.MissingRequiredData, SampleDefinitions.ActiveMissingRequiredData },
    };

    [Theory]
    [MemberData(nameof(AssignmentExamples))]
    public void The_ai_answer_to_each_assignment_example_translates_to_the_same_control_as_built_by_hand(string fixture, string handMade) =>
        Canonical(AiSuggestionMapper.Map(Suggestion(fixture))!).ShouldBe(Canonical(DefinitionSerializer.Deserialize(handMade)));

    [Fact]
    public void Translates_values_comparisons_and_groups_into_the_definition()
    {
        var definition = AiSuggestionMapper.Map(new ControlSuggestion(
            RootEntity: "employee",
            Logic: LogicalOperator.Or,
            Conditions:
            [
                new("salary", ConditionOperator.Between, JsonDocument.Parse("[5000, 9000]").RootElement),
                new("terminationDate", ConditionOperator.Lt, CompareToField: "hireDate"),
                new("email", ConditionOperator.IsEmpty),
            ],
            Groups: [new(LogicalOperator.And, [new("isActive", ConditionOperator.Eq, JsonDocument.Parse("true").RootElement)])]))!;

        definition.RootEntity.ShouldBe("employee");
        definition.Filter.Logic.ShouldBe(LogicalOperator.Or);
        var children = definition.Filter.Children;
        ((LiteralOperand)((ConditionNode)children[0]).Operand!).Value.GetRawText().ShouldBe("[5000, 9000]");
        ((ConditionNode)children[1]).Operand.ShouldBe(new FieldOperand("hireDate"));
        ((ConditionNode)children[2]).Operand.ShouldBeNull();
        var group = (GroupNode)children[3];
        group.Logic.ShouldBe(LogicalOperator.And);
        ((LiteralOperand)((ConditionNode)group.Children[0]).Operand!).Value.GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void An_answer_without_conditions_or_records_translates_to_nothing()
    {
        AiSuggestionMapper.Map(new ControlSuggestion(Explanation: "לא הבנתי")).ShouldBeNull();
        AiSuggestionMapper.Map(new ControlSuggestion(RootEntity: "employee")).ShouldBeNull();
    }

    // ---------- AiSuggestionService ----------

    private sealed class FakeSuggestionClient(Func<SuggestRequest, ControlSuggestion> answer) : IControlSuggestionClient
    {
        public List<SuggestRequest> Requests { get; } = [];

        public Task<ControlSuggestion> SuggestAsync(SuggestRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(answer(request));
        }

        public Task<IReadOnlyList<AiExample>> GetExamplesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AiExample>>([new("דוגמה", "טקסט הדוגמה")]);
    }

    [Fact]
    public async Task The_examples_are_the_ais_own()
    {
        var (service, _) = Service(_ => new ControlSuggestion());

        (await service.GetExamplesAsync(CancellationToken.None)).ShouldBe([new AiExample("דוגמה", "טקסט הדוגמה")]);
    }

    private static (AiSuggestionService Service, FakeSuggestionClient Client) Service(
        Func<SuggestRequest, ControlSuggestion> answer, FakeControlRepository? controls = null)
    {
        var client = new FakeSuggestionClient(answer);
        return (new AiSuggestionService(client, new FakeCatalogProvider(), controls ?? new FakeControlRepository(), new DefinitionValidator()), client);
    }

    [Theory]
    [InlineData(new string[0], "עובדים פעילים מתחת לגיל 18 עם שכר גבוה")]                                   // free: as suggested
    [InlineData(new[] { "עובדים פעילים מתחת לגיל 18 עם שכר גבוה" }, "עובדים פעילים מתחת לגיל 18 עם שכר גבוה (2)")]
    [InlineData(new[] { "עובדים פעילים מתחת לגיל 18 עם שכר גבוה", "עובדים פעילים מתחת לגיל 18 עם שכר גבוה (2)" },
        "עובדים פעילים מתחת לגיל 18 עם שכר גבוה (3)")]
    public async Task A_suggested_name_already_taken_becomes_the_first_free_one_so_approving_always_saves(string[] existing, string expected)
    {
        var controls = new FakeControlRepository();
        foreach (var name in existing)
        {
            controls.Seed(new ControlData(name, null, DefinitionSerializer.Deserialize(SampleDefinitions.MinorsWithHighSalary)));
        }
        var (service, _) = Service(_ => Suggestion(AiMockFixtures.ActiveMinorsHighSalary), controls);

        var result = await service.SuggestAsync(new("עובדים פעילים מתחת לגיל 18"), CancellationToken.None);

        result.Name.ShouldBe(expected);
    }

    [Fact]
    public async Task A_valid_suggestion_comes_back_translated_validated_and_worded_in_Hebrew()
    {
        var (service, client) = Service(_ => Suggestion(AiMockFixtures.ActiveMinorsHighSalary));

        var result = await service.SuggestAsync(new("  מצא עובדים פעילים מתחת לגיל 18  "), CancellationToken.None);

        client.Requests.Single().Text.ShouldBe("מצא עובדים פעילים מתחת לגיל 18");
        client.Requests.Single().Context.Entities.ShouldNotBeEmpty();
        result.Understood.ShouldBeTrue();
        result.IsValid.ShouldBeTrue();
        result.ValidationErrors.ShouldBeEmpty();
        result.DefinitionDescription.ShouldBe("עובד פעיל וגם גיל קטן מ-18 וגם שכר חודשי גדול מ-15,000 ₪");
        result.Name.ShouldBe("עובדים פעילים מתחת לגיל 18 עם שכר גבוה");
        result.Assumptions.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task An_invented_field_is_caught_by_the_same_validator_as_a_hand_made_control()
    {
        var (service, _) = Service(_ => Suggestion(AiMockFixtures.ErrorDemo));

        var result = await service.SuggestAsync(new("דמה שגיאה"), CancellationToken.None);

        result.Understood.ShouldBeTrue();
        result.IsValid.ShouldBeFalse();
        result.DefinitionDescription.ShouldBeNull();
        var error = result.ValidationErrors.ShouldHaveSingleItem();
        error.Path.ShouldBe("filter.children[0].field");
        error.Code.ShouldBe(nameof(ValidationErrorCode.UnknownField));
    }

    [Fact]
    public async Task A_request_the_ai_did_not_understand_comes_back_with_its_explanation_and_no_definition()
    {
        var (service, _) = Service(r => new ControlSuggestion(Explanation: "לא הצלחתי להבין את הבקשה.", UnmappedTerms: [r.Text]));

        var result = await service.SuggestAsync(new("מזג אוויר"), CancellationToken.None);

        result.Understood.ShouldBeFalse();
        result.Definition.ShouldBeNull();
        result.IsValid.ShouldBeFalse();
        result.Explanation.ShouldBe("לא הצלחתי להבין את הבקשה.");
        result.UnmappedTerms.ShouldBe(["מזג אוויר"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task An_empty_request_is_rejected_before_asking_the_ai(string? text)
    {
        var (service, client) = Service(_ => throw new InvalidOperationException("must not be called"));

        var error = await Should.ThrowAsync<InputValidationException>(() => service.SuggestAsync(new(text), CancellationToken.None));

        error.Errors.ShouldContainKey("text");
        client.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_too_long_request_is_rejected_before_asking_the_ai()
    {
        var (service, client) = Service(_ => throw new InvalidOperationException("must not be called"));

        await Should.ThrowAsync<InputValidationException>(() =>
            service.SuggestAsync(new(new string('א', ValidationLimits.Default.MaxAiTextLength + 1)), CancellationToken.None));
        client.Requests.ShouldBeEmpty();
    }
}
