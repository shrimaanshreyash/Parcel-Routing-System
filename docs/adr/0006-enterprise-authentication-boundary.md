# ADR 0006: Use Enterprise Authentication, Not Local Accounts

- Status: Accepted
- Date: 2026-07-27

## Context

The application does not need registration, password recovery, profile
management, or a consumer account system. A production deployment still needs
to identify users and enforce the Operator, Insurance Approver, and Rule
Administrator capabilities.

Building password storage and self-service accounts would add
security-sensitive scope without improving parcel routing.

## Decision

- Do not build local username and password registration.
- Keep the API authorization boundary and role model explicit from the beginning.
- Integrate an OpenID Connect provider appropriate to the eventual client environment, such as Microsoft Entra ID, before public or client deployment.
- Keep development authentication replaceable so contributors can run the
  complete workflow locally.
- Do not show a fake signed-in user, fabricated profile, or account menu before authentication exists.
- Validate OIDC JWT access tokens in production and reject startup unless the
  HTTPS authority and API audience are configured.
- Permit the automatic local reviewer only in the ASP.NET Core Development
  environment, using allow-listed roles and a non-personal subject.
- Acquire production browser access tokens through the chosen client identity
  provider; do not store tokens in local storage.

## Consequences

- The interface identifies the local runtime as `Local reviewer` without
  pretending that production identity integration is complete.
- The client retains control of identity lifecycle, multifactor authentication, password policy, and account removal.
- The final provider remains a deployment decision.
- Server authorization has 401 and 403 integration coverage; provider-specific
  login, logout, MFA, and tenant policy remain deployment work.
