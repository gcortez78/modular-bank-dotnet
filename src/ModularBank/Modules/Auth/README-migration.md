# Auth — Migration Guide

## Public interface
None — consumed via JWT only.

## To extract as microservice
1. Create `auth-service` with the same DB schema
2. Extract `JwtUtil` to a shared NuGet package
3. All services validate JWT locally using the shared package
4. Only `/auth/refresh` hits auth-service
5. Rotate JWT secret via env var simultaneously across all services
