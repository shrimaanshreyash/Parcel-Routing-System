# API integration test responsibility

This project boots the real ASP.NET Core entry point against disposable
PostgreSQL 17 and verifies liveness/readiness, authentication `401`,
authorization `403`, production JWT rejection, role separation, rate limits,
safe errors, hardening headers, upload limits, trusted-proxy configuration,
explainable routing, DTD rejection, and the privacy-safe 17-row reference
corpus through the durable hosted processor.

Provider-specific production OIDC login is not simulated here; the tests use the
same Development-only reviewer scheme allowed for local evaluation.
