using System.Text.Json;

namespace DataControls.Core.Tests.Fixtures;

/// <summary>
/// The AI service's mock answers (ai-service/app/providers/mock_fixtures), read from their files so .NET tests use
/// exactly what the Python service returns - the two sides of the contract cannot drift apart unnoticed.
/// </summary>
public static class AiMockFixtures
{
    public const string ActiveMinorsHighSalary = "01-active-minors-high-salary";
    public const string MinorsHighSalary = "02-minors-high-salary";
    public const string TerminationBeforeHire = "03-termination-before-hire";
    public const string ActiveInInactiveDepartment = "04-active-in-inactive-department";
    public const string MissingRequiredData = "05-missing-required-data";
    public const string ErrorDemo = "90-error-demo";

    private static readonly Lazy<string> Directory = new(FindDirectory);

    /// <summary>The answer the mock returns, as JSON text.</summary>
    public static string Response(string name) => Root(name).GetProperty("response").GetRawText();

    /// <summary>The request text the mock answers with this fixture.</summary>
    public static string Request(string name) => Root(name).GetProperty("requests")[0].GetString()!;

    /// <summary>The label under which the fixture is offered as a one-click example, or null when it is not.</summary>
    public static string? Example(string name) =>
        Root(name).TryGetProperty("example", out var label) ? label.GetString() : null;

    public static IReadOnlyList<string> All() =>
        [.. System.IO.Directory.GetFiles(Directory.Value, "*.json").Select(Path.GetFileNameWithoutExtension).Order()!];

    private static JsonElement Root(string name) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Directory.Value, name + ".json"))).RootElement;

    private static string FindDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "ai-service", "app", "providers", "mock_fixtures");
            if (System.IO.Directory.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new DirectoryNotFoundException("ai-service/app/providers/mock_fixtures not found above the test folder.");
    }
}
