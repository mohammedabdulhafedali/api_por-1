using ApiGovernance.Modules.VersionsFiles.Contracts;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.CustomProperties;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.VariantTypes;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ApiGovernance.Modules.VersionsFiles.OpenXml;

public sealed record WordGovernanceMetadataContext(
    string DocumentTitle,
    string IntegrationCode,
    string ApiGroupName,
    string ProviderName,
    string ConsumerName,
    string VersionLabel,
    int RevisionNumber,
    string RevisionStatus,
    string SourceSha256,
    DateTimeOffset GeneratedAtUtc)
{
    public IReadOnlyDictionary<string, string> ToFieldMap() => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["APIGOV_DOCUMENT_TITLE"] = DocumentTitle,
        ["APIGOV_INTEGRATION_CODE"] = IntegrationCode,
        ["APIGOV_API_GROUP"] = ApiGroupName,
        ["APIGOV_PROVIDER_NAME"] = ProviderName,
        ["APIGOV_CONSUMER_NAME"] = ConsumerName,
        ["APIGOV_VERSION_LABEL"] = VersionLabel,
        ["APIGOV_REVISION_NUMBER"] = $"#{RevisionNumber}",
        ["APIGOV_REVISION_STATUS"] = RevisionStatus,
        ["APIGOV_SOURCE_SHA256"] = SourceSha256,
        ["APIGOV_GENERATED_AT_UTC"] = GeneratedAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ")
    };
}

public sealed record WordDerivativeGenerationOutcome(
    bool PackageValid,
    string TemplateKindDetected,
    IReadOnlyList<WordFieldUpdateEntryDto> MatchedFields,
    IReadOnlyList<string> ValidationErrors,
    string? ErrorMessage);

public interface IWordGovernanceDerivativeService
{
    Task<WordDerivativeGenerationOutcome> TryGenerateDerivedCopyAsync(
        Stream verifiedSourceDocxStream,
        string destinationStagingPath,
        WordGovernanceMetadataContext metadata,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Task V08: Open XML SDK PoC for updating recognized Word governance fields in a derived .docx copy
/// while preserving the original file object intact and blocking unverified derivatives (OP-03).
/// Supports three distinct template patterns without claiming universal support on arbitrary Word files:
/// 1) Structured Document Tags (w:sdt Content Controls via Tag/Alias)
/// 2) Explicit placeholder tokens ({{KEY}} / [[KEY]]) across paragraphs and split Word runs
/// 3) Custom Document Properties (docProps/custom.xml)
/// </summary>
public sealed class OpenXmlWordGovernanceDerivativeService : IWordGovernanceDerivativeService
{
    public async Task<WordDerivativeGenerationOutcome> TryGenerateDerivedCopyAsync(
        Stream verifiedSourceDocxStream,
        string destinationStagingPath,
        WordGovernanceMetadataContext metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verifiedSourceDocxStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationStagingPath);
        ArgumentNullException.ThrowIfNull(metadata);

        var fieldMap = metadata.ToFieldMap();
        var matched = new List<WordFieldUpdateEntryDto>();
        var strategiesUsed = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            await using (var destStream = new FileStream(
                destinationStagingPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                useAsync: true))
            {
                await verifiedSourceDocxStream.CopyToAsync(destStream, cancellationToken);
                await destStream.FlushAsync(cancellationToken);
            }

            using (var wordDoc = WordprocessingDocument.Open(destinationStagingPath, isEditable: true))
            {
                var mainPart = wordDoc.MainDocumentPart;
                if (mainPart?.Document?.Body is null)
                {
                    return new WordDerivativeGenerationOutcome(
                        PackageValid: false,
                        TemplateKindDetected: "NONE",
                        MatchedFields: [],
                        ValidationErrors: ["MainDocumentPart or Document Body is missing from the Word package."],
                        ErrorMessage: "Missing main document body in OpenXML package.");
                }

                // Strategy 1: Content Controls (w:sdt) in main body, headers, and footers
                UpdateSdtControlsInRoot(mainPart.Document, "MainDocument", fieldMap, matched, strategiesUsed);
                foreach (var hp in mainPart.HeaderParts)
                {
                    if (hp.Header is not null)
                    {
                        UpdateSdtControlsInRoot(hp.Header, "Header", fieldMap, matched, strategiesUsed);
                    }
                }
                foreach (var fp in mainPart.FooterParts)
                {
                    if (fp.Footer is not null)
                    {
                        UpdateSdtControlsInRoot(fp.Footer, "Footer", fieldMap, matched, strategiesUsed);
                    }
                }

                // Strategy 2: Explicit Placeholder Tokens ({{APIGOV_*}} or [[APIGOV_*]]) in paragraphs (handling split runs)
                UpdatePlaceholdersInRoot(mainPart.Document, "MainDocument", fieldMap, matched, strategiesUsed);
                foreach (var hp in mainPart.HeaderParts)
                {
                    if (hp.Header is not null)
                    {
                        UpdatePlaceholdersInRoot(hp.Header, "Header", fieldMap, matched, strategiesUsed);
                    }
                }
                foreach (var fp in mainPart.FooterParts)
                {
                    if (fp.Footer is not null)
                    {
                        UpdatePlaceholdersInRoot(fp.Footer, "Footer", fieldMap, matched, strategiesUsed);
                    }
                }

                // Strategy 3: Custom Document Properties (docProps/custom.xml)
                UpdateCustomProperties(wordDoc.CustomFilePropertiesPart, fieldMap, matched, strategiesUsed);

                mainPart.Document.Save();
                foreach (var hp in mainPart.HeaderParts) hp.Header?.Save();
                foreach (var fp in mainPart.FooterParts) fp.Footer?.Save();
                wordDoc.CustomFilePropertiesPart?.Properties?.Save();
            }

            // Re-open read-only and validate strictly with OpenXmlValidator before trusting the derived file (OP-03)
            var validationErrors = new List<string>();
            using (var verifyDoc = WordprocessingDocument.Open(destinationStagingPath, isEditable: false))
            {
                var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
                foreach (var err in validator.Validate(verifyDoc).Take(25))
                {
                    validationErrors.Add($"{err.ErrorType}: {err.Description} (Part: {err.Part?.Uri})");
                }
            }

            if (validationErrors.Count > 0)
            {
                return new WordDerivativeGenerationOutcome(
                    PackageValid: false,
                    TemplateKindDetected: ResolveTemplateKind(strategiesUsed),
                    MatchedFields: matched,
                    ValidationErrors: validationErrors,
                    ErrorMessage: $"OpenXML validation reported {validationErrors.Count} structural error(s) on the derived Word copy.");
            }

            return new WordDerivativeGenerationOutcome(
                PackageValid: true,
                TemplateKindDetected: ResolveTemplateKind(strategiesUsed),
                MatchedFields: matched,
                ValidationErrors: [],
                ErrorMessage: null);
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or System.Xml.XmlException or IOException)
        {
            return new WordDerivativeGenerationOutcome(
                PackageValid: false,
                TemplateKindDetected: "NONE",
                MatchedFields: [],
                ValidationErrors: [ex.Message],
                ErrorMessage: $"Word OpenXML processing failed: {ex.Message}");
        }
    }

    private static string ResolveTemplateKind(HashSet<string> strategiesUsed) => strategiesUsed.Count switch
    {
        0 => "NONE",
        1 => strategiesUsed.First(),
        _ => "MIXED"
    };

    private static void UpdateSdtControlsInRoot(
        OpenXmlElement root,
        string location,
        IReadOnlyDictionary<string, string> fieldMap,
        List<WordFieldUpdateEntryDto> matched,
        HashSet<string> strategiesUsed)
    {
        foreach (var sdt in root.Descendants<SdtElement>())
        {
            var sdtProps = sdt.SdtProperties;
            if (sdtProps is null)
            {
                continue;
            }

            var tagVal = sdtProps.GetFirstChild<Tag>()?.Val?.Value?.Trim();
            var aliasVal = sdtProps.GetFirstChild<SdtAlias>()?.Val?.Value?.Trim();
            var matchedKey = ResolveFieldKey(tagVal, fieldMap) ?? ResolveFieldKey(aliasVal, fieldMap);
            if (matchedKey is null)
            {
                continue;
            }

            var newValue = fieldMap[matchedKey];
            var textElements = sdt.Descendants<Text>().ToList();
            if (textElements.Count > 0)
            {
                textElements[0].Text = newValue;
                textElements[0].Space = SpaceProcessingModeValues.Preserve;
                for (var i = 1; i < textElements.Count; i++)
                {
                    textElements[i].Text = string.Empty;
                }
            }
            else
            {
                var contentBlock = sdt.GetFirstChild<SdtContentBlock>();
                var contentRun = sdt.GetFirstChild<SdtContentRun>();
                var contentCell = sdt.GetFirstChild<SdtContentCell>();

                if (contentRun is not null)
                {
                    contentRun.AppendChild(new Run(new Text(newValue) { Space = SpaceProcessingModeValues.Preserve }));
                }
                else if (contentBlock is not null)
                {
                    contentBlock.AppendChild(new Paragraph(new Run(new Text(newValue) { Space = SpaceProcessingModeValues.Preserve })));
                }
                else if (contentCell is not null)
                {
                    var tc = contentCell.GetFirstChild<TableCell>() ?? contentCell.AppendChild(new TableCell());
                    tc.AppendChild(new Paragraph(new Run(new Text(newValue) { Space = SpaceProcessingModeValues.Preserve })));
                }
                else
                {
                    continue;
                }
            }

            strategiesUsed.Add("SDT_CONTROLS");
            matched.Add(new WordFieldUpdateEntryDto(matchedKey, "SDT_CONTENT_CONTROL", newValue, location));
        }
    }

    private static void UpdatePlaceholdersInRoot(
        OpenXmlElement root,
        string location,
        IReadOnlyDictionary<string, string> fieldMap,
        List<WordFieldUpdateEntryDto> matched,
        HashSet<string> strategiesUsed)
    {
        foreach (var paragraph in root.Descendants<Paragraph>())
        {
            var textNodes = paragraph.Descendants<Text>().ToList();
            if (textNodes.Count == 0)
            {
                continue;
            }

            var combined = string.Concat(textNodes.Select(t => t.Text));
            if (string.IsNullOrEmpty(combined) ||
                (!combined.Contains("{{", StringComparison.Ordinal) && !combined.Contains("[[", StringComparison.Ordinal)))
            {
                continue;
            }

            var updated = combined;
            var changedKeys = new List<string>();
            foreach (var (key, value) in fieldMap)
            {
                var curlyToken = "{{" + key + "}}";
                var bracketToken = "[[" + key + "]]";

                if (updated.Contains(curlyToken, StringComparison.OrdinalIgnoreCase))
                {
                    updated = updated.Replace(curlyToken, value, StringComparison.OrdinalIgnoreCase);
                    changedKeys.Add(key);
                }

                if (updated.Contains(bracketToken, StringComparison.OrdinalIgnoreCase))
                {
                    updated = updated.Replace(bracketToken, value, StringComparison.OrdinalIgnoreCase);
                    if (!changedKeys.Contains(key))
                    {
                        changedKeys.Add(key);
                    }
                }
            }

            if (changedKeys.Count == 0)
            {
                continue;
            }

            // Consolidate paragraph text safely into the first text node while preserving its RunProperties
            textNodes[0].Text = updated;
            textNodes[0].Space = SpaceProcessingModeValues.Preserve;
            for (var i = 1; i < textNodes.Count; i++)
            {
                textNodes[i].Text = string.Empty;
            }

            strategiesUsed.Add("PLACEHOLDER_TOKENS");
            foreach (var key in changedKeys)
            {
                matched.Add(new WordFieldUpdateEntryDto(key, "PLACEHOLDER_TOKEN", fieldMap[key], location));
            }
        }
    }

    private static void UpdateCustomProperties(
        CustomFilePropertiesPart? customPart,
        IReadOnlyDictionary<string, string> fieldMap,
        List<WordFieldUpdateEntryDto> matched,
        HashSet<string> strategiesUsed)
    {
        if (customPart?.Properties is null)
        {
            return;
        }

        foreach (var prop in customPart.Properties.Elements<CustomDocumentProperty>())
        {
            var propName = prop.Name?.Value?.Trim();
            var matchedKey = ResolveFieldKey(propName, fieldMap);
            if (matchedKey is null)
            {
                continue;
            }

            var newValue = fieldMap[matchedKey];
            prop.RemoveAllChildren();
            prop.AppendChild(new VTLPWSTR(newValue));

            strategiesUsed.Add("CUSTOM_PROPERTIES");
            matched.Add(new WordFieldUpdateEntryDto(matchedKey, "CUSTOM_PROPERTY", newValue, "docProps/custom.xml"));
        }
    }

    private static string? ResolveFieldKey(string? rawMarker, IReadOnlyDictionary<string, string> fieldMap)
    {
        if (string.IsNullOrWhiteSpace(rawMarker))
        {
            return null;
        }

        var normalized = rawMarker.Trim();
        if (normalized.StartsWith("{{", StringComparison.Ordinal) && normalized.EndsWith("}}", StringComparison.Ordinal) && normalized.Length > 4)
        {
            normalized = normalized[2..^2].Trim();
        }
        else if (normalized.StartsWith("[[", StringComparison.Ordinal) && normalized.EndsWith("]]", StringComparison.Ordinal) && normalized.Length > 4)
        {
            normalized = normalized[2..^2].Trim();
        }

        return fieldMap.Keys.FirstOrDefault(k => string.Equals(k, normalized, StringComparison.OrdinalIgnoreCase));
    }
}
