# Assumptions

- A student can hold one seat per class at a time; authentication is outside this sample.
- A hold is valid for ten minutes and is released automatically after expiration.
- A confirmed hold represents an admission and permanently consumes one seat.
- PostgreSQL is the production database. The API creates its schema on startup for this sample.
- The frontend talks to the API through `/api`; the Vite development proxy targets `http://localhost:8080`.
- Names are required but are not yet associated with a user account.

