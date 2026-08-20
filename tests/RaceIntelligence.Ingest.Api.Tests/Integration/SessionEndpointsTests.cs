using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Npgsql;
using RaceIntelligence.Ingest.Api.Tests.Support;
using RaceIntelligence.Ingest.Contracts;
using Shouldly;

namespace RaceIntelligence.Ingest.Api.Tests.Integration;

/// <summary>
/// End-to-end tests against the real AppHost graph (Postgres + ingest API) for the JSON session/lap
/// endpoints. Skips gracefully when Docker is unavailable — see <see cref="AspireAppFixture"/>.
/// </summary>
[Collection(AspireAppCollection.Name)]
public sealed class SessionEndpointsTests(AspireAppFixture fixture)
{
    [Fact]
    public async Task Creating_the_same_session_twice_is_idempotent()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        var request = DtoFactory.SessionCreateRequest();

        var first = await PostAsync("/api/v1/sessions", request);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        var second = await PostAsync("/api/v1/sessions", request);
        second.StatusCode.ShouldBe(HttpStatusCode.OK, "a repeated create with the same SessionId must succeed, not conflict or duplicate");
    }

    /// <summary>
    /// The check the per-simulator split turns on.
    /// </summary>
    /// <remarks>
    /// This database holds one simulator's telemetry and its unique keys no longer scope by game,
    /// so accepting a session from another simulator would merge two simulators' cars and drivers
    /// into shared rows — silently, and unrecoverably once telemetry hangs off them. A misconfigured
    /// collector pointed at the wrong ingest API is the way that actually happens.
    /// </remarks>
    [Fact]
    public async Task A_session_from_another_simulator_is_refused()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        var elsewhere = DtoFactory.SessionCreateRequest() with
        {
            GameVersion = DtoFactory.UniqueGameVersion() with { GameKey = "assetto-corsa-competizione" },
        };

        var response = await PostAsync("/api/v1/sessions", elsewhere);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Both keys, because the useful question is which end is misconfigured and neither one
        // answers it alone.
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain(DtoFactory.GameKey);
        body.ShouldContain("assetto-corsa-competizione");
    }

    /// <summary>Capitalisation is a convention on a game key, not a constraint worth refusing over.</summary>
    [Fact]
    public async Task The_simulator_check_ignores_case()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        var shouted = DtoFactory.SessionCreateRequest() with
        {
            GameVersion = DtoFactory.UniqueGameVersion() with { GameKey = DtoFactory.GameKey.ToUpperInvariant() },
        };

        (await PostAsync("/api/v1/sessions", shouted)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unsupported_schema_version_is_rejected_with_400()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        var request = DtoFactory.SessionCreateRequest(schemaVersion: SchemaVersion.Current + 1);

        var response = await PostAsync("/api/v1/sessions", request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Missing_api_key_is_rejected_with_401()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sessions")
        {
            Content = JsonContent.Create(DtoFactory.SessionCreateRequest()),
        };

        using var response = await fixture.ApiClient.SendAsync(message, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Wrong_api_key_is_rejected_with_401()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sessions")
        {
            Content = JsonContent.Create(DtoFactory.SessionCreateRequest()),
        };
        message.Headers.Add("X-Api-Key", "not-the-right-key");

        using var response = await fixture.ApiClient.SendAsync(message, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Health_endpoint_is_reachable_without_an_api_key()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        using var response = await fixture.ApiClient.GetAsync("/alive", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Patching_an_unknown_session_returns_404()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        var update = new SessionUpdateRequest(SchemaVersion.Current, DateTimeOffset.UtcNow, null, null, null);

        using var message = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/sessions/{Guid.CreateVersion7()}")
        {
            Content = JsonContent.Create(update),
        };
        message.Headers.Add("X-Api-Key", AspireAppFixture.ApiKey);

        using var response = await fixture.ApiClient.SendAsync(message, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Recording_a_lap_for_an_unknown_session_returns_404()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        var lap = new LapCompletedRequest(SchemaVersion.Current, 1, TimeSpan.FromMinutes(1.5), 2f, 40f, 60f, true);

        var response = await PostAsync($"/api/v1/sessions/{Guid.CreateVersion7()}/laps", lap);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Recording_the_same_lap_number_twice_upserts_rather_than_duplicates()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        var session = DtoFactory.SessionCreateRequest();
        (await PostAsync("/api/v1/sessions", session)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var lap = new LapCompletedRequest(SchemaVersion.Current, 1, TimeSpan.FromMinutes(1.5), 2f, 40f, 60f, true);
        var firstLap = await PostAsync($"/api/v1/sessions/{session.SessionId}/laps", lap);
        firstLap.StatusCode.ShouldBe(HttpStatusCode.OK);

        var updatedLap = lap with { LapTime = TimeSpan.FromMinutes(1.4) };
        var secondLap = await PostAsync($"/api/v1/sessions/{session.SessionId}/laps", updatedLap);
        secondLap.StatusCode.ShouldBe(HttpStatusCode.OK, "re-submitting the same lap number must upsert, not error");
    }

    [Fact]
    public async Task Renaming_a_driver_updates_the_one_driver_row_and_leaves_each_sessions_own_name_intact()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        // Same game, same sim driver id, different reported name: one person who renamed themselves.
        var gameVersion = DtoFactory.UniqueGameVersion();
        var simDriverId = UniqueSimDriverId();

        var before = DtoFactory.SessionCreateRequest() with
        {
            GameVersion = gameVersion,
            SimDriverId = simDriverId,
            PlayerName = "Name Before Rename",
        };
        var after = DtoFactory.SessionCreateRequest() with
        {
            GameVersion = gameVersion,
            SimDriverId = simDriverId,
            PlayerName = "Name After Rename",
        };

        (await PostAsync("/api/v1/sessions", before)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PostAsync("/api/v1/sessions", after)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var db = await OpenDatabaseAsync();

        (await CountAsync(db, "SELECT count(*) FROM drivers WHERE sim_driver_id = @simDriverId", ("simDriverId", simDriverId)))
            .ShouldBe(1, "the sim driver id is the identity, so a rename must not fork a second driver row");

        (await ScalarAsync(db, "SELECT display_name FROM drivers WHERE sim_driver_id = @simDriverId", ("simDriverId", simDriverId)))
            .ShouldBe("Name After Rename", "display_name is a mutable label tracking the most recently seen name");

        (await CountAsync(
            db,
            "SELECT count(DISTINCT driver_id) FROM sessions WHERE id IN (@first, @second)",
            ("first", before.SessionId),
            ("second", after.SessionId)))
            .ShouldBe(1, "both sessions belong to the same person");

        // The name used at the time is per session, so overwriting display_name loses nothing.
        (await ScalarAsync(db, "SELECT player_name FROM sessions WHERE id = @id", ("id", before.SessionId)))
            .ShouldBe("Name Before Rename");
        (await ScalarAsync(db, "SELECT player_name FROM sessions WHERE id = @id", ("id", after.SessionId)))
            .ShouldBe("Name After Rename");
    }

    /// <summary>
    /// The counterpart of the persistence-layer test of the same name, updated for the same reason:
    /// this database is one simulator, so one sim driver id is one driver.
    /// </summary>
    /// <remarks>
    /// It used to assert the opposite — two sessions from two different game keys producing two
    /// driver rows scoped by <c>game_id</c>. That could not happen now even in principle, because a
    /// session claiming a second simulator is refused at the door. Sim driver ids do still share a
    /// numeric namespace across simulators; recognising one human across them is the identity
    /// registry's job (ADR 0002), not this row's.
    /// </remarks>
    [Fact]
    public async Task The_same_sim_driver_id_across_two_sessions_is_one_driver()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        var simDriverId = UniqueSimDriverId();
        var first = DtoFactory.SessionCreateRequest() with { SimDriverId = simDriverId, PlayerName = "First Session" };
        var second = DtoFactory.SessionCreateRequest() with { SimDriverId = simDriverId, PlayerName = "Second Session" };

        (await PostAsync("/api/v1/sessions", first)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PostAsync("/api/v1/sessions", second)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var db = await OpenDatabaseAsync();

        (await CountAsync(db, "SELECT count(*) FROM drivers WHERE sim_driver_id = @simDriverId", ("simDriverId", simDriverId)))
            .ShouldBe(1, "one simulator, one id, one person");
    }

    [Theory]
    // 3 = 3x wear with fuel consumption switched off entirely: the two rates are configured
    // independently, and 0 is a real setting that must not be conflated with "unknown".
    [InlineData(3, 0)]
    // RaceRoom's "not available" sentinel, which is carried through raw rather than filtered out.
    [InlineData(-1, -1)]
    public async Task Session_wear_and_fuel_rates_are_persisted_exactly_as_posted(int tyreWearRate, int fuelUsageRate)
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        var request = DtoFactory.SessionCreateRequest() with
        {
            TyreWearRate = tyreWearRate,
            FuelUsageRate = fuelUsageRate,
        };

        (await PostAsync("/api/v1/sessions", request)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var db = await OpenDatabaseAsync();

        var storedFuel = await ScalarAsync(db, "SELECT fuel_usage_rate FROM sessions WHERE id = @id", ("id", request.SessionId));
        storedFuel.ShouldNotBeOfType<DBNull>("a rate that was reported must never read back as unknown");
        Convert.ToInt32(storedFuel, CultureInfo.InvariantCulture).ShouldBe(fuelUsageRate);

        var storedWear = await ScalarAsync(db, "SELECT tyre_wear_rate FROM sessions WHERE id = @id", ("id", request.SessionId));
        storedWear.ShouldNotBeOfType<DBNull>();
        Convert.ToInt32(storedWear, CultureInfo.InvariantCulture).ShouldBe(tyreWearRate);
    }

    [Fact]
    public async Task Two_sessions_without_a_sim_driver_id_fall_back_to_one_driver_per_name()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        // A sim that exposes no driver id at all: identity degrades to (game, display name).
        var gameVersion = DtoFactory.UniqueGameVersion();
        var playerName = $"Name Only Driver {Guid.NewGuid():N}";

        var first = DtoFactory.SessionCreateRequest() with { GameVersion = gameVersion, SimDriverId = null, PlayerName = playerName };
        var second = DtoFactory.SessionCreateRequest() with { GameVersion = gameVersion, SimDriverId = null, PlayerName = playerName };

        (await PostAsync("/api/v1/sessions", first)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PostAsync("/api/v1/sessions", second)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var db = await OpenDatabaseAsync();

        (await CountAsync(
            db,
            "SELECT count(*) FROM drivers WHERE sim_driver_id IS NULL AND display_name = @displayName",
            ("displayName", playerName)))
            .ShouldBe(1, "the name-fallback path must still collapse repeat sessions onto one driver");

        (await CountAsync(
            db,
            "SELECT count(DISTINCT driver_id) FROM sessions WHERE id IN (@first, @second)",
            ("first", first.SessionId),
            ("second", second.SessionId)))
            .ShouldBe(1);
    }

    [Fact]
    public async Task A_cars_sim_id_and_display_name_are_stored_in_their_own_columns()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        // The two must differ: posting the same string as both is what hid sim_car_id being fed the
        // display name for every row ever written.
        var simCarId = $"sim-car-{Guid.NewGuid():N}";
        var request = DtoFactory.SessionCreateRequest() with { SimCarId = simCarId, CarName = "Audi R8 LMS GT3" };

        (await PostAsync("/api/v1/sessions", request)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var db = await OpenDatabaseAsync();

        (await ScalarAsync(db, "SELECT name FROM cars WHERE sim_car_id = @simCarId", ("simCarId", simCarId)))
            .ShouldBe("Audi R8 LMS GT3", "sim_car_id identifies the car; name is the label shown for it");
    }

    [Fact]
    public async Task Renaming_a_car_updates_the_one_car_row_rather_than_forking_a_second()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        var gameVersion = DtoFactory.UniqueGameVersion();
        var simCarId = $"sim-car-{Guid.NewGuid():N}";

        var before = DtoFactory.SessionCreateRequest() with
        {
            GameVersion = gameVersion,
            SimCarId = simCarId,
            CarName = "Car Name Before Rename",
        };
        var after = DtoFactory.SessionCreateRequest() with
        {
            GameVersion = gameVersion,
            SimCarId = simCarId,
            CarName = "Car Name After Rename",
        };

        (await PostAsync("/api/v1/sessions", before)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PostAsync("/api/v1/sessions", after)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var db = await OpenDatabaseAsync();

        (await CountAsync(db, "SELECT count(*) FROM cars WHERE sim_car_id = @simCarId", ("simCarId", simCarId)))
            .ShouldBe(1, "a car renamed between sims' content updates must not fork into a second row");

        (await ScalarAsync(db, "SELECT name FROM cars WHERE sim_car_id = @simCarId", ("simCarId", simCarId)))
            .ShouldBe("Car Name After Rename");

        (await CountAsync(
            db,
            "SELECT count(DISTINCT car_id) FROM sessions WHERE id IN (@first, @second)",
            ("first", before.SessionId),
            ("second", after.SessionId)))
            .ShouldBe(1, "both sessions were driven in the same car");
    }

    [Fact]
    public async Task A_session_create_that_fails_at_the_last_step_leaves_no_reference_rows_behind()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        // PostgreSQL text cannot hold U+0000, so a car name containing one fails at the car insert —
        // by which point the game, its version, the driver, the track and the layout have each
        // already been committed by their own SaveChanges. This is the shape of the failure that
        // used to leave orphan rows behind.
        var gameVersion = DtoFactory.UniqueGameVersion();
        var playerName = $"Orphan Check Driver {Guid.NewGuid():N}";
        var trackName = $"Orphan Check Track {Guid.NewGuid():N}";

        var doomed = DtoFactory.SessionCreateRequest() with
        {
            GameVersion = gameVersion,
            PlayerName = playerName,
            SimDriverId = null,
            TrackName = trackName,
            CarName = "Rejected\0Car Name",
            SimCarId = null,
        };

        var response = await PostAsync("/api/v1/sessions", doomed);
        response.StatusCode.ShouldNotBe(HttpStatusCode.OK, "the session row cannot be written, so the request must not report success");

        await using var db = await OpenDatabaseAsync();

        (await CountAsync(db, "SELECT count(*) FROM sessions WHERE id = @id", ("id", doomed.SessionId)))
            .ShouldBe(0);
        (await CountAsync(db, "SELECT count(*) FROM game_versions WHERE connector_version = @v", ("v", gameVersion.ConnectorVersion)))
            .ShouldBe(0, "the game version resolved on the way to the failed insert must be rolled back with it");
        (await CountAsync(db, "SELECT count(*) FROM drivers WHERE display_name = @name", ("name", playerName)))
            .ShouldBe(0, "an orphan driver row is exactly the residue this transaction exists to prevent");
        (await CountAsync(db, "SELECT count(*) FROM tracks WHERE name = @name", ("name", trackName)))
            .ShouldBe(0);
    }

    [Fact]
    public async Task Malformed_extras_json_on_create_is_a_400_not_a_500()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        var gameVersion = DtoFactory.UniqueGameVersion();
        var request = DtoFactory.SessionCreateRequest() with { GameVersion = gameVersion, ExtrasJson = "{not json" };

        using var response = await PostAsync("/api/v1/sessions", request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .ShouldContain(nameof(SessionCreateRequest.ExtrasJson), Case.Insensitive);

        // Rejected before the transaction opens, so nothing was written on the way to the failure.
        await using var db = await OpenDatabaseAsync();
        (await CountAsync(db, "SELECT count(*) FROM game_versions WHERE connector_version = @v", ("v", gameVersion.ConnectorVersion))).ShouldBe(0);
    }

    [Theory]
    [InlineData("FuelUsageRate")]
    [InlineData("TyreWearRate")]
    [InlineData("SessionType")]
    public async Task A_raw_sim_code_too_large_for_smallint_is_a_400_not_a_silently_wrapped_value(string field)
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        // int.MaxValue narrows to -1 unchecked, which is indistinguishable from RaceRoom's
        // "not available" sentinel — the request has to be refused, not quietly recorded as -1.
        var gameVersion = DtoFactory.UniqueGameVersion();
        var request = DtoFactory.SessionCreateRequest() with { GameVersion = gameVersion };
        request = field switch
        {
            "FuelUsageRate" => request with { FuelUsageRate = int.MaxValue },
            "TyreWearRate" => request with { TyreWearRate = int.MaxValue },
            _ => request with { SessionType = int.MaxValue },
        };

        using var response = await PostAsync("/api/v1/sessions", request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .ShouldContain(field, Case.Insensitive);

        await using var db = await OpenDatabaseAsync();
        (await CountAsync(db, "SELECT count(*) FROM game_versions WHERE connector_version = @v", ("v", gameVersion.ConnectorVersion))).ShouldBe(0);
    }

    [Fact]
    public async Task A_raw_sim_code_at_the_smallint_boundary_is_stored_verbatim()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        // The boundary itself is legal: the range check must reject only what genuinely cannot be
        // represented, not clamp the sim's raw codes to something narrower than the column allows.
        var request = DtoFactory.SessionCreateRequest() with
        {
            FuelUsageRate = short.MaxValue,
            TyreWearRate = short.MinValue,
        };

        using var response = await PostAsync("/api/v1/sessions", request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var db = await OpenDatabaseAsync();
        (await CountAsync(
            db,
            "SELECT count(*) FROM sessions WHERE id = @id AND fuel_usage_rate = @fuel AND tyre_wear_rate = @wear",
            ("id", request.SessionId),
            ("fuel", (short)short.MaxValue),
            ("wear", (short)short.MinValue))).ShouldBe(1);
    }

    [Theory]
    [InlineData("weather")]
    [InlineData("setup")]
    [InlineData("extras")]
    public async Task Malformed_json_on_patch_is_a_400_not_a_500(string field)
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        var session = DtoFactory.SessionCreateRequest();
        (await PostAsync("/api/v1/sessions", session)).StatusCode.ShouldBe(HttpStatusCode.OK);

        const string Malformed = "{\"unterminated\": ";
        var update = new SessionUpdateRequest(
            SchemaVersion.Current,
            null,
            WeatherJson: field == "weather" ? Malformed : null,
            SetupJson: field == "setup" ? Malformed : null,
            ExtrasJson: field == "extras" ? Malformed : null);

        using var message = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/sessions/{session.SessionId}")
        {
            Content = JsonContent.Create(update),
        };
        message.Headers.Add("X-Api-Key", AspireAppFixture.ApiKey);

        using var response = await fixture.ApiClient.SendAsync(message, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "malformed client JSON is a client error, like every other rejection this endpoint makes");
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .ShouldContain(field, Case.Insensitive);
    }

    private async Task<HttpResponseMessage> PostAsync<T>(string path, T body)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        message.Headers.Add("X-Api-Key", AspireAppFixture.ApiKey);
        return await fixture.ApiClient.SendAsync(message, TestContext.Current.CancellationToken);
    }

    /// <summary>A sim driver id no other test run can collide with, so the assertions below can match on it alone.</summary>
    private static string UniqueSimDriverId() => $"sim-{Guid.NewGuid():N}";

    /// <summary>
    /// Opens a connection to the fixture's throwaway Postgres container.
    /// </summary>
    /// <remarks>
    /// The ingest API is write-only — it exposes no endpoint that reads a driver or a session back —
    /// so the driver-identity and rate assertions above have to inspect the rows themselves. The
    /// integration-test Postgres resource is deliberately given no fixed port, so the connection
    /// string comes from the fixture rather than configuration.
    /// </remarks>
    private async Task<NpgsqlConnection> OpenDatabaseAsync()
    {
        var connectionString = await fixture.GetConnectionStringAsync("raceintel", TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The 'raceintel' connection string could not be resolved from the AppHost.");

        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    /// <summary>
    /// Runs a single-value query that is expected to match a row, and fails loudly if it does not.
    /// </summary>
    /// <remarks>
    /// <see cref="NpgsqlCommand.ExecuteScalarAsync(CancellationToken)"/> returns <see langword="null"/>
    /// when no row matched but <see cref="DBNull"/> when a row matched and the column was null —
    /// two very different outcomes that <c>Convert.ToInt32</c> flattens to the same <c>0</c>. Without
    /// this guard an assertion like <c>ShouldBe(0)</c> passes just as happily when the session was
    /// never inserted at all, which is precisely the failure these tests exist to catch.
    /// </remarks>
    private static async Task<object> ScalarAsync(NpgsqlConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException($"Expected a row, but no row matched: {sql}");
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, string sql, params (string Name, object? Value)[] parameters) =>
        Convert.ToInt64(await ScalarAsync(connection, sql, parameters), CultureInfo.InvariantCulture);
}
