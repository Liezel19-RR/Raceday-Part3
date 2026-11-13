CREATE TABLE Users (
    Id int IDENTITY PRIMARY KEY,
    FullName nvarchar(100) NOT NULL,
    Email nvarchar(200) NOT NULL UNIQUE,
    PasswordHash nvarchar(max) NOT NULL,
    Role nvarchar(20) NOT NULL,
    Phone nvarchar(30) NULL,
    ProfilePictureUrl nvarchar(2048) NULL
);

CREATE TABLE Events (
    Id int IDENTITY PRIMARY KEY,
    Name nvarchar(150) NOT NULL,
    Description nvarchar(1000) NOT NULL,
    Date datetime2 NOT NULL,
    Location nvarchar(200) NOT NULL,
    DistanceKm decimal(8,2) NOT NULL,
    EventType nvarchar(20) NOT NULL,
    BannerImageUrl nvarchar(2048) NULL,
    OrganiserId int NOT NULL REFERENCES Users(Id)
);

CREATE TABLE Categories (
    Id int IDENTITY PRIMARY KEY,
    EventId int NOT NULL REFERENCES Events(Id),
    Name nvarchar(80) NOT NULL,
    Description nvarchar(200) NULL,
    DistanceKm decimal(8,2) NOT NULL,
    Price decimal(10,2) NOT NULL,
    Capacity int NULL
);

CREATE TABLE Enrolments (
    Id int IDENTITY PRIMARY KEY,
    ParticipantId int NOT NULL REFERENCES Users(Id),
    EventId int NOT NULL REFERENCES Events(Id),
    CategoryId int NOT NULL REFERENCES Categories(Id),
    Status nvarchar(20) NOT NULL,
    EnrolledAt datetime2 NOT NULL,
    CONSTRAINT UQ_Enrolment_Participant_Event UNIQUE (ParticipantId, EventId)
);

CREATE TABLE Results (
    Id int IDENTITY PRIMARY KEY,
    EnrolmentId int NOT NULL UNIQUE REFERENCES Enrolments(Id),
    FinishTime time NOT NULL,
    FinishingPosition int NOT NULL,
    Status nvarchar(20) NOT NULL,
    RecordedAt datetime2 NOT NULL
);

INSERT INTO Users (FullName, Email, PasswordHash, Role, Phone)
VALUES (N'Sample Organiser', N'organiser@example.test', N'REPLACE_WITH_HASHED_PASSWORD', N'Organiser', NULL);

INSERT INTO Events (Name, Description, Date, Location, DistanceKm, EventType, OrganiserId)
VALUES (N'Community 5K', N'Sample event; replace or remove before production.', DATEADD(day, 30, SYSUTCDATETIME()),
        N'Johannesburg', 5.00, N'Run', SCOPE_IDENTITY());
