# API project responsibility

This project hosts the ASP.NET Core controller API, authentication and
authorization boundaries, request validation, Problem Details, middleware,
rate limits, liveness/readiness checks, dependency composition, and durable
batch worker.

Controllers must remain thin and must not implement routing rules.

Development uses the explicit Local reviewer identity. Production validates
OIDC JWT access tokens and refuses startup when required provider values are
missing.
