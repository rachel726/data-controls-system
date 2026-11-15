using DataControls.Core.Controls;
using DataControls.Core.Definitions;
using DataControls.Core.Tests.Fixtures;
using DataControls.Infrastructure.Controls;
using DataControls.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Shouldly;

namespace DataControls.IntegrationTests.Controls;

/// <summary>Repository behaviour on the real database. Every test removes the rows it created.</summary>
public sealed class ControlRepositoryTests : IAsyncLifetime
{
    private readonly ControlRepository _repository = new(new SqlConnectionFactory(TestDatabase.ConnectionString));
    private readonly string _prefix = $"בדיקה-{Guid.NewGuid():N}"[..20];
    private static readonly CancellationToken None = CancellationToken.None;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var connection = new SqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("DELETE FROM meta.Controls WHERE Name LIKE @p + '%';", connection);
        command.Parameters.AddWithValue("@p", _prefix);
        await command.ExecuteNonQueryAsync();
    }

    private ControlData Data(string name, string json = SampleDefinitions.MinorsWithHighSalary, string? description = "תיאור") =>
        new($"{_prefix} {name}", description, DefinitionSerializer.Deserialize(json));

    [Fact]
    public async Task Creates_and_reads_back_a_control_with_its_definition()
    {
        var data = Data("קטינים");

        var id = await _repository.CreateAsync(data, None);
        var control = (await _repository.GetAsync(id, None)).ShouldNotBeNull();

        control.Name.ShouldBe(data.Name);
        control.Description.ShouldBe("תיאור");
        DefinitionSerializer.Serialize(control.Definition).ShouldBe(DefinitionSerializer.Serialize(data.Definition));
        control.LastRun.ShouldBeNull();
        control.UpdatedAt.ShouldBe(control.CreatedAt);
    }

    [Fact]
    public async Task Timestamps_are_returned_as_UTC_so_clients_convert_them_to_local_time()
    {
        // Regression: DATETIME2 comes back as Unspecified; serialized without "Z" the UI showed UTC as local time.
        var id = await _repository.CreateAsync(Data("זמנים"), None);
        await _repository.RecordRunAsync(id, new LastRun(DateTime.UtcNow, RunStatus.Succeeded, 1, null), None);

        var control = (await _repository.GetAsync(id, None))!;
        var summary = (await _repository.ListAsync(_prefix, None)).Single();

        control.CreatedAt.Kind.ShouldBe(DateTimeKind.Utc);
        control.UpdatedAt.Kind.ShouldBe(DateTimeKind.Utc);
        control.LastRun!.RunAt.Kind.ShouldBe(DateTimeKind.Utc);
        summary.UpdatedAt.Kind.ShouldBe(DateTimeKind.Utc);
        summary.LastRun!.RunAt.Kind.ShouldBe(DateTimeKind.Utc);
        (DateTime.UtcNow - control.LastRun.RunAt).Duration().ShouldBeLessThan(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Trims_name_and_stores_blank_description_as_null()
    {
        var id = await _repository.CreateAsync(Data("  עם רווחים  ", description: "   ") with { Name = $"  {_prefix} רווחים  " }, None);

        var control = (await _repository.GetAsync(id, None))!;
        control.Name.ShouldBe($"{_prefix} רווחים");
        control.Description.ShouldBeNull();
    }

    [Fact]
    public async Task Lists_active_controls_and_searches_name_and_description_literally()
    {
        await _repository.CreateAsync(Data("עובדים קטינים", description: "בדיקת גיל"), None);
        await _repository.CreateAsync(Data("פרויקטים", SampleDefinitions.ProjectEndBeforeStart, "תאריכים 100%"), None);

        var mine = (await _repository.ListAsync(_prefix, None)).Select(c => c.Name).ToList();
        mine.Count.ShouldBe(2);
        mine.ShouldBeInOrder();   // ordered by name

        (await _repository.ListAsync($"{_prefix} עובדים", None)).ShouldHaveSingleItem().RootEntityKey.ShouldBe("employee");
        (await _repository.ListAsync("בדיקת גיל", None)).ShouldContain(c => c.Name.StartsWith(_prefix));        // description
        (await _repository.ListAsync("100%", None)).ShouldContain(c => c.Name.EndsWith("פרויקטים"));           // literal %

        // '%' typed by the user is a literal character, not a wildcard: only the control whose text contains '%' matches.
        var percent = (await _repository.ListAsync("%", None)).Where(c => c.Name.StartsWith(_prefix, StringComparison.Ordinal)).ToList();
        percent.ShouldHaveSingleItem().Name.ShouldEndWith("פרויקטים");
    }

    [Fact]
    public async Task Update_changes_the_control_and_clears_the_last_run()
    {
        var id = await _repository.CreateAsync(Data("לפני"), None);
        await _repository.RecordRunAsync(id, new LastRun(DateTime.UtcNow, RunStatus.Succeeded, 3, null), None);

        var updated = await _repository.UpdateAsync(id, Data("אחרי", SampleDefinitions.ActiveDepartmentWithoutActiveEmployees, "חדש"), None);

        updated.ShouldBeTrue();
        var control = (await _repository.GetAsync(id, None))!;
        control.Name.ShouldEndWith("אחרי");
        control.Description.ShouldBe("חדש");
        control.Definition.RootEntity.ShouldBe("department");
        control.LastRun.ShouldBeNull();
        control.UpdatedAt.ShouldBeGreaterThanOrEqualTo(control.CreatedAt);
    }

    [Fact]
    public async Task Duplicate_active_name_is_rejected_with_a_Hebrew_message_on_create_and_update()
    {
        await _repository.CreateAsync(Data("כפול"), None);
        var otherId = await _repository.CreateAsync(Data("אחר"), None);

        var onCreate = await Should.ThrowAsync<DuplicateControlNameException>(() => _repository.CreateAsync(Data("כפול"), None));
        onCreate.Message.ShouldContain("כבר קיימת בקרה");

        await Should.ThrowAsync<DuplicateControlNameException>(() => _repository.UpdateAsync(otherId, Data("כפול"), None));
    }

    [Fact]
    public async Task Delete_hides_the_control_and_frees_its_name()
    {
        var id = await _repository.CreateAsync(Data("למחיקה"), None);

        (await _repository.DeleteAsync(id, None)).ShouldBeTrue();

        (await _repository.GetAsync(id, None)).ShouldBeNull();
        (await _repository.ListAsync(_prefix, None)).ShouldBeEmpty();
        (await _repository.DeleteAsync(id, None)).ShouldBeFalse();                     // already deleted
        (await _repository.UpdateAsync(id, Data("למחיקה"), None)).ShouldBeFalse();     // cannot edit a deleted control
        await _repository.CreateAsync(Data("למחיקה"), None);                            // the name is free again
    }

    [Fact]
    public async Task Records_successful_and_failed_runs()
    {
        var id = await _repository.CreateAsync(Data("ריצות"), None);
        var at = new DateTime(2026, 11, 12, 10, 30, 0, DateTimeKind.Utc);

        await _repository.RecordRunAsync(id, new LastRun(at, RunStatus.Succeeded, 3, null), None);
        (await _repository.GetAsync(id, None))!.LastRun.ShouldBe(new LastRun(at, RunStatus.Succeeded, 3, null));

        var longError = new string('ש', ControlRepository.MaxErrorLength + 50);
        await _repository.RecordRunAsync(id, new LastRun(at, RunStatus.Failed, null, longError), None);
        var failed = (await _repository.ListAsync(_prefix, None)).Single().LastRun.ShouldNotBeNull();
        failed.Status.ShouldBe(RunStatus.Failed);
        failed.ViolationCount.ShouldBeNull();
        failed.Error!.Length.ShouldBe(ControlRepository.MaxErrorLength);
    }

    [Fact]
    public async Task Operations_on_a_missing_control_report_not_found()
    {
        const int missing = int.MaxValue;

        (await _repository.GetAsync(missing, None)).ShouldBeNull();
        (await _repository.UpdateAsync(missing, Data("x"), None)).ShouldBeFalse();
        (await _repository.DeleteAsync(missing, None)).ShouldBeFalse();
        (await _repository.RecordRunAsync(missing, new LastRun(DateTime.UtcNow, RunStatus.Succeeded, 0, null), None)).ShouldBeFalse();
    }
}
