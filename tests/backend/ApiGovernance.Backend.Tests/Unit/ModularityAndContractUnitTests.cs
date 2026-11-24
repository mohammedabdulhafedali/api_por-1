using System.ComponentModel.DataAnnotations;
using ApiGovernance.Modules.AuditNotifications;
using ApiGovernance.Modules.Changes;
using ApiGovernance.Modules.Configuration;
using ApiGovernance.Modules.Documents;
using ApiGovernance.Modules.IdentityAccess;
using ApiGovernance.Modules.Integrations;
using ApiGovernance.Modules.MasterData;
using ApiGovernance.Modules.PublishingPortal;
using ApiGovernance.Modules.VersionsFiles;
using ApiGovernance.Modules.Workflow;
using ApiGovernance.Shared.Contracts;
using ApiGovernance.Shared.Modularity;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ApiGovernance.Backend.Tests.Unit;

public sealed class ModularityAndContractUnitTests
{
    [Fact]
    public void ApprovedModularMonolith_HasAllTenBoundedModulesInOrder()
    {
        IBackendModule[] modules =
        [
            new IdentityAccessModule(),
            new MasterDataModule(),
            new ConfigurationModule(),
            new DocumentsModule(),
            new IntegrationsModule(),
            new VersionsFilesModule(),
            new ChangesModule(),
            new WorkflowModule(),
            new PublishingPortalModule(),
            new AuditNotificationsModule()
        ];

        Assert.Equal(10, modules.Length);
        Assert.Equal(
            [
                "IdentityAccess",
                "MasterData",
                "Configuration",
                "Documents",
                "Integrations",
                "VersionsFiles",
                "Changes",
                "Workflow",
                "PublishingPortal",
                "AuditNotifications"
            ],
            modules.Select(m => m.Descriptor.Code).ToArray());

        Assert.All(modules, module =>
        {
            Assert.False(string.IsNullOrWhiteSpace(module.Descriptor.Name));
            Assert.False(string.IsNullOrWhiteSpace(module.Descriptor.PlannedTasks));
        });
    }

    [Theory]
    [InlineData(StatusCodes.Status400BadRequest, ApiErrorCodes.BadRequest)]
    [InlineData(StatusCodes.Status401Unauthorized, ApiErrorCodes.Unauthorized)]
    [InlineData(StatusCodes.Status403Forbidden, ApiErrorCodes.Forbidden)]
    [InlineData(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound)]
    [InlineData(StatusCodes.Status405MethodNotAllowed, ApiErrorCodes.MethodNotAllowed)]
    [InlineData(StatusCodes.Status409Conflict, ApiErrorCodes.Conflict)]
    [InlineData(StatusCodes.Status413PayloadTooLarge, ApiErrorCodes.PayloadTooLarge)]
    [InlineData(StatusCodes.Status422UnprocessableEntity, ApiErrorCodes.UnprocessableEntity)]
    [InlineData(StatusCodes.Status429TooManyRequests, ApiErrorCodes.TooManyRequests)]
    [InlineData(StatusCodes.Status500InternalServerError, ApiErrorCodes.InternalError)]
    public void ApiErrorCodes_MapsHttpStatusCodeDeterministically(int statusCode, string expectedErrorCode)
    {
        var actual = ApiErrorCodes.FromStatusCode(statusCode);
        Assert.Equal(expectedErrorCode, actual);
    }

    [Fact]
    public void ApiErrorLocalizer_DefaultsToArabic_AndSwitchesToEnglishOnHeader()
    {
        var defaultCtx = new DefaultHttpContext();
        Assert.True(ApiErrorLocalizer.PrefersArabic(defaultCtx));

        var arCtx = new DefaultHttpContext();
        arCtx.Request.Headers.AcceptLanguage = "ar-YE,ar;q=0.9,en;q=0.8";
        Assert.True(ApiErrorLocalizer.PrefersArabic(arCtx));

        var enCtx = new DefaultHttpContext();
        enCtx.Request.Headers.AcceptLanguage = "en-US,en;q=0.9";
        Assert.False(ApiErrorLocalizer.PrefersArabic(enCtx));

        var (arTitle, arMsg) = ApiErrorLocalizer.GetLocalizedDefaults(StatusCodes.Status403Forbidden, isArabic: true);
        var (enTitle, enMsg) = ApiErrorLocalizer.GetLocalizedDefaults(StatusCodes.Status403Forbidden, isArabic: false);

        Assert.Equal("إجراء غير مصرح به", arTitle);
        Assert.Contains("الصلاحية", arMsg);
        Assert.Equal("Forbidden", enTitle);
        Assert.Contains("permissions", enMsg);
    }

    [Fact]
    public void PagedQueryRequest_ValidatesBoundsAndSortDirection()
    {
        var valid = new PagedQueryRequest { Page = 1, PageSize = 25, SortDirection = "desc" };
        var validResults = new List<ValidationResult>();
        Assert.True(Validator.TryValidateObject(valid, new ValidationContext(valid), validResults, validateAllProperties: true));
        Assert.Empty(validResults);

        var invalid = new PagedQueryRequest { Page = 0, PageSize = 250, SortDirection = "invalid_dir" };
        var invalidResults = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(invalid, new ValidationContext(invalid), invalidResults, validateAllProperties: true));
        Assert.NotEmpty(invalidResults);
    }

    [Fact]
    public void ContractValidationProbeRequest_RejectsReservedCodeAndInvalidFields()
    {
        var probe = new ContractValidationProbeRequest
        {
            Code = "RESERVED",
            DisplayTitle = "Valid Title",
            PageSize = 20
        };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(probe, new ValidationContext(probe), results, validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(ContractValidationProbeRequest.Code)));
    }
}

