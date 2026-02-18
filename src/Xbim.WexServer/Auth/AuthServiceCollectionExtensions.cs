using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Xbim.WexServer.Abstractions.Auth;

namespace Xbim.WexServer.Auth;

/// <summary>
/// Options for configuring the Xbim authorization service.
/// </summary>
public class XbimAuthorizationOptions
{
    /// <summary>
    /// Gets or sets whether workspace Members have implicit Viewer access to all projects in the workspace.
    /// Default is true.
    /// </summary>
    public bool WorkspaceMemberImplicitProjectAccess { get; set; } = true;
}

/// <summary>
/// Extension methods for configuring authentication in Xbim.WexServer.
/// </summary>
public static class AuthServiceCollectionExtensions
{
    /// <summary>
    /// Adds development authentication mode with a fixed principal.
    /// This is intended for local development without requiring an external identity provider.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional action to configure the dev auth options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddXbimDevAuth(
        this IServiceCollection services,
        Action<DevAuthenticationOptions>? configureOptions = null)
    {
        // Add HttpContextAccessor for IUserContext
        services.AddHttpContextAccessor();

        // Register IUserContext and IWorkspaceContext
        services.AddScoped<IUserContext, HttpContextUserContext>();
        services.AddScoped<IWorkspaceContext, HttpContextWorkspaceContext>();

        // Register IAuthorizationService
        services.AddScoped<IAuthorizationService, AuthorizationService>();

        // Configure authentication
        services.AddAuthentication(DevAuthenticationHandler.SchemeName)
            .AddScheme<DevAuthenticationOptions, DevAuthenticationHandler>(
                DevAuthenticationHandler.SchemeName,
                configureOptions ?? (_ => { }));

        // Add authorization
        services.AddAuthorization();

        return services;
    }

    /// <summary>
    /// Adds OIDC/JWT bearer authentication mode.
    /// Validates tokens via Authority and Audience configuration.
    /// Maps sub/email/name claims and auto-provisions local User via UserProvisioningMiddleware.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration containing Auth:OIDC settings.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddXbimOidcAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Add HttpContextAccessor for IUserContext
        services.AddHttpContextAccessor();

        // Register IUserContext and IWorkspaceContext
        services.AddScoped<IUserContext, HttpContextUserContext>();
        services.AddScoped<IWorkspaceContext, HttpContextWorkspaceContext>();

        // Register IAuthorizationService
        services.AddScoped<IAuthorizationService, AuthorizationService>();

        // Get OIDC configuration
        var oidcSection = configuration.GetSection("Auth:OIDC");
        var authority = oidcSection.GetValue<string>("Authority");
        var audience = oidcSection.GetValue<string>("Audience");
        var requireHttpsMetadata = oidcSection.GetValue<bool?>("RequireHttpsMetadata") ?? true;

        if (string.IsNullOrEmpty(authority))
        {
            throw new InvalidOperationException("Auth:OIDC:Authority must be configured for OIDC authentication mode.");
        }

        // Configure JWT Bearer authentication
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.Authority = authority;
            options.RequireHttpsMetadata = requireHttpsMetadata;

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = !string.IsNullOrEmpty(audience),
                ValidAudience = audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                // Map standard OIDC claims
                NameClaimType = "name",
                RoleClaimType = "roles"
            };

            // Map additional claims from the token
            options.MapInboundClaims = false; // Preserve original claim names (sub, email, name)
        });

        // Add authorization
        services.AddAuthorization();

        return services;
    }

    /// <summary>
    /// Adds the Xbim user context service without configuring authentication.
    /// Use this when authentication is configured separately (e.g., OIDC/JWT).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddXbimUserContext(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IUserContext, HttpContextUserContext>();
        services.AddScoped<IWorkspaceContext, HttpContextWorkspaceContext>();
        services.AddScoped<IAuthorizationService, AuthorizationService>();
        return services;
    }

    /// <summary>
    /// Adds Personal Access Token (PAT) authentication as an additional authentication scheme.
    /// PATs are identified by the "ocpat_" prefix in the Bearer token.
    /// This should be called after the primary authentication scheme is configured.
    /// </summary>
    /// <param name="builder">The authentication builder.</param>
    /// <param name="configureOptions">Optional action to configure PAT auth options.</param>
    /// <returns>The authentication builder for chaining.</returns>
    public static AuthenticationBuilder AddPersonalAccessTokenAuthentication(
        this AuthenticationBuilder builder,
        Action<PersonalAccessTokenAuthenticationOptions>? configureOptions = null)
    {
        return builder.AddScheme<PersonalAccessTokenAuthenticationOptions, PersonalAccessTokenAuthenticationHandler>(
            PersonalAccessTokenAuthenticationHandler.SchemeName,
            configureOptions ?? (_ => { }));
    }

    /// <summary>
    /// Adds development authentication with Personal Access Token support.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureDevOptions">Optional action to configure dev auth options.</param>
    /// <param name="configurePatOptions">Optional action to configure PAT auth options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddXbimDevAuthWithPat(
        this IServiceCollection services,
        Action<DevAuthenticationOptions>? configureDevOptions = null,
        Action<PersonalAccessTokenAuthenticationOptions>? configurePatOptions = null)
    {
        // Add HttpContextAccessor for IUserContext
        services.AddHttpContextAccessor();

        // Register IUserContext and IWorkspaceContext
        services.AddScoped<IUserContext, HttpContextUserContext>();
        services.AddScoped<IWorkspaceContext, HttpContextWorkspaceContext>();

        // Register IAuthorizationService
        services.AddScoped<IAuthorizationService, AuthorizationService>();

        // Configure authentication with a policy scheme that selects between DevAuth and PAT
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = "XbimAuth";
            options.DefaultChallengeScheme = "XbimAuth";
        })
        .AddPolicyScheme("XbimAuth", "Xbim Authentication", options =>
        {
            options.ForwardDefaultSelector = context =>
            {
                // Check if the request has a PAT token
                var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
                if (!string.IsNullOrEmpty(authHeader) &&
                    authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    var token = authHeader["Bearer ".Length..].Trim();
                    if (token.StartsWith(PersonalAccessTokenAuthenticationHandler.TokenPrefix, StringComparison.Ordinal))
                    {
                        return PersonalAccessTokenAuthenticationHandler.SchemeName;
                    }
                }

                // Default to dev auth
                return DevAuthenticationHandler.SchemeName;
            };
        })
        .AddScheme<DevAuthenticationOptions, DevAuthenticationHandler>(
            DevAuthenticationHandler.SchemeName,
            configureDevOptions ?? (_ => { }))
        .AddPersonalAccessTokenAuthentication(configurePatOptions);

        // Add authorization
        services.AddAuthorization();

        return services;
    }

    /// <summary>
    /// Adds OIDC/JWT bearer authentication with Personal Access Token support.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration containing Auth:OIDC settings.</param>
    /// <param name="configurePatOptions">Optional action to configure PAT auth options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddXbimOidcAuthWithPat(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<PersonalAccessTokenAuthenticationOptions>? configurePatOptions = null)
    {
        // Add HttpContextAccessor for IUserContext
        services.AddHttpContextAccessor();

        // Register IUserContext and IWorkspaceContext
        services.AddScoped<IUserContext, HttpContextUserContext>();
        services.AddScoped<IWorkspaceContext, HttpContextWorkspaceContext>();

        // Register IAuthorizationService
        services.AddScoped<IAuthorizationService, AuthorizationService>();

        // Get OIDC configuration
        var oidcSection = configuration.GetSection("Auth:OIDC");
        var authority = oidcSection.GetValue<string>("Authority");
        var audience = oidcSection.GetValue<string>("Audience");
        var requireHttpsMetadata = oidcSection.GetValue<bool?>("RequireHttpsMetadata") ?? true;

        if (string.IsNullOrEmpty(authority))
        {
            throw new InvalidOperationException("Auth:OIDC:Authority must be configured for OIDC authentication mode.");
        }

        // Configure authentication with a policy scheme that selects between JWT and PAT
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = "XbimAuth";
            options.DefaultChallengeScheme = "XbimAuth";
        })
        .AddPolicyScheme("XbimAuth", "Xbim Authentication", options =>
        {
            options.ForwardDefaultSelector = context =>
            {
                // Check if the request has a PAT token
                var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
                if (!string.IsNullOrEmpty(authHeader) &&
                    authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    var token = authHeader["Bearer ".Length..].Trim();
                    if (token.StartsWith(PersonalAccessTokenAuthenticationHandler.TokenPrefix, StringComparison.Ordinal))
                    {
                        return PersonalAccessTokenAuthenticationHandler.SchemeName;
                    }
                }

                // Default to JWT bearer
                return JwtBearerDefaults.AuthenticationScheme;
            };
        })
        .AddJwtBearer(options =>
        {
            options.Authority = authority;
            options.RequireHttpsMetadata = requireHttpsMetadata;

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = !string.IsNullOrEmpty(audience),
                ValidAudience = audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                // Map standard OIDC claims
                NameClaimType = "name",
                RoleClaimType = "roles"
            };

            // Map additional claims from the token
            options.MapInboundClaims = false; // Preserve original claim names (sub, email, name)
        })
        .AddPersonalAccessTokenAuthentication(configurePatOptions);

        // Add authorization
        services.AddAuthorization();

        return services;
    }
}
