# School Admission System

A small full-stack admission portal for viewing classes and temporarily holding an available seat.

## Run with Docker

```bash
docker compose up --build
```

Open <http://localhost:5173>. The API is available at <http://localhost:8080>.

## Run locally

The API uses PostgreSQL by default. Set `ConnectionStrings__Default` to a reachable database
connection string, then run:

```bash
cd backend
dotnet run --project SchoolAdmission.Api

cd ../frontend
npm install
npm run dev
```

The API seeds sample classes on first startup. A seat hold expires after ten minutes unless
confirmed. This sample intentionally has no authentication or payment integration.

## API

- `GET /api/classes` lists classes and remaining seats.
- `POST /api/classes/{classId}/holds` creates a temporary hold. Body: `{ "studentName": "..." }`.
- `DELETE /api/holds/{holdId}` releases a hold.
- `POST /api/holds/{holdId}/confirm` confirms an active hold.

