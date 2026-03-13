using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
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
                    return ExecuteOnMainThread(() => HandleBudget(body));

                case "/api/speed" when method == "POST":
                    return ExecuteOnMainThread(() => HandleSpeed(body));

                case "/api/connect-power" when method == "POST":
                    return ExecuteOnMainThread(() => HandleConnectPower(body));

                case "/api/prefabs":
                    return ExecuteOnMainThread(() => ListBuildingPrefabs());

                case "/api/nets":
                    return ExecuteOnMainThread(() => ListNetPrefabs());

                case "/api/map":
                    return ExecuteOnMainThread(() => GetMapInfo());

                default:
                    return "{\"error\":\"not_found\"}";
            }
        }

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
                    Debug.LogError($"[TwitchCity] Action error: {ex}");
                }
                finally
                {
                    done.Set();
                }
            });

            done.WaitOne(10000);
            return result ?? "{\"error\":\"timeout\"}";
        }

        // ─────────────────────────────────────────────
        //  STATS
        // ─────────────────────────────────────────────

        private string GetStats()
        {
            var dm = Singleton<DistrictManager>.instance;
            var district = dm.m_districts.m_buffer[0];
            var em = Singleton<EconomyManager>.instance;
            var zm = Singleton<ZoneManager>.instance;

            long money = em.LastCashAmount;
            int happiness = (int)district.m_finalHappiness;
            int population = ReadUIntField(district.m_populationData, "m_finalCount");

            int elecCap = (int)district.GetElectricityCapacity();
            int elecCon = (int)district.GetElectricityConsumption();
            int waterCap = (int)district.GetWaterCapacity();
            int waterCon = (int)district.GetWaterConsumption();
            int sewageCap = (int)district.GetSewageCapacity();
            int sewageCon = (int)district.GetSewageAccumulation();
            int garbageAmt = (int)district.GetGarbageAmount();
            int garbageCap = (int)district.GetGarbageCapacity();
            int crimeAmt = (int)district.GetCriminalAmount();
            int crimeCap = (int)district.GetCriminalCapacity();
            int edu1 = (int)district.GetEducation1Rate();
            int edu2 = (int)district.GetEducation2Rate();
            int edu3 = (int)district.GetEducation3Rate();
            int unemployment = (int)district.GetUnemployment();
            int landValue = (int)district.GetLandValue();

            return $@"{{
                ""population"": {population},
                ""money"": {money},
                ""cityName"": ""{EscapeJson(Singleton<SimulationManager>.instance.m_metaData?.m_CityName ?? "TwitchCity")}"",
                ""happiness"": {happiness},
                ""electricity"": {{ ""production"": {elecCap}, ""consumption"": {elecCon} }},
                ""water"": {{ ""production"": {waterCap}, ""consumption"": {waterCon} }},
                ""sewage"": {{ ""capacity"": {sewageCap}, ""accumulation"": {sewageCon} }},
                ""garbage"": {{ ""amount"": {garbageAmt}, ""capacity"": {garbageCap} }},
                ""crime"": {{ ""criminals"": {crimeAmt}, ""capacity"": {crimeCap} }},
                ""education"": {{ ""elementary"": {edu1}, ""highSchool"": {edu2}, ""university"": {edu3} }},
                ""unemployment"": {unemployment},
                ""landValue"": {landValue},
                ""demand"": {{ ""residential"": {zm.m_residentialDemand}, ""commercial"": {zm.m_commercialDemand}, ""industrial"": {zm.m_workplaceDemand} }}
            }}";
        }

        private string GetZones()
        {
            var dm = Singleton<DistrictManager>.instance;
            var d = dm.m_districts.m_buffer[0];

            int res = ReadUIntField(d.m_residentialData, "m_finalHomeOrWorkCount", "m_finalCount");
            int com = ReadUIntField(d.m_commercialData, "m_finalHomeOrWorkCount", "m_finalCount");
            int ind = ReadUIntField(d.m_industrialData, "m_finalHomeOrWorkCount", "m_finalCount");
            int off = ReadUIntField(d.m_officeData, "m_finalHomeOrWorkCount", "m_finalCount");

            return $@"{{
                ""residential"": {res},
                ""commercial"": {com},
                ""industrial"": {ind},
                ""office"": {off}
            }}";
        }

        // ─────────────────────────────────────────────
        //  BUILD - Roads, pipes, power lines
        //  Connects to existing nodes when nearby!
        // ─────────────────────────────────────────────

        private string HandleBuild(string body)
        {
            var data = SimpleJson.Parse(body);
            Debug.Log($"[TwitchCity] Build request: {body}");

            string buildType = data.ContainsKey("build_type") ? data["build_type"]
                             : data.ContainsKey("type") ? data["type"] : "road";
            float sx = float.Parse(data.ContainsKey("startX") ? data["startX"] : "0");
            float sz = float.Parse(data.ContainsKey("startZ") ? data["startZ"] : "0");
            float ex = float.Parse(data.ContainsKey("endX") ? data["endX"] : "0");
            float ez = float.Parse(data.ContainsKey("endZ") ? data["endZ"] : "0");

            // Optional: auto-zone after building road
            string autoZone = data.ContainsKey("autoZone") ? data["autoZone"] : null;

            var startPos = new Vector3(sx, 0, sz);
            var endPos = new Vector3(ex, 0, ez);

            startPos.y = Singleton<TerrainManager>.instance.SampleRawHeightSmooth(startPos);
            endPos.y = Singleton<TerrainManager>.instance.SampleRawHeightSmooth(endPos);

            NetInfo prefab = FindNetPrefab(buildType);
            if (prefab == null)
                return $"{{\"error\":\"unknown_build_type\",\"type\":\"{EscapeJson(buildType)}\"}}";

            var nm = Singleton<NetManager>.instance;
            var sm = Singleton<SimulationManager>.instance;

            // Find or create start/end nodes (connects to existing!)
            bool startExisted, endExisted;
            ushort startNode = FindOrCreateNode(prefab, startPos, out startExisted);
            ushort endNode = FindOrCreateNode(prefab, endPos, out endExisted);

            if (startNode == 0 || endNode == 0)
                return $"{{\"success\":false,\"type\":\"{buildType}\",\"error\":\"node_creation_failed\"}}";

            // Use actual node positions for direction calc
            Vector3 actualStart = nm.m_nodes.m_buffer[startNode].m_position;
            Vector3 actualEnd = nm.m_nodes.m_buffer[endNode].m_position;
            Vector3 dir = (actualEnd - actualStart).normalized;

            ushort segment;
            if (nm.CreateSegment(out segment, ref sm.m_randomizer,
                prefab, startNode, endNode, dir, -dir,
                sm.m_currentBuildIndex++, sm.m_currentBuildIndex++, false))
            {
                // Generate zone blocks for roads
                if (prefab.m_class != null && prefab.m_class.m_service == ItemClass.Service.Road)
                {
                    nm.m_segments.m_buffer[segment].UpdateZones(segment);

                    // Auto-zone if requested
                    if (!string.IsNullOrEmpty(autoZone))
                    {
                        ZoneSegment(segment, ParseZoneType(autoZone));
                    }
                }

                Debug.Log($"[TwitchCity] Built {buildType}: segment={segment}, startNode={startNode}(existed={startExisted}), endNode={endNode}(existed={endExisted})");
                return $"{{\"success\":true,\"type\":\"{buildType}\",\"segment\":{segment},\"startNode\":{startNode},\"endNode\":{endNode},\"startExisted\":{startExisted.ToString().ToLower()},\"endExisted\":{endExisted.ToString().ToLower()}}}";
            }

            return $"{{\"success\":false,\"type\":\"{buildType}\",\"error\":\"segment_creation_failed\"}}";
        }

        /// <summary>
        /// Find existing node near position (same net type, within 20 units) or create new one.
        /// </summary>
        private ushort FindOrCreateNode(NetInfo prefab, Vector3 position, out bool existed)
        {
            var nm = Singleton<NetManager>.instance;
            var sm = Singleton<SimulationManager>.instance;

            // Search for existing node nearby
            ushort bestNode = 0;
            float bestDist = 20f; // snap radius

            // Use node grid or iterate buffer
            int limit = Math.Min(nm.m_nodes.m_buffer.Length, 32768);
            for (ushort i = 1; i < limit; i++)
            {
                var node = nm.m_nodes.m_buffer[i];
                if ((node.m_flags & NetNode.Flags.Created) == 0) continue;
                if ((node.m_flags & NetNode.Flags.Deleted) != 0) continue;
                if (node.Info == null) continue;

                // Match compatible network types
                bool compatible = false;
                if (prefab.m_class != null && node.Info.m_class != null)
                {
                    // Roads connect to roads, pipes to pipes, etc
                    if (prefab.m_class.m_service == node.Info.m_class.m_service)
                        compatible = true;
                    // Also allow road nodes to connect to any road type
                    if (prefab.m_class.m_service == ItemClass.Service.Road &&
                        node.Info.m_class.m_service == ItemClass.Service.Road)
                        compatible = true;
                }

                if (!compatible) continue;

                float dist = Vector3.Distance(node.m_position, position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestNode = i;
                }
            }

            if (bestNode != 0)
            {
                existed = true;
                Debug.Log($"[TwitchCity] Reusing existing node {bestNode} at dist={bestDist:F1}");
                return bestNode;
            }

            // Create new node
            existed = false;
            ushort newNode;
            if (nm.CreateNode(out newNode, ref sm.m_randomizer, prefab, position, sm.m_currentBuildIndex++))
            {
                return newNode;
            }
            return 0;
        }

        private NetInfo FindNetPrefab(string buildType)
        {
            switch (buildType)
            {
                case "road":
                    return PrefabCollection<NetInfo>.FindLoaded("Basic Road");
                case "medium_road":
                    return PrefabCollection<NetInfo>.FindLoaded("Medium Road");
                case "large_road":
                    return PrefabCollection<NetInfo>.FindLoaded("Large Road");
                case "highway":
                    return PrefabCollection<NetInfo>.FindLoaded("Highway");
                case "oneway":
                    return PrefabCollection<NetInfo>.FindLoaded("Oneway Road");
                case "powerline":
                    return PrefabCollection<NetInfo>.FindLoaded("Electricity Wire")
                        ?? PrefabCollection<NetInfo>.FindLoaded("Power Line")
                        ?? PrefabCollection<NetInfo>.FindLoaded("Powerline");
                case "water_pipe":
                    return PrefabCollection<NetInfo>.FindLoaded("Water Pipe");
                default:
                    // Try direct name
                    return PrefabCollection<NetInfo>.FindLoaded(buildType);
            }
        }

        // ─────────────────────────────────────────────
        //  ZONE - By segment or position
        // ─────────────────────────────────────────────

        private string HandleZone(string body)
        {
            var data = SimpleJson.Parse(body);
            Debug.Log($"[TwitchCity] Zone request: {body}");

            string zoneType = data.ContainsKey("zone_type") ? data["zone_type"]
                            : data.ContainsKey("type") ? data["type"] : "residential";

            ItemClass.Zone zone = ParseZoneType(zoneType);
            int zonedCount = 0;

            // Mode 1: Zone by segment ID
            if (data.ContainsKey("segment"))
            {
                ushort segId = ushort.Parse(data["segment"]);
                zonedCount = ZoneSegment(segId, zone);
                return $"{{\"success\":{(zonedCount > 0).ToString().ToLower()},\"zone\":\"{zoneType}\",\"segment\":{segId},\"cellsZoned\":{zonedCount}}}";
            }

            // Mode 2: Zone by position (find nearest segment first)
            float x = float.Parse(data.ContainsKey("x") ? data["x"] : "0");
            float z = float.Parse(data.ContainsKey("z") ? data["z"] : "0");
            float radius = float.Parse(data.ContainsKey("radius") ? data["radius"] : "100");
            var pos = new Vector3(x, 0, z);

            // Find closest road segments
            var nm = Singleton<NetManager>.instance;
            ushort[] segments = new ushort[16];
            int count;
            nm.GetClosestSegments(pos, segments, out count);

            int segmentsZoned = 0;
            for (int i = 0; i < count; i++)
            {
                var seg = nm.m_segments.m_buffer[segments[i]];
                if (seg.Info == null) continue;
                if (seg.Info.m_class == null) continue;
                if (seg.Info.m_class.m_service != ItemClass.Service.Road) continue;

                // Check distance
                float dist = Vector3.Distance(seg.m_middlePosition, pos);
                if (dist > radius) continue;

                int zoned = ZoneSegment(segments[i], zone);
                zonedCount += zoned;
                if (zoned > 0) segmentsZoned++;
            }

            return $"{{\"success\":{(zonedCount > 0).ToString().ToLower()},\"zone\":\"{zoneType}\",\"x\":{x},\"z\":{z},\"segmentsZoned\":{segmentsZoned},\"cellsZoned\":{zonedCount}}}";
        }

        /// <summary>
        /// Zone all blocks belonging to a segment.
        /// Returns number of cells zoned.
        /// </summary>
        private int ZoneSegment(ushort segId, ItemClass.Zone zone)
        {
            var nm = Singleton<NetManager>.instance;
            var zm = Singleton<ZoneManager>.instance;
            var seg = nm.m_segments.m_buffer[segId];

            if ((seg.m_flags & NetSegment.Flags.Created) == 0) return 0;

            // Ensure zone blocks exist
            seg.UpdateZones(segId);

            // Re-read after update
            seg = nm.m_segments.m_buffer[segId];

            ushort[] blockIds = new ushort[] {
                seg.m_blockStartLeft, seg.m_blockStartRight,
                seg.m_blockEndLeft, seg.m_blockEndRight
            };

            int count = 0;
            for (int b = 0; b < blockIds.Length; b++)
            {
                ushort blockId = blockIds[b];
                if (blockId == 0) continue;

                var block = zm.m_blocks.m_buffer[blockId];
                if (block.m_flags == 0) continue;

                int rows = block.RowCount;
                for (int row = 0; row < rows; row++)
                {
                    for (int col = 0; col < 4; col++)
                    {
                        if (zm.m_blocks.m_buffer[blockId].GetZone(col, row) == ItemClass.Zone.Unzoned)
                        {
                            zm.m_blocks.m_buffer[blockId].SetZone(col, row, zone);
                            count++;
                        }
                    }
                }
            }

            return count;
        }

        private ItemClass.Zone ParseZoneType(string zoneType)
        {
            switch (zoneType)
            {
                case "residential":
                case "residential_low": return ItemClass.Zone.ResidentialLow;
                case "residential_high": return ItemClass.Zone.ResidentialHigh;
                case "commercial":
                case "commercial_low": return ItemClass.Zone.CommercialLow;
                case "commercial_high": return ItemClass.Zone.CommercialHigh;
                case "industrial": return ItemClass.Zone.Industrial;
                case "office": return ItemClass.Zone.Office;
                default: return ItemClass.Zone.ResidentialLow;
            }
        }

        // ─────────────────────────────────────────────
        //  PLACE - Buildings with smart positioning
        //  Water buildings auto-snap to shore!
        // ─────────────────────────────────────────────

        private string HandlePlace(string body)
        {
            var data = SimpleJson.Parse(body);
            string prefabName = data.ContainsKey("prefab") ? data["prefab"] : "";
            float x = float.Parse(data.ContainsKey("x") ? data["x"] : "0");
            float z = float.Parse(data.ContainsKey("z") ? data["z"] : "0");
            float angle = -1f;
            if (data.ContainsKey("angle"))
                angle = float.Parse(data["angle"]);

            // Map friendly names to actual prefab names
            var prefabMap = new Dictionary<string, string>
            {
                { "coal_power_plant", "Coal Power Plant" },
                { "wind_turbine", "Wind Turbine" },
                { "water_pump", "Water Intake" },
                { "water_intake", "Water Intake" },
                { "water_drain", "Water Outlet" },
                { "water_outlet", "Water Outlet" },
                { "water_tower", "Water Tower" },
                { "water_treatment", "Water Treatment Plant" },
                { "police_station", "Police Station" },
                { "fire_station", "Fire Station" },
                { "hospital", "Hospital" },
                { "elementary_school", "Elementary School" },
                { "high_school", "High School" },
                { "landfill", "Landfill Site" },
                { "cemetery", "Cemetery" },
                { "crematory", "Crematory" },
                { "nuclear_power_plant", "Nuclear Power Plant" },
                { "solar_power_plant", "Solar Power Plant" },
                { "oil_power_plant", "Oil Power Plant" },
                { "medical_clinic", "Medical Clinic" },
                { "university", "University" },
                { "police_headquarters", "Police Headquarters" },
                { "fire_house", "Fire House" },
            };

            string actualName = prefabMap.ContainsKey(prefabName) ? prefabMap[prefabName] : prefabName;
            var buildingInfo = PrefabCollection<BuildingInfo>.FindLoaded(actualName);

            // Fallback: fuzzy match
            if (buildingInfo == null)
            {
                string lower = actualName.ToLower();
                int count = PrefabCollection<BuildingInfo>.LoadedCount();
                for (uint i = 0; i < count; i++)
                {
                    var info = PrefabCollection<BuildingInfo>.GetLoaded(i);
                    if (info != null && info.name.ToLower().Contains(lower))
                    {
                        buildingInfo = info;
                        Debug.Log($"[TwitchCity] Fuzzy matched '{prefabName}' -> '{info.name}'");
                        break;
                    }
                }
            }

            if (buildingInfo == null)
                return $"{{\"error\":\"prefab_not_found\",\"prefab\":\"{EscapeJson(prefabName)}\"}}";

            var pos = new Vector3(x, 0, z);
            bool isWaterBuilding = IsWaterShoreBuilding(buildingInfo);
            string snapInfo = "none";

            // Smart positioning for water buildings
            if (isWaterBuilding)
            {
                var tm = Singleton<TerrainManager>.instance;
                Vector3 shorePos;
                Vector3 shoreDir;
                float waterHeight;

                if (tm.GetShorePos(pos, 300f, out shorePos, out shoreDir, out waterHeight))
                {
                    pos = shorePos;
                    pos.y = tm.SampleRawHeightSmooth(pos);

                    // Calculate angle perpendicular to shore, facing water
                    if (angle < 0f)
                    {
                        Vector3 toWater = Vector3.Cross(shoreDir, Vector3.up).normalized;
                        // Verify direction points toward water
                        Vector3 testPoint = pos + toWater * 20f;
                        if (!tm.HasWater(new Vector2(testPoint.x, testPoint.z)))
                        {
                            toWater = -toWater;
                        }
                        angle = Mathf.Atan2(toWater.x, toWater.z);
                    }

                    snapInfo = "shore";
                    Debug.Log($"[TwitchCity] Snapped water building to shore: ({pos.x:F0}, {pos.z:F0}), angle={angle:F2}");
                }
                else
                {
                    // No shore found, try GetClosestWaterPos
                    Vector3 waterPos = pos;
                    if (tm.GetClosestWaterPos(ref waterPos, 300f))
                    {
                        pos = waterPos;
                        pos.y = tm.SampleRawHeightSmooth(pos);
                        snapInfo = "water_proximity";
                        Debug.Log($"[TwitchCity] Snapped water building to water proximity: ({pos.x:F0}, {pos.z:F0})");
                    }
                    else
                    {
                        snapInfo = "no_water_found";
                        Debug.LogWarning($"[TwitchCity] No water found near ({x:F0}, {z:F0}) for water building");
                    }
                }
            }

            if (angle < 0f) angle = 0f;
            pos.y = Singleton<TerrainManager>.instance.SampleRawHeightSmooth(pos);

            var bm = Singleton<BuildingManager>.instance;
            var sm = Singleton<SimulationManager>.instance;
            ushort buildingId;

            if (bm.CreateBuilding(out buildingId, ref sm.m_randomizer,
                buildingInfo, pos, angle, 0, sm.m_currentBuildIndex++))
            {
                return $"{{\"success\":true,\"buildingId\":{buildingId},\"prefab\":\"{EscapeJson(buildingInfo.name)}\",\"x\":{pos.x:F1},\"z\":{pos.z:F1},\"angle\":{angle:F2},\"snap\":\"{snapInfo}\"}}";
            }

            return $"{{\"success\":false,\"prefab\":\"{EscapeJson(prefabName)}\",\"error\":\"creation_failed\"}}";
        }

        /// <summary>
        /// Check if building needs to be placed near water (Water Intake/Outlet).
        /// Water Tower and Treatment Plant don't need shore placement.
        /// </summary>
        private bool IsWaterShoreBuilding(BuildingInfo info)
        {
            var ai = info.GetAI() as WaterFacilityAI;
            if (ai == null) return false;
            // Only intake/outlet need shore - not tower or treatment
            return !ai.m_useGroundWater && (ai.m_waterIntake > 0 || ai.m_waterOutlet > 0 || ai.m_sewageOutlet > 0);
        }

        // ─────────────────────────────────────────────
        //  BUDGET - Real implementation
        // ─────────────────────────────────────────────

        private string HandleBudget(string body)
        {
            var data = SimpleJson.Parse(body);
            string serviceName = data.ContainsKey("service") ? data["service"] : "";

            ItemClass.Service service;
            ItemClass.SubService subService;
            ParseService(serviceName, out service, out subService);

            if (service == ItemClass.Service.None)
                return $"{{\"error\":\"unknown_service\",\"service\":\"{EscapeJson(serviceName)}\"}}";

            var em = Singleton<EconomyManager>.instance;
            var sb = new StringBuilder();
            sb.Append("{\"success\":true");

            // Set budget (0-150, default 100)
            if (data.ContainsKey("budget"))
            {
                int budget = Math.Max(50, Math.Min(150, int.Parse(data["budget"])));
                em.SetBudget(service, subService, budget, false);
                em.SetBudget(service, subService, budget, true);
                sb.Append($",\"budget\":{budget}");
            }

            // Set tax rate (0-29)
            if (data.ContainsKey("taxRate"))
            {
                int rate = Math.Max(0, Math.Min(29, int.Parse(data["taxRate"])));
                em.SetTaxRate(service, subService, ItemClass.Level.Level1, rate);
                em.SetTaxRate(service, subService, ItemClass.Level.Level2, rate);
                em.SetTaxRate(service, subService, ItemClass.Level.Level3, rate);
                sb.Append($",\"taxRate\":{rate}");
            }

            sb.Append($",\"service\":\"{EscapeJson(serviceName)}\"}}");
            return sb.ToString();
        }

        private void ParseService(string name, out ItemClass.Service service, out ItemClass.SubService subService)
        {
            subService = ItemClass.SubService.None;
            switch (name)
            {
                case "electricity": service = ItemClass.Service.Electricity; break;
                case "water": service = ItemClass.Service.Water; break;
                case "garbage": service = ItemClass.Service.Garbage; break;
                case "healthcare": service = ItemClass.Service.HealthCare; break;
                case "fire": service = ItemClass.Service.FireDepartment; break;
                case "police": service = ItemClass.Service.PoliceDepartment; break;
                case "education": service = ItemClass.Service.Education; break;
                case "transport": service = ItemClass.Service.PublicTransport; break;
                case "road": service = ItemClass.Service.Road; break;
                case "residential":
                    service = ItemClass.Service.Residential;
                    subService = ItemClass.SubService.ResidentialLow;
                    break;
                case "commercial":
                    service = ItemClass.Service.Commercial;
                    subService = ItemClass.SubService.CommercialLow;
                    break;
                case "industrial":
                    service = ItemClass.Service.Industrial;
                    subService = ItemClass.SubService.IndustrialGeneric;
                    break;
                case "office":
                    service = ItemClass.Service.Office;
                    subService = ItemClass.SubService.OfficeGeneric;
                    break;
                default: service = ItemClass.Service.None; break;
            }
        }

        // ─────────────────────────────────────────────
        //  CONNECT-POWER - Auto power line
        // ─────────────────────────────────────────────

        private string HandleConnectPower(string body)
        {
            var data = SimpleJson.Parse(body);
            float toX = float.Parse(data.ContainsKey("toX") ? data["toX"] : data.ContainsKey("x") ? data["x"] : "0");
            float toZ = float.Parse(data.ContainsKey("toZ") ? data["toZ"] : data.ContainsKey("z") ? data["z"] : "0");

            var tm = Singleton<TerrainManager>.instance;
            var nm = Singleton<NetManager>.instance;
            var bm = Singleton<BuildingManager>.instance;
            var sm = Singleton<SimulationManager>.instance;

            var toPos = new Vector3(toX, 0, toZ);
            toPos.y = tm.SampleRawHeightSmooth(toPos);

            Vector3 fromPos;

            // Explicit source position or auto-find nearest power building
            if (data.ContainsKey("fromX") && data.ContainsKey("fromZ"))
            {
                fromPos = new Vector3(float.Parse(data["fromX"]), 0, float.Parse(data["fromZ"]));
            }
            else
            {
                ushort powerBuilding = bm.FindBuilding(toPos, 2000f,
                    ItemClass.Service.Electricity, ItemClass.SubService.None,
                    Building.Flags.Created, Building.Flags.Deleted);

                if (powerBuilding == 0)
                    return "{\"error\":\"no_power_source_found\"}";

                fromPos = bm.m_buildings.m_buffer[powerBuilding].m_position;
            }
            fromPos.y = tm.SampleRawHeightSmooth(fromPos);

            NetInfo powerLine = PrefabCollection<NetInfo>.FindLoaded("Electricity Wire")
                ?? PrefabCollection<NetInfo>.FindLoaded("Power Line");

            if (powerLine == null)
                return "{\"error\":\"powerline_prefab_not_found\"}";

            // Create power line with intermediate nodes if distance > 200
            float totalDist = Vector3.Distance(fromPos, toPos);
            int segments = Math.Max(1, (int)(totalDist / 200f));
            ushort prevNode = 0;
            int createdSegments = 0;

            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                Vector3 pos = Vector3.Lerp(fromPos, toPos, t);
                pos.y = tm.SampleRawHeightSmooth(pos);

                bool existed;
                ushort node = FindOrCreateNode(powerLine, pos, out existed);
                if (node == 0) continue;

                if (prevNode != 0 && prevNode != node)
                {
                    Vector3 p1 = nm.m_nodes.m_buffer[prevNode].m_position;
                    Vector3 p2 = nm.m_nodes.m_buffer[node].m_position;
                    Vector3 dir = (p2 - p1).normalized;

                    ushort seg;
                    if (nm.CreateSegment(out seg, ref sm.m_randomizer, powerLine,
                        prevNode, node, dir, -dir,
                        sm.m_currentBuildIndex++, sm.m_currentBuildIndex++, false))
                    {
                        createdSegments++;
                    }
                }
                prevNode = node;
            }

            return $"{{\"success\":{(createdSegments > 0).ToString().ToLower()},\"segments\":{createdSegments},\"distance\":{totalDist:F0}}}";
        }

        // ─────────────────────────────────────────────
        //  SPEED
        // ─────────────────────────────────────────────

        private string HandleSpeed(string body)
        {
            var data = SimpleJson.Parse(body);
            int speed = int.Parse(data.ContainsKey("speed") ? data["speed"] : "1");

            var sm = Singleton<SimulationManager>.instance;
            sm.SelectedSimulationSpeed = Math.Max(1, Math.Min(3, speed));

            return $"{{\"success\":true,\"speed\":{speed}}}";
        }

        // ─────────────────────────────────────────────
        //  PREFABS & MAP
        // ─────────────────────────────────────────────

        private string ListBuildingPrefabs()
        {
            var sb = new StringBuilder();
            sb.Append("{\"prefabs\":[");
            int count = PrefabCollection<BuildingInfo>.LoadedCount();
            bool first = true;
            for (uint i = 0; i < count; i++)
            {
                var info = PrefabCollection<BuildingInfo>.GetLoaded(i);
                if (info == null) continue;
                var service = info.GetService().ToString();
                if (service == "Electricity" || service == "Water" ||
                    service == "HealthCare" || service == "FireDepartment" ||
                    service == "PoliceDepartment" || service == "Education" ||
                    service == "Garbage" || service == "Monument" ||
                    service == "PublicTransport" || service == "Beautification")
                {
                    if (!first) sb.Append(",");
                    sb.Append($"{{\"name\":\"{EscapeJson(info.name)}\",\"service\":\"{service}\"}}");
                    first = false;
                }
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private string ListNetPrefabs()
        {
            var sb = new StringBuilder();
            sb.Append("{\"nets\":[");
            int count = PrefabCollection<NetInfo>.LoadedCount();
            bool first = true;
            for (uint i = 0; i < count; i++)
            {
                var info = PrefabCollection<NetInfo>.GetLoaded(i);
                if (info == null) continue;
                var cat = info.category ?? "";
                if (cat.Contains("Road") || cat.Contains("Electricity") || cat.Contains("Water") ||
                    info.name.Contains("Road") || info.name.Contains("Pipe") || info.name.Contains("Power") ||
                    info.name.Contains("Wire") || info.name.Contains("Highway"))
                {
                    if (!first) sb.Append(",");
                    sb.Append($"{{\"name\":\"{EscapeJson(info.name)}\",\"category\":\"{EscapeJson(cat)}\"}}");
                    first = false;
                }
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private string GetMapInfo()
        {
            var nm = Singleton<NetManager>.instance;
            var tm = Singleton<TerrainManager>.instance;
            var bm = Singleton<BuildingManager>.instance;
            var sb = new StringBuilder();

            // ── Roads ──
            sb.Append("{\"roads\":[");
            bool firstRoad = true;
            int roadNodeCount = 0;
            for (ushort i = 1; i < 32768 && i < nm.m_nodes.m_buffer.Length; i++)
            {
                var node = nm.m_nodes.m_buffer[i];
                if ((node.m_flags & NetNode.Flags.Created) == 0) continue;
                if (node.Info == null || node.Info.m_class == null) continue;
                if (node.Info.m_class.m_service != ItemClass.Service.Road) continue;

                if (!firstRoad) sb.Append(",");
                sb.Append($"{{\"id\":{i},\"x\":{node.m_position.x:F0},\"z\":{node.m_position.z:F0},\"name\":\"{EscapeJson(node.Info.name)}\"}}");
                firstRoad = false;
                roadNodeCount++;
                if (roadNodeCount >= 50) break;
            }
            sb.Append("],");

            // ── Water points (higher resolution grid) ──
            sb.Append("\"waterPoints\":[");
            bool firstWater = true;
            for (int gx = -10; gx <= 10; gx++)
            {
                for (int gz = -10; gz <= 10; gz++)
                {
                    float wx = gx * 100f;
                    float wz = gz * 100f;
                    float terrain = tm.SampleRawHeightSmooth(new Vector3(wx, 0, wz));
                    float water = tm.WaterLevel(new Vector2(wx, wz));
                    if (water > terrain + 1f)
                    {
                        if (!firstWater) sb.Append(",");
                        sb.Append($"{{\"x\":{wx:F0},\"z\":{wz:F0},\"waterLevel\":{water:F0},\"terrainHeight\":{terrain:F0}}}");
                        firstWater = false;
                    }
                }
            }
            sb.Append("],");

            // ── Shore positions (best spots for water buildings) ──
            sb.Append("\"shore\":[");
            bool firstShore = true;
            for (int gx = -8; gx <= 8; gx += 2)
            {
                for (int gz = -8; gz <= 8; gz += 2)
                {
                    Vector3 refPos = new Vector3(gx * 120f, 0, gz * 120f);
                    Vector3 shorePos;
                    Vector3 shoreDir;
                    float waterH;
                    if (tm.GetShorePos(refPos, 200f, out shorePos, out shoreDir, out waterH))
                    {
                        if (!firstShore) sb.Append(",");
                        sb.Append($"{{\"x\":{shorePos.x:F0},\"z\":{shorePos.z:F0},\"waterHeight\":{waterH:F0},\"dirX\":{shoreDir.x:F2},\"dirZ\":{shoreDir.z:F2}}}");
                        firstShore = false;
                    }
                }
            }
            sb.Append("],");

            // ── Service buildings ──
            sb.Append("\"buildings\":[");
            bool firstBuilding = true;
            int buildingCount = 0;
            for (ushort i = 1; i < 32768 && i < bm.m_buildings.m_buffer.Length; i++)
            {
                var building = bm.m_buildings.m_buffer[i];
                if ((building.m_flags & Building.Flags.Created) == 0) continue;
                if ((building.m_flags & Building.Flags.Deleted) != 0) continue;
                if (building.Info == null) continue;
                var svc = building.Info.GetService();
                if (svc != ItemClass.Service.Electricity && svc != ItemClass.Service.Water &&
                    svc != ItemClass.Service.HealthCare && svc != ItemClass.Service.FireDepartment &&
                    svc != ItemClass.Service.PoliceDepartment && svc != ItemClass.Service.Education &&
                    svc != ItemClass.Service.Garbage) continue;

                if (!firstBuilding) sb.Append(",");
                sb.Append($"{{\"id\":{i},\"name\":\"{EscapeJson(building.Info.name)}\",\"service\":\"{svc}\",\"x\":{building.m_position.x:F0},\"z\":{building.m_position.z:F0}}}");
                firstBuilding = false;
                buildingCount++;
                if (buildingCount >= 100) break;
            }
            sb.Append("]}");

            return sb.ToString();
        }

        // ─────────────────────────────────────────────
        //  UTILS
        // ─────────────────────────────────────────────

        private static int ReadUIntField(object obj, params string[] fieldNames)
        {
            if (obj == null) return 0;
            var type = obj.GetType();

            foreach (var name in fieldNames)
            {
                var field = type.GetField(name);
                if (field != null)
                {
                    try
                    {
                        var val = field.GetValue(obj);
                        if (val is uint u) return (int)u;
                        if (val is int i) return i;
                        if (val is long l) return (int)l;
                        return Convert.ToInt32(val);
                    }
                    catch { }
                }
            }

            return 0;
        }

        private static string EscapeJson(string s)
        {
            return s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") ?? "";
        }
    }

    /// <summary>
    /// Minimal JSON parser for flat objects (no external dependencies).
    /// </summary>
    public static class SimpleJson
    {
        public static Dictionary<string, string> Parse(string json)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(json)) return result;

            json = json.Trim();
            if (json.StartsWith("{")) json = json.Substring(1);
            if (json.EndsWith("}")) json = json.Substring(0, json.Length - 1);

            int i = 0;
            while (i < json.Length)
            {
                while (i < json.Length && (json[i] == ' ' || json[i] == ',' || json[i] == '\n' || json[i] == '\r' || json[i] == '\t'))
                    i++;
                if (i >= json.Length) break;

                string key = ReadValue(json, ref i);
                if (key == null) break;

                while (i < json.Length && (json[i] == ' ' || json[i] == ':'))
                    i++;
                if (i >= json.Length) break;

                string value = ReadValue(json, ref i);
                if (value != null)
                    result[key] = value;
            }

            return result;
        }

        private static string ReadValue(string json, ref int i)
        {
            while (i < json.Length && json[i] == ' ') i++;
            if (i >= json.Length) return null;

            if (json[i] == '"')
            {
                i++;
                int start = i;
                while (i < json.Length && !(json[i] == '"' && json[i - 1] != '\\'))
                    i++;
                string val = json.Substring(start, i - start);
                if (i < json.Length) i++;
                return val;
            }

            {
                int start = i;
                while (i < json.Length && json[i] != ',' && json[i] != '}' && json[i] != ' ' && json[i] != '\n')
                    i++;
                return json.Substring(start, i - start).Trim();
            }
        }
    }
}
