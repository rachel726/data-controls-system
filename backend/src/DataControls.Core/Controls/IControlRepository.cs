namespace DataControls.Core.Controls;

/// <summary>Persistence of saved controls. Deleted controls are kept (soft delete) but never returned.</summary>
public interface IControlRepository
{
    /// <summary>Active controls ordered by name, optionally filtered by text in the name or description.</summary>
    Task<IReadOnlyList<ControlSummary>> ListAsync(string? search, CancellationToken cancellationToken);

    /// <returns>The control, or null if it does not exist or was deleted.</returns>
    Task<Control?> GetAsync(int id, CancellationToken cancellationToken);

    /// <exception cref="DuplicateControlNameException">An active control has the same name.</exception>
    Task<int> CreateAsync(ControlData data, CancellationToken cancellationToken);

    /// <summary>Updates the control and clears its last-run outcome (it no longer reflects the new definition).</summary>
    /// <returns>False if the control does not exist or was deleted.</returns>
    /// <exception cref="DuplicateControlNameException">Another active control has the same name.</exception>
    Task<bool> UpdateAsync(int id, ControlData data, CancellationToken cancellationToken);

    /// <returns>False if the control does not exist or was already deleted.</returns>
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken);

    /// <returns>False if the control does not exist or was deleted.</returns>
    Task<bool> RecordRunAsync(int id, LastRun run, CancellationToken cancellationToken);
}
