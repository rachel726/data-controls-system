using DataControls.Application.Common;
using DataControls.Application.Controls;
using DataControls.Core.Controls;
using DataControls.Core.Definitions;
using DataControls.Core.Tests.Fixtures;
using DataControls.Core.Validation;
using Shouldly;

namespace DataControls.Application.Tests;

public class ControlServiceTests
{
    private readonly FakeControlRepository _repository = new();
    private readonly ControlService _service;
    private static readonly CancellationToken None = CancellationToken.None;

    public ControlServiceTests() => _service = new ControlService(_repository, new FakeCatalogProvider(), new DefinitionValidator());

    private static ControlDefinition Def(string json) => DefinitionSerializer.Deserialize(json);

    [Fact]
    public async Task Creates_a_valid_control_and_returns_its_Hebrew_description()
    {
        var dto = await _service.CreateAsync(new SaveControlRequest("  קטינים  ", "תיאור", Def(SampleDefinitions.MinorsWithHighSalary)), None);

        dto.Name.ShouldBe("קטינים");
        dto.DefinitionDescription.ShouldBe("גיל קטן מ-18 וגם שכר חודשי גדול מ-15,000 ₪");
        dto.LastRun.ShouldBeNull();
    }

    [Fact]
    public async Task Rejects_an_invalid_definition_without_writing_anything()
    {
        var invalid = Def(TestJson.UnknownField);

        var ex = await Should.ThrowAsync<DefinitionInvalidException>(() => _service.CreateAsync(new SaveControlRequest("x", null, invalid), None));

        ex.Errors.ShouldHaveSingleItem().Code.ShouldBe(ValidationErrorCode.UnknownField);
        _repository.Writes.ShouldBe(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Requires_a_name(string? name)
    {
        var ex = await Should.ThrowAsync<InputValidationException>(() =>
            _service.CreateAsync(new SaveControlRequest(name, null, Def(SampleDefinitions.MinorsWithHighSalary)), None));

        ex.Errors.Keys.ShouldBe(["name"]);
    }

    [Fact]
    public async Task Reports_every_invalid_request_field_at_once()
    {
        var ex = await Should.ThrowAsync<InputValidationException>(() =>
            _service.CreateAsync(new SaveControlRequest(new string('א', 201), new string('ב', 1001), null), None));

        ex.Errors.Keys.ShouldBe(["name", "description", "definition"], ignoreOrder: true);
    }

    [Fact]
    public async Task Updates_and_reports_missing_controls()
    {
        var created = await _service.CreateAsync(new SaveControlRequest("לפני", null, Def(SampleDefinitions.MinorsWithHighSalary)), None);

        var updated = await _service.UpdateAsync(created.Id, new SaveControlRequest("אחרי", null, Def(SampleDefinitions.TerminationBeforeHire)), None);
        updated.Name.ShouldBe("אחרי");
        updated.DefinitionDescription.ShouldBe("תאריך סיום העסקה לפני תאריך תחילת העסקה");

        await Should.ThrowAsync<NotFoundException>(() =>
            _service.UpdateAsync(999, new SaveControlRequest("x", null, Def(SampleDefinitions.MinorsWithHighSalary)), None));
        await Should.ThrowAsync<NotFoundException>(() => _service.DeleteAsync(999, None));
        await Should.ThrowAsync<NotFoundException>(() => _service.GetAsync(999, None));
    }

    [Fact]
    public async Task Lists_with_the_business_name_of_the_root_entity()
    {
        await _service.CreateAsync(new SaveControlRequest("פרויקטים", null, Def(SampleDefinitions.ProjectEndBeforeStart)), None);

        var item = (await _service.ListAsync(null, None)).ShouldHaveSingleItem();

        (item.RootEntityKey, item.RootEntityName).ShouldBe(("project", "פרויקטים"));
    }

    [Fact]
    public async Task Validate_returns_the_sentence_for_a_valid_definition_and_errors_otherwise()
    {
        var ok = await _service.ValidateAsync(Def(SampleDefinitions.ActiveDepartmentWithoutActiveEmployees), None);
        ok.IsValid.ShouldBeTrue();
        ok.Description.ShouldBe("מחלקה פעילה וגם אין אף עובד פעיל");

        var bad = await _service.ValidateAsync(Def(TestJson.ContainsOnAge), None);
        bad.IsValid.ShouldBeFalse();
        bad.Description.ShouldBeNull();
        bad.Errors.ShouldHaveSingleItem().ShouldBe(new ValidationErrorDto("filter.children[0].operator", "OperatorNotAllowedForType",
            "האפשרות 'מכיל' אינה מתאימה לשדה 'גיל' (מספר שלם).", IsIncomplete: false));

        (await _service.ValidateAsync(null, None)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task A_stored_control_that_no_longer_fits_the_catalog_is_flagged_not_crashed()
    {
        // Saved when the catalog had "shoeSize"; the catalog no longer has it.
        var stale = _repository.Seed(new ControlData("ישנה", null, Def(TestJson.UnknownField)));

        var dto = await _service.GetAsync(stale.Id, None);

        dto.DefinitionDescription.ShouldContain("אינה תקינה עוד");
    }

    [Fact]
    public async Task Duplicate_names_surface_the_repository_error() =>
        await Should.ThrowAsync<DuplicateControlNameException>(async () =>
        {
            await _service.CreateAsync(new SaveControlRequest("כפול", null, Def(SampleDefinitions.MinorsWithHighSalary)), None);
            await _service.CreateAsync(new SaveControlRequest("כפול", null, Def(SampleDefinitions.MinorsWithHighSalary)), None);
        });
}
