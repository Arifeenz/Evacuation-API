using Microsoft.OpenApi.Models;
using StackExchange.Redis;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Add logging ไว้ดูว่าการทำงานถูกต้องมั้ย
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Add Redis เชื่อมต่อกับ Redis ถ้ามี env ใช้ Redis Cloud ถ้าไม่มีก็ใช้ Redis Local
builder.Services.AddSingleton<IConnectionMultiplexer>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<Program>>();
    try
    {
        ConfigurationOptions config;
        
        var redisCloudHost = Environment.GetEnvironmentVariable("REDIS_CLOUD_HOST");
        var redisCloudPassword = Environment.GetEnvironmentVariable("REDIS_CLOUD_PASSWORD");
        
        if (!string.IsNullOrEmpty(redisCloudHost) && !string.IsNullOrEmpty(redisCloudPassword))
        {
            // Redis Cloud
            config = new ConfigurationOptions
            {
                EndPoints = { { redisCloudHost, 18884 } },
                User = "default",
                Password = redisCloudPassword,
                AbortOnConnectFail = false
            };
            logger.LogInformation("🔴 Connecting to Redis Cloud");
        }
        else
        {
            // Redis Local
            config = ConfigurationOptions.Parse("localhost:6379,abortConnect=false");
            logger.LogInformation("🔴 Connecting to local Redis");
        }
        
        var connection = ConnectionMultiplexer.Connect(config);
        logger.LogInformation("✅ Redis connected successfully");
        return connection;
    }
    catch (Exception ex)
    {
        logger.LogWarning("⚠️ Redis connection failed: {Error}. API will continue with degraded functionality.", ex.Message);
        var config = ConfigurationOptions.Parse("localhost:6379,abortConnect=false");
        return ConnectionMultiplexer.Connect(config);
    }
});

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Get logger and Redis
var logger = app.Services.GetRequiredService<ILogger<Program>>();
var redis = app.Services.GetRequiredService<IConnectionMultiplexer>();
var db = redis.GetDatabase();

logger.LogInformation("🚀 Evacuation Planning API Starting...");

try
{
    logger.LogInformation("🔴 Redis connection: {ConnectionStatus}", redis.IsConnected ? "Connected" : "Disconnected");
}
catch (Exception ex)
{
    logger.LogWarning("⚠️ Unable to check Redis status: {Error}", ex.Message);
}

// Redis Keys สำหรับเก็บข้อมูลแต่ละประเภท
const string ZONES_KEY = "evacuation:zones";
const string VEHICLES_KEY = "evacuation:vehicles";
const string ZONE_STATUS_KEY = "evacuation:zone_status";
const string VEHICLE_STATUS_KEY = "evacuation:vehicle_status";
const string ACTIVE_ASSIGNMENTS_KEY = "evacuation:active_assignments";

// ค่าคงที่สำหรับควบคุมระบบ
const double MAX_REASONABLE_DISTANCE = 50.0; // km
const int MAX_REASONABLE_TRIPS = 10; // trips per vehicle
const double CAPACITY_WARNING_THRESHOLD = 5.0; // trips

app.MapOpenApi();
app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Redirect("/swagger"));

app.UseHttpsRedirection();

// Helper methods for Redis operations สำหรับจัดการข้อมูล Redis
async Task<List<T>> GetListFromRedis<T>(string key)
{
    try
    {
        var json = await db.StringGetAsync(key);
        if (!json.HasValue) return new List<T>();
        
        return JsonSerializer.Deserialize<List<T>>(json!) ?? new List<T>();
    }
    catch (RedisException ex)
    {
        logger.LogError("❌ Redis error in GetListFromRedis: {Error}", ex.Message);
        return new List<T>(); // Return empty list as fallback
    }
    catch (Exception ex)
    {
        logger.LogError("❌ Unexpected error in GetListFromRedis: {Error}", ex.Message);
        return new List<T>();
    }
}

async Task SetListToRedis<T>(string key, List<T> data)
{
    try
    {
        var json = JsonSerializer.Serialize(data);
        await db.StringSetAsync(key, json);
    }
    catch (RedisException ex)
    {
        logger.LogError("❌ Redis error in SetListToRedis: {Error}", ex.Message);
        throw;
    }
    catch (Exception ex)
    {
        logger.LogError("❌ Unexpected error in SetListToRedis: {Error}", ex.Message);
        throw;
    }
}

async Task<Dictionary<string, T>> GetDictionaryFromRedis<T>(string key)
{
    try
    {
        var json = await db.StringGetAsync(key);
        if (!json.HasValue) return new Dictionary<string, T>();
        
        return JsonSerializer.Deserialize<Dictionary<string, T>>(json!) ?? new Dictionary<string, T>();
    }
    catch (RedisException ex)
    {
        logger.LogError("❌ Redis error in GetDictionaryFromRedis: {Error}", ex.Message);
        return new Dictionary<string, T>(); // Return empty dictionary as fallback
    }
    catch (Exception ex)
    {
        logger.LogError("❌ Unexpected error in GetDictionaryFromRedis: {Error}", ex.Message);
        return new Dictionary<string, T>();
    }
}

async Task SetDictionaryToRedis<T>(string key, Dictionary<string, T> data)
{
    var json = JsonSerializer.Serialize(data);
    await db.StringSetAsync(key, json);
}

// =============================================================================
// Evacuation API endpoints
// =============================================================================
// POST /api/evacuation-zones
app.MapPost("/api/evacuation-zones", async (EvacuationZone zone) =>
{
    logger.LogInformation("➕ Adding evacuation zone: {ZoneId} with {People} people, urgency level {Urgency}", 
        zone.ZoneId, zone.NumberOfPeople, zone.UrgencyLevel);
    
    var evacuationZones = await GetListFromRedis<EvacuationZone>(ZONES_KEY);
    var zoneStatus = await GetDictionaryFromRedis<EvacuationStatus>(ZONE_STATUS_KEY);
    
    evacuationZones.Add(zone);
    
    zoneStatus[zone.ZoneId] = new EvacuationStatus(
        zone.ZoneId,
        zone.NumberOfPeople,
        0,
        zone.NumberOfPeople,
        null
    );
    
    await SetListToRedis(ZONES_KEY, evacuationZones);
    await SetDictionaryToRedis(ZONE_STATUS_KEY, zoneStatus);
    
    logger.LogInformation("✅ Zone {ZoneId} added to Redis. Total zones: {TotalZones}", 
        zone.ZoneId, evacuationZones.Count);
    
    return Results.Ok(new { 
        message = "Evacuation zone added successfully", 
        zone,
        status = zoneStatus[zone.ZoneId],
        totalZones = evacuationZones.Count,
        storedIn = "Redis"
    });
})
.WithName("AddEvacuationZone")
.WithSummary("Add evacuation zone information");

// POST /api/vehicles
app.MapPost("/api/vehicles", async (Vehicle vehicle) =>
{
    logger.LogInformation("🚐 Adding vehicle: {VehicleId} ({Type}) with capacity {Capacity}", 
        vehicle.VehicleId, vehicle.Type, vehicle.Capacity);
    
    var vehicles = await GetListFromRedis<Vehicle>(VEHICLES_KEY);
    var vehicleStatuses = await GetDictionaryFromRedis<string>(VEHICLE_STATUS_KEY);
    
    vehicles.Add(vehicle);
    vehicleStatuses[vehicle.VehicleId] = "Available";
    
    await SetListToRedis(VEHICLES_KEY, vehicles);
    await SetDictionaryToRedis(VEHICLE_STATUS_KEY, vehicleStatuses);
    
    logger.LogInformation("✅ Vehicle {VehicleId} added to Redis. Total vehicles: {TotalVehicles}", 
        vehicle.VehicleId, vehicles.Count);
    
    return Results.Ok(new { 
        message = "Vehicle added successfully", 
        vehicle,
        status = vehicleStatuses[vehicle.VehicleId],
        totalVehicles = vehicles.Count,
        storedIn = "Redis"
    });
})
.WithName("AddVehicle")
.WithSummary("Add vehicle information");

// GET /api/evacuation-zones
app.MapGet("/api/evacuation-zones", async () =>
{
    var evacuationZones = await GetListFromRedis<EvacuationZone>(ZONES_KEY);
    
    logger.LogInformation("📋 Retrieved {ZoneCount} evacuation zones from Redis", evacuationZones.Count);
    
    return Results.Ok(new {
        zones = evacuationZones,
        totalCount = evacuationZones.Count,
        source = "Redis"
    });
})
.WithName("GetEvacuationZones")
.WithSummary("Get all evacuation zones");

// GET /api/vehicles
app.MapGet("/api/vehicles", async () =>
{
    var vehicles = await GetListFromRedis<Vehicle>(VEHICLES_KEY);
    var vehicleStatuses = await GetDictionaryFromRedis<string>(VEHICLE_STATUS_KEY);
    
    var vehiclesWithStatus = vehicles.Select(v => new {
        v.VehicleId,
        v.Capacity,
        v.Type,
        v.Latitude,
        v.Longitude,
        v.Speed,
        Status = vehicleStatuses.GetValueOrDefault(v.VehicleId, "Available")
    }).ToList();
    
    logger.LogInformation("🚗 Retrieved {Total} vehicles from Redis. Available: {Available}, In Use: {InUse}", 
        vehiclesWithStatus.Count,
        vehiclesWithStatus.Count(v => v.Status == "Available"),
        vehiclesWithStatus.Count(v => v.Status == "InUse"));
    
    return Results.Ok(new {
        vehicles = vehiclesWithStatus,
        totalCount = vehicles.Count,
        availableCount = vehiclesWithStatus.Count(v => v.Status == "Available"),
        inUseCount = vehiclesWithStatus.Count(v => v.Status == "InUse"),
        source = "Redis"
    });
})
.WithName("GetVehicles")
.WithSummary("Get all vehicles");

// GET /api/evacuations/status
app.MapGet("/api/evacuations/status", async () =>
{
    var evacuationZones = await GetListFromRedis<EvacuationZone>(ZONES_KEY);
    var zoneStatus = await GetDictionaryFromRedis<EvacuationStatus>(ZONE_STATUS_KEY);
    
    var statusList = new List<EvacuationStatus>();
    
    foreach (var zone in evacuationZones)
    {
        if (zoneStatus.ContainsKey(zone.ZoneId))
        {
            statusList.Add(zoneStatus[zone.ZoneId]);
        }
        else
        {
            var initialStatus = new EvacuationStatus(
                zone.ZoneId,
                zone.NumberOfPeople,
                0,
                zone.NumberOfPeople,
                null
            );
            zoneStatus[zone.ZoneId] = initialStatus;
            statusList.Add(initialStatus);
        }
    }
    
    if (statusList.Count > zoneStatus.Count - statusList.Count)
    {
        await SetDictionaryToRedis(ZONE_STATUS_KEY, zoneStatus);
    }
    
    var totalPeople = statusList.Sum(s => s.TotalPeople);
    var totalEvacuated = statusList.Sum(s => s.Evacuated);
    var totalRemaining = statusList.Sum(s => s.Remaining);
    
    logger.LogInformation("📊 Status from Redis - Total people: {Total}, Evacuated: {Evacuated}, Remaining: {Remaining}", 
        totalPeople, totalEvacuated, totalRemaining);
    
    return Results.Ok(new {
        zones = statusList,
        totalZones = statusList.Count,
        totalPeople,
        totalEvacuated,
        totalRemaining,
        source = "Redis"
    });
})
.WithName("GetEvacuationStatus")
.WithSummary("Get current status of all evacuation zones");

// POST /api/evacuations/plan
app.MapPost("/api/evacuations/plan", async () =>
{
    logger.LogInformation("📋 Creating evacuation plan using Redis data...");
    var startTime = DateTime.Now;
    
    var vehicles = await GetListFromRedis<Vehicle>(VEHICLES_KEY);
    var evacuationZones = await GetListFromRedis<EvacuationZone>(ZONES_KEY);
    var vehicleStatuses = await GetDictionaryFromRedis<string>(VEHICLE_STATUS_KEY);
    var zoneStatus = await GetDictionaryFromRedis<EvacuationStatus>(ZONE_STATUS_KEY);
    var activeAssignments = await GetListFromRedis<VehicleAssignment>(ACTIVE_ASSIGNMENTS_KEY);
    
    var assignments = new List<EvacuationAssignment>();
    var availableVehicles = vehicles.Where(v => 
        vehicleStatuses.GetValueOrDefault(v.VehicleId, "Available") == "Available"
    ).ToList();
    
    logger.LogInformation("🔍 Found {AvailableVehicles} available vehicles out of {TotalVehicles}", 
        availableVehicles.Count, vehicles.Count);
    
    // ERROR HANDLING 1: No available vehicles
    if (!availableVehicles.Any())
    {
        logger.LogWarning("⚠️ No available vehicles for evacuation planning");
        return Results.BadRequest(new {
            error = "No available vehicles for evacuation",
            totalVehicles = vehicles.Count,
            inUseVehicles = vehicles.Count(v => vehicleStatuses.GetValueOrDefault(v.VehicleId) == "InUse"),
            suggestion = "Wait for vehicles to complete current assignments or add more vehicles"
        });
    }
    
    // Calculate all Vehicle-Zone combinations with distance filtering
    var vehicleZonePairs = new List<VehicleZonePair>();
    var distanceRejections = new List<object>();
    
    foreach (var vehicle in availableVehicles)
    {
        foreach (var zone in evacuationZones)
        {
            var currentStatus = zoneStatus.GetValueOrDefault(zone.ZoneId);
            var remainingPeople = currentStatus?.Remaining ?? zone.NumberOfPeople;
            
            if (remainingPeople <= 0) continue;
            
            var distance = CalculateDistance(vehicle.Latitude, vehicle.Longitude, zone.Latitude, zone.Longitude);
            
            // ERROR HANDLING 2: Distance too far
            if (distance > MAX_REASONABLE_DISTANCE)
            {
                distanceRejections.Add(new {
                    VehicleId = vehicle.VehicleId,
                    ZoneId = zone.ZoneId,
                    Distance = Math.Round(distance, 2),
                    MaxAllowed = MAX_REASONABLE_DISTANCE,
                    Reason = "Vehicle too far from evacuation zone"
                });
                continue;
            }
            
            var eta = CalculateETA(distance, vehicle.Speed);
            
            vehicleZonePairs.Add(new VehicleZonePair
            {
                Vehicle = vehicle,
                Zone = zone,
                Distance = distance,
                ETA = eta,
                RemainingPeople = remainingPeople
            });
        }
    }
    
    logger.LogInformation("🎯 Found {ValidPairs} valid vehicle-zone pairs after distance filtering", 
        vehicleZonePairs.Count);
    
    // ลำดับความสำคัญ : Distance > Urgency > Capacity
    var sortedPairs = vehicleZonePairs
        .OrderBy(p => Math.Round(p.ETA, 1))
        .ThenByDescending(p => p.Zone.UrgencyLevel)
        .ThenByDescending(p => p.Vehicle.Capacity)
        .ToList();
    
    logger.LogInformation("🧮 Applied Priority Hierarchy Algorithm: Distance > Urgency > Capacity");
    
    // Assign vehicles
    var assignedVehicles = new HashSet<string>();
    var capacityWarnings = new List<object>();
    
    foreach (var pair in sortedPairs)
    {
        if (assignedVehicles.Contains(pair.Vehicle.VehicleId)) continue;
        
        var currentStatus = zoneStatus.GetValueOrDefault(pair.Zone.ZoneId);
        var currentRemaining = currentStatus?.Remaining ?? pair.Zone.NumberOfPeople;
        
        var alreadyPlanned = assignments
            .Where(a => a.ZoneId == pair.Zone.ZoneId)
            .Sum(a => a.NumberOfPeople);
        
        var actualRemaining = currentRemaining - alreadyPlanned;
        if (actualRemaining <= 0) continue;
        
        var peopleToEvacuate = Math.Min(actualRemaining, pair.Vehicle.Capacity);
        
        // ERROR HANDLING: Capacity warnings
        var estimatedTrips = Math.Ceiling((double)actualRemaining / pair.Vehicle.Capacity);
        if (estimatedTrips > CAPACITY_WARNING_THRESHOLD)
        {
            var severity = estimatedTrips > MAX_REASONABLE_TRIPS ? "Critical" : "Warning";
            capacityWarnings.Add(new {
                ZoneId = pair.Zone.ZoneId,
                VehicleId = pair.Vehicle.VehicleId,
                RemainingPeople = actualRemaining,
                VehicleCapacity = pair.Vehicle.Capacity,
                EstimatedTrips = (int)estimatedTrips,
                Severity = severity,
                Recommendation = severity == "Critical" 
                    ? "Request additional large vehicles or helicopter support"
                    : "Consider sending additional vehicles to reduce trip count"
            });
        }
        
        assignments.Add(new EvacuationAssignment(
            pair.Zone.ZoneId,
            pair.Vehicle.VehicleId,
            Math.Round(pair.ETA, 2),
            peopleToEvacuate
        ));
        
        activeAssignments.Add(new VehicleAssignment(
            pair.Zone.ZoneId,
            pair.Vehicle.VehicleId,
            DateTime.Now
        ));
        
        vehicleStatuses[pair.Vehicle.VehicleId] = "InUse";
        assignedVehicles.Add(pair.Vehicle.VehicleId);
        
        logger.LogInformation("✅ Assigned vehicle {VehicleId} to zone {ZoneId} - ETA: {ETA} min, People: {People}", 
            pair.Vehicle.VehicleId, pair.Zone.ZoneId, Math.Round(pair.ETA, 1), peopleToEvacuate);
    }
    
    // Save updated data to Redis
    await SetDictionaryToRedis(VEHICLE_STATUS_KEY, vehicleStatuses);
    await SetListToRedis(ACTIVE_ASSIGNMENTS_KEY, activeAssignments);
    
    var plan = new EvacuationPlan(assignments, DateTime.Now);
    var executionTime = (DateTime.Now - startTime).TotalMilliseconds;
    
    logger.LogInformation("🎯 Evacuation plan created and saved to Redis! Assignments: {AssignmentCount}, Execution time: {ExecutionTime} ms", 
        assignments.Count, Math.Round(executionTime, 2));
    
    if (capacityWarnings.Any())
    {
        logger.LogWarning("⚠️ Capacity warnings detected: {WarningCount}", capacityWarnings.Count);
    }
    
    return Results.Ok(new {
        plan,
        totalAssignments = assignments.Count,
        algorithmUsed = "Priority Hierarchy: Distance > Urgency > Capacity",
        executionTimeMs = Math.Round(executionTime, 2),
        dataSource = "Redis",
        redisConnection = redis.IsConnected ? "Connected" : "Disconnected",
        capacityWarnings = capacityWarnings.Any() ? capacityWarnings : null,
        distanceRejections = distanceRejections.Any() ? distanceRejections : null
    });
})
.WithName("CreateEvacuationPlan")
.WithSummary("Generate evacuation plan with Redis persistence");

// PUT /api/evacuations/update
app.MapPut("/api/evacuations/update", async (UpdateRequest request) =>
{
    logger.LogInformation("🔄 Updating evacuation status - Zone: {ZoneId}, Vehicle: {VehicleId}, People: {People}", 
        request.ZoneId, request.VehicleId, request.NumberEvacuated);
    
    // ดึงข้อมูลจาก Redis
    var zoneStatus = await GetDictionaryFromRedis<EvacuationStatus>(ZONE_STATUS_KEY);
    var activeAssignments = await GetListFromRedis<VehicleAssignment>(ACTIVE_ASSIGNMENTS_KEY);
    var vehicleStatuses = await GetDictionaryFromRedis<string>(VEHICLE_STATUS_KEY);
    var vehicles = await GetListFromRedis<Vehicle>(VEHICLES_KEY);
    
    // Validation: ตรวจสอบว่า zone มีอยู่จริง
    if (!zoneStatus.ContainsKey(request.ZoneId))
    {
        logger.LogWarning("❌ Zone {ZoneId} not found", request.ZoneId);
        return Results.BadRequest(new {
            error = "Zone not found",
            zoneId = request.ZoneId,
            availableZones = zoneStatus.Keys.ToList(),
            suggestion = "Check zone ID or add the zone first"
        });
    }
    
    // Validation: ตรวจสอบจำนวนคนที่อพยพ
    if (request.NumberEvacuated <= 0)
    {
        logger.LogWarning("❌ Invalid evacuation count: {Count}", request.NumberEvacuated);
        return Results.BadRequest(new {
            error = "Invalid evacuation count",
            requested = request.NumberEvacuated,
            suggestion = "Number of evacuated people must be greater than 0"
        });
    }
    
    // Validation: ตรวจสอบว่ายานพาหนะถูก assign ให้ zone นี้จริง
    var assignment = activeAssignments.FirstOrDefault(a => 
        a.ZoneId == request.ZoneId && a.VehicleId == request.VehicleId);
    
    if (assignment == null)
    {
        logger.LogWarning("❌ Vehicle {VehicleId} not assigned to zone {ZoneId}", 
            request.VehicleId, request.ZoneId);
        return Results.BadRequest(new {
            error = "Vehicle not assigned to this zone",
            vehicleId = request.VehicleId,
            zoneId = request.ZoneId,
            activeAssignments = activeAssignments.Where(a => a.ZoneId == request.ZoneId).Select(a => a.VehicleId).ToList(),
            suggestion = "Check if vehicle ID is correct or if assignment exists"
        });
    }
    
    var currentStatus = zoneStatus[request.ZoneId];
    var vehicle = vehicles.FirstOrDefault(v => v.VehicleId == request.VehicleId);
    
    // Smart evacuation calculation - ปรับจำนวนอัตโนมัติ
    var actualEvacuated = request.NumberEvacuated;
    var adjustmentReasons = new List<string>();
    
    // จำกัดตาม vehicle capacity
    if (vehicle != null && actualEvacuated > vehicle.Capacity)
    {
        actualEvacuated = vehicle.Capacity;
        adjustmentReasons.Add($"Limited by vehicle capacity ({vehicle.Capacity})");
    }
    
    // จำกัดตามจำนวนคนที่เหลือ
    if (actualEvacuated > currentStatus.Remaining)
    {
        actualEvacuated = currentStatus.Remaining;
        adjustmentReasons.Add($"Limited by remaining people ({currentStatus.Remaining})");
    }
    
    var newEvacuated = currentStatus.Evacuated + actualEvacuated;
    var newRemaining = currentStatus.Remaining - actualEvacuated;
    
    // อัพเดต zone status
    zoneStatus[request.ZoneId] = new EvacuationStatus(
        request.ZoneId,
        currentStatus.TotalPeople,
        newEvacuated,
        newRemaining,
        request.VehicleId
    );

    // ปล่อยยานพาหนะให้พร้อมใช้งานใหม่
    vehicleStatuses[request.VehicleId] = "Available";
    activeAssignments.RemoveAll(a => a.ZoneId == request.ZoneId && a.VehicleId == request.VehicleId);
    
    // บันทึกข้อมูลกลับไป Redis
    await SetDictionaryToRedis(ZONE_STATUS_KEY, zoneStatus);
    await SetDictionaryToRedis(VEHICLE_STATUS_KEY, vehicleStatuses);
    await SetListToRedis(ACTIVE_ASSIGNMENTS_KEY, activeAssignments);
    
    var completionPercentage = Math.Round((double)newEvacuated / currentStatus.TotalPeople * 100, 1);
    
    logger.LogInformation("✅ Status updated successfully! Zone {ZoneId}: {Evacuated}/{Total} people ({Percentage}%), Vehicle {VehicleId} now available", 
        request.ZoneId, newEvacuated, currentStatus.TotalPeople, completionPercentage, request.VehicleId);
    
    if (adjustmentReasons.Any())
    {
        logger.LogInformation("ℹ️ Smart adjustments applied: {Adjustments}", string.Join(", ", adjustmentReasons));
    }
    
    return Results.Ok(new {
        message = "Evacuation status updated successfully",
        requested = request.NumberEvacuated,
        actualEvacuated = actualEvacuated,
        adjustmentReasons = adjustmentReasons.Any() ? adjustmentReasons : null,
        updatedStatus = zoneStatus[request.ZoneId],
        vehicleStatus = $"Vehicle {request.VehicleId} is now Available",
        completionPercentage
    });
})
.WithName("UpdateEvacuationStatus")
.WithSummary("Update evacuation status with smart adjustment logic");

// DELETE /api/evacuations/clear
app.MapDelete("/api/evacuations/clear", async () =>
{
    logger.LogInformation("🗑️ Clearing all evacuation data from Redis...");
    
    var keys = new[] { ZONES_KEY, VEHICLES_KEY, ZONE_STATUS_KEY, VEHICLE_STATUS_KEY, ACTIVE_ASSIGNMENTS_KEY };
    
    foreach (var key in keys)
    {
        await db.KeyDeleteAsync(key);
    }
    
    logger.LogInformation("✅ All Redis data cleared successfully");
    
    return Results.Ok(new {
        message = "All evacuation data cleared successfully",
        clearedAt = DateTime.Now,
        source = "Redis",
        keysCleared = keys.Length
    });
})
.WithName("ClearEvacuationData")
.WithSummary("Clear all evacuation plans and reset Redis data");

logger.LogInformation("🎯 All API endpoints configured successfully with Redis integration");

app.Run();

// Distance Calculation using Haversine Formula
static double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
{
    const double R = 6371;
    var dLat = ToRadians(lat2 - lat1);
    var dLon = ToRadians(lon2 - lon1);
    var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
            Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
            Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
    var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    return R * c;
}

static double ToRadians(double degrees) => degrees * Math.PI / 180;

static double CalculateETA(double distance, double speed) => distance / speed * 60;

// Models โครงสร้างข้อมูล
public record EvacuationZone(
    string ZoneId,
    double Latitude,
    double Longitude,
    int NumberOfPeople,
    int UrgencyLevel
);

public record Vehicle(
    string VehicleId,
    int Capacity,
    string Type,
    double Latitude,
    double Longitude,
    double Speed
);

public record EvacuationAssignment(
    string ZoneId,
    string VehicleId,
    double ETA,
    int NumberOfPeople
);

public record EvacuationPlan(
    List<EvacuationAssignment> Assignments,
    DateTime CreatedAt
);

public record EvacuationStatus(
    string ZoneId,
    int TotalPeople,
    int Evacuated,
    int Remaining,
    string? LastVehicleUsed
);

public record UpdateRequest(
    string ZoneId,
    int NumberEvacuated,
    string VehicleId
);

public record VehicleAssignment(
    string ZoneId,
    string VehicleId,
    DateTime AssignedAt
);

public class VehicleZonePair
{
    public required Vehicle Vehicle { get; set; }
    public required EvacuationZone Zone { get; set; }
    public double Distance { get; set; }
    public double ETA { get; set; }
    public int RemainingPeople { get; set; }
}