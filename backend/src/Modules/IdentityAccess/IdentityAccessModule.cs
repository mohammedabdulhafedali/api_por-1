using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using ApiGovernance.Modules.IdentityAccess.Configuration;
using ApiGovernance.Modules.IdentityAccess.Contracts;
using ApiGovernance.Modules.IdentityAccess.Identity;
using ApiGovernance.Modules.IdentityAccess.Services;
using ApiGovernance.Shared.Contracts;
using ApiGovernance.Shared.Modularity;
using ApiGovernance.Shared.Persistence;
using ApiGovernance.Shared.Persistence.Entities;
using ApiGovernance.Shared.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ApiGovernance.Modules.IdentityAccess;

/// <summary>
/// Module 1: Identity &amp; Access (I01 Local ASP.NET Core Identity &amp; Session Management;
/// I02-I07 user management &amp; ACL deferred to subsequent tasks).
/// </summary>
public sealed class IdentityAccessModule : IBackendModule
{
    public const string AuthStrictRateLimitPolicyName = "AuthStrictRateLimit";

    public ModuleDescriptor Descriptor { get; } = new(
        Code: "IdentityAccess",
        Name: "Identity & Access Management",
        PlannedTasks: "I01, I02, I03, I04, I05, I06, I07");

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(AuthenticationPolicyOptions.SectionName);
        services.Configure<AuthenticationPolicyOptions>(section);
        services.Configure<BootstrapAdminOptions>(configuration.GetSection(BootstrapAdminOptions.SectionName));
        var policy = section.Get<AuthenticationPolicyOptions>() ?? new AuthenticationPolicyOptions();

        // Persist Data Protection keys outside the Git repository
        var keyDirectory = !string.IsNullOrWhiteSpace(policy.DataProtectionKeyDirectory)
            ? policy.DataProtectionKeyDirectory
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ApiGovernance",
                "DataProtectionKeys");
        Directory.CreateDirectory(keyDirectory);

        services.AddDataProtection()
            .SetApplicationName(policy.DataProtectionApplicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));

        // ASP.NET Core Identity Core backed by existing UUID 'users' table (UserEntity)
        services.AddIdentityCore<UserEntity>(options =>
            {
                options.Password.RequiredLength = policy.PasswordMinLength;
                options.Password.RequireDigit = policy.PasswordRequireDigit;
                options.Password.RequireLowercase = policy.PasswordRequireLowercase;
                options.Password.RequireUppercase = policy.PasswordRequireUppercase;
                options.Password.RequireNonAlphanumeric = policy.PasswordRequireNonAlphanumeric;
                options.Password.RequiredUniqueChars = policy.PasswordRequiredUniqueChars;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = policy.MaxFailedAccessAttempts;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(Math.Max(1, policy.LockoutMinutes));

                options.User.RequireUniqueEmail = true;
            })
            .AddUserStore<PlatformUserStore>()
            .AddDefaultTokenProviders();

        services.Configure<DataProtectionTokenProviderOptions>(options =>
        {
            options.TokenLifespan = TimeSpan.FromMinutes(Math.Max(1, policy.ResetTokenLifespanMinutes));
        });

        // Server-side revocable ITicketStore backed by PostgreSQL 'auth_sessions'
        services.AddSingleton<PostgreSqlSessionTicketStore>();
        services.AddSingleton<ISessionRevocationService>(sp => sp.GetRequiredService<PostgreSqlSessionTicketStore>());
        services.AddSingleton<IPostConfigureOptions<IdentityOptions>, ConfigureIdentityPolicyOptions>();
        services.AddSingleton<IPostConfigureOptions<CookieAuthenticationOptions>, ConfigureCookieSessionTicketStore>();
        services.AddSingleton<IPostConfigureOptions<AntiforgeryOptions>, ConfigureAntiforgeryCookiePolicy>();

        // Same-origin Antiforgery (CSRF) token protection
        services.AddAntiforgery(options =>
        {
            options.HeaderName = policy.AntiforgeryHeaderName;
            options.Cookie.Name = policy.AntiforgeryCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        // Cookie Authentication Scheme + strict active user & security stamp verification (never HTML redirect)
        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
                options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignOutScheme = IdentityConstants.ApplicationScheme;
            })
            .AddCookie(IdentityConstants.ApplicationScheme, options =>
            {
                options.Cookie.Name = policy.SessionCookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.IsEssential = true;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(Math.Max(1, policy.SessionIdleTimeoutMinutes));
                options.SlidingExpiration = true;

                options.Events = new CookieAuthenticationEvents
                {
                    OnValidatePrincipal = async context =>
                    {
                        var userIdStr = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                        var claimStamp = context.Principal?.FindFirstValue(PlatformClaimTypes.SecurityStamp);

                        if (!Guid.TryParse(userIdStr, out var userId))
                        {
                            context.RejectPrincipal();
                            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                            return;
                        }

                        var db = context.HttpContext.RequestServices.GetRequiredService<ApiGovernanceDbContext>();
                        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(
                            u => u.Id == userId,
                            context.HttpContext.RequestAborted);

                        var now = DateTimeOffset.UtcNow;
                        if (user is null ||
                            !user.IsActive ||
                            string.IsNullOrEmpty(user.PasswordHash) ||
                            (user.LockoutEnd is not null && user.LockoutEnd > now) ||
                            !string.Equals(user.SecurityStamp, claimStamp, StringComparison.Ordinal))
                        {
                            context.RejectPrincipal();
                            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                        }
                    },
                    OnRedirectToLogin = context => WriteAuthProblemDetailsAsync(
                        context.HttpContext,
                        StatusCodes.Status401Unauthorized),
                    OnRedirectToAccessDenied = context => WriteAuthProblemDetailsAsync(
                        context.HttpContext,
                        StatusCodes.Status403Forbidden)
                };
            });

        services.AddAuthorization();

        // Auth endpoint rate limiting (HTTP 429 Problem Details)
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                await WriteAuthProblemDetailsAsync(
                    context.HttpContext,
                    StatusCodes.Status429TooManyRequests,
                    cancellationToken);
            };

            options.AddPolicy(AuthStrictRateLimitPolicyName, httpContext =>
            {
                var currentPolicy = httpContext.RequestServices
                    .GetRequiredService<IOptions<AuthenticationPolicyOptions>>()
                    .Value;
                var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "local-loopback";
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: remoteIp,
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Max(1, currentPolicy.LoginRateLimitPermitLimit),
                        Window = TimeSpan.FromSeconds(Math.Max(1, currentPolicy.LoginRateLimitWindowSeconds)),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });
        });

        // Swappable auth provider, user management (I02), initial admin bootstrap, and deferred email token channel
        services.AddScoped<IdentityAccessAuthorizationHelper>();
        services.AddScoped<IAuthTokenDeliveryChannel, DeferredEnterpriseEmailDeliveryChannel>();
        services.AddScoped<IIdentityAuthService, LocalIdentityAuthService>();
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<IRoleManagementService, RoleManagementService>();
        services.AddScoped<IPermissionManagementService, PermissionManagementService>();
        services.AddScoped<IInitialAdminBootstrapService, InitialAdminBootstrapService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var authGroup = endpoints.MapGroup("/auth").WithTags("Authentication & Sessions (I01)");

        // API-094: GET /api/v1/auth/csrf (same-origin CSRF token; Cache-Control: no-store)
        authGroup.MapGet("/csrf", (
                HttpContext httpContext,
                IAntiforgery antiforgery,
                IOptions<AuthenticationPolicyOptions> options) =>
            {
                httpContext.Response.Headers.CacheControl = "no-store, no-cache";
                httpContext.Response.Headers.Pragma = "no-cache";
                var tokens = antiforgery.GetAndStoreTokens(httpContext);
                return Results.Ok(new CsrfTokenResponse(
                    Token: tokens.RequestToken ?? string.Empty,
                    HeaderName: options.Value.AntiforgeryHeaderName));
            })
            .AllowAnonymous()
            .WithName("GetCsrfToken")
            .Produces<CsrfTokenResponse>(StatusCodes.Status200OK);

        // API-095: POST /api/v1/auth/login (email + password + CSRF + rate limit + lockout + session ticket)
        authGroup.MapPost("/login", async (
                LoginRequest request,
                HttpContext httpContext,
                IIdentityAuthService authService,
                PostgreSqlSessionTicketStore sessionTicketStore,
                IOptionsMonitor<CookieAuthenticationOptions> cookieOptionsMonitor,
                IOptions<AuthenticationPolicyOptions> options,
                CancellationToken cancellationToken) =>
            {
                var result = await authService.LoginAsync(request, httpContext.TraceIdentifier, cancellationToken);
                var idleMinutes = Math.Max(1, options.Value.SessionIdleTimeoutMinutes);
                var now = DateTimeOffset.UtcNow;
                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = true,
                    AllowRefresh = true,
                    IssuedUtc = now,
                    ExpiresUtc = now.AddMinutes(idleMinutes)
                };

                // Check if caller already holds a session cookie; if so, rotate session ID cleanly (SEC-T-006)
                var cookieOptions = cookieOptionsMonitor.Get(IdentityConstants.ApplicationScheme);
                var existingAuth = await httpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
                if (existingAuth.Succeeded)
                {
                    var rawPriorCookie = cookieOptions.CookieManager.GetRequestCookie(
                        httpContext,
                        options.Value.SessionCookieName);
                    if (!string.IsNullOrWhiteSpace(rawPriorCookie))
                    {
                        var unprotectedPrior = cookieOptions.TicketDataFormat.Unprotect(rawPriorCookie);
                        var oldTicketKey = unprotectedPrior?.Principal.FindFirstValue("Microsoft.AspNetCore.Authentication.Cookies-SessionId");
                        if (!string.IsNullOrWhiteSpace(oldTicketKey))
                        {
                            await sessionTicketStore.RevokeSessionAsync(oldTicketKey, cancellationToken);
                        }
                    }

                    // Mint brand-new server-side ticket & cookie directly so HandleSignInAsync cannot reuse cached _sessionId
                    var fullTicket = new AuthenticationTicket(
                        result.Principal,
                        authProperties,
                        IdentityConstants.ApplicationScheme);
                    var newTicketKey = await sessionTicketStore.StoreAsync(fullTicket);

                    var cookieIdentity = new ClaimsIdentity(
                        [new Claim("Microsoft.AspNetCore.Authentication.Cookies-SessionId", newTicketKey, ClaimValueTypes.String, cookieOptions.ClaimsIssuer)],
                        IdentityConstants.ApplicationScheme);
                    var cookieTicket = new AuthenticationTicket(
                        new ClaimsPrincipal(cookieIdentity),
                        authProperties,
                        IdentityConstants.ApplicationScheme);
                    var protectedCookie = cookieOptions.TicketDataFormat.Protect(cookieTicket);
                    cookieOptions.CookieManager.AppendResponseCookie(
                        httpContext,
                        options.Value.SessionCookieName,
                        protectedCookie,
                        cookieOptions.Cookie.Build(httpContext, now));
                }
                else
                {
                    await httpContext.SignInAsync(
                        IdentityConstants.ApplicationScheme,
                        result.Principal,
                        authProperties);
                }

                return Results.Ok(result.User);
            })
            .AllowAnonymous()
            .RequireRateLimiting(AuthStrictRateLimitPolicyName)
            .WithCsrfValidation()
            .WithRequestValidation<LoginRequest>()
            .WithName("LoginLocalIdentity")
            .Produces<AuthenticatedUserResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        // API-096: POST /api/v1/auth/logout (active session + CSRF -> 204 + server-side ticket revocation)
        authGroup.MapPost("/logout", async (
                HttpContext httpContext,
                IIdentityAuthService authService,
                CancellationToken cancellationToken) =>
            {
                var userIdStr = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
                Guid? userId = Guid.TryParse(userIdStr, out var parsed) ? parsed : null;

                await httpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                await authService.LogoutAsync(userId, ticketKey: null, cancellationToken);
                return Results.NoContent();
            })
            .RequireActiveSession()
            .WithCsrfValidation()
            .WithName("LogoutSession")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // API-001: GET /api/v1/me (active authenticated session -> identity & effective permissions summary)
        endpoints.MapGet("/me", async (
                HttpContext httpContext,
                IIdentityAuthService authService,
                CancellationToken cancellationToken) =>
            {
                var identity = await authService.GetCurrentIdentityAsync(
                    httpContext.User,
                    httpContext.TraceIdentifier,
                    cancellationToken);
                return Results.Ok(identity);
            })
            .WithTags("Authentication & Sessions (I01)")
            .RequireActiveSession()
            .WithName("GetCurrentIdentity")
            .Produces<AuthenticatedUserResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        // API-097: POST /api/v1/auth/password/change (session + CSRF + current password -> revokes sessions -> 204)
        authGroup.MapPost("/password/change", async (
                ChangePasswordRequest request,
                HttpContext httpContext,
                IIdentityAuthService authService,
                CancellationToken cancellationToken) =>
            {
                await authService.ChangePasswordAsync(httpContext.User, request, cancellationToken);
                await httpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                return Results.NoContent();
            })
            .RequireActiveSession()
            .WithCsrfValidation()
            .WithRequestValidation<ChangePasswordRequest>()
            .WithName("ChangeOwnPassword")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // API-098: POST /api/v1/users/{userId}/password/reset (requires users.password.reset permission + CSRF -> 204)
        endpoints.MapPost("/users/{userId:guid}/password/reset", async (
                Guid userId,
                HttpContext httpContext,
                IIdentityAuthService authService,
                CancellationToken cancellationToken) =>
            {
                await authService.InitiatePasswordResetAsync(httpContext.User, userId, cancellationToken);
                return Results.NoContent();
            })
            .WithTags("Authentication & Sessions (I01)")
            .WithCsrfValidation()
            .RequireGeneralPermission(UserPermissionCodes.UsersPasswordReset, "PASSWORD_RESET_DENIED", "USER")
            .WithName("InitiateUserPasswordReset")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // API-099: POST /api/v1/auth/password/reset/complete (email + single-use token + newPassword + CSRF + rate limit -> 204)
        authGroup.MapPost("/password/reset/complete", async (
                CompletePasswordResetRequest request,
                IIdentityAuthService authService,
                CancellationToken cancellationToken) =>
            {
                await authService.CompletePasswordResetAsync(request, cancellationToken);
                return Results.NoContent();
            })
            .AllowAnonymous()
            .RequireRateLimiting(AuthStrictRateLimitPolicyName)
            .WithCsrfValidation()
            .WithRequestValidation<CompletePasswordResetRequest>()
            .WithName("CompletePasswordResetOrActivation")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        // ====================================================================
        // Task I02 • User Management & Activation Lifecycle Endpoints (API-002..API-004)
        // ====================================================================
        var usersGroup = endpoints.MapGroup("/users")
            .WithTags("Users Management (I02)")
            .RequireAuthorization();

        // API-002: GET /api/v1/users (requires users.view)
        usersGroup.MapGet("/", async (
                [FromQuery] int? page,
                [FromQuery] int? pageSize,
                [FromQuery] string? search,
                [FromQuery] bool? isActive,
                HttpContext httpContext,
                IUserManagementService userManagementService,
                CancellationToken cancellationToken) =>
            {
                var result = await userManagementService.GetUsersAsync(
                    httpContext.User,
                    page ?? 1,
                    pageSize ?? 20,
                    search,
                    isActive,
                    httpContext.TraceIdentifier,
                    cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission(UserPermissionCodes.UsersView, "USERS_LIST_DENIED", "USER")
            .WithName("ListUsers")
            .Produces<PagedUsersResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // API-003: POST /api/v1/users (requires users.create + CSRF)
        usersGroup.MapPost("/", async (
                CreateUserRequest request,
                HttpContext httpContext,
                IUserManagementService userManagementService,
                CancellationToken cancellationToken) =>
            {
                var created = await userManagementService.CreateUserAsync(
                    httpContext.User,
                    request,
                    httpContext.TraceIdentifier,
                    cancellationToken);
                return Results.Created($"/api/v1/users/{created.Id}", created);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission(UserPermissionCodes.UsersCreate, "USER_CREATE_DENIED", "USER")
            .WithRequestValidation<CreateUserRequest>()
            .WithName("CreateUser")
            .Produces<UserSummaryDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // API-004: PATCH /api/v1/users/{userId} (requires users.edit + CSRF)
        usersGroup.MapPatch("/{userId:guid}", async (
                Guid userId,
                UpdateUserRequest request,
                HttpContext httpContext,
                IUserManagementService userManagementService,
                CancellationToken cancellationToken) =>
            {
                var updated = await userManagementService.UpdateUserAsync(
                    httpContext.User,
                    userId,
                    request,
                    httpContext.TraceIdentifier,
                    cancellationToken);
                return Results.Ok(updated);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission(UserPermissionCodes.UsersEdit, "USER_UPDATE_DENIED", "USER")
            .WithRequestValidation<UpdateUserRequest>()
            .WithName("UpdateUser")
            .Produces<UserSummaryDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        usersGroup.MapDelete("/{userId:guid}", async (
                Guid userId,
                HttpContext httpContext,
                IUserManagementService userManagementService,
                CancellationToken cancellationToken) =>
            {
                await userManagementService.DeleteUserAsync(httpContext.User, userId, cancellationToken);
                return Results.NoContent();
            })
            .WithCsrfValidation()
            .RequireGeneralPermission(UserPermissionCodes.UsersDelete, "USER_DELETE_DENIED", "USER")
            .WithName("DeleteUser")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // ====================================================================
        // Task I03 • Role Management & Atomic Multi-Role Assignment (API-005, API-008..010, API-013)
        // ====================================================================
        // API-005: PUT /api/v1/users/{userId}/roles (requires users.assign_roles + CSRF)
        usersGroup.MapPut("/{userId:guid}/roles", async (
                Guid userId,
                ReplaceUserRolesRequest request,
                HttpContext httpContext,
                IRoleManagementService roleManagementService,
                CancellationToken cancellationToken) =>
            {
                var updated = await roleManagementService.ReplaceUserRolesAsync(
                    httpContext.User,
                    userId,
                    request,
                    httpContext.TraceIdentifier,
                    cancellationToken);
                return Results.Ok(updated);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission(UserPermissionCodes.UsersAssignRoles, "USER_ROLES_REPLACE_DENIED", "USER")
            .WithRequestValidation<ReplaceUserRolesRequest>()
            .WithName("ReplaceUserRoles")
            .Produces<ReplaceUserRolesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        var rolesGroup = endpoints.MapGroup("/roles")
            .WithTags("Roles Management (I03)")
            .RequireAuthorization();

        // API-008: GET /api/v1/roles (requires roles.view)
        rolesGroup.MapGet("/", async (
                [FromQuery] int? page,
                [FromQuery] int? pageSize,
                [FromQuery] string? search,
                [FromQuery] bool? isActive,
                HttpContext httpContext,
                IRoleManagementService roleManagementService,
                CancellationToken cancellationToken) =>
            {
                var result = await roleManagementService.GetRolesAsync(
                    httpContext.User,
                    page ?? 1,
                    pageSize ?? 20,
                    search,
                    isActive,
                    httpContext.TraceIdentifier,
                    cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission(UserPermissionCodes.RolesView, "ROLES_LIST_DENIED", "ROLE")
            .WithName("ListRoles")
            .Produces<PagedRolesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // API-009: POST /api/v1/roles (requires roles.create + CSRF)
        rolesGroup.MapPost("/", async (
                CreateRoleRequest request,
                HttpContext httpContext,
                IRoleManagementService roleManagementService,
                CancellationToken cancellationToken) =>
            {
                var created = await roleManagementService.CreateRoleAsync(
                    httpContext.User,
                    request,
                    httpContext.TraceIdentifier,
                    cancellationToken);
                return Results.Created($"/api/v1/roles/{created.Id}", created);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission(UserPermissionCodes.RolesCreate, "ROLE_CREATE_DENIED", "ROLE")
            .WithRequestValidation<CreateRoleRequest>()
            .WithName("CreateRole")
            .Produces<RoleDetailsDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // API-010: PATCH /api/v1/roles/{roleId} (requires roles.edit + CSRF)
        rolesGroup.MapPatch("/{roleId:guid}", async (
                Guid roleId,
                UpdateRoleRequest request,
                HttpContext httpContext,
                IRoleManagementService roleManagementService,
                CancellationToken cancellationToken) =>
            {
                var updated = await roleManagementService.UpdateRoleAsync(
                    httpContext.User,
                    roleId,
                    request,
                    httpContext.TraceIdentifier,
                    cancellationToken);
                return Results.Ok(updated);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission(UserPermissionCodes.RolesEdit, "ROLE_EDIT_DENIED", "ROLE")
            .WithRequestValidation<UpdateRoleRequest>()
            .WithName("UpdateRole")
            .Produces<RoleDetailsDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // API-013: DELETE /api/v1/roles/{roleId} (requires roles.delete + CSRF; protects pinned or in-use roles)
        rolesGroup.MapDelete("/{roleId:guid}", async (
                Guid roleId,
                HttpContext httpContext,
                IRoleManagementService roleManagementService,
                CancellationToken cancellationToken) =>
            {
                await roleManagementService.DeleteRoleAsync(httpContext.User, roleId, cancellationToken);
                return Results.NoContent();
            })
            .WithCsrfValidation()
            .RequireGeneralPermission(UserPermissionCodes.RolesDelete, "ROLE_DELETE_DENIED", "ROLE")
            .WithName("DeleteRole")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // ====================================================================
        // Task I04 • Role Permission Matrix & User Permission Overrides (API-006, API-007, API-011, API-012)
        // ====================================================================
        var permissionsGroup = endpoints.MapGroup("/permissions")
            .WithTags("Permissions Matrix & Overrides (I04)")
            .RequireAuthorization();

        // API-012: GET /api/v1/permissions (read-only permanent permission dictionary + matrix metadata; permissions.view)
        permissionsGroup.MapGet("/", async (
                HttpContext httpContext,
                IPermissionManagementService permissionService,
                CancellationToken cancellationToken) =>
            {
                var catalog = await permissionService.GetPermissionsCatalogAsync(
                    httpContext.User,
                    httpContext.TraceIdentifier,
                    cancellationToken);
                return Results.Ok(catalog);
            })
            .RequireGeneralPermission(UserPermissionCodes.PermissionsView, "PERMISSIONS_CATALOG_DENIED", "PERMISSION")
            .WithName("GetPermissionsCatalog")
            .Produces<PermissionsCatalogResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // GET /api/v1/roles/{roleId}/permissions (requires permissions.view)
        rolesGroup.MapGet("/{roleId:guid}/permissions", async (
                Guid roleId,
                HttpContext httpContext,
                IPermissionManagementService permissionService,
                CancellationToken cancellationToken) =>
            {
                var result = await permissionService.GetRolePermissionsAsync(
                    httpContext.User,
                    roleId,
                    httpContext.TraceIdentifier,
                    cancellationToken);
                return Results.Ok(result);
            })
            .RequireGeneralPermission(UserPermissionCodes.PermissionsView, "ROLE_PERMISSIONS_VIEW_DENIED", "ROLE")
            .WithName("GetRolePermissions")
            .Produces<RolePermissionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // API-011: PUT /api/v1/roles/{roleId}/permissions (requires permissions.manage + CSRF + anti-escalation)
        rolesGroup.MapPut("/{roleId:guid}/permissions", async (
                Guid roleId,
                ReplaceRolePermissionsRequest request,
                HttpContext httpContext,
                IPermissionManagementService permissionService,
                CancellationToken cancellationToken) =>
            {
                var updated = await permissionService.ReplaceRolePermissionsAsync(
                    httpContext.User,
                    roleId,
                    request,
                    httpContext.TraceIdentifier,
                    cancellationToken);
                return Results.Ok(updated);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission(UserPermissionCodes.PermissionsManage, "ROLE_PERMISSIONS_REPLACE_DENIED", "ROLE")
            .WithRequestValidation<ReplaceRolePermissionsRequest>()
            .WithName("ReplaceRolePermissions")
            .Produces<RolePermissionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // API-006: GET /api/v1/users/{userId}/permissions (inherited + overrides + effective; permissions.view)
        usersGroup.MapGet("/{userId:guid}/permissions", async (
                Guid userId,
                HttpContext httpContext,
                IPermissionManagementService permissionService,
                CancellationToken cancellationToken) =>
            {
                var matrix = await permissionService.GetUserPermissionsMatrixAsync(
                    httpContext.User,
                    userId,
                    httpContext.TraceIdentifier,
                    cancellationToken);
                return Results.Ok(matrix);
            })
            .RequireGeneralPermission(UserPermissionCodes.PermissionsView, "USER_PERMISSIONS_VIEW_DENIED", "USER")
            .WithName("GetUserPermissionsMatrix")
            .Produces<UserPermissionsMatrixResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // API-007: PUT /api/v1/users/{userId}/permission-overrides (GRANT/REVOKE/Inherit; permissions.manage + CSRF + anti-escalation)
        usersGroup.MapPut("/{userId:guid}/permission-overrides", async (
                Guid userId,
                ReplaceUserPermissionOverridesRequest request,
                HttpContext httpContext,
                IPermissionManagementService permissionService,
                CancellationToken cancellationToken) =>
            {
                var updated = await permissionService.ReplaceUserPermissionOverridesAsync(
                    httpContext.User,
                    userId,
                    request,
                    httpContext.TraceIdentifier,
                    cancellationToken);
                return Results.Ok(updated);
            })
            .WithCsrfValidation()
            .RequireGeneralPermission(UserPermissionCodes.PermissionsManage, "USER_OVERRIDES_UPDATE_DENIED", "USER")
            .WithRequestValidation<ReplaceUserPermissionOverridesRequest>()
            .WithName("ReplaceUserPermissionOverrides")
            .Produces<UserPermissionsMatrixResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task WriteAuthProblemDetailsAsync(
        HttpContext httpContext,
        int statusCode,
        CancellationToken cancellationToken = default)
    {
        if (httpContext.Response.HasStarted)
        {
            return;
        }

        var isArabic = ApiErrorLocalizer.PrefersArabic(httpContext);
        var errorCode = ApiErrorCodes.FromStatusCode(statusCode);
        var (title, message) = ApiErrorLocalizer.GetLocalizedDefaults(statusCode, isArabic, errorCode);

        var problem = GlobalProblemDetailsExceptionHandler.CreateProblemDetails(
            httpContext,
            statusCode,
            errorCode,
            title,
            message,
            details: null,
            traceId: httpContext.TraceIdentifier);

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/problem+json; charset=utf-8";
        await httpContext.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json; charset=utf-8",
            cancellationToken: cancellationToken);
    }

    private sealed class ConfigureIdentityPolicyOptions(
        IOptions<AuthenticationPolicyOptions> policyOptions) : IPostConfigureOptions<IdentityOptions>
    {
        public void PostConfigure(string? name, IdentityOptions options)
        {
            var policy = policyOptions.Value;
            options.Password.RequiredLength = policy.PasswordMinLength;
            options.Password.RequireDigit = policy.PasswordRequireDigit;
            options.Password.RequireLowercase = policy.PasswordRequireLowercase;
            options.Password.RequireUppercase = policy.PasswordRequireUppercase;
            options.Password.RequireNonAlphanumeric = policy.PasswordRequireNonAlphanumeric;
            options.Password.RequiredUniqueChars = policy.PasswordRequiredUniqueChars;

            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = policy.MaxFailedAccessAttempts;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(Math.Max(1, policy.LockoutMinutes));
        }
    }

    private sealed class ConfigureCookieSessionTicketStore(
        PostgreSqlSessionTicketStore ticketStore,
        IWebHostEnvironment environment,
        IOptions<AuthenticationPolicyOptions> policyOptions) : IPostConfigureOptions<CookieAuthenticationOptions>
    {
        public void PostConfigure(string? name, CookieAuthenticationOptions options)
        {
            if (!string.Equals(name, IdentityConstants.ApplicationScheme, StringComparison.Ordinal))
            {
                return;
            }

            options.SessionStore = ticketStore;
            options.Cookie.Name = policyOptions.Value.SessionCookieName;
            options.Cookie.SecurePolicy = environment.IsProduction()
                ? CookieSecurePolicy.Always
                : CookieSecurePolicy.SameAsRequest;
        }
    }

    private sealed class ConfigureAntiforgeryCookiePolicy(
        IWebHostEnvironment environment,
        IOptions<AuthenticationPolicyOptions> policyOptions) : IPostConfigureOptions<AntiforgeryOptions>
    {
        public void PostConfigure(string? name, AntiforgeryOptions options)
        {
            options.HeaderName = policyOptions.Value.AntiforgeryHeaderName;
            options.Cookie.Name = policyOptions.Value.AntiforgeryCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = environment.IsProduction()
                ? CookieSecurePolicy.Always
                : CookieSecurePolicy.SameAsRequest;
        }
    }
}
