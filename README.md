# School Admission System

A concurrency-safe online school admission and seat reservation system built with **.NET 8 Minimal APIs**, **PostgreSQL 16**, **React 18 (Vite + TypeScript)**, and **Docker**.

## Prerequisites
- **Docker** and **Docker Compose** (Nothing else needs to be installed on the host machine).

---

## Quick Start (Running with Docker)

From the root directory of the repository, run:

```bash
docker compose up --builds


Service URLs
Frontend UI: http://localhost:5173

Backend API: http://localhost:8080

PostgreSQL Database: localhost:5432 (persisted via Docker volume pgdata)

Environment Variables
Backend (docker-compose.yml):

DATABASE_URL: PostgreSQL connection string (supports both Npgsql key-value format and postgres:// URI format).

HOLD_DURATION_SECONDS: Seat hold expiration time in seconds (default: 600).

Frontend (docker-compose.yml build args / env):

VITE_API_BASE_URL: Backend base URL (default: http://localhost:8080).