using LibEvents.Data;
using Microsoft.EntityFrameworkCore;

namespace AlbionApp.Infrastructure.Persistence;

/// <summary>
/// Configura y expone un IDbContextFactory para EventsDbContext.
/// La base de datos SQLite se crea automáticamente si no existe.
/// </summary>
public static class EventsDbContextFactory
{
    public static readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AlbionTracker",
        "tracker.db");

    public static IDbContextFactory<EventsDbContext> Create()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);

        var options = new DbContextOptionsBuilder<EventsDbContext>()
            .UseSqlite($"Data Source={DbPath}")
            .Options;

        var factory = new SimpleFactory(options);

        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();   // crea el schema base si la DB no existe
        ApplySchemaUpdates(db);        // añade tablas/columnas nuevas sin EF Migrations

        return factory;
    }

    private const string GuildEventsSchema = """
        (
            Id           INTEGER PRIMARY KEY AUTOINCREMENT,
            Name         TEXT    NOT NULL,
            Description  TEXT,
            ScheduledAt  TEXT,
            Status       INTEGER NOT NULL DEFAULT 0,
            BuildGroupId INTEGER,
            FOREIGN KEY (BuildGroupId) REFERENCES BuildGroups(Id) ON DELETE SET NULL
        )
        """;

    /// <summary>
    /// Aplica DDL incremental para tablas/columnas añadidas después de la creación inicial.
    /// Cada sentencia usa IF NOT EXISTS para ser idempotente.
    /// </summary>
    private static void ApplySchemaUpdates(EventsDbContext db)
    {
        // Crea la tabla si no existe aún (DB nueva).
        db.Database.ExecuteSqlRaw($"CREATE TABLE IF NOT EXISTS GuildEvents {GuildEventsSchema}");

        // v2: ScheduledAt debe ser nullable.
        // PRAGMA table_info devuelve notnull=1 cuando la columna tiene NOT NULL constraint.
        // Si es así, recreamos la tabla (vacía en dev; migración segura en producción porque
        // los eventos activos tienen ScheduledAt no nulo de todas formas).
        var conn = db.Database.GetDbConnection();
        conn.Open();
        bool scheduledAtIsNotNull = false;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(GuildEvents)";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var colName  = reader.GetString(1);   // name
                var notNull  = reader.GetInt32(3);    // notnull  (1 = NOT NULL)
                if (colName == "ScheduledAt" && notNull == 1)
                {
                    scheduledAtIsNotNull = true;
                    break;
                }
            }
        }
        conn.Close();

        if (scheduledAtIsNotNull)
        {
            db.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS GuildEvents");
            db.Database.ExecuteSqlRaw($"CREATE TABLE GuildEvents {GuildEventsSchema}");
        }

        // v4: columna Emoji en BuildGroupSlots (emoji por slot en lugar de por Build)
        TryAddColumn(conn, "BuildGroupSlots", "Emoji", "TEXT");

        // v5: slots adicionales en Build (cualquier ítem, solo visual)
        TryAddColumn(conn, "Builds", "Extra1ItemId", "TEXT");
        TryAddColumn(conn, "Builds", "Extra2ItemId", "TEXT");
        TryAddColumn(conn, "Builds", "Extra3ItemId", "TEXT");
        TryAddColumn(conn, "Builds", "Extra4ItemId", "TEXT");

        // v3: columnas Discord en GuildEvents (idempotente con ADD COLUMN IF NOT EXISTS no existe
        // en SQLite < 3.37 — usamos try/catch por cada columna).
        TryAddColumn(conn, "GuildEvents", "DiscordMessageId", "INTEGER");
        TryAddColumn(conn, "GuildEvents", "DiscordChannelId", "INTEGER");

        // v3: tabla de participantes
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS EventParticipants (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                EventId         INTEGER NOT NULL,
                DiscordUserId   INTEGER NOT NULL,
                DiscordUsername TEXT    NOT NULL,
                BuildId         INTEGER,
                JoinedAt        TEXT    NOT NULL,
                FOREIGN KEY (EventId) REFERENCES GuildEvents(Id) ON DELETE CASCADE,
                FOREIGN KEY (BuildId) REFERENCES Builds(Id)      ON DELETE SET NULL
            );
            """);

        // v6: nickname del servidor para trazabilidad histórica de cambios de nombre
        TryAddColumn(conn, "EventParticipants", "DiscordNickname", "TEXT");

        // v8: Builds.Emoji fue eliminado de la entidad (movido a BuildGroupSlot.Emoji en v4).
        // Si la columna sigue existiendo como NOT NULL la inserción falla → hay que dropearlo.
        // ALTER TABLE … DROP COLUMN requiere SQLite ≥ 3.35 (Microsoft.Data.Sqlite 9.x lo incluye).
        if (ColumnExists(conn, "Builds", "Emoji"))
            db.Database.ExecuteSqlRaw("ALTER TABLE Builds DROP COLUMN Emoji");

        // v7: state machine — remap Status int values and add timer/snapshot columns
        // Guard: only remap if InProgressStartedAt does not yet exist (first run of v7).
        if (!ColumnExists(conn, "GuildEvents", "InProgressStartedAt"))
        {
            db.Database.ExecuteSqlRaw("""
                UPDATE GuildEvents SET Status = CASE
                  WHEN Status = 0 THEN 1
                  WHEN Status = 1 THEN 0
                  WHEN Status = 2 THEN 6
                  WHEN Status = 3 THEN 5
                  ELSE 0
                END
                """);
        }
        TryAddColumn(conn, "GuildEvents", "InProgressStartedAt",  "TEXT");
        TryAddColumn(conn, "GuildEvents", "ElapsedBeforePause",   "TEXT NOT NULL DEFAULT '00:00:00'");
        TryAddColumn(conn, "GuildEvents", "CancelledFromStatus",  "INTEGER");
        TryAddColumn(conn, "GuildEvents", "CancelledElapsedTime", "TEXT");
    }

    private static bool ColumnExists(System.Data.Common.DbConnection conn, string table, string column)
    {
        conn.Open();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table})";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (reader.GetString(1) == column)
                    return true;
            }
            return false;
        }
        finally { conn.Close(); }
    }

    private static void TryAddColumn(System.Data.Common.DbConnection conn, string table, string column, string type)
    {
        conn.Open();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
            cmd.ExecuteNonQuery();
        }
        catch { /* columna ya existe */ }
        finally { conn.Close(); }
    }

    private sealed class SimpleFactory(DbContextOptions<EventsDbContext> options)
        : IDbContextFactory<EventsDbContext>
    {
        public EventsDbContext CreateDbContext() => new(options);
    }
}
