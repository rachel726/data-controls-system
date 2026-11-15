using DataControls.Core.Catalog;
using Shouldly;

namespace DataControls.Core.Tests;

/// <summary>Core must stay pure: no data access, no HTTP, no dependency on other layers.</summary>
public class ArchitectureTests
{
    [Theory]
    [InlineData("Dapper")]
    [InlineData("Microsoft.Data.SqlClient")]
    [InlineData("System.Net.Http")]
    [InlineData("DataControls.Application")]
    [InlineData("DataControls.Infrastructure")]
    [InlineData("DataControls.Api")]
    public void Core_does_not_reference(string forbiddenAssembly)
    {
        var references = typeof(MetadataCatalog).Assembly.GetReferencedAssemblies().Select(a => a.Name);

        references.ShouldNotContain(forbiddenAssembly);
    }
}
