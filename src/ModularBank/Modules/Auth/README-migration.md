# Auth — Migration Guide

## Public interface
None — consumed via JWT only.

## To extract as microservice
1. Create `auth-service` with the same DB schema
2. Publish `Shared/Infrastructure/JwtUtil.cs` (already in the `Shared` layer) as a NuGet package
3. All services validate JWT locally using the shared package
4. At runtime, services use local JWT validation — only `/auth/login`, `/auth/register`, and `/auth/refresh` route traffic to auth-service
5. Rotate JWT secret via env var simultaneously across all services
