using DataControls.Application.Common;
using DataControls.Application.Runs;
using DataControls.Core.Catalog;
using DataControls.Core.Controls;
using DataControls.Core.Definitions;
using DataControls.Core.Sql;
using DataControls.Core.Tests.Fixtures;
using DataControls.Core.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace DataControls.Application.Tests;

public class ControlRunServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly FakeControlRepository _repository = new();
    private readonly FakeQueryExecutor _executor = new();
    private readonly ControlRunService _service;

    public ControlRunServiceTests() => _service = new ControlRunService(
        _repository, new FakeCatalogProvider(), new DefinitionValidator(), new SqlServerRenderer(), _executor,
        new FixedTime(Now), Options.Create(new ControlRunOptions()), NullLogger<ControlRunService>.Instance);

    private Control Seed(string json, string name = "בקרה") =>
        _repository.Seed(new ControlData(name, null, DefinitionSerializer.Deserialize(json)));

    private static Dictionary<string, object?> MinorRow(int id, string name, int age, decimal salary) => new()
    {
        [SqlServerRenderer.IdColumn] = id, [SqlServerRenderer.TitleColumn] = name, [SqlServerRenderer.TotalColumn] = 3,
        ["employeeNumber"] = $"E{id}", ["fullName"] = name, ["isActive"] = true, ["age"] = age, ["salary"] = salary,
        ["__c0"] = 1, ["__c1"] = 1,
    };

    [Fact]
    public async Task Run_returns_the_violations_with_reasons_and_records_a_successful_run()
    {
        var control = Seed(SampleDefinitions.MinorsWithHighSalary, "קטינים");
        _executor.PageRows = [MinorRow(201, "מיכל חדד", 16, 22000m)];

        var result = await _service.RunAsync(control.Id, None);

        (result.ViolationCount, result.TotalScanned, result.TotalFiltered, result.Page, result.PageSize).ShouldBe(((long?)3, (long?)222, 3L, 1, 25));
        (result.ControlName, result.RootEntityName).ShouldBe(("קטינים", "עובדים"));
        result.Columns.Select(c => (c.Key, c.IsConditionField)).ShouldBe(
            [("employeeNumber", false), ("fullName", false), ("isActive", false), ("age", true), ("salary", true)]);

        var row = result.Rows.ShouldHaveSingleItem();
        (row.RecordId, row.Title).ShouldBe((201, "מיכל חדד"));
        row.Values["salary"].ShouldBe("22,000 ₪");
        row.Values["isActive"].ShouldBe("כן");
        row.Reasons.ShouldBe(["גיל (16) קטן מ-18", "שכר חודשי (22,000 ₪) גדול מ-15,000 ₪"]);

        _repository.RecordedRuns.ShouldHaveSingleItem().ShouldBe((control.Id, new LastRun(Now.UtcDateTime, RunStatus.Succeeded, 3, null)));
    }

    [Fact]
    public async Task A_failed_run_is_recorded_with_its_Hebrew_reason_and_the_error_still_surfaces()
    {
        var control = Seed(SampleDefinitions.MinorsWithHighSalary);
        _executor.Throw = new QueryTimeoutException(30);

        var ex = await Should.ThrowAsync<QueryTimeoutException>(() => _service.RunAsync(control.Id, None));

        var (id, run) = _repository.RecordedRuns.ShouldHaveSingleItem();
        id.ShouldBe(control.Id);
        (run.Status, run.ViolationCount, run.Error).ShouldBe((RunStatus.Failed, (int?)null, ex.Message));
    }

    [Fact]
    public async Task A_run_plans_with_the_configured_limits_not_the_defaults()
    {
        // Three relations: beyond the default limit (2), allowed by this configuration - validation and planning must agree.
        var limits = new ValidationLimits { MaxRelationHops = 3 };
        var service = new ControlRunService(
            _repository, new FakeCatalogProvider(), new DefinitionValidator(limits), new SqlServerRenderer(), _executor,
            new FixedTime(Now), Options.Create(new ControlRunOptions()), NullLogger<ControlRunService>.Instance);
        var control = Seed("""
            { "schemaVersion": 1, "rootEntity": "employee", "filter": { "type": "group", "logic": "and", "children": [
              { "type": "condition", "field": "manager.manager.department.name", "operator": "isEmpty" } ] } }
            """);

        var result = await service.RunAsync(control.Id, None);

        result.ViolationCount.ShouldBe(3);
        _repository.RecordedRuns.ShouldHaveSingleItem().Run.Status.ShouldBe(RunStatus.Succeeded);
    }

    [Fact]
    public async Task A_control_that_no_longer_fits_the_catalog_fails_safely_before_any_query()
    {
        var control = Seed(TestJson.UnknownField);

        var ex = await Should.ThrowAsync<DefinitionInvalidException>(() => _service.RunAsync(control.Id, None));

        ex.Message.ShouldContain("אינה תקינה עוד");
        _executor.Queries.ShouldBeEmpty();   // no broken SQL ever reaches the database
        _repository.RecordedRuns.Single().Run.Status.ShouldBe(RunStatus.Failed);
    }

    [Fact]
    public async Task Invalid_result_options_are_rejected_before_the_database_is_touched()
    {
        var control = Seed(SampleDefinitions.MinorsWithHighSalary);

        await Should.ThrowAsync<QueryOptionsException>(() =>
            _service.GetResultsAsync(control.Id, new ResultsRequest(SortKey: "nosuch"), None));

        _executor.Queries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Results_pass_paging_search_and_filters_through_without_recording_a_run()
    {
        var control = Seed(SampleDefinitions.MinorsWithHighSalary);

        var result = await _service.GetResultsAsync(control.Id,
            new ResultsRequest(Page: 2, PageSize: 10, Search: "כהן", SortKey: "salary", SortDescending: true,
                Filters: [new ColumnFilterDto("isActive", "true")]), None);

        var page = _executor.Queries.First(q => q.Sql.StartsWith("WITH", StringComparison.Ordinal));
        page.Sql.ShouldContain("ORDER BY v.[salary] DESC");
        page.Parameters.ShouldContain(p => p.Value as string == "%כהן%");
        page.Parameters.ShouldContain(p => p.Value is long && (long)p.Value == 10);   // offset (2-1)*10
        _repository.RecordedRuns.ShouldBeEmpty();

        // Nothing on page 2 and nothing at all: the count is probed, and the last page that exists (1) is served.
        (result.Page, result.PageSize, result.TotalFiltered).ShouldBe((1, 10, 0L));
    }

    [Fact]
    public async Task Browsing_without_the_summary_does_not_count_again()
    {
        var control = Seed(SampleDefinitions.MinorsWithHighSalary);
        _executor.PageRows = [MinorRow(201, "מיכל חדד", 16, 22000m)];

        var browsed = await _service.GetResultsAsync(control.Id, new ResultsRequest(Search: "חדד", IncludeSummary: false), None);

        _executor.Queries.ShouldHaveSingleItem().Sql.ShouldStartWith("WITH");   // the page only - no counting query
        (browsed.ViolationCount, browsed.TotalScanned, browsed.TotalFiltered).ShouldBe(((long?)null, (long?)null, 3L));
        browsed.Rows.ShouldHaveSingleItem().Reasons.ShouldNotBeEmpty();

        var opened = await _service.GetResultsAsync(control.Id, new ResultsRequest(), None);   // the default counts
        (opened.ViolationCount, opened.TotalScanned).ShouldBe(((long?)3, (long?)222));
    }

    [Fact]
    public async Task Today_comes_from_the_clock_so_age_is_deterministic()
    {
        var control = Seed(SampleDefinitions.MinorsWithHighSalary);

        await _service.GetResultsAsync(control.Id, new ResultsRequest(), None);

        // "under 18" on 12-Nov-2026 ⇔ born after 12-Nov-2008
        _executor.Queries[0].Parameters.ShouldContain(p => p.Value is DateOnly && (DateOnly)p.Value == new DateOnly(2008, 11, 12));
    }

    [Fact]
    public async Task Unknown_controls_are_not_found() =>
        await Should.ThrowAsync<NotFoundException>(() => _service.RunAsync(42, None));

    [Fact]
    public async Task An_empty_first_page_means_zero_filtered()
    {
        var control = Seed(SampleDefinitions.MinorsWithHighSalary);
        _executor.Violations = 0;

        var result = await _service.RunAsync(control.Id, None);

        (result.ViolationCount, result.TotalFiltered).ShouldBe(((long?)0, 0L));
        result.Rows.ShouldBeEmpty();
        _repository.RecordedRuns.Single().Run.ViolationCount.ShouldBe(0);
    }
}
