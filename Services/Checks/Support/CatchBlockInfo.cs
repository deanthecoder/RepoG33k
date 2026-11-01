// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace ReviewG33k.Services.Checks.Support;

internal sealed class CatchBlockInfo
{
    public CatchBlockInfo(
        int startLine,
        string bodyWithoutComments,
        bool hasComments,
        string exceptionVariableName,
        string exceptionTypeName)
    {
        StartLine = startLine;
        BodyWithoutComments = bodyWithoutComments ?? string.Empty;
        HasComments = hasComments;
        ExceptionVariableName = exceptionVariableName;
        ExceptionTypeName = exceptionTypeName;
    }

    public int StartLine { get; }

    public string BodyWithoutComments { get; }

    public bool HasComments { get; }

    public string ExceptionVariableName { get; }

    public string ExceptionTypeName { get; }
}
