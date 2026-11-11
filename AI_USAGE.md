# AI Tool Usage Disclosure

## AI Tools Used
- **Google Gemini**

## Parts Where AI Was Used
1. **Project Scaffolding & Boilerplate:** Generating initial DTO records (`ApiContracts.cs`), EF Core entity configurations (`AppDbContext.cs`), and Docker multi-stage build files.
2. **Concurrency Strategy Review:** Reviewing PostgreSQL `SELECT ... FOR UPDATE` transaction locking in EF Core (`FromSqlInterpolated`) to ensure multi-instance concurrency safety.
3. **Automated Tests:** Structuring the xUnit integration test suite using `WebApplicationFactory<Program>` and `Task.WhenAll` for the 50-request concurrency test.
4. **Frontend UI Components:** Structuring the React polling loop (every 2.5 seconds), 1-second countdown timer, and responsive table/card layout.

## Where AI Made Mistakes & How I Caught and Fixed Them

1. **EF Core Expression Tree Compilation Error (`CS8110`) in `GET /me/holds`:**
   - **AI Mistake:** In `Program.cs`, the AI generated an EF Core `.Select(...)` query that called a local C# helper function (`FormatUtcIso(h.ExpiresAt)`) directly inside the `IQueryable` projection before `.ToListAsync()`.
   - **How I Caught It:** When building and running the test suite in the `.NET 8 SDK` container, the compiler threw `error CS8110: An expression tree may not contain a reference to a local function`.
   - **How I Fixed It:** I modified `GET /me/holds` to first materialize the raw database fields into memory using `.ToListAsync()`, and then mapped the in-memory list to `MyHoldResponse` using `FormatUtcIso`.

2. **TypeScript Build Failure (`TS2305` & `TS2339`) in Frontend Docker Build:**
   - **AI Mistake:** In `frontend/src/api.ts`, the AI imported a non-existent type name (`ClassSummaryResponse` instead of `SchoolClassSummary` defined in `types.ts`) and accessed `import.meta.env.VITE_API_BASE_URL` without declaring Vite's client type definitions.
   - **How I Caught It:** Running `docker compose up --build` failed during the `tsc -b && vite build` step inside the frontend Dockerfile with `TS2305` and `TS2339`.
   - **How I Fixed It:** I removed the invalid `ClassSummaryResponse` import and added `/// <reference types="vite/client" />` at the top of `frontend/src/api.ts`, allowing the production Docker image to compile cleanly.