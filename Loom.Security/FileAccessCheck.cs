namespace Loom.Security;

/// <summary>File.Exists returns false both when a file does not exist and when this
/// process cannot find out - most dangerously for a file inside a directory it cannot
/// traverse, which File.Exists silently reports as absent rather than "unknown". A
/// decision that treats those two as the same thing can silently write to, or report on,
/// the wrong file.</summary>
public enum FileAccessState
{
    Exists,
    Missing,
    Indeterminate,
}

public static class FileAccessCheck
{
    /// <summary>Decides by attempting to open the file for reading and disposing it
    /// immediately - the only way to distinguish "not there" from "can't tell" without
    /// P/Invoke. FileNotFoundException/DirectoryNotFoundException (the latter covers a
    /// path segment that is itself missing) mean Missing. UnauthorizedAccessException -
    /// including the case where the file may exist but a parent directory cannot be
    /// traversed - means Indeterminate: the caller genuinely does not know, and must not
    /// report or treat this as Missing. Any other IOException (a sharing violation, for
    /// instance) proves the file is present and merely busy right now, so it counts as
    /// Exists rather than Missing or Indeterminate - being unable to open it this instant
    /// says nothing about whether it is there.</summary>
    public static FileAccessState Check(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return FileAccessState.Exists;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return FileAccessState.Missing;
        }
        catch (UnauthorizedAccessException)
        {
            return FileAccessState.Indeterminate;
        }
        catch (IOException)
        {
            return FileAccessState.Exists;
        }
    }
}
