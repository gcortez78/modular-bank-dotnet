# Transfers — Migration Guide

## Public interface
None — Transfers is the top-level orchestrator.

## To extract as microservice
1. Create `transfers-service` with the same DB schema
2. Implement a Saga:
   - Step 1: POST accounts-service/debit (source)
   - Step 2: POST accounts-service/credit (target) → if fails, compensate step 1
   - Step 3: Save transfer record locally
   - Step 4: POST notifications-service (fire-and-forget)
   - Step 5: POST audit-service (fire-and-forget)
