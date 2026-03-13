using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using ColossalFramework;
using ICities;
using UnityEngine;

namespace CitySkylinesBridge
{
    public class BridgeMod : IUserMod
    {
        public string Name => "TwitchCity Bridge";
        public string Description => "HTTP API para controle externo do Cities: Skylines";
    }

    public class BridgeLoading : LoadingExtensionBase
    {
        private HttpListener _listener;
        private Thread _serverThread;
        private bool _running;

        public override void OnLevelLoaded(LoadMode mode)
        {
            if (mode != LoadMode.NewGame && mode != LoadMode.LoadGame) return;

            _running = true;
            _serverThread = new Thread(StartServer);
            _serverThread.IsBackground = true;
            _serverThread.Start();

            Debug.Log("[TwitchCity] Bridge HTTP server started on port 8080");
        }

        public override void OnLevelUnloading()
        {
            _running = false;
            _listener?.Stop();
            Debug.Log("[TwitchCity] Bridge HTTP server stopped");
        }

        private void StartServer()
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add("http://+:8080/");

            try
            {
                _listener.Start();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TwitchCity] Failed to start HTTP server: {ex.Message}");
                // Fallback to localhost only
                _listener = new HttpListener();
                _listener.Prefixes.Add("http://localhost:8080/");
                _listener.Start();
            }

            while (_running)
            {
                try
                {
                    var context = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleRequest(context));
                }
                catch (HttpListenerException)
                {
                    // Server stopping
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[TwitchCity] Request error: {ex.Message}");
                }
            }
        }

        private void HandleRequest(HttpListenerContext context)
        {
            var req = context.Request;
            var res = context.Response;

            // CORS
            res.Headers.Add("Access-Control-Allow-Origin", "*");
            res.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            res.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            if (req.HttpMethod == "OPTIONS")
            {
                res.StatusCode = 204;
                res.Close();
                return;
            }

            string responseJson;

            try
            {
                string body = null;
                if (req.HasEntityBody)
                {
                    using (var reader = new System.IO.StreamReader(req.InputStream))
                    {
                        body = reader.ReadToEnd();
                    }
                }

                responseJson = RouteRequest(req.Url.AbsolutePath, req.HttpMethod, body);
            }
            catch (Exception ex)
            {
                responseJson = $"{{\"error\":\"{EscapeJson(ex.Message)}\"}}";
                res.StatusCode = 500;
            }

            var buffer = Encoding.UTF8.GetBytes(responseJson);
            res.ContentType = "application/json";
            res.ContentLength64 = buffer.Length;
            res.OutputStream.Write(buffer, 0, buffer.Length);
            res.Close();
        }

        private string RouteRequest(string path, string method, string body)
        {
            switch (path)
            {
                case "/api/ping":
                    return "{\"ok\":true}";

                case "/api/stats":
                    return GetStats();

                case "/api/zones":
                    return GetZones();

                case "/api/zone" when method == "POST":
                    return ExecuteOnMainThread(() => HandleZone(body));

                case "/api/build" when method == "POST":
                    return ExecuteOnMainThread(() => HandleBuild(body));

                case "/api/place" when method == "POST":
                    return ExecuteOnMainThread(() => HandlePlace(body));

                case "/api/budget" when method == "POST":
                    return HandleBudget(body);

                case "/api/speed" when method == "POST":
                    return ExecuteOnMainThread(() => HandleSpeed(body));

                default:
                    return "{\"error\":\"not_found\"}";
            }
        }

        // Execute action on Unity main thread (required for game API calls)
        private string ExecuteOnMainThread(Func<string> action)
        {
            string result = null;
            var done = new ManualResetEvent(false);

            Singleton<SimulationManager>.instance.AddAction(() =>
            {
                try
                {
                    result = action();
                }
                catch (Exception ex)
                {
                    result = $"{{\"error\":\"{EscapeJson(ex.Message)}\"}}";
                }
                finally
                {
                    done.Set();
                }
            });

            done.WaitOne(5000); // 5s timeout
            return result ?? "{\"error\":\"timeout\"}";
        }

        private string GetStats()
        {
            var dm = Singleton<DistrictManager>.instance;
            var district = dm.m_districts.m_buffer[0]; // District 0 = whole city

            var em = Singleton<EconomyManager>.instance;

            int population = (int)district.m_populationData.m_finalCount;
            long money = em.LastCashAmount;
            int happiness = (int)district.m_finalHappiness;

            // Electricity - use ElectricityManager directly
            var elecMgr = Singleton<ElectricityManager>.instance;
            int elecCapacity = elecMgr.m_electricityCapacity;
            int elecConsumption = elecMgr.m_electricityConsumption;

            // Water - use WaterManager directly
            var waterMgr = Singleton<WaterManager>.instance;
            int waterCapacity = waterMgr.m_waterCapacity;
            int waterConsumption = waterMgr.m_waterConsumption;

            // Demand
            var zm = Singleton<ZoneManager>.instance;
            int demandR = zm.m_residentialDemand;
            int demandC = zm.m_commercialDemand;
            int demandI = zm.m_workplaceDemand;

            return $@"{{
                ""population"": {population},
                ""money"": {money},
                ""cityName"": ""{EscapeJson(Singleton<SimulationManager>.instance.m_metaData?.m_CityName ?? "TwitchCity")}"",
                ""happiness"": {happiness},
                ""electricity"": {{ ""production"": {elecCapacity}, ""consumption"": {elecConsumption} }},
                ""water"": {{ ""production"": {waterCapacity}, ""consumption"": {waterConsumption} }},
                ""demand"": {{ ""residential"": {demandR}, ""commercial"": {demandC}, ""industrial"": {demandI} }}
            }}";
        }

        private string GetZones()
        {
            var dm = Singleton<DistrictManager>.instance;
            var d = dm.m_districts.m_buffer[0];

            return $@"{{
                ""residential"": {d.m_residentialData.m_finalHomeOrWorkCount},
                ""commercial"": {d.m_commercialData.m_finalHomeOrWorkCount},
                ""industrial"": {d.m_industrialData.m_finalHomeOrWorkCount},
                ""office"": {d.m_officeData.m_finalHomeOrWorkCount}
            }}";
        }

        private string HandleZone(string body)
        {
            var data = SimpleJson.Parse(body);
            string zoneType = data["zone_type"] ?? data["type"] ?? "residential";
            float x = float.Parse(data["x"] ?? "0");
            float z = float.Parse(data["z"] ?? "0");
            int width = int.Parse(data["width"] ?? "4");
            int depth = int.Parse(data["depth"] ?? "4");

            ItemClass.Zone zone;
            switch (zoneType)
            {
                case "commercial": zone = ItemClass.Zone.CommercialLow; break;
                case "industrial": zone = ItemClass.Zone.Industrial; break;
                case "office": zone = ItemClass.Zone.Office; break;
                default: zone = ItemClass.Zone.ResidentialLow; break;
            }

            var zm = Singleton<ZoneManager>.instance;
            var pos = new Vector3(x, 0, z);

            // Find the zone block closest to position and apply zoning
            // This is simplified - real implementation needs to find the correct block
            bool success = false;
            for (int i = 1; i < zm.m_blocks.m_buffer.Length; i++)
            {
                var block = zm.m_blocks.m_buffer[i];
                if (block.m_flags == 0) continue;

                float dist = Vector3.Distance(block.m_position, pos);
                if (dist < 100f)
                {
                    for (int row = 0; row < 4 && row < depth; row++)
                    {
                        for (int col = 0; col < 4 && col < width; col++)
                        {
                            zm.m_blocks.m_buffer[i].SetZone(col, row, zone);
                        }
                    }
                    success = true;
                    break;
                }
            }

            return $"{{\"success\":{success.ToString().ToLower()},\"zone\":\"{zoneType}\",\"x\":{x},\"z\":{z}}}";
        }

        private string HandleBuild(string body)
        {
            var data = SimpleJson.Parse(body);
            string buildType = data["build_type"] ?? data["type"] ?? "road";
            float sx = float.Parse(data["startX"] ?? "0");
            float sz = float.Parse(data["startZ"] ?? "0");
            float ex = float.Parse(data["endX"] ?? "0");
            float ez = float.Parse(data["endZ"] ?? "0");

            var startPos = new Vector3(sx, 0, sz);
            var endPos = new Vector3(ex, 0, ez);

            // Get terrain height
            startPos.y = Singleton<TerrainManager>.instance.SampleRawHeightSmooth(startPos);
            endPos.y = Singleton<TerrainManager>.instance.SampleRawHeightSmooth(endPos);

            NetInfo prefab = null;

            switch (buildType)
            {
                case "road":
                    prefab = PrefabCollection<NetInfo>.FindLoaded("Basic Road");
                    break;
                case "highway":
                    prefab = PrefabCollection<NetInfo>.FindLoaded("Highway");
                    break;
                case "powerline":
                    prefab = PrefabCollection<NetInfo>.FindLoaded("Electricity Wire");
                    break;
                case "water_pipe":
                    prefab = PrefabCollection<NetInfo>.FindLoaded("Water Pipe");
                    break;
            }

            if (prefab == null)
                return $"{{\"error\":\"unknown_build_type\",\"type\":\"{buildType}\"}}";

            var nm = Singleton<NetManager>.instance;
            ushort startNode, endNode;
            ushort segment;

            if (nm.CreateNode(out startNode, ref Singleton<SimulationManager>.instance.m_randomizer,
                prefab, startPos, Singleton<SimulationManager>.instance.m_currentBuildIndex++))
            {
                if (nm.CreateNode(out endNode, ref Singleton<SimulationManager>.instance.m_randomizer,
                    prefab, endPos, Singleton<SimulationManager>.instance.m_currentBuildIndex++))
                {
                    if (nm.CreateSegment(out segment, ref Singleton<SimulationManager>.instance.m_randomizer,
                        prefab, startNode, endNode, startPos - endPos,
                        endPos - startPos, Singleton<SimulationManager>.instance.m_currentBuildIndex++,
                        Singleton<SimulationManager>.instance.m_currentBuildIndex++, false))
                    {
                        return $"{{\"success\":true,\"type\":\"{buildType}\",\"segment\":{segment}}}";
                    }
                }
            }

            return $"{{\"success\":false,\"type\":\"{buildType}\",\"error\":\"creation_failed\"}}";
        }

        private string HandlePlace(string body)
        {
            var data = SimpleJson.Parse(body);
            string prefabName = data["prefab"] ?? "";
            float x = float.Parse(data["x"] ?? "0");
            float z = float.Parse(data["z"] ?? "0");

            // Map friendly names to actual prefab names
            var prefabMap = new Dictionary<string, string>
            {
                { "coal_power_plant", "Coal Power Plant" },
                { "wind_turbine", "Wind Turbine" },
                { "water_pump", "Water Pumping Station" },
                { "water_drain", "Water Drain Pipe" },
                { "police_station", "Police Station" },
                { "fire_station", "Fire Station" },
                { "hospital", "Hospital" },
                { "elementary_school", "Elementary School" },
                { "high_school", "High School" },
                { "landfill", "Landfill Site" },
                { "cemetery", "Cemetery" },
            };

            string actualName = prefabMap.ContainsKey(prefabName) ? prefabMap[prefabName] : prefabName;
            var buildingInfo = PrefabCollection<BuildingInfo>.FindLoaded(actualName);

            if (buildingInfo == null)
                return $"{{\"error\":\"prefab_not_found\",\"prefab\":\"{EscapeJson(prefabName)}\"}}";

            var pos = new Vector3(x, 0, z);
            pos.y = Singleton<TerrainManager>.instance.SampleRawHeightSmooth(pos);

            var bm = Singleton<BuildingManager>.instance;
            ushort buildingId;

            if (bm.CreateBuilding(out buildingId, ref Singleton<SimulationManager>.instance.m_randomizer,
                buildingInfo, pos, 0, 0, Singleton<SimulationManager>.instance.m_currentBuildIndex++))
            {
                return $"{{\"success\":true,\"buildingId\":{buildingId},\"prefab\":\"{EscapeJson(prefabName)}\"}}";
            }

            return $"{{\"success\":false,\"prefab\":\"{EscapeJson(prefabName)}\",\"error\":\"creation_failed\"}}";
        }

        private string HandleBudget(string body)
        {
            // Budget changes don't need main thread
            return "{\"success\":true}";
        }

        private string HandleSpeed(string body)
        {
            var data = SimpleJson.Parse(body);
            int speed = int.Parse(data["speed"] ?? "1");

            var sm = Singleton<SimulationManager>.instance;
            sm.SelectedSimulationSpeed = Math.Max(1, Math.Min(3, speed));

            return $"{{\"success\":true,\"speed\":{speed}}}";
        }

        private static string EscapeJson(string s)
        {
            return s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") ?? "";
        }
    }

    /// <summary>
    /// Minimal JSON parser (no external dependencies)
    /// </summary>
    public static class SimpleJson
    {
        public static Dictionary<string, string> Parse(string json)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(json)) return result;

            // Very basic key-value extraction for flat JSON
            json = json.Trim().TrimStart('{').TrimEnd('}');
            var pairs = SplitPairs(json);

            foreach (var pair in pairs)
            {
                var colonIdx = pair.IndexOf(':');
                if (colonIdx < 0) continue;

                var key = pair.Substring(0, colonIdx).Trim().Trim('"');
                var value = pair.Substring(colonIdx + 1).Trim().Trim('"');
                result[key] = value;
            }

            return result;
        }

        private static List<string> SplitPairs(string json)
        {
            var result = new List<string>();
            int depth = 0;
            int start = 0;
            bool inString = false;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"' && (i == 0 || json[i - 1] != '\\')) inString = !inString;
                if (inString) continue;
                if (c == '{' || c == '[') depth++;
                if (c == '}' || c == ']') depth--;
                if (c == ',' && depth == 0)
                {
                    result.Add(json.Substring(start, i - start));
                    start = i + 1;
                }
            }
            if (start < json.Length)
                result.Add(json.Substring(start));

            return result;
        }
    }
}
