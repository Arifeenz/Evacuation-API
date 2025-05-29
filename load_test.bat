@echo off
echo Starting Redis Load Test...

echo.
echo === Adding 10 Evacuation Zones ===
curl -X POST http://localhost:5170/api/evacuation-zones -H "Content-Type: application/json" -d "{\"zoneId\":\"ZONE_1\",\"latitude\":13.7561,\"longitude\":100.5011,\"numberOfPeople\":110,\"urgencyLevel\":2}"
curl -X POST http://localhost:5170/api/evacuation-zones -H "Content-Type: application/json" -d "{\"zoneId\":\"ZONE_2\",\"latitude\":13.7562,\"longitude\":100.5012,\"numberOfPeople\":120,\"urgencyLevel\":3}"
curl -X POST http://localhost:5170/api/evacuation-zones -H "Content-Type: application/json" -d "{\"zoneId\":\"ZONE_3\",\"latitude\":13.7563,\"longitude\":100.5013,\"numberOfPeople\":130,\"urgencyLevel\":4}"
curl -X POST http://localhost:5170/api/evacuation-zones -H "Content-Type: application/json" -d "{\"zoneId\":\"ZONE_4\",\"latitude\":13.7564,\"longitude\":100.5014,\"numberOfPeople\":140,\"urgencyLevel\":5}"
curl -X POST http://localhost:5170/api/evacuation-zones -H "Content-Type: application/json" -d "{\"zoneId\":\"ZONE_5\",\"latitude\":13.7565,\"longitude\":100.5015,\"numberOfPeople\":150,\"urgencyLevel\":1}"
curl -X POST http://localhost:5170/api/evacuation-zones -H "Content-Type: application/json" -d "{\"zoneId\":\"ZONE_6\",\"latitude\":13.7566,\"longitude\":100.5016,\"numberOfPeople\":160,\"urgencyLevel\":2}"
curl -X POST http://localhost:5170/api/evacuation-zones -H "Content-Type: application/json" -d "{\"zoneId\":\"ZONE_7\",\"latitude\":13.7567,\"longitude\":100.5017,\"numberOfPeople\":170,\"urgencyLevel\":3}"
curl -X POST http://localhost:5170/api/evacuation-zones -H "Content-Type: application/json" -d "{\"zoneId\":\"ZONE_8\",\"latitude\":13.7568,\"longitude\":100.5018,\"numberOfPeople\":180,\"urgencyLevel\":4}"
curl -X POST http://localhost:5170/api/evacuation-zones -H "Content-Type: application/json" -d "{\"zoneId\":\"ZONE_9\",\"latitude\":13.7569,\"longitude\":100.5019,\"numberOfPeople\":190,\"urgencyLevel\":5}"
curl -X POST http://localhost:5170/api/evacuation-zones -H "Content-Type: application/json" -d "{\"zoneId\":\"ZONE_10\",\"latitude\":13.7570,\"longitude\":100.5020,\"numberOfPeople\":200,\"urgencyLevel\":1}"

echo.
echo === Adding 10 Vehicles ===
curl -X POST http://localhost:5170/api/vehicles -H "Content-Type: application/json" -d "{\"vehicleId\":\"VEH_1\",\"capacity\":35,\"type\":\"bus\",\"latitude\":13.7651,\"longitude\":100.5381,\"speed\":52}"
curl -X POST http://localhost:5170/api/vehicles -H "Content-Type: application/json" -d "{\"vehicleId\":\"VEH_2\",\"capacity\":40,\"type\":\"bus\",\"latitude\":13.7652,\"longitude\":100.5382,\"speed\":54}"
curl -X POST http://localhost:5170/api/vehicles -H "Content-Type: application/json" -d "{\"vehicleId\":\"VEH_3\",\"capacity\":45,\"type\":\"bus\",\"latitude\":13.7653,\"longitude\":100.5383,\"speed\":56}"
curl -X POST http://localhost:5170/api/vehicles -H "Content-Type: application/json" -d "{\"vehicleId\":\"VEH_4\",\"capacity\":50,\"type\":\"bus\",\"latitude\":13.7654,\"longitude\":100.5384,\"speed\":58}"
curl -X POST http://localhost:5170/api/vehicles -H "Content-Type: application/json" -d "{\"vehicleId\":\"VEH_5\",\"capacity\":55,\"type\":\"bus\",\"latitude\":13.7655,\"longitude\":100.5385,\"speed\":60}"
curl -X POST http://localhost:5170/api/vehicles -H "Content-Type: application/json" -d "{\"vehicleId\":\"VEH_6\",\"capacity\":60,\"type\":\"truck\",\"latitude\":13.7656,\"longitude\":100.5386,\"speed\":62}"
curl -X POST http://localhost:5170/api/vehicles -H "Content-Type: application/json" -d "{\"vehicleId\":\"VEH_7\",\"capacity\":65,\"type\":\"truck\",\"latitude\":13.7657,\"longitude\":100.5387,\"speed\":64}"
curl -X POST http://localhost:5170/api/vehicles -H "Content-Type: application/json" -d "{\"vehicleId\":\"VEH_8\",\"capacity\":70,\"type\":\"truck\",\"latitude\":13.7658,\"longitude\":100.5388,\"speed\":66}"
curl -X POST http://localhost:5170/api/vehicles -H "Content-Type: application/json" -d "{\"vehicleId\":\"VEH_9\",\"capacity\":75,\"type\":\"helicopter\",\"latitude\":13.7659,\"longitude\":100.5389,\"speed\":68}"
curl -X POST http://localhost:5170/api/vehicles -H "Content-Type: application/json" -d "{\"vehicleId\":\"VEH_10\",\"capacity\":80,\"type\":\"helicopter\",\"latitude\":13.7660,\"longitude\":100.5390,\"speed\":70}"

echo.
echo === Testing Evacuation Plan Creation ===
curl -X POST http://localhost:5170/api/evacuations/plan

echo.
echo === Checking Status ===
curl http://localhost:5170/api/evacuations/status

echo.
echo === Load Test Complete ===
echo Check the API logs for performance metrics!