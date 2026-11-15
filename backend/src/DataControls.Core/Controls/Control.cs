using DataControls.Core.Definitions;

namespace DataControls.Core.Controls;

/// <summary>A saved control: business metadata + its definition + the outcome of its last run.</summary>
public sealed record Control(
    int Id,
    string Name,
    string? Description,
    ControlDefinition Definition,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    LastRun? LastRun);

/// <summary>A control in the list screen (no definition needed).</summary>
public sealed record ControlSummary(
    int Id,
    string Name,
    string? Description,
    string RootEntityKey,
    DateTime UpdatedAt,
    LastRun? LastRun);

/// <summary>What the user edits: name, description and definition.</summary>
public sealed record ControlData(string Name, string? Description, ControlDefinition Definition);

public enum RunStatus
{
    Succeeded,
    Failed,
}

/// <summary>Outcome of the most recent run: when, success or failure, how many violations, or why it failed.</summary>
public sealed record LastRun(DateTime RunAt, RunStatus Status, int? ViolationCount, string? Error);

/// <summary>Another active control already has this name. <see cref="Exception.Message"/> is user-facing Hebrew.</summary>
public sealed class DuplicateControlNameException(string name)
    : Exception($"כבר קיימת בקרה בשם '{name}'. בחרו שם אחר.")
{
    public string Name { get; } = name;
}
