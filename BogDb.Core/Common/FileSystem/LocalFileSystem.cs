using System;
using System.Collections.Generic;
using System.IO;

namespace BogDb.Core.Common.FileSystem;

public sealed class LocalFileSystem : VirtualFileSystem
{
    public override FileInfo OpenFile(string path, FileFlags flags)
    {
        FileMode mode = FileMode.Open;
        FileAccess access = FileAccess.Read;

        if (flags.HasFlag(FileFlags.CreateIfMissing)) mode = FileMode.OpenOrCreate;
        if (flags.HasFlag(FileFlags.Create)) mode = FileMode.CreateNew;
        if (flags.HasFlag(FileFlags.Truncate)) mode = FileMode.Truncate;

        if (flags.HasFlag(FileFlags.ReadWrite)) access = FileAccess.ReadWrite;
        else if (flags.HasFlag(FileFlags.Write)) access = FileAccess.Write;

        var stream = new FileStream(path, mode, access, FileShare.ReadWrite);
        return new LocalFileInfo(path, stream);
    }

    public override void CreateDirectory(string path)
    {
        Directory.CreateDirectory(path);
    }

    public override void RemoveFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public override bool FileExists(string path)
    {
        return File.Exists(path);
    }

    public override IReadOnlyList<string> GetPathsInDirectory(string path)
    {
        return Directory.GetFileSystemEntries(path);
    }

    public override string JoinPath(string baseDir, string name)
    {
        return Path.Combine(baseDir, name);
    }
}
