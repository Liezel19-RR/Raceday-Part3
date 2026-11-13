<div align="center">

# RaceDay

### Run it. Ride it. Race it.

An event-registration platform for South African running, walking, and cycling events.

**Part 2 · REST API** &nbsp; | &nbsp; **Part 3 · Web application and extensions**

[Explore the features](#what-is-included) · [Run locally](#run-locally) · [Use Docker](#run-with-docker-compose) · [View the API](#main-endpoints)

</div>

| **Take part** | **Organise** | **Follow the results** |
|---|---|---|
| Browse events, enrol, and keep track of your races. | Manage events, categories, participants, and results. | See race results and event leaderboards. |

---

RaceDay combines an ASP.NET Core REST API, an MVC website, SQL Server persistence, optional Azure Blob Storage image uploads, and Docker Compose deployment.

## What is included

### Part 2 — REST API foundation

- ASP.NET Core Web API with Entity Framework Core and SQL Server.
- Registration and login with hashed passwords.
- Server-side session authentication and Organiser/Participant role checks.
- Event, category, enrolment, and race-result workflows.
- Swagger API documentation.

### Part 3 — API extensions and web application

- Event distance/type/banner, user profile pictures, category distance/price/capacity, and result status.
- Event filtering and organiser dashboard data, image uploads, enrolment status management, and richer result/leaderboard responses.
- An MVC website for account/profile, event browsing, organiser event/category/enrolment/result management, and participant enrolments/results.
- Dockerfiles, Docker Compose, CI, an initial EF migration, and updated ERD/endpoint/SQL documentation.

## Folder structure

```text
RaceDay.sln                         Solution containing API, MVC, and tests
RaceDay.Api/                        Part 2 API foundation + Part 3 API changes
  Controllers/                      Auth, users, events, categories, enrolments, results
  Data/
    RaceDayDbContext.cs             EF Core model configuration
    Migrations/                     Part 3 initial database migration
  Models/                           Entities and API request/response contracts
  Security/                         Session-based API authorization
  Services/                         Part 3 Azure Blob Storage and image validation
  Properties/launchSettings.json    Local API URLs and launch profile
RaceDay.Mvc/                        Part 3 MVC web application
  Controllers/                      Account, events, home, organiser, participant
  Models/                           API mirrors and MVC view models
  Services/                         Typed API client, session authorization, formatters
  Views/                             Razor pages and reusable partials
  wwwroot/                           CSS and JavaScript
RaceDay.Tests/                       API smoke and Part 3 integration tests
docs/
  erd.md                             Entity relationship diagram
  endpoint-plan.md                   API endpoint reference
sql/
  raceday.sql                        SQL Server schema/sample-data reference
.github/workflows/dotnet-ci.yml      Build, test, and Docker image CI
docker-compose.yml                   SQL Server + API + MVC deployment
```

The API source has both the Part 2 foundation and Part 3 changes in the same project; the MVC app and Azure/image-upload services are Part 3 additions.

## Data model

The core entities are `User`, `RaceEvent`, `EventCategory`, `Enrolment`, and `RaceResult`. Part 3 adds profile/banner image URLs, category distance/price/optional capacity, enrolment status rules, and result status. Distances and prices use explicit decimal precision. See the [ERD](docs/erd.md), [endpoint plan](docs/endpoint-plan.md), and [SQL reference script](sql/raceday.sql).

## Run locally

Prerequisites: .NET 10 SDK and SQL Server or SQL Server LocalDB. LocalDB is a Windows-only SQL Server component; it is not included with the .NET SDK.

1. Install SQL Server Express LocalDB through Visual Studio Installer (Individual components > SQL Server Express LocalDB), or install it with SQL Server Express.
2. From a Visual Studio Developer PowerShell, VS Code terminal, or a regular PowerShell terminal, verify and start the default instance:
   ```powershell
   sqllocaldb info
   sqllocaldb start MSSQLLocalDB
   ```
   If the instance does not exist, create it first with `sqllocaldb create MSSQLLocalDB`.
3. Restore the repository-local EF CLI and apply the included migration from the repository root:
   ```powershell
   dotnet tool restore
   dotnet ef database update --project RaceDay.Api\RaceDay.Api.csproj
   ```
4. The Development environment automatically applies pending migrations when the API starts. The LocalDB connection string is in [RaceDay.Api/appsettings.json](RaceDay.Api/appsettings.json); production/default configuration keeps startup migration disabled.
5. Set `ApiBaseUrl` in [RaceDay.Mvc/appsettings.json](RaceDay.Mvc/appsettings.json) to the API's HTTP URL. The supplied launch profiles use API port `5184` and MVC port `5284`.
6. In separate terminals, start the API and MVC app:
   ```powershell
   dotnet run --project RaceDay.Api\RaceDay.Api.csproj
   dotnet run --project RaceDay.Mvc\RaceDay.Mvc.csproj
   ```
7. Open `http://localhost:5284` for the website or `http://localhost:5184/swagger` for Swagger.

### Visual Studio and VS Code

The solution can be opened in Visual Studio with `RaceDay.sln`. Set `RaceDay.Api` and `RaceDay.Mvc` as the startup projects when you want both applications to run together. Their launch profiles use the same ports documented above.

In VS Code, install the C# Dev Kit extension, open the repository root, and choose **Run and Debug > RaceDay API + MVC**. The API launch configuration starts LocalDB, restores the local EF tool, and applies the migration before launching both applications. The **build** task builds `RaceDay.sln`.

Startup database initialization is optional and defaults to `false`. When enabled, it applies EF migrations with retry handling. The API enables Swagger in all environments and does not use HTTPS redirection, so the local/container HTTP configuration works.

## Run with Docker Compose

1. Copy `.env.example` to `.env` and set `SA_PASSWORD` to a strong local password. Add an Azure Storage connection string only if you want to test image uploads.
2. Ensure Docker Desktop is running, then run:
   ```powershell
   docker compose up --build
   ```
3. Visit `http://localhost:5284` for MVC and `http://localhost:5184/swagger` for the API.

Compose starts SQL Server with a health check, then the API with startup migrations/retries, and finally MVC. The SQL Server host port is `14330` by default (`SQL_PORT` in `.env`); the API connects to SQL Server internally on port `1433`. Use `docker compose down` to stop this Compose stack.

## Session usage in Swagger

Register with `POST /api/auth/register`, then log in with `POST /api/auth/login`. The API uses a session cookie. Swagger UI cookie handling can vary by browser; if the cookie is not retained, use a browser or API client that preserves cookies.

Example registration request:

```json
{
  "fullName": "Alex Runner",
  "email": "alex@example.com",
  "password": "StrongPass123!",
  "role": "Participant",
  "phone": "0712345678"
}
```

Valid roles are `Organiser` and `Participant`. Use fictional test data; do not enter real personal information into a demo environment.

## Main endpoints

- **Auth and users:** `POST /api/auth/register`, `POST /api/auth/login`, `POST /api/auth/logout`, `GET/PUT /api/users/me`, `POST /api/users/me/profile-picture`
- **Events:** `GET /api/events` (filters supported), `GET /api/events/{id}`, organiser-only `GET /api/events/mine`, `POST/PUT/DELETE /api/events`, `POST /api/events/{id}/banner`
- **Categories:** `GET/POST /api/events/{eventId}/categories`, organiser-only category update/delete
- **Enrolments:** `POST /api/enrolments`, `GET /api/enrolments/mine`, organiser-only `GET /api/enrolments/event/{eventId}`, `PUT /api/enrolments/{id}/status`
- **Results:** organiser-only `POST /api/results`, participant `GET /api/results/me`, public `GET /api/results/event/{eventId}` leaderboard

See the [endpoint plan](docs/endpoint-plan.md) for access rules, filters, and response behavior.

## Azure Blob Storage

Create an Azure Storage Account and configure `Azure:ConnectionString` (or `AZURE_STORAGE_CONNECTION_STRING` in Compose). `Azure:ContainerName` defaults to `raceday-images`. Uploads accept JPG/JPEG, PNG, WEBP, or GIF images up to 5 MB. The container is created for blob-public access so the API can return image URLs. Protect the connection string and never commit it. Until storage is configured, image uploads return `503 Service Unavailable`.

## Build, tests, and CI

```powershell
dotnet restore RaceDay.sln
dotnet build RaceDay.sln --configuration Release
dotnet test RaceDay.Tests\RaceDay.Tests.csproj --configuration Release
```

The test project contains 25 API smoke/integration tests and uses an in-memory database plus fake blob storage, so tests do not require SQL Server or Azure. GitHub Actions restores and builds the solution, builds MVC explicitly, runs tests, and builds both Docker images on pushes and pull requests.

## Before submission

- Compare the entities and endpoints with your approved Part 1 ERD, SQL script, and endpoint plan; update them if your approved design differs.
- Run the application and tests against the environment you plan to demonstrate.
- Check the GitHub Actions page for a successful build; if required by the brief, include a genuine screenshot of the passing workflow.
- Make meaningful commits that reflect your work, and record the required demonstration video/voice-over.
- Submit your GitHub repository link through the required course portal (for example, ARC). Check your brief's final-submission format; if it says ZIP files are not accepted, submit the repository link instead.
- Follow your institution's academic-integrity requirements. Understand and adapt the work yourself, and disclose AI assistance if required.
