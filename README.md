# School Admission System

An online school admission and seat reservation system built on a first-come, first-served basis using **.NET 8 Minimal APIs**, **PostgreSQL 16**, **React 18 (Vite + TypeScript)**, and **Docker**.

## Tech Stack
- **Backend:** C# (.NET 8 Web API), Entity Framework Core, Npgsql
- **Database:** PostgreSQL 16 (with persistent Docker volume)
- **Frontend:** React 18, TypeScript, Vite, Nginx
- **Containerization:** Docker & Docker Compose

---

## How to Run the Application (Docker)

No local dependencies other than **Docker** and **Docker Compose** are required.

From the root of the repository, run:

```bash
docker compose up --build
```

*(To run in the background, use `docker compose up --build -d`).*

### Application URLs
- **Frontend UI:** http://localhost:5173
- **Backend API:** http://localhost:8080

### Configurable Environment Variables
Configured in `docker-compose.yml`:
- `DATABASE_URL`: PostgreSQL connection string for the backend.
- `HOLD_DURATION_SECONDS`: Duration (in seconds) a seat remains held before expiring automatically (default: `600`).
- `VITE_API_BASE_URL`: Backend API URL used by the React frontend (default: `http://localhost:8080`).

### Data Persistence Across Restarts
Database records are persisted in the `pgdata` Docker volume. Stopping and restarting the containers retains all classes and student applications:

```bash
docker compose down
docker compose up -d
```

---

## How to Run Automated Tests

The backend test suite (`backend/SchoolAdmission.Tests`) includes **4 automated integration and concurrency tests**:
1. `ConcurrencyTest_50SimultaneousRequestsFor10Seats_Exactly10SucceedAndNoOverbooking`: Sends 50 parallel requests (`Task.WhenAll`) for a class with 10 seats and verifies that exactly 10 succeed (`201 Created`), 40 return `409 Conflict`, and `heldSeats + confirmedSeats` never exceeds `totalSeats`.
2. `SingleStudent_CannotHoldMultipleActiveSeatsInSameClass`: Verifies that a student cannot hold more than one active seat in the same class simultaneously.
3. `ConfirmHold_IsIdempotent_AndEnforcesOwnership`: Verifies that only the hold owner can confirm a seat (`403 Forbidden` for others) and repeated confirmation requests return `200 OK` without incrementing `confirmedSeats` more than once.
4. `HoldExpiration_AutomaticallyReleasesSeatAfterDuration`: Sets `HOLD_DURATION_SECONDS=2` and verifies that an unconfirmed hold automatically transitions to `EXPIRED` and releases the seat back to `availableSeats`.

### Option 1: Run Tests Using Docker (No Local .NET SDK Needed)

1. Ensure the PostgreSQL container is running:
   ```bash
   docker compose up -d postgres
   ```

2. Execute the xUnit test suite inside the .NET 8 SDK container:
   ```bash
   MSYS_NO_PATHCONV=1 docker run --rm --network container:admission_postgres -e DATABASE_URL="Host=localhost;Port=5432;Database=school_admission;Username=postgres;Password=postgrespassword" -v "$(pwd)/backend:/src" -w /src [mcr.microsoft.com/dotnet/sdk:8.0](https://mcr.microsoft.com/dotnet/sdk:8.0) dotnet test SchoolAdmission.Tests/SchoolAdmission.Tests.csproj
   ```

### Option 2: Run Tests Using Local .NET 8 SDK
If the .NET 8 SDK is installed on the host machine and PostgreSQL is running on `localhost:5432`:

```bash
dotnet test backend/SchoolAdmission.Tests/SchoolAdmission.Tests.csproj
```