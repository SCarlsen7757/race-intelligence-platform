using RaceIntelligence.Connectors.RaceRoom.Interop;

namespace RaceIntelligence.Connectors.RaceRoom.Tests.Support;

/// <summary>
/// Fluent builder for one entry of RaceRoom's <c>all_drivers_data_1</c> array. Defaults every
/// documented "-1 = N/A" field to its N/A value, matching <see cref="R3ESharedRawBuilder"/>, so a
/// test only sets what it is actually asserting on.
/// </summary>
internal sealed class R3EDriverDataBuilder
{
    private R3EDriverData _driver = CreateDefault();

    private static R3EDriverData CreateDefault()
    {
        R3EDriverData driver = default;

        driver.DriverInfo.CarNumber = -1;
        driver.DriverInfo.ClassId = -1;
        driver.DriverInfo.ModelId = -1;
        driver.DriverInfo.TeamId = -1;
        driver.DriverInfo.LiveryId = -1;
        driver.DriverInfo.ManufacturerId = -1;
        driver.DriverInfo.UserId = -1;
        driver.DriverInfo.SlotId = -1;
        driver.DriverInfo.ClassPerformanceIndex = -1;
        driver.DriverInfo.EngineType = -1;

        driver.FinishStatus = -1;
        driver.Place = -1;
        driver.PlaceClass = -1;
        driver.LapDistance = -1f;
        driver.LapDistanceFraction = -1f;
        driver.TrackSector = 0;
        driver.CompletedLaps = -1;
        driver.CurrentLapValid = -1;
        driver.LapTimeCurrentSelf = -1f;
        SetAllThree(ref driver.SectorTimeCurrentSelf, -1f);
        SetAllThree(ref driver.SectorTimePreviousSelf, -1f);
        SetAllThree(ref driver.SectorTimeBestSelf, -1f);
        driver.TimeDeltaFront = -1f;
        driver.TimeDeltaBehind = -1f;
        driver.PitStopStatus = -1;
        driver.InPitlane = -1;
        driver.NumPitstops = -1;
        driver.CarSpeed = -1f;
        driver.TireTypeFront = -1;
        driver.TireTypeRear = -1;
        driver.TireSubtypeFront = -1;
        driver.TireSubtypeRear = -1;
        driver.DrsState = -1;
        driver.PtpState = -1;
        driver.VirtualEnergy = -1f;
        driver.PenaltyType = -1;
        driver.EngineState = -1;

        driver.Penalties.DriveThrough = -1f;
        driver.Penalties.StopAndGo = -1f;
        driver.Penalties.PitStop = -1f;
        driver.Penalties.TimeDeduction = -1f;
        driver.Penalties.SlowDown = -1f;

        return driver;
    }

    private static void SetAllThree(ref Float3 field, float value)
    {
        field[0] = value;
        field[1] = value;
        field[2] = value;
    }

    public R3EDriverDataBuilder WithName(string name)
    {
        _driver.DriverInfo.Name = R3ESharedRawBuilder.EncodeName(name);
        return this;
    }

    public R3EDriverDataBuilder WithUserId(int userId)
    {
        _driver.DriverInfo.UserId = userId;
        return this;
    }

    public R3EDriverDataBuilder WithSlotId(int slotId)
    {
        _driver.DriverInfo.SlotId = slotId;
        return this;
    }

    public R3EDriverDataBuilder WithCarNumber(int carNumber)
    {
        _driver.DriverInfo.CarNumber = carNumber;
        return this;
    }

    public R3EDriverDataBuilder WithCarIds(int modelId, int classId, int manufacturerId)
    {
        _driver.DriverInfo.ModelId = modelId;
        _driver.DriverInfo.ClassId = classId;
        _driver.DriverInfo.ManufacturerId = manufacturerId;
        return this;
    }

    public R3EDriverDataBuilder WithPlace(int place, int placeClass)
    {
        _driver.Place = place;
        _driver.PlaceClass = placeClass;
        return this;
    }

    public R3EDriverDataBuilder WithLapProgress(int completedLaps, float lapDistanceFraction, int trackSector)
    {
        _driver.CompletedLaps = completedLaps;
        _driver.LapDistanceFraction = lapDistanceFraction;
        _driver.TrackSector = trackSector;
        return this;
    }

    public R3EDriverDataBuilder WithSpeed(float metersPerSecond)
    {
        _driver.CarSpeed = metersPerSecond;
        return this;
    }

    /// <summary>Sets the lap in progress: its elapsed time, cumulative splits so far, and validity.</summary>
    public R3EDriverDataBuilder WithCurrentLap(float lapTimeSeconds, float sector1, float sector2, float sector3, int currentLapValid = 1)
    {
        _driver.LapTimeCurrentSelf = lapTimeSeconds;
        _driver.SectorTimeCurrentSelf[0] = sector1;
        _driver.SectorTimeCurrentSelf[1] = sector2;
        _driver.SectorTimeCurrentSelf[2] = sector3;
        _driver.CurrentLapValid = currentLapValid;
        return this;
    }

    /// <summary>Sets the most recently completed lap's cumulative splits. The third is the lap total.</summary>
    public R3EDriverDataBuilder WithPreviousLap(float sector1, float sector2, float sector3)
    {
        _driver.SectorTimePreviousSelf[0] = sector1;
        _driver.SectorTimePreviousSelf[1] = sector2;
        _driver.SectorTimePreviousSelf[2] = sector3;
        return this;
    }

    /// <summary>Sets the best lap's cumulative splits. The third is the lap total.</summary>
    public R3EDriverDataBuilder WithBestLap(float sector1, float sector2, float sector3)
    {
        _driver.SectorTimeBestSelf[0] = sector1;
        _driver.SectorTimeBestSelf[1] = sector2;
        _driver.SectorTimeBestSelf[2] = sector3;
        return this;
    }

    public R3EDriverDataBuilder WithGaps(float toCarAhead, float toCarBehind)
    {
        _driver.TimeDeltaFront = toCarAhead;
        _driver.TimeDeltaBehind = toCarBehind;
        return this;
    }

    public R3EDriverDataBuilder WithPitState(int inPitlane, int pitStopStatus, int numPitstops)
    {
        _driver.InPitlane = inPitlane;
        _driver.PitStopStatus = pitStopStatus;
        _driver.NumPitstops = numPitstops;
        return this;
    }

    public R3EDriverDataBuilder WithFinishStatus(int finishStatus)
    {
        _driver.FinishStatus = finishStatus;
        return this;
    }

    /// <summary>Sets pending cut-track penalties. <c>-1</c> means that type is not pending.</summary>
    public R3EDriverDataBuilder WithPenalties(float driveThrough = -1f, float stopAndGo = -1f, float pitStop = -1f, float timeDeduction = -1f, float slowDown = -1f)
    {
        _driver.Penalties.DriveThrough = driveThrough;
        _driver.Penalties.StopAndGo = stopAndGo;
        _driver.Penalties.PitStop = pitStop;
        _driver.Penalties.TimeDeduction = timeDeduction;
        _driver.Penalties.SlowDown = slowDown;
        return this;
    }

    public R3EDriverData Build() => _driver;
}
