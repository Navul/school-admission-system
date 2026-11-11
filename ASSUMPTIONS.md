# Assumptions & Architectural Decisions

Where the specification left room for interpretation, the following engineering decisions and assumptions were made:

## 1. Multi-Instance Concurrency & Zero Overbooking (Bonus §1.6)
- **How it is guaranteed:** Inside `POST /classes/{classId}/holds`, seat allocation runs inside a PostgreSQL transaction (`IsolationLevel.ReadCommitted`) that acquires a pessimistic row-level lock on the target class before counting or inserting holds:
  ```sql
  SELECT * FROM classes WHERE id = @classId FOR UPDATE
  ```
- **Multi-process safety:** Because synchronization happens in PostgreSQL on the `classes` row rather than using in-memory application locks (`lock` or `SemaphoreSlim`), running two or more backend processes on different ports against the same database still serializes concurrent requests for the same class. `heldSeats + confirmedSeats` can never exceed `totalSeats`.

## 2. Dual-Layer Automatic Hold Expiration
- **Background Worker (`HoldExpirationWorker`):** A hosted `.NET BackgroundService` ticks every 1 second and runs a set-based `ExecuteUpdateAsync` query to mark overdue `HELD` rows (`expires_at <= NOW()`) as `EXPIRED` without requiring any external API trigger.
- **Immediate On-Read / In-Transaction Evaluation:** To avoid sub-second race conditions between background ticks, API endpoints also sync overdue holds and evaluate active holds using `status == 'HELD' && expiresAt > DateTime.UtcNow`.

## 3. Student Identity (`X-Student-Id`) Validation & Persistence
- The `X-Student-Id` header is validated as a 17-digit numeric Birth Registration Number (`^\d{17}$`). Requests with missing or invalid `X-Student-Id` headers receive `400 Bad Request` with a clear error message.
- On the React frontend, the 17-digit student ID and selected hold ID are stored in `localStorage` so page refreshes or browser restarts immediately restore the student's ongoing applications.

## 4. Re-Applying After Expiration or Cancellation
- A student is restricted to **one** active (`HELD` or `CONFIRMED`) application per class at a time (duplicate attempts return `409 Conflict`).
- If a student's prior application in a class has transitioned to `EXPIRED` or `CANCELLED`, they are allowed to submit a new application for that class provided seats are available.

## 5. Ownership, Idempotency, and Cancellation Rules
- **Ownership:** Only the student whose `X-Student-Id` matches the hold's `studentId` can confirm or cancel it. Attempts by a different `X-Student-Id` return `403 Forbidden`.
- **Idempotent Confirm (`POST /holds/{holdId}/confirm`):** Calling confirm multiple times on an already-confirmed hold by the owner returns `200 OK` (`{ "holdId": "...", "status": "CONFIRMED" }`) without incrementing `confirmedSeats` again. Attempting to confirm an `EXPIRED` or `CANCELLED` hold returns `409 Conflict`.
- **Cancel (`DELETE /holds/{holdId}`):** Cancelling an active `HELD` application transitions its status to `CANCELLED`, frees the seat, and returns `204 No Content`. Confirmed or already-expired holds cannot be cancelled (`409 Conflict`).