using DataControls.Application.Controls;
using Shouldly;

namespace DataControls.Application.Tests;

/// <summary>The application layer orchestrates use cases through Core abstractions only - no data access, no web.</summary>
public class ArchitectureTests
{
    [Theory]
    [InlineData("Dapper")]
    [InlineData("Microsoft.Data.SqlClient")]
    [InlineData("DataControls.Infrastructure")]
    [InlineData("DataControls.Api")]
    public void Application_does_not_reference(string forbiddenAssembly) =>
        typeof(ControlService).Assembly.GetReferencedAssemblies().Select(a => a.Name).ShouldNotContain(forbiddenAssembly);
}
