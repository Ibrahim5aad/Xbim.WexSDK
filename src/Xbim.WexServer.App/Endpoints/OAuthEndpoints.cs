using System.Text.Json;
using System.Web;
using Microsoft.EntityFrameworkCore;
using Xbim.WexServer.Abstractions.Auth;
using Xbim.WexServer.App.Auth;
using Xbim.WexServer.Contracts;
using Xbim.WexServer.Domain.Entities;
using Xbim.WexServer.Persistence.EfCore;

using DomainOAuthClientType = Xbim.WexServer.Domain.Enums.OAuthClientType;
using DomainAuditEventType = Xbim.WexServer.Domain.Enums.OAuthAppAuditEventType;

namespace Xbim.WexServer.App.Endpoints;

public static class OAuthEndpoints
{
    public static IEndpointRouteBuilder MapOAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // Authorization endpoint (browser-based)
        app.MapGet("/oauth/authorize", Authorize)
            .WithName("OAuthAuthorize")
            .WithTags("OAuth")
            .Produces(StatusCodes.Status302Found)
            .Produces<OAuthErrorResponse>(StatusCodes.Status400BadRequest)
            .WithOpenApi(operation =>
            {
                operation.Summary = "OAuth 2.0 Authorization Endpoint";
                operation.Description = "Initiates the OAuth 2.0 authorization code flow with PKCE support.";
                return operation;
            })
            .RequireAuthorization();

        // Token endpoint (API)
        app.MapPost("/oauth/token", Token)
            .WithName("OAuthToken")
            .WithTags("OAuth")
            .Accepts<AuthorizationCodeTokenRequest>("application/x-www-form-urlencoded")
            .Produces<OAuthTokenResponse>()
            .Produces<OAuthErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<OAuthErrorResponse>(StatusCodes.Status401Unauthorized)
            .WithOpenApi(operation =>
            {
                operation.Summary = "OAuth 2.0 Token Endpoint";
                operation.Description = "Exchanges an authorization code or refresh token for access tokens. Supports grant_type=authorization_code and grant_type=refresh_token.";
                return operation;
            })
            .AllowAnonymous();

        // Token revocation endpoint (RFC 7009)
        app.MapPost("/oauth/revoke", Revoke)
            .WithName("OAuthRevoke")
            .WithTags("OAuth")
            .Produces(StatusCodes.Status200OK)
            .Produces<OAuthErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<OAuthErrorResponse>(StatusCodes.Status401Unauthorized)
            .WithOpenApi(operation =>
            {
                operation.Summary = "OAuth 2.0 Token Revocation Endpoint";
                operation.Description = "Revokes an access token or refresh token (RFC 7009). The response is always 200 OK regardless of whether the token was actually revoked.";
                return operation;
            })
            .AllowAnonymous();

        return app;
    }

    /// <summary>
    /// OAuth 2.0 Authorization Endpoint (RFC 6749 Section 4.1.1)
    /// </summary>
    private static async Task<IResult> Authorize(
        HttpContext httpContext,
        IUserContext userContext,
        IOAuthTokenService tokenService,
        XbimDbContext dbContext,
        string response_type,
        string client_id,
        string redirect_uri,
        string? scope = null,
        string? state = null,
        string? code_challenge = null,
        string? code_challenge_method = null,
        CancellationToken cancellationToken = default)
    {
        // Validate user is authenticated
        if (!userContext.IsAuthenticated || !userContext.UserId.HasValue || string.IsNullOrEmpty(userContext.Subject))
        {
            return Results.Unauthorized();
        }

        // Validate response_type (must be "code" for authorization code flow)
        if (response_type != "code")
        {
            // Invalid response_type - return error via redirect if possible
            if (!string.IsNullOrEmpty(redirect_uri) && Uri.TryCreate(redirect_uri, UriKind.Absolute, out _))
            {
                return RedirectWithError(redirect_uri, OAuthErrorCodes.UnsupportedResponseType,
                    "Only 'code' response type is supported.", state);
            }
            return Results.BadRequest(new OAuthErrorResponse
            {
                Error = OAuthErrorCodes.UnsupportedResponseType,
                ErrorDescription = "Only 'code' response type is supported."
            });
        }

        // Validate client_id
        if (string.IsNullOrEmpty(client_id))
        {
            return Results.BadRequest(new OAuthErrorResponse
            {
                Error = OAuthErrorCodes.InvalidRequest,
                ErrorDescription = "client_id is required."
            });
        }

        // Look up the OAuth app
        var app = await dbContext.OAuthApps
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ClientId == client_id, cancellationToken);

        if (app == null)
        {
            return Results.BadRequest(new OAuthErrorResponse
            {
                Error = OAuthErrorCodes.InvalidRequest,
                ErrorDescription = "Unknown client_id."
            });
        }

        if (!app.IsEnabled)
        {
            return Results.BadRequest(new OAuthErrorResponse
            {
                Error = OAuthErrorCodes.UnauthorizedClient,
                ErrorDescription = "The client application is disabled."
            });
        }

        // Validate redirect_uri - CRITICAL: must validate BEFORE redirecting
        var registeredUris = JsonSerializer.Deserialize<List<string>>(app.RedirectUris) ?? new List<string>();
        if (string.IsNullOrEmpty(redirect_uri) || !registeredUris.Contains(redirect_uri))
        {
            // DO NOT redirect - invalid redirect_uri is a security issue
            return Results.BadRequest(new OAuthErrorResponse
            {
                Error = OAuthErrorCodes.InvalidRequest,
                ErrorDescription = "redirect_uri is not registered for this client."
            });
        }

        // Validate PKCE for public clients (required) and confidential clients (recommended)
        if (app.ClientType == DomainOAuthClientType.Public)
        {
            if (string.IsNullOrEmpty(code_challenge))
            {
                return RedirectWithError(redirect_uri, OAuthErrorCodes.InvalidRequest,
                    "code_challenge is required for public clients.", state);
            }
            if (string.IsNullOrEmpty(code_challenge_method) || code_challenge_method != "S256")
            {
                return RedirectWithError(redirect_uri, OAuthErrorCodes.InvalidRequest,
                    "code_challenge_method must be 'S256' for public clients.", state);
            }
        }

        // Validate code_challenge_method if provided
        if (!string.IsNullOrEmpty(code_challenge_method) && code_challenge_method != "S256" && code_challenge_method != "plain")
        {
            return RedirectWithError(redirect_uri, OAuthErrorCodes.InvalidRequest,
                "code_challenge_method must be 'S256' or 'plain'.", state);
        }

        // Validate and filter scopes
        var requestedScopes = (scope ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var allowedScopes = app.AllowedScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        // Filter to only allowed scopes
        var grantedScopes = requestedScopes.Where(s => allowedScopes.Contains(s)).ToList();

        // If no valid scopes requested, grant all allowed scopes (default behavior)
        if (!grantedScopes.Any() && allowedScopes.Any())
        {
            grantedScopes = allowedScopes.ToList();
        }

        // Check if any requested scope is not allowed
        var invalidScopes = requestedScopes.Except(allowedScopes).ToList();
        if (invalidScopes.Any())
        {
            return RedirectWithError(redirect_uri, OAuthErrorCodes.InvalidScope,
                $"Invalid scope(s): {string.Join(", ", invalidScopes)}", state);
        }

        // Generate authorization code
        var code = tokenService.GenerateAuthorizationCode();
        var codeHash = tokenService.HashCode(code);

        // Store authorization code
        var authCode = new AuthorizationCode
        {
            Id = Guid.NewGuid(),
            CodeHash = codeHash,
            OAuthAppId = app.Id,
            UserId = userContext.UserId.Value,
            WorkspaceId = app.WorkspaceId,
            Scopes = string.Join(" ", grantedScopes),
            RedirectUri = redirect_uri,
            CodeChallenge = code_challenge,
            CodeChallengeMethod = code_challenge_method ?? (string.IsNullOrEmpty(code_challenge) ? null : "plain"),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = tokenService.GetAuthorizationCodeExpiration(),
            IsUsed = false
        };

        dbContext.AuthorizationCodes.Add(authCode);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Redirect with authorization code
        var redirectUrl = BuildRedirectUrl(redirect_uri, code, state);
        return Results.Redirect(redirectUrl);
    }

    /// <summary>
    /// OAuth 2.0 Token Endpoint (RFC 6749 Section 4.1.3)
    /// </summary>
    private static async Task<IResult> Token(
        HttpRequest request,
        IOAuthTokenService tokenService,
        XbimDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        // Parse form data
        var form = await request.ReadFormAsync(cancellationToken);

        var grantType = form["grant_type"].FirstOrDefault();
        var clientId = form["client_id"].FirstOrDefault();
        var clientSecret = form["client_secret"].FirstOrDefault();

        // Validate client_id is always required
        if (string.IsNullOrEmpty(clientId))
        {
            return Results.BadRequest(new OAuthErrorResponse
            {
                Error = OAuthErrorCodes.InvalidRequest,
                ErrorDescription = "client_id is required."
            });
        }

        // Look up the OAuth app
        var app = await dbContext.OAuthApps
            .FirstOrDefaultAsync(a => a.ClientId == clientId, cancellationToken);

        if (app == null)
        {
            return Results.Json(new OAuthErrorResponse
            {
                Error = OAuthErrorCodes.InvalidClient,
                ErrorDescription = "Unknown client_id."
            }, statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!app.IsEnabled)
        {
            return Results.Json(new OAuthErrorResponse
            {
                Error = OAuthErrorCodes.InvalidClient,
                ErrorDescription = "The client application is disabled."
            }, statusCode: StatusCodes.Status401Unauthorized);
        }

        // Authenticate confidential clients
        if (app.ClientType == DomainOAuthClientType.Confidential)
        {
            if (string.IsNullOrEmpty(clientSecret))
            {
                return Results.Json(new OAuthErrorResponse
                {
                    Error = OAuthErrorCodes.InvalidClient,
                    ErrorDescription = "client_secret is required for confidential clients."
                }, statusCode: StatusCodes.Status401Unauthorized);
            }

            if (!tokenService.ValidateClientSecret(clientSecret, app.ClientSecretHash!))
            {
                return Results.Json(new OAuthErrorResponse
                {
                    Error = OAuthErrorCodes.InvalidClient,
                    ErrorDescription = "Invalid client credentials."
                }, statusCode: StatusCodes.Status401Unauthorized);
            }
        }

        // Route to appropriate grant handler
        return grantType switch
        {
            "authorization_code" => await HandleAuthorizationCodeGrant(request, form, app, tokenService, dbContext, cancellationToken),
            "refresh_token" => await HandleRefreshTokenGrant(request, form, app, tokenService, dbContext, cancellationToken),
            _ => Results.BadRequest(new OAuthErrorResponse
            {
                Error = OAuthErrorCodes.UnsupportedGrantType,
                ErrorDescription = "Supported grant types: authorization_code, refresh_token."
            })
        };
    }

    /// <summary>
    /// Handles authorization_code grant type.
    /// </summary>
    private static async Task<IResult> HandleAuthorizationCodeGrant(
        HttpRequest request,
        IFormCollection form,
        OAuthApp app,
        IOAuthTokenService tokenService,
        XbimDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var code = form["code"].FirstOrDefault();
        var redirectUri = form["redirect_uri"].FirstOrDefault();
        var codeVerifier = form["code_verifier"].FirstOrDefault();

        // Validate required parameters
        if (string.IsNullOrEmpty(code))
        {
            return Results.BadRequest(new OAuthErrorResponse
            {
                Error = OAuthErrorCodes.InvalidRequest,
                ErrorDescription = "code is required."
            });
        }

        if (string.IsNullOrEmpty(redirectUri))
        {
            return Results.BadRequest(new OAuthErrorResponse
            {
                Error = OAuthErrorCodes.InvalidRequest,
                ErrorDescription = "redirect_uri is required."
            });
        }

        // Look up the authorization code
        var codeHash = tokenService.HashCode(code);
        var authCode = await dbContext.AuthorizationCodes
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.CodeHash == codeHash && c.OAuthAppId == app.Id, cancellationToken);

        if (authCode == null)
        {
            return Results.BadRequest(new OAuthErrorResponse
            {
                Error = OAuthErrorCodes.InvalidGrant,
                ErrorDescription = "Invalid authorization code."
            });
        }

        // Validate code hasn't been used
        if (authCode.IsUsed)
        {
            return Results.BadRequest(new OAuthErrorResponse
            {
                Error = OAuthErrorCodes.InvalidGrant,
                ErrorDescription = "Authorization code has already been used."
            });
        }

        // Validate code hasn't expired
        if (authCode.ExpiresAt < DateTimeOffset.UtcNow)
        {
            return Results.BadRequest(new OAuthErrorResponse
            {
                Error = OAuthErrorCodes.InvalidGrant,
                ErrorDescription = "Authorization code has expired."
            });
        }

        // Validate redirect_uri matches
        if (authCode.RedirectUri != redirectUri)
        {
            return Results.BadRequest(new OAuthErrorResponse
            {
                Error = OAuthErrorCodes.InvalidGrant,
                ErrorDescription = "redirect_uri does not match the authorization request."
            });
        }

        // Validate PKCE if code challenge was provided
        if (!string.IsNullOrEmpty(authCode.CodeChallenge))
        {
            if (string.IsNullOrEmpty(codeVerifier))
            {
                return Results.BadRequest(new OAuthErrorResponse
                {
                    Error = OAuthErrorCodes.InvalidGrant,
                    ErrorDescription = "code_verifier is required."
                });
            }

            if (!tokenService.VerifyPkceChallenge(codeVerifier, authCode.CodeChallenge, authCode.CodeChallengeMethod ?? "plain"))
            {
                return Results.BadRequest(new OAuthErrorResponse
                {
                    Error = OAuthErrorCodes.InvalidGrant,
                    ErrorDescription = "Invalid code_verifier."
                });
            }
        }
        else if (app.ClientType == DomainOAuthClientType.Public)
        {
            // Public clients must always use PKCE
            return Results.BadRequest(new OAuthErrorResponse
            {
                Error = OAuthErrorCodes.InvalidGrant,
                ErrorDescription = "PKCE is required for public clients."
            });
        }

        // Mark code as used
        authCode.IsUsed = true;
        authCode.UsedAt = DateTimeOffset.UtcNow;

        // Get user subject
        var userSubject = authCode.User?.Subject ?? authCode.UserId.ToString();

        // Generate access token
        var scopes = authCode.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var accessToken = tokenService.GenerateAccessToken(
            subject: userSubject,
            userId: authCode.UserId,
            workspaceId: authCode.WorkspaceId,
            clientId: app.ClientId,
            scopes: scopes);

        var response = new OAuthTokenResponse
        {
            AccessToken = accessToken,
            TokenType = "Bearer",
            ExpiresIn = tokenService.AccessTokenLifetimeSeconds,
            Scope = authCode.Scopes
        };

        // Issue refresh token if enabled
        if (tokenService.RefreshTokensEnabled)
        {
            var refreshTokenValue = tokenService.GenerateRefreshToken();
            var tokenFamilyId = Guid.NewGuid();

            var refreshToken = new RefreshToken
            {
                Id = Guid.NewGuid(),
                TokenHash = tokenService.HashRefreshToken(refreshTokenValue),
                OAuthAppId = app.Id,
                UserId = authCode.UserId,
                WorkspaceId = authCode.WorkspaceId,
                Scopes = authCode.Scopes,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = tokenService.GetRefreshTokenExpiration(),
                IsRevoked = false,
                TokenFamilyId = tokenFamilyId,
                IpAddress = GetClientIpAddress(request.HttpContext),
                UserAgent = request.Headers.UserAgent.FirstOrDefault()
            };

            dbContext.RefreshTokens.Add(refreshToken);

            // Audit log for token issuance
            var auditLog = new OAuthAppAuditLog
            {
                Id = Guid.NewGuid(),
                OAuthAppId = app.Id,
                EventType = DomainAuditEventType.RefreshTokenIssued,
                ActorUserId = authCode.UserId,
                Timestamp = DateTimeOffset.UtcNow,
                Details = JsonSerializer.Serialize(new
                {
                    TokenId = refreshToken.Id,
                    TokenFamilyId = tokenFamilyId,
                    Scopes = authCode.Scopes,
                    ExpiresAt = refreshToken.ExpiresAt
                }),
                IpAddress = GetClientIpAddress(request.HttpContext)
            };

            dbContext.OAuthAppAuditLogs.Add(auditLog);

            response = response with { RefreshToken = refreshTokenValue };
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(response);
    }

    /// <summary>
    /// Handles refresh_token grant type with token rotation and reuse detection.
    /// </summary>
    private static async Task<IResult> HandleRefreshTokenGrant(
        HttpRequest request,
        IFormCollection form,
        OAuthApp app,
        IOAuthTokenService tokenService,
        XbimDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!tokenService.RefreshTokensEnabled)
        {
            return Results.BadRequest(new OAuthErrorResponse
            {
                Error = OAuthErrorCodes.UnsupportedGrantType,
                ErrorDescription = "Refresh tokens are not enabled."
            });
        }

        var refreshTokenValue = form["refresh_token"].FirstOrDefault();

        if (string.IsNullOrEmpty(refreshTokenValue))
        {
            return Results.BadRequest(new OAuthErrorResponse
            {
                Error = OAuthErrorCodes.InvalidRequest,
                ErrorDescription = "refresh_token is required."
            });
        }

        // Look up the refresh token
        var tokenHash = tokenService.HashRefreshToken(refreshTokenValue);
        var existingToken = await dbContext.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.OAuthAppId == app.Id, cancellationToken);

        if (existingToken == null)
        {
            return Results.BadRequest(new OAuthErrorResponse
            {
                Error = OAuthErrorCodes.InvalidGrant,
                ErrorDescription = "Invalid refresh token."
            });
        }

        // Check if token has been revoked - this could indicate a reuse attack
        if (existingToken.IsRevoked)
        {
            // Token reuse detected! Revoke the entire token family for security
            await RevokeTokenFamily(dbContext, existingToken.TokenFamilyId, "token_reuse_detected", cancellationToken);

            // Log security event
            var securityAuditLog = new OAuthAppAuditLog
            {
                Id = Guid.NewGuid(),
                OAuthAppId = app.Id,
                EventType = DomainAuditEventType.TokenReuseDetected,
                ActorUserId = existingToken.UserId,
                Timestamp = DateTimeOffset.UtcNow,
                Details = JsonSerializer.Serialize(new
                {
                    TokenId = existingToken.Id,
                    TokenFamilyId = existingToken.TokenFamilyId,
                    OriginalRevocationReason = existingToken.RevokedReason,
                    Message = "Attempted reuse of revoked refresh token - entire token family revoked"
                }),
                IpAddress = GetClientIpAddress(request.HttpContext)
            };

            dbContext.OAuthAppAuditLogs.Add(securityAuditLog);
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.BadRequest(new OAuthErrorResponse
            {
                Error = OAuthErrorCodes.InvalidGrant,
                ErrorDescription = "Refresh token has been revoked."
            });
        }

        // Check if token has expired
        if (existingToken.ExpiresAt < DateTimeOffset.UtcNow)
        {
            return Results.BadRequest(new OAuthErrorResponse
            {
                Error = OAuthErrorCodes.InvalidGrant,
                ErrorDescription = "Refresh token has expired."
            });
        }

        // Get user subject
        var userSubject = existingToken.User?.Subject ?? existingToken.UserId.ToString();

        // Generate new access token
        var scopes = existingToken.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var accessToken = tokenService.GenerateAccessToken(
            subject: userSubject,
            userId: existingToken.UserId,
            workspaceId: existingToken.WorkspaceId,
            clientId: app.ClientId,
            scopes: scopes);

        // Token rotation: revoke old token and issue new one
        existingToken.IsRevoked = true;
        existingToken.RevokedAt = DateTimeOffset.UtcNow;
        existingToken.RevokedReason = "token_rotation";

        var newRefreshTokenValue = tokenService.GenerateRefreshToken();
        var newRefreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            TokenHash = tokenService.HashRefreshToken(newRefreshTokenValue),
            OAuthAppId = app.Id,
            UserId = existingToken.UserId,
            WorkspaceId = existingToken.WorkspaceId,
            Scopes = existingToken.Scopes,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = tokenService.GetRefreshTokenExpiration(),
            IsRevoked = false,
            ParentTokenId = existingToken.Id,
            TokenFamilyId = existingToken.TokenFamilyId,
            IpAddress = GetClientIpAddress(request.HttpContext),
            UserAgent = request.Headers.UserAgent.FirstOrDefault()
        };

        existingToken.ReplacedByTokenId = newRefreshToken.Id;
        dbContext.RefreshTokens.Add(newRefreshToken);

        // Audit log for token rotation
        var auditLog = new OAuthAppAuditLog
        {
            Id = Guid.NewGuid(),
            OAuthAppId = app.Id,
            EventType = DomainAuditEventType.RefreshTokenIssued,
            ActorUserId = existingToken.UserId,
            Timestamp = DateTimeOffset.UtcNow,
            Details = JsonSerializer.Serialize(new
            {
                TokenId = newRefreshToken.Id,
                TokenFamilyId = existingToken.TokenFamilyId,
                PreviousTokenId = existingToken.Id,
                Scopes = existingToken.Scopes,
                ExpiresAt = newRefreshToken.ExpiresAt,
                Reason = "token_rotation"
            }),
            IpAddress = GetClientIpAddress(request.HttpContext)
        };

        dbContext.OAuthAppAuditLogs.Add(auditLog);
        await dbContext.SaveChangesAsync(cancellationToken);

        var response = new OAuthTokenResponse
        {
            AccessToken = accessToken,
            TokenType = "Bearer",
            ExpiresIn = tokenService.AccessTokenLifetimeSeconds,
            RefreshToken = newRefreshTokenValue,
            Scope = existingToken.Scopes
        };

        return Results.Ok(response);
    }

    /// <summary>
    /// OAuth 2.0 Token Revocation Endpoint (RFC 7009)
    /// </summary>
    private static async Task<IResult> Revoke(
        HttpRequest request,
        IOAuthTokenService tokenService,
        XbimDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        // Parse form data
        var form = await request.ReadFormAsync(cancellationToken);

        var token = form["token"].FirstOrDefault();
        var tokenTypeHint = form["token_type_hint"].FirstOrDefault();
        var clientId = form["client_id"].FirstOrDefault();
        var clientSecret = form["client_secret"].FirstOrDefault();

        // Validate required parameters
        if (string.IsNullOrEmpty(token))
        {
            return Results.BadRequest(new OAuthErrorResponse
            {
                Error = OAuthErrorCodes.InvalidRequest,
                ErrorDescription = "token is required."
            });
        }

        if (string.IsNullOrEmpty(clientId))
        {
            return Results.BadRequest(new OAuthErrorResponse
            {
                Error = OAuthErrorCodes.InvalidRequest,
                ErrorDescription = "client_id is required."
            });
        }

        // Look up the OAuth app
        var app = await dbContext.OAuthApps
            .FirstOrDefaultAsync(a => a.ClientId == clientId, cancellationToken);

        if (app == null)
        {
            return Results.Json(new OAuthErrorResponse
            {
                Error = OAuthErrorCodes.InvalidClient,
                ErrorDescription = "Unknown client_id."
            }, statusCode: StatusCodes.Status401Unauthorized);
        }

        // Authenticate confidential clients
        if (app.ClientType == DomainOAuthClientType.Confidential)
        {
            if (string.IsNullOrEmpty(clientSecret))
            {
                return Results.Json(new OAuthErrorResponse
                {
                    Error = OAuthErrorCodes.InvalidClient,
                    ErrorDescription = "client_secret is required for confidential clients."
                }, statusCode: StatusCodes.Status401Unauthorized);
            }

            if (!tokenService.ValidateClientSecret(clientSecret, app.ClientSecretHash!))
            {
                return Results.Json(new OAuthErrorResponse
                {
                    Error = OAuthErrorCodes.InvalidClient,
                    ErrorDescription = "Invalid client credentials."
                }, statusCode: StatusCodes.Status401Unauthorized);
            }
        }

        // Per RFC 7009, we always return 200 OK regardless of whether token was found/revoked
        // Try to revoke as refresh token (we only store refresh tokens, not access tokens)
        if (token.StartsWith("octr_") || tokenTypeHint == "refresh_token" || string.IsNullOrEmpty(tokenTypeHint))
        {
            var tokenHash = tokenService.HashRefreshToken(token);
            var refreshToken = await dbContext.RefreshTokens
                .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.OAuthAppId == app.Id, cancellationToken);

            if (refreshToken != null && !refreshToken.IsRevoked)
            {
                refreshToken.IsRevoked = true;
                refreshToken.RevokedAt = DateTimeOffset.UtcNow;
                refreshToken.RevokedReason = "user_revoked";

                // Audit log
                var auditLog = new OAuthAppAuditLog
                {
                    Id = Guid.NewGuid(),
                    OAuthAppId = app.Id,
                    EventType = DomainAuditEventType.RefreshTokenRevoked,
                    ActorUserId = refreshToken.UserId,
                    Timestamp = DateTimeOffset.UtcNow,
                    Details = JsonSerializer.Serialize(new
                    {
                        TokenId = refreshToken.Id,
                        TokenFamilyId = refreshToken.TokenFamilyId,
                        Reason = "user_revoked"
                    }),
                    IpAddress = GetClientIpAddress(request.HttpContext)
                };

                dbContext.OAuthAppAuditLogs.Add(auditLog);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        // Per RFC 7009, always return 200 OK
        return Results.Ok();
    }

    /// <summary>
    /// Revokes all refresh tokens in a token family (for security when reuse is detected).
    /// </summary>
    private static async Task RevokeTokenFamily(
        XbimDbContext dbContext,
        Guid tokenFamilyId,
        string reason,
        CancellationToken cancellationToken)
    {
        var familyTokens = await dbContext.RefreshTokens
            .Where(t => t.TokenFamilyId == tokenFamilyId && !t.IsRevoked)
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        foreach (var token in familyTokens)
        {
            token.IsRevoked = true;
            token.RevokedAt = now;
            token.RevokedReason = reason;
        }
    }

    /// <summary>
    /// Gets the client IP address from the request context.
    /// </summary>
    private static string? GetClientIpAddress(HttpContext context)
    {
        // Check for forwarded header first (for reverse proxies)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // Take the first IP in the chain (client's IP)
            return forwardedFor.Split(',')[0].Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString();
    }

    private static string BuildRedirectUrl(string redirectUri, string code, string? state)
    {
        var uriBuilder = new UriBuilder(redirectUri);
        var query = HttpUtility.ParseQueryString(uriBuilder.Query);
        query["code"] = code;
        if (!string.IsNullOrEmpty(state))
        {
            query["state"] = state;
        }
        uriBuilder.Query = query.ToString();
        return uriBuilder.ToString();
    }

    private static IResult RedirectWithError(string redirectUri, string error, string errorDescription, string? state)
    {
        var uriBuilder = new UriBuilder(redirectUri);
        var query = HttpUtility.ParseQueryString(uriBuilder.Query);
        query["error"] = error;
        query["error_description"] = errorDescription;
        if (!string.IsNullOrEmpty(state))
        {
            query["state"] = state;
        }
        uriBuilder.Query = query.ToString();
        return Results.Redirect(uriBuilder.ToString());
    }
}
