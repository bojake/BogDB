using System;
using System.Collections.Generic;

namespace BogDb.Core.Common.FileSystem;

[Flags]
public enum FileFlags
{
    Read = 1,
    Write = 2,
    ReadWrite = Read | Write,
    Create = 4,
    CreateIfMissing = 8,
    Truncate = 16
}

/// <summary>
/// Interface for a Virtual File System, mapping database engine IO paths
/// to pluggable storage infrastructure (local disk, memory, or cloud object stores).
/// </summary>
public abstract class VirtualFileSystem
{
    public abstract FileInfo OpenFile(string path, FileFlags flags);
    public abstract void CreateDirectory(string path);
    public abstract void RemoveFileIfExists(string path);
    public abstract bool FileExists(string path);
    public abstract IReadOnlyList<string> GetPathsInDirectory(string path);
    public abstract string JoinPath(string baseDir, string name);
}
