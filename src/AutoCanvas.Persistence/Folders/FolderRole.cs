namespace AutoCanvas.Persistence.Folders;

/// <summary>
/// Roles on a folder (ADR-0012), each including the ones before it. The numeric order is the
/// order of power; the database stores the names.
/// </summary>
public enum FolderRole
{
    /// <summary>No access; never stored, only the result of evaluating grants.</summary>
    None = 0,

    /// <summary>See the folder, its automations and (from S2) runs and logs.</summary>
    Viewer = 1,

    /// <summary>Also start and cancel runs, read variable values.</summary>
    Operator = 2,

    /// <summary>Also create, change and move automations and subfolders, set variables and secrets.</summary>
    Editor = 3,

    /// <summary>Also manage grants, delete the folder, mark it restricted.</summary>
    Owner = 4,
}
