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
using System.Text.RegularExpressions;
using System.Text;

namespace ReviewG33k.Services.Checks.Support;

internal static class TextFileChangeUtilities
{
    public static bool TryGetComparableText(CodeReviewChangedFile file, out string baselineText, out string currentText)
    {
        baselineText = null;
        currentText = null;

        if (file == null || file.IsAdded || !file.HasComparableBaseline)
            return false;

        baselineText = file.BaselineText ?? GitChangedFileContent.DecodeText(file.BaselineBytes);
        currentText = file.Text ?? GitChangedFileContent.DecodeText(file.CurrentBytes);

        return baselineText != null && currentText != null;
    }

    public static TextEncodingKind DetectEncoding(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
            return TextEncodingKind.None;

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return TextEncodingKind.Utf8Bom;

        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
            return TextEncodingKind.Utf32LittleEndianBom;

        if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
            return TextEncodingKind.Utf32BigEndianBom;

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return TextEncodingKind.Utf16LittleEndianBom;

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return TextEncodingKind.Utf16BigEndianBom;

        try
        {
            _ = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
            return TextEncodingKind.Utf8;
        }
        catch (DecoderFallbackException)
        {
            return TextEncodingKind.Unknown;
        }
    }

    public static string GetEncodingDisplayName(TextEncodingKind kind) =>
        kind switch
        {
            TextEncodingKind.Utf8 => "UTF-8",
            TextEncodingKind.Utf8Bom => "UTF-8 with BOM",
            TextEncodingKind.Utf16LittleEndianBom => "UTF-16 LE with BOM",
            TextEncodingKind.Utf16BigEndianBom => "UTF-16 BE with BOM",
            TextEncodingKind.Utf32LittleEndianBom => "UTF-32 LE with BOM",
            TextEncodingKind.Utf32BigEndianBom => "UTF-32 BE with BOM",
            TextEncodingKind.None => "empty",
            _ => "unknown"
        };

    public static NewlineKind DetectNewlineKind(string text)
    {
        if (string.IsNullOrEmpty(text))
            return NewlineKind.None;

        var crlfCount = 0;
        var lfCount = 0;
        var crCount = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (current == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    crlfCount++;
                    index++;
                }
                else
                {
                    crCount++;
                }

                continue;
            }

            if (current == '\n')
                lfCount++;
        }

        var kindCount = (crlfCount > 0 ? 1 : 0) + (lfCount > 0 ? 1 : 0) + (crCount > 0 ? 1 : 0);
        if (kindCount == 0)
            return NewlineKind.None;
        if (kindCount > 1)
            return NewlineKind.Mixed;
        if (crlfCount > 0)
            return NewlineKind.CarriageReturnLineFeed;
        if (lfCount > 0)
            return NewlineKind.LineFeed;

        return NewlineKind.CarriageReturn;
    }

    public static string GetNewlineDisplayName(NewlineKind kind) =>
        kind switch
        {
            NewlineKind.CarriageReturnLineFeed => "CRLF",
            NewlineKind.LineFeed => "LF",
            NewlineKind.CarriageReturn => "CR",
            NewlineKind.Mixed => "mixed newline style",
            _ => "none"
        };

    public static bool IsOnlyTrailingWhitespaceChange(string baselineText, string currentText)
    {
        if (string.IsNullOrEmpty(baselineText) && string.IsNullOrEmpty(currentText))
            return false;
        if (string.Equals(baselineText, currentText, StringComparison.Ordinal))
            return false;

        return string.Equals(
            RemoveTrailingLineWhitespace(baselineText),
            RemoveTrailingLineWhitespace(currentText),
            StringComparison.Ordinal);
    }

    public static bool IsOnlyNewlineStyleChange(string baselineText, string currentText)
    {
        if (string.IsNullOrEmpty(baselineText) && string.IsNullOrEmpty(currentText))
            return false;
        if (string.Equals(baselineText, currentText, StringComparison.Ordinal))
            return false;

        return string.Equals(
            NormalizeLineEndings(baselineText, "\n"),
            NormalizeLineEndings(currentText, "\n"),
            StringComparison.Ordinal);
    }

    public static string DetectPreferredNewline(string text)
    {
        var kind = DetectNewlineKind(text);
        return kind switch
        {
            NewlineKind.CarriageReturnLineFeed => "\r\n",
            NewlineKind.LineFeed => "\n",
            NewlineKind.CarriageReturn => "\r",
            _ => null
        };
    }

    public static string NormalizeLineEndings(string text, string newline)
    {
        if (text == null)
            return null;
        if (string.IsNullOrEmpty(newline))
            return text;

        return text
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace("\n", newline);
    }

    public static string RemoveTrailingWhitespace(string text)
    {
        if (text == null)
            return null;

        return TrailingWhitespaceRegex.Replace(text, string.Empty);
    }

    private static string RemoveTrailingLineWhitespace(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var builder = new StringBuilder(text.Length);
        var pendingWhitespace = new StringBuilder();
        foreach (var ch in text)
        {
            if (ch is ' ' or '\t')
            {
                pendingWhitespace.Append(ch);
                continue;
            }

            if (ch is '\r' or '\n')
            {
                pendingWhitespace.Clear();
                builder.Append(ch);
                continue;
            }

            if (pendingWhitespace.Length > 0)
            {
                builder.Append(pendingWhitespace);
                pendingWhitespace.Clear();
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static readonly Regex TrailingWhitespaceRegex = new(@"[ \t]+(?=\r?\n|$)", RegexOptions.Compiled);
}

internal enum TextEncodingKind
{
    None,
    Utf8,
    Utf8Bom,
    Utf16LittleEndianBom,
    Utf16BigEndianBom,
    Utf32LittleEndianBom,
    Utf32BigEndianBom,
    Unknown
}

internal enum NewlineKind
{
    None,
    LineFeed,
    CarriageReturnLineFeed,
    CarriageReturn,
    Mixed
}
