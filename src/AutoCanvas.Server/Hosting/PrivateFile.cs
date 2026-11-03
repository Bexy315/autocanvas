namespace AutoCanvas.Server.Hosting;

/// <summary>Files only the server's user (and, if asked, its group) may read, such as keys and tokens.</summary>
internal static class PrivateFile
{
    /// <summary>Opens a new file for writing; on Unix it is created with mode 600 (640 with <paramref name="groupRead"/>).</summary>
    public static FileStream Create(string path, FileMode mode, bool groupRead = false)
    {
        var options = new FileStreamOptions { Mode = mode, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | (groupRead ? UnixFileMode.GroupRead : UnixFileMode.None);
        }

        return new FileStream(path, options);
    }
}
