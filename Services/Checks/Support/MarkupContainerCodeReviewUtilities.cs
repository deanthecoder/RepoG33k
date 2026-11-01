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
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace ReviewG33k.Services.Checks.Support;

internal static class MarkupContainerCodeReviewUtilities
{
    private const string XamlNamespace2006 = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly HashSet<string> MultiChildContainerNames =
    [
        "Grid",
        "StackPanel"
    ];
    private static readonly HashSet<string> KnownEventAttributeNames = new(StringComparer.Ordinal)
    {
        "Loaded",
        "Unloaded",
        "Initialized",
        "AttachedToVisualTree",
        "DetachedFromVisualTree",
        "Click",
        "Tapped",
        "DoubleTapped",
        "PointerPressed",
        "PointerReleased",
        "PointerMoved",
        "PointerEntered",
        "PointerExited",
        "KeyDown",
        "KeyUp",
        "TextChanged",
        "SelectionChanged",
        "ValueChanged",
        "Checked",
        "Unchecked"
    };

    public static bool TryParseDocument(string markupText, out XDocument document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(markupText))
            return false;

        try
        {
            document = XDocument.Parse(markupText, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsTargetMultiChildContainer(XElement element)
    {
        if (element == null)
            return false;

        return MultiChildContainerNames.Contains(element.Name.LocalName);
    }

    public static IReadOnlyList<XElement> GetVisualChildren(XElement element)
    {
        if (element == null)
            return [];

        return element
            .Elements()
            .Where(child => !IsPropertyElement(child))
            .ToArray();
    }

    public static bool IsPropertyElement(XElement element)
    {
        if (element == null)
            return false;

        return element.Name.LocalName.Contains('.', StringComparison.Ordinal);
    }

    public static bool IsElementLineRelevant(CodeReviewChangedFile file, XElement element)
    {
        if (file == null || element == null)
            return false;
        if (file.IsAdded)
            return true;

        var lineNumber = GetLineNumber(element);
        return lineNumber > 0 && file.AddedLineNumbers.Contains(lineNumber);
    }

    public static bool IsElementOrAnyAttributeLineRelevant(CodeReviewChangedFile file, XElement element, params string[] attributeLocalNames)
    {
        if (file == null || element == null)
            return false;
        if (file.IsAdded)
            return true;
        if (IsElementLineRelevant(file, element))
            return true;
        if (attributeLocalNames == null || attributeLocalNames.Length == 0)
            return false;

        foreach (var attributeName in attributeLocalNames)
        {
            var attribute = element.Attributes()
                .FirstOrDefault(candidate => string.Equals(candidate.Name.LocalName, attributeName, StringComparison.Ordinal));
            if (attribute == null)
                continue;

            var lineNumber = GetLineNumber(attribute);
            if (lineNumber > 0 && file.AddedLineNumbers.Contains(lineNumber))
                return true;
        }

        return false;
    }

    public static int GetLineNumber(XElement element)
    {
        if (element is not IXmlLineInfo lineInfo || !lineInfo.HasLineInfo())
            return 1;

        return Math.Max(1, lineInfo.LineNumber);
    }

    public static int GetLineNumber(XAttribute attribute)
    {
        if (attribute is not IXmlLineInfo lineInfo || !lineInfo.HasLineInfo())
            return 1;

        return Math.Max(1, lineInfo.LineNumber);
    }

    public static bool IsNestedSamePanelWithoutEffect(XElement outerContainer, XElement childElement)
    {
        if (!IsTargetMultiChildContainer(outerContainer) || !IsTargetMultiChildContainer(childElement))
            return false;
        if (!string.Equals(outerContainer.Name.LocalName, childElement.Name.LocalName, StringComparison.Ordinal))
            return false;
        if (!HasNoEffectWrapperState(outerContainer))
            return false;

        if (string.Equals(outerContainer.Name.LocalName, "StackPanel", StringComparison.Ordinal))
        {
            var outerOrientation = GetStackPanelOrientation(outerContainer);
            var innerOrientation = GetStackPanelOrientation(childElement);
            return string.Equals(outerOrientation, innerOrientation, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    public static bool HasCodeBehindHookAttributes(XElement element)
    {
        if (element == null)
            return false;

        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
                continue;
            if (IsNameAttribute(attribute))
                return true;
            if (IsEventAttribute(attribute))
                return true;
        }

        return false;
    }

    private static bool IsNameAttribute(XAttribute attribute)
    {
        if (attribute == null || !string.Equals(attribute.Name.LocalName, "Name", StringComparison.Ordinal))
            return false;

        // x:Name and Name both imply potential code-behind references.
        return string.IsNullOrEmpty(attribute.Name.NamespaceName) ||
               string.Equals(attribute.Name.NamespaceName, XamlNamespace2006, StringComparison.Ordinal);
    }

    private static bool IsEventAttribute(XAttribute attribute)
    {
        if (attribute == null)
            return false;
        if (!string.IsNullOrEmpty(attribute.Name.NamespaceName))
            return false;

        var localName = attribute.Name.LocalName;
        if (KnownEventAttributeNames.Contains(localName))
            return true;

        return localName.EndsWith("Changed", StringComparison.Ordinal);
    }

    private static bool HasNoEffectWrapperState(XElement container)
    {
        if (container == null)
            return false;

        if (container.Elements().Any(IsPropertyElement))
            return false;

        foreach (var attribute in container.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
                continue;

            if (string.Equals(container.Name.LocalName, "StackPanel", StringComparison.Ordinal) &&
                string.Equals(attribute.Name.LocalName, "Orientation", StringComparison.Ordinal))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static string GetStackPanelOrientation(XElement stackPanel)
    {
        var orientationAttribute = stackPanel
            .Attributes()
            .FirstOrDefault(attribute =>
                !attribute.IsNamespaceDeclaration &&
                string.Equals(attribute.Name.LocalName, "Orientation", StringComparison.Ordinal));

        var orientationValue = orientationAttribute?.Value?.Trim();
        return string.IsNullOrWhiteSpace(orientationValue)
            ? "Vertical"
            : orientationValue;
    }
}
