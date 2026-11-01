// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace ReviewG33k.Services.DiffLens;

/// <summary>
/// Reads selected tree blobs through one Git batch process.
/// </summary>
/// <remarks>
/// Comparison and whole-repository views avoid starting separate Git processes for every file.
/// Requests use object IDs so spaces and newlines in paths do not affect the batch protocol.
/// </remarks>
internal static class GitBaselineBatchReader
{
    public static async Task<IReadOnlyDictionary<string, byte[]>> ReadAsync(string root, string revision, IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var wanted = paths.ToHashSet(StringComparer.Ordinal);
        var tree = await new GitCommandRunner().RunAsync(root, cancellationToken, "ls-tree", "-r", "-z", revision);
        if (!tree.IsSuccess)
            throw new IOException(tree.StandardError);
        var objects = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in tree.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = entry.IndexOf('\t');
            if (separator < 0 || !wanted.Contains(entry[(separator + 1)..]))
                continue;
            var fields = entry[..separator].Split(' ');
            if (fields.Length == 3 && fields[1] == "blob")
                objects[entry[(separator + 1)..]] = fields[2];
        }
        if (objects.Count == 0)
            return new Dictionary<string, byte[]>();

        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("cat-file");
        startInfo.ArgumentList.Add("--batch");
        using var process = Process.Start(startInfo) ?? throw new IOException("Could not start Git baseline reader.");
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            using var output = new BufferedStream(process.StandardOutput.BaseStream, 65536);
            var blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var singleByte = new byte[1];
            foreach (var objectId in objects.Values.Distinct(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await process.StandardInput.WriteLineAsync(objectId.AsMemory(), cancellationToken);
                await process.StandardInput.FlushAsync();
                using var header = new MemoryStream();
                while (true)
                {
                    await output.ReadExactlyAsync(singleByte, cancellationToken);
                    if (singleByte[0] == '\n')
                        break;
                    header.WriteByte(singleByte[0]);
                    if (header.Length > 1024)
                        throw new IOException("Invalid Git batch response.");
                }
                var fields = Encoding.ASCII.GetString(header.ToArray()).Split(' ');
                if (fields.Length != 3 || fields[0] != objectId || fields[1] != "blob" || !int.TryParse(fields[2], out var length) || length < 0)
                    throw new IOException("Invalid Git baseline blob response.");
                var bytes = new byte[length];
                await output.ReadExactlyAsync(bytes, cancellationToken);
                await output.ReadExactlyAsync(singleByte, cancellationToken);
                if (singleByte[0] != '\n')
                    throw new IOException("Invalid Git baseline blob terminator.");
                blobs[objectId] = bytes;
            }
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellationToken);
            var error = await errors;
            if (process.ExitCode != 0)
                throw new IOException(error);
            return objects.ToDictionary(pair => pair.Key, pair => blobs[pair.Value], StringComparer.Ordinal);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }
}
