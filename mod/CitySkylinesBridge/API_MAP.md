# Cities: Skylines 1 - Mapa Completo de APIs (via Reflection)

## 1. SINGLETON MANAGERS (46 total)

Todos acessados via `Singleton<XxxManager>.instance`:

| Manager | BaseType | Uso |
|---------|----------|-----|
| **SimulationManager** | Singleton | Main thread, randomizer, build index, game time |
| **NetManager** | SimulationManagerBase | Estradas, nodes, segments, lanes |
| **BuildingManager** | SimulationManagerBase | Prédios, criação, destruição |
| **ZoneManager** | SimulationManagerBase | Zoning blocks |
| **TerrainManager** | SimulationManagerBase | Terreno, altura, água |
| **WaterManager** | SimulationManagerBase | Rede de água/esgoto |
| **ElectricityManager** | SimulationManagerBase | Rede elétrica |
| **DistrictManager** | SimulationManagerBase | Distritos, políticas, stats |
| **EconomyManager** | SimulationManagerBase | Dinheiro, impostos, orçamento |
| **TransportManager** | SimulationManagerBase | Linhas de transporte público |
| **CitizenManager** | SimulationManagerBase | Cidadãos |
| **VehicleManager** | SimulationManagerBase | Veículos |
| **PathManager** | SimulationManagerBase | Pathfinding |
| **TreeManager** | SimulationManagerBase | Árvores |
| **PropManager** | SimulationManagerBase | Props decorativos |
| **GameAreaManager** | SimulationManagerBase | Tiles desbloqueados do mapa |
| **NaturalResourceManager** | SimulationManagerBase | Recursos naturais (oil, ore, etc) |
| **DisasterManager** | SimulationManagerBase | Desastres |
| **TransferManager** | SimulationManagerBase | Transferências de recursos |
| **CoverageManager** | SimulationManagerBase | Cobertura de serviços |
| **ImmaterialResourceManager** | SimulationManagerBase | Recursos imateriais (educação, etc) |
| **StatisticsManager** | SimulationManagerBase | Estatísticas da cidade |
| **UnlockManager** | SimulationManagerBase | Milestones |
| **EventManager** | SimulationManagerBase | Eventos da cidade |
| **NotificationManager** | SimulationManagerBase | Notificações |
| **AudioManager** | SimulationManagerBase | Áudio |
| **EffectManager** | SimulationManagerBase | Efeitos visuais |
| **InfoManager** | SimulationManagerBase | Info overlays |
| **WeatherManager** | SimulationManagerBase | Clima |
| **ToolManager** | SimulationManagerBase | Ferramentas do jogador |
| **RenderManager** | SimulationManagerBase | Rendering |
| **InstanceManager** | SimulationManagerBase | Instâncias globais |
| **MessageManager** | SimulationManagerBase | Mensagens do jogo |
| **GuideManager** | SimulationManagerBase | Tutorial/guide |
| **LoadingManager** | Singleton | Loading de saves/maps |

---

## 2. SIMULATIONMANAGER - Thread Principal

```csharp
// Executar código na main thread (OBRIGATÓRIO pra qualquer API do jogo)
Singleton<SimulationManager>.instance.AddAction(() => {
    // código aqui roda na simulation thread
});

// AddAction com retorno
Singleton<SimulationManager>.instance.AddAction("name", () => { ... });

// Campos importantes
sm.m_currentBuildIndex      // uint - build index (incrementar ao criar)
sm.m_randomizer             // Randomizer - RNG do jogo
sm.m_currentGameTime        // DateTime
sm.m_currentTickIndex       // uint
sm.m_currentFrameIndex      // uint
sm.m_metaData               // SimulationMetaData (m_CityName, etc)
sm.m_simulationTimeDelta    // float
sm.SimulationPaused         // bool get/set
sm.SelectedSimulationSpeed  // int get/set (1-3)
sm.FinalSimulationSpeed     // int get
sm.m_isNightTime            // bool
sm.m_enableDayNight          // bool
```

---

## 3. NETMANAGER - Estradas, Pipes, Power Lines

### Criar Nodes e Segments

```csharp
var nm = Singleton<NetManager>.instance;
var sm = Singleton<SimulationManager>.instance;

// Criar um node
ushort nodeId;
nm.CreateNode(out nodeId, ref sm.m_randomizer, netInfo, position, sm.m_currentBuildIndex++);

// Criar um segment conectando 2 nodes
ushort segmentId;
nm.CreateSegment(out segmentId, ref sm.m_randomizer, netInfo,
    startNode, endNode,
    startDirection, endDirection,  // Vector3 - direção de saída
    sm.m_currentBuildIndex++,
    sm.m_currentBuildIndex++,
    false); // invert

// Mover node
nm.MoveNode(nodeId, newPosition);

// Deletar
nm.ReleaseNode(nodeId);
nm.ReleaseSegment(segmentId, keepNodes: false);
```

### Buscar Nodes/Segments Existentes

```csharp
// Buffer de nodes (Array16<NetNode>)
nm.m_nodes.m_buffer[nodeId]     // NetNode struct
nm.m_segments.m_buffer[segId]   // NetSegment struct
nm.m_nodeCount                  // int
nm.m_segmentCount               // int

// Encontrar segments perto de um ponto
ushort[] segments = new ushort[16];
int count;
nm.GetClosestSegments(position, segments, out count);

// Node entre dois segments
ushort node = nm.GetNodeBetweenSegments(seg1, seg2);

// Raycast para encontrar node/segment mais próximo
Vector3 hit;
ushort nodeIdx, segIdx;
nm.RayCast(..., out hit, out nodeIdx, out segIdx);
```

### NetNode Struct (campos)
```
Vector3  m_position          // posição world
Flags    m_flags             // None/Created/End/Middle/Junction/etc
ushort   m_segment0..m_segment7  // até 8 segments conectados
ushort   m_building          // building associado (se houver)
ushort   m_infoIndex         // index do NetInfo
byte     m_elevation         // elevação
byte     m_connectCount      // quantos segments conectados

// Métodos
node.CountSegments()            // int - quantos segments conectados
node.GetSegment(index)          // ushort - segment no index
node.IsConnectedTo(otherNode)   // bool
node.Info                       // NetInfo get/set
```

### NetNode.Flags (enum - bitmask)
```
None, Created, Deleted, Original, Disabled, End, Middle, Bend,
Junction, Moveable, Untouchable, Outside, Temporary, Double,
Fixed, OnGround, Water, Sewage, Underground, Transition,
LevelCrossing, OneWayOut, TrafficLights, OneWayIn,
Heating, Electricity, Collapsed
```

### NetSegment Struct (campos)
```
ushort  m_startNode, m_endNode   // nodes nas pontas
Vector3 m_startDirection         // direção do segment na ponta start
Vector3 m_endDirection           // direção na ponta end
Vector3 m_middlePosition         // posição do meio
Flags   m_flags                  // bitmask
ushort  m_blockStartLeft/Right   // zone blocks associados
ushort  m_blockEndLeft/Right     // zone blocks associados
ushort  m_infoIndex              // NetInfo index
uint    m_lanes                  // first lane ID

// Métodos úteis
segment.Info                     // NetInfo get
segment.GetClosestPosition(point) // Vector3
segment.GetClosestPositionAndDirection(point, out pos, out dir)
segment.FindDirection(segID, nodeID) // Vector3 - direção saindo do node
segment.UpdateZones(segID)       // ATUALIZA zone blocks após criar road!
segment.IsStraight()             // bool
segment.GetLeftSegment(nodeID)   // ushort
segment.GetRightSegment(nodeID)  // ushort
segment.GetOtherNode(nodeID)     // ushort - node do outro lado
segment.GetClosestZoneBlock(point, out distSq, out blockId) // encontra zone block!
segment.CalculateCorner(segID, heightOffset, start, leftSide, out pos, out dir, out smooth)
segment.GenerateBezier(segID, startNodeID) // Bezier3 - curva do segment
```

### NetSegment.Flags
```
None, Created, Deleted, Original, Collapsed, Invert, End, Bend,
StopRight, StopLeft, Blocked, Flooded, HeavyBan, BikeBan, CarBan
```

### Conectar Estrada a Node Existente

**IMPORTANTE**: Para conectar uma nova estrada a um node existente, use o nodeId existente como `startNode` ou `endNode` no `CreateSegment`. Não crie um novo node na mesma posição!

```csharp
// 1. Encontrar node mais próximo
ushort closestNode = 0;
float minDist = float.MaxValue;
for (ushort i = 1; i < nm.m_nodes.m_buffer.Length; i++) {
    var node = nm.m_nodes.m_buffer[i];
    if ((node.m_flags & NetNode.Flags.Created) == 0) continue;
    if (node.Info == null) continue;
    float dist = Vector3.Distance(node.m_position, targetPos);
    if (dist < minDist) {
        minDist = dist;
        closestNode = i;
    }
}

// 2. Criar segment conectando ao node existente
ushort newEndNode;
nm.CreateNode(out newEndNode, ref sm.m_randomizer, roadInfo, endPos, sm.m_currentBuildIndex++);
Vector3 dir = (endPos - nm.m_nodes.m_buffer[closestNode].m_position).normalized;
nm.CreateSegment(out segId, ref sm.m_randomizer, roadInfo,
    closestNode, newEndNode, dir, -dir, sm.m_currentBuildIndex++, sm.m_currentBuildIndex++, false);
```

---

## 4. PREFABS - NetInfo e BuildingInfo

### Encontrar Prefabs

```csharp
// Por nome exato
NetInfo road = PrefabCollection<NetInfo>.FindLoaded("Basic Road");
BuildingInfo building = PrefabCollection<BuildingInfo>.FindLoaded("Coal Power Plant");

// Iterar todos
int count = PrefabCollection<NetInfo>.LoadedCount();
for (uint i = 0; i < count; i++) {
    var info = PrefabCollection<NetInfo>.GetLoaded(i);
    if (info != null) {
        string name = info.name;
        string category = info.category;
    }
}
```

### Nomes Comuns de NetInfo
```
"Basic Road"              // estrada simples 2 lanes
"Medium Road"             // 4 lanes
"Large Road"              // 6 lanes
"Highway"                 // highway
"Oneway Road"             // mão única
"Basic Road Elevated"     // elevada
"Basic Road Bridge"       // ponte
"Highway Ramp"            // rampa de highway
"Water Pipe"              // cano de água
"Electricity Wire"        // linha de energia (tentar "Power Line" se falhar)
```

### Nomes Comuns de BuildingInfo
```
// Energia
"Coal Power Plant"
"Wind Turbine"
"Oil Power Plant"
"Nuclear Power Plant"
"Solar Power Plant"

// Água
"Water Intake"            // bombear água (na beira do rio!)
"Water Outlet"            // saída de esgoto (na beira do rio!)
"Water Tower"             // torre de água (qualquer lugar)
"Water Treatment Plant"

// Serviços
"Police Station"
"Fire Station"
"Hospital"
"Medical Clinic"
"Elementary School"
"High School"
"University"
"Landfill Site"
"Cemetery"
"Crematory"
```

---

## 5. ZONEMANAGER - Zoning Programático

### Estrutura

```csharp
var zm = Singleton<ZoneManager>.instance;

// Zone blocks são criados automaticamente quando uma estrada é construída!
// Cada segment de estrada cria zone blocks nas laterais

// Buffer de zone blocks
zm.m_blocks.m_buffer[blockId]  // ZoneBlock struct
zm.m_blockCount                // int

// Demanda
zm.m_residentialDemand         // int 0-100
zm.m_commercialDemand          // int 0-100
zm.m_workplaceDemand           // int 0-100
```

### ZoneBlock Struct
```csharp
// Campos
block.m_position     // Vector3 - posição do block
block.m_angle        // float - ângulo (radianos)
block.m_flags        // uint - 0 = inválido
block.m_segment      // ushort - segment de estrada associado
block.m_valid        // ulong - bitmask de cells válidas
block.m_occupied1    // ulong - cells ocupadas por prédios
block.m_zone1        // ulong - zona do primeiro conjunto
block.m_zone2        // ulong - zona do segundo conjunto

// Métodos
block.RowCount       // int get/set - quantas rows (1-4)
block.SetZone(x, z, zone)  // define zona numa cell (x=0-3, z=0-3)
block.GetZone(x, z)        // ItemClass.Zone - zona atual
block.IsOccupied1(x, z)    // bool - tem prédio?
block.RefreshZoning(blockId) // recalcula
block.PointDistanceSq(point, minDistSq) // float - distância ao ponto
```

### ItemClass.Zone (enum)
```
Unzoned, Distant, ResidentialLow, ResidentialHigh,
CommercialLow, CommercialHigh, Industrial, Office, None
```

### Como Zonear Corretamente

```csharp
// 1. PRIMEIRO construa a estrada (CreateNode + CreateSegment)
// 2. DEPOIS chame UpdateZones no segment para criar os zone blocks
nm.m_segments.m_buffer[segId].UpdateZones(segId);

// 3. Espere 1 frame para os blocks serem criados

// 4. Encontre os zone blocks adjacentes à estrada
var pos = new Vector3(x, 0, z);
for (int i = 1; i < zm.m_blocks.m_buffer.Length; i++) {
    var block = zm.m_blocks.m_buffer[i];
    if (block.m_flags == 0) continue;

    float dist = Vector3.Distance(block.m_position, pos);
    if (dist < 80f) { // zone blocks ficam perto da estrada
        // Cada block tem grid 4x4 cells
        int rows = block.RowCount;  // geralmente 4
        for (int row = 0; row < rows; row++) {
            for (int col = 0; col < 4; col++) {
                block.SetZone(col, row, ItemClass.Zone.ResidentialLow);
            }
        }
        zm.m_blocks.m_buffer[i] = block; // write back (é struct!)
    }
}

// 5. ALTERNATIVA mais precisa: use o segment para encontrar seus blocks
var seg = nm.m_segments.m_buffer[segId];
// Os blocks estão em:
ushort blockStartLeft = seg.m_blockStartLeft;
ushort blockStartRight = seg.m_blockStartRight;
ushort blockEndLeft = seg.m_blockEndLeft;
ushort blockEndRight = seg.m_blockEndRight;

// Zone cada block
if (blockStartLeft != 0) {
    for (int r = 0; r < zm.m_blocks.m_buffer[blockStartLeft].RowCount; r++)
        for (int c = 0; c < 4; c++)
            zm.m_blocks.m_buffer[blockStartLeft].SetZone(c, r, zone);
}
// Repetir para os outros 3 blocks...
```

### Criar Zone Block manualmente (raro, mas possível)

```csharp
ushort blockId;
zm.CreateBlock(out blockId, ref sm.m_randomizer, segmentId, position, angle, rows, distance, sm.m_currentBuildIndex++);
```

---

## 6. TERRAINMANAGER - Terreno e Detecção de Água

```csharp
var tm = Singleton<TerrainManager>.instance;
```

### Altura do Terreno

```csharp
// Smooth height (mais precisa, interpolada)
float height = tm.SampleRawHeightSmooth(position);   // Vector3
float height = tm.SampleRawHeightSmooth(x, z);        // floats

// Altura com água (retorna o max entre terreno e água)
float h = tm.SampleRawHeightSmoothWithWater(position, timeLerp: false, waterOffset: 0f);

// Block height (terreno editável)
float h = tm.SampleBlockHeightSmooth(position);

// Detail height (com decorações)
float h = tm.SampleDetailHeightSmooth(position);
```

### Detecção de Água (ESSENCIAL para water_pump/water_outlet!)

```csharp
// Verificar se tem água num ponto
bool hasWater = tm.HasWater(new Vector2(x, z));

// Nível da água
float waterLevel = tm.WaterLevel(new Vector2(x, z));

// Dados completos: terreno, água, velocidade, normal
float terrainH, waterH;
Vector3 velocity, normal;
bool hasWater = tm.SampleWaterData(new Vector2(x, z), out terrainH, out waterH, out velocity, out normal);

// Verificar cobertura de água numa área
int water, shore, pollution;
tm.CountWaterCoverage(position, maxDistance: 100f, out water, out shore, out pollution);

// *** ENCONTRAR POSIÇÃO NA BEIRA DO RIO ***
Vector3 shorePos;
Vector3 shoreDir;
float waterHeight;
bool found = tm.GetShorePos(referencePos, maxDistance: 200f, out shorePos, out shoreDir, out waterHeight);

// *** ENCONTRAR POSIÇÃO MAIS PRÓXIMA COM ÁGUA ***
Vector3 waterPos = someStartPos;
bool found = tm.GetClosestWaterPos(ref waterPos, maxDistance: 200f);

// Calcular fluxo de água (para posicionar water intake upstream)
Vector3 flowPos, flowDir;
bool hasFlow = tm.CalculateWaterFlow(segment, radius: 100f, out flowPos, out flowDir);

// Proximidade de água com velocidade
float velocity2;
float proximity = tm.CalculateWaterProximity(position, radius: 50f, out velocity2);
```

### Para Posicionar Water Intake/Outlet Corretamente

```csharp
// 1. Encontrar a beira do rio mais próxima
Vector3 shorePos;
Vector3 shoreDir;
float waterH;
if (tm.GetShorePos(desiredPos, 300f, out shorePos, out shoreDir, out waterH)) {
    // shorePos = posição na margem
    // shoreDir = direção ao longo da margem
    // waterH = nível da água

    // 2. O prédio precisa estar um pouco dentro da água
    // WaterFacilityAI tem m_waterLocationOffset e m_maxWaterDistance
    Vector3 buildPos = shorePos;
    buildPos.y = tm.SampleRawHeightSmooth(buildPos);

    // 3. Calcular ângulo: o prédio deve "olhar" para a água
    float angle = Mathf.Atan2(shoreDir.x, shoreDir.z);

    // 4. Criar o prédio
    bm.CreateBuilding(out id, ref sm.m_randomizer, waterIntakeInfo, buildPos, angle, 0, sm.m_currentBuildIndex++);
}
```

---

## 7. WATERMANAGER - Rede de Água/Esgoto

```csharp
var wm = Singleton<WaterManager>.instance;
```

### Métodos

```csharp
// Verificar se tem água/esgoto numa posição
bool water, sewage;
byte waterPollution;
wm.CheckWater(position, out water, out sewage, out waterPollution);

// Verificar aquecimento
bool heating;
wm.CheckHeating(position, out heating);

// Tentar buscar/despejar água (usado internamente pelos buildings)
int fetched = wm.TryFetchWater(nodeOrPos, rate, max, out pollution);
int dumped = wm.TryDumpWater(node, rate, max, waterPollution);
int dumped = wm.TryDumpSewage(nodeOrPos, rate, max);

// Atualizar grid após mudanças
wm.UpdateGrid(minX, minZ, maxX, maxZ);
```

### WaterFacilityAI - Campos do Prefab

```csharp
// Acessar a AI do BuildingInfo
var ai = waterIntakeInfo.GetAI() as WaterFacilityAI;
ai.m_waterIntake        // int - capacidade de captação
ai.m_waterOutlet        // int - capacidade de saída
ai.m_sewageOutlet       // int - capacidade de esgoto
ai.m_waterStorage       // int - armazenamento
ai.m_maxWaterDistance    // float - distância máxima da água
ai.m_waterLocationOffset // Vector3 - offset do ponto de captação
ai.m_useGroundWater     // bool - se usa água subterrânea
ai.m_outletPollution    // int - poluição gerada
ai.m_pumpingVehicles    // int - veículos de bombeamento
```

---

## 8. ELECTRICITYMANAGER - Rede Elétrica

```csharp
var em = Singleton<ElectricityManager>.instance;
```

### Métodos

```csharp
// Verificar se tem eletricidade numa posição
bool hasElectricity;
em.CheckElectricity(position, out hasElectricity);

// Verificar condutividade (se a posição pode conduzir eletricidade)
bool conductive = em.CheckConductivity(position);

// Tentar produzir/consumir eletricidade
int dumped = em.TryDumpElectricity(position, rate, max);
int fetched = em.TryFetchElectricity(position, rate, max);

// Atualizar grid
em.UpdateGrid(minX, minZ, maxX, maxZ);
em.AreaModified(minX, minZ, maxX, maxZ);
```

### Como a Eletricidade Funciona

A rede elétrica propaga automaticamente:
1. **Estradas** conduzem eletricidade (zona de ~3 tiles ao redor)
2. **Power lines** (`Electricity Wire`) conduzem eletricidade a longas distâncias
3. **Prédios** conectados à estrada recebem eletricidade automaticamente

Para conectar áreas distantes, basta criar um segment de `Electricity Wire` entre elas.

```csharp
// Conectar eletricidade entre duas áreas
var powerLineInfo = PrefabCollection<NetInfo>.FindLoaded("Electricity Wire");
if (powerLineInfo == null) powerLineInfo = PrefabCollection<NetInfo>.FindLoaded("Power Line");

ushort node1, node2, seg;
nm.CreateNode(out node1, ref sm.m_randomizer, powerLineInfo, posA, sm.m_currentBuildIndex++);
nm.CreateNode(out node2, ref sm.m_randomizer, powerLineInfo, posB, sm.m_currentBuildIndex++);
Vector3 dir = (posB - posA).normalized;
nm.CreateSegment(out seg, ref sm.m_randomizer, powerLineInfo,
    node1, node2, dir, -dir, sm.m_currentBuildIndex++, sm.m_currentBuildIndex++, false);
```

---

## 9. BUILDINGMANAGER - Prédios

```csharp
var bm = Singleton<BuildingManager>.instance;
```

### Criar e Gerenciar Prédios

```csharp
// Criar prédio
ushort buildingId;
bm.CreateBuilding(out buildingId, ref sm.m_randomizer, buildingInfo,
    position, angle, length, sm.m_currentBuildIndex++);

// Deletar prédio
bm.ReleaseBuilding(buildingId);

// Mover prédio
bm.RelocateBuilding(buildingId, newPosition, newAngle);

// Upgrade prédio
bm.UpgradeBuilding(buildingId);

// Encontrar prédio mais próximo por serviço
ushort found = bm.FindBuilding(position, maxDistance: 100f,
    ItemClass.Service.Water, ItemClass.SubService.None,
    Building.Flags.Created, Building.Flags.Deleted);

// Buffer de prédios
bm.m_buildings.m_buffer[buildingId]  // Building struct
bm.m_buildingCount

// Eventos
bm.EventBuildingCreated += handler;
bm.EventBuildingReleased += handler;
```

### Building Struct (campos-chave)

```csharp
building.m_position       // Vector3
building.m_angle          // float (radianos)
building.m_flags          // Building.Flags
building.m_netNode        // ushort - node de rede conectado
building.m_waterSource    // ushort - source de água (para water facilities)
building.Info             // BuildingInfo
building.m_electricityBuffer  // ushort
building.m_waterBuffer    // ushort
building.m_sewageBuffer   // ushort
building.m_productionRate // byte (0-255)
building.m_happiness      // byte
building.m_health         // byte
building.m_level          // byte
building.m_width          // byte
building.m_length         // byte
building.m_citizenCount   // byte
building.m_accessSegment  // ushort - segment de acesso à estrada

// Métodos úteis
building.CheckZoning(zone1, zone2, allowCollapsed)  // bool
building.CalculateMeshPosition()  // Vector3
building.FindSegment(service, subService, layers) // ushort - segment conectado
```

### BuildingAI Subclasses Importantes

| AI Class | Base | Uso |
|----------|------|-----|
| PowerPlantAI | PlayerBuildingAI | Usinas (coal, oil, nuclear) |
| WindTurbineAI | PowerPlantAI | Turbinas eólicas |
| SolarPowerPlantAI | PowerPlantAI | Painéis solares |
| DamPowerHouseAI | PowerPlantAI | Hidrelétrica |
| WaterFacilityAI | PlayerBuildingAI | Water intake/outlet/tower |
| HospitalAI | PlayerBuildingAI | Hospitais |
| SchoolAI | PlayerBuildingAI | Escolas |
| PoliceStationAI | PlayerBuildingAI | Delegacias |
| FireStationAI | PlayerBuildingAI | Bombeiros |
| LandfillSiteAI | PlayerBuildingAI | Lixões |
| CemeteryAI | PlayerBuildingAI | Cemitérios |
| MonumentAI | PlayerBuildingAI | Monumentos |
| DepotAI | PlayerBuildingAI | Depósitos de transporte |
| CargoStationAI | PlayerBuildingAI | Estações de carga |
| WarehouseAI | PlayerBuildingAI | Armazéns |
| PrivateBuildingAI | CommonBuildingAI | Prédios privados (res/com/ind) |

### PowerPlantAI - Campos

```csharp
var ppAI = buildingInfo.GetAI() as PowerPlantAI;
ppAI.m_electricityProduction   // int - produção de eletricidade
ppAI.m_resourceType            // TransferReason - tipo de recurso (Coal, Oil, etc)
ppAI.m_resourceCapacity        // int
ppAI.m_resourceConsumption     // int
ppAI.m_pollutionAccumulation   // int
ppAI.m_pollutionRadius         // float
ppAI.m_noiseAccumulation       // int
ppAI.m_isRenewable             // bool
ppAI.m_constructionCost        // int
ppAI.m_maintenanceCost         // int
```

---

## 10. DISTRICTMANAGER - Stats da Cidade

```csharp
var dm = Singleton<DistrictManager>.instance;
var district = dm.m_districts.m_buffer[0]; // 0 = cidade inteira
```

### District Struct - Stats Disponíveis

```csharp
// Capacidades
district.GetElectricityCapacity()
district.GetWaterCapacity()
district.GetSewageCapacity()
district.GetGarbageCapacity()
district.GetHealCapacity()
district.GetEducation1Capacity()  // elementary
district.GetEducation2Capacity()  // high school
district.GetEducation3Capacity()  // university
district.GetCriminalCapacity()

// Consumos
district.GetElectricityConsumption()
district.GetWaterConsumption()
district.GetSewageAccumulation()
district.GetGarbageAccumulation()
district.GetIncomeAccumulation()

// Stats sociais
district.GetWorkerCount()
district.GetWorkplaceCount()
district.GetUnemployment()
district.GetLandValue()
district.GetGroundPollution()
district.GetDeadCount()
district.GetSickCount()
district.GetCriminalAmount()
district.GetExportAmount()
district.GetImportAmount()

// Educação
district.GetEducation1Rate()
district.GetEducation2Rate()
district.GetEducation3Rate()

// Demanda
district.CalculateResidentialDemandOffset()
district.CalculateCommercialDemandOffset()
district.CalculateWorkplaceDemandOffset()
district.CalculateIndustrialDemandOffset()
district.CalculateOfficeDemandOffset()

// Indústria
district.GetGenericIndustryArea()
district.GetFarmingIndustryArea()
district.GetForestryIndustryArea()
district.GetOreIndustryArea()
district.GetOilIndustryArea()
```

### Políticas

```csharp
dm.IsCityPolicySet(DistrictPolicies.Policies.SomePolicy)
dm.SetCityPolicy(DistrictPolicies.Policies.SomePolicy)
dm.UnsetCityPolicy(DistrictPolicies.Policies.SomePolicy)

// Criar distrito
byte districtId;
dm.CreateDistrict(out districtId);
dm.SetDistrictName(districtId, "Meu Distrito");

// Qual distrito está num ponto
byte district = dm.GetDistrict(worldPos);
```

---

## 11. ECONOMYMANAGER - Dinheiro e Orçamento

```csharp
var em = Singleton<EconomyManager>.instance;
```

### Dinheiro

```csharp
long money = em.LastCashAmount;       // dinheiro atual
long delta = em.LastCashDelta;        // variação
long start = em.StartMoney;          // dinheiro inicial

// Empréstimos
int count = em.CountLoans();
EconomyManager.Loan loan;
em.GetLoan(0, out loan);
```

### Orçamento e Impostos

```csharp
// Budget (0-150, default 100)
int budget = em.GetBudget(ItemClass.Service.Water, ItemClass.SubService.None, night: false);
em.SetBudget(ItemClass.Service.Water, ItemClass.SubService.None, 100, night: false);

// Impostos (0-29)
int tax = em.GetTaxRate(ItemClass.Service.Residential, ItemClass.SubService.ResidentialLow, ItemClass.Level.Level1);
em.SetTaxRate(ItemClass.Service.Residential, ItemClass.SubService.ResidentialLow, ItemClass.Level.Level1, 12);

// Receita/Despesa
long income, expenses;
em.GetIncomeAndExpenses(ItemClass.Service.Residential, ItemClass.SubService.None, ItemClass.Level.None, out income, out expenses);

// Adicionar/remover dinheiro
em.AddResource(EconomyManager.Resource.PublicIncome, amount, itemClass);
em.FetchResource(EconomyManager.Resource.Maintenance, amount, itemClass);
```

---

## 12. TRANSPORTMANAGER - Transporte Público

```csharp
var tm = Singleton<TransportManager>.instance;
```

### Criar Linhas

```csharp
ushort lineId;
TransportInfo info = tm.GetTransportInfo(TransportType.Bus);
tm.CreateLine(out lineId, ref sm.m_randomizer, info, newNumber: true);

// Deletar
tm.ReleaseLine(lineId);

// Nome/Cor
tm.SetLineName(lineId, "Linha Norte");
tm.SetLineColor(lineId, Color.red);
string name = tm.GetLineName(lineId);
```

### Tipos de Transporte

```
PublicTransportBus, PublicTransportMetro, PublicTransportTrain,
PublicTransportShip, PublicTransportPlane, PublicTransportTaxi,
PublicTransportTram, PublicTransportMonorail, PublicTransportCableCar,
PublicTransportTrolleybus
```

---

## 13. GAMEAREAMANAGER - Tiles do Mapa

```csharp
var gam = Singleton<GameAreaManager>.instance;

// Verificar se tile está desbloqueado
bool unlocked = gam.IsUnlocked(x, z);  // x,z = coordenadas do tile (0-4)

// Desbloquear tile
gam.UnlockArea(index);

// Verificar se ponto está fora da área jogável
bool outside = gam.PointOutOfArea(position);

// Limpar posição dentro da área
Vector3 pos = somePos;
gam.ClampPoint(ref pos);

// Área de cada tile
float minX, minZ, maxX, maxZ;
gam.GetAreaBounds(tileX, tileZ, out minX, out minZ, out maxX, out maxZ);

// Preço de tile
int price = gam.CalculateTilePrice(tileIndex);
```

---

## 14. ICITIES.DLL - Interfaces de Modding

### Interfaces Implementáveis

| Interface | Uso |
|-----------|-----|
| **IUserMod** | Definir nome/descrição do mod (obrigatório) |
| **ILoadingExtension** | OnLevelLoaded/OnLevelUnloading |
| **IThreadingExtension** | OnUpdate/OnAfterSimulationTick |
| **ISerializableDataExtension** | Salvar/carregar dados do mod |
| **IBuildingExtension** | Hooks quando buildings são criados/demolidos |
| **ITerrainExtension** | Hooks de modificação de terreno |
| **IEconomyExtension** | Hooks de economia |
| **IDemandExtension** | Modificar demanda |
| **IAreasExtension** | Hooks de áreas |
| **IChirperExtension** | Chirper messages |
| **IMilestonesExtension** | Milestones |
| **ILevelUpExtension** | Level up de prédios |

### Classes Base Disponíveis

```csharp
LoadingExtensionBase    // implementa ILoadingExtension com métodos vazios
ThreadingExtensionBase  // implementa IThreadingExtension
SerializableDataExtensionBase
BuildingExtensionBase
EconomyExtensionBase
DemandExtensionBase
TerrainExtensionBase
```

---

## 15. ItemClass.Service (todos os serviços)

```
None, Residential, Commercial, Industrial, Natural, Vehicles,
Citizen, Tourism, Office, Road, Electricity, Water, Beautification,
Garbage, HealthCare, PoliceDepartment, Education, Monument,
FireDepartment, PublicTransport, Disaster, PlayerIndustry,
PlayerEducation, Museums, VarsitySports, Fishing,
ServicePoint, Hotel, Race
```

---

## 16. RECEITAS PRÁTICAS PARA O MOD

### Receita 1: Construir Estrada Conectada a Node Existente

```csharp
// Encontrar o node mais próximo do tipo certo
var nm = Singleton<NetManager>.instance;
var sm = Singleton<SimulationManager>.instance;
var roadInfo = PrefabCollection<NetInfo>.FindLoaded("Basic Road");

ushort nearestNode = 0;
float nearestDist = float.MaxValue;
for (ushort i = 1; i < 32768; i++) {
    var n = nm.m_nodes.m_buffer[i];
    if ((n.m_flags & NetNode.Flags.Created) == 0) continue;
    if (n.Info?.m_class?.m_service != ItemClass.Service.Road) continue;
    float d = Vector3.Distance(n.m_position, startPos);
    if (d < nearestDist && d < 50f) {
        nearestDist = d;
        nearestNode = i;
    }
}

if (nearestNode != 0) {
    // Conectar ao node existente
    ushort newNode;
    endPos.y = Singleton<TerrainManager>.instance.SampleRawHeightSmooth(endPos);
    nm.CreateNode(out newNode, ref sm.m_randomizer, roadInfo, endPos, sm.m_currentBuildIndex++);

    Vector3 dir = (endPos - nm.m_nodes.m_buffer[nearestNode].m_position).normalized;
    ushort seg;
    nm.CreateSegment(out seg, ref sm.m_randomizer, roadInfo,
        nearestNode, newNode, dir, -dir, sm.m_currentBuildIndex++, sm.m_currentBuildIndex++, false);

    // Atualizar zones no novo segment
    nm.m_segments.m_buffer[seg].UpdateZones(seg);
}
```

### Receita 2: Zonear Área Adjacente a Estrada

```csharp
// Após construir a estrada (segment segId):
nm.m_segments.m_buffer[segId].UpdateZones(segId);

// Esperar 1 tick, depois zonear os blocks do segment
var seg = nm.m_segments.m_buffer[segId];
ushort[] blocks = new ushort[] {
    seg.m_blockStartLeft, seg.m_blockStartRight,
    seg.m_blockEndLeft, seg.m_blockEndRight
};

var zm = Singleton<ZoneManager>.instance;
foreach (var blockId in blocks) {
    if (blockId == 0) continue;
    ref var block = ref zm.m_blocks.m_buffer[blockId];
    int rows = block.RowCount;
    for (int r = 0; r < rows; r++) {
        for (int c = 0; c < 4; c++) {
            block.SetZone(c, r, ItemClass.Zone.ResidentialLow);
        }
    }
}
```

### Receita 3: Posicionar Water Intake na Beira do Rio

```csharp
var tm = Singleton<TerrainManager>.instance;
var bm = Singleton<BuildingManager>.instance;
var sm = Singleton<SimulationManager>.instance;

var waterIntake = PrefabCollection<BuildingInfo>.FindLoaded("Water Intake");

// 1. Encontrar margem
Vector3 shorePos;
Vector3 shoreDir;
float waterH;
if (tm.GetShorePos(desiredPos, 300f, out shorePos, out shoreDir, out waterH)) {
    // 2. Posicionar o prédio na margem
    shorePos.y = tm.SampleRawHeightSmooth(shorePos);

    // 3. Ângulo perpendicular à margem (olhando para a água)
    Vector3 toWater = Vector3.Cross(shoreDir, Vector3.up).normalized;
    if (!tm.HasWater(new Vector2(shorePos.x + toWater.x * 10, shorePos.z + toWater.z * 10))) {
        toWater = -toWater; // inverter se apontar pro lado errado
    }
    float angle = Mathf.Atan2(toWater.x, toWater.z);

    // 4. Criar
    ushort buildingId;
    bm.CreateBuilding(out buildingId, ref sm.m_randomizer,
        waterIntake, shorePos, angle, 0, sm.m_currentBuildIndex++);
}
```

### Receita 4: Conectar Rede Elétrica

```csharp
// A eletricidade propaga automaticamente por estradas (~3 tiles).
// Para áreas distantes, use Power Lines:

var powerLine = PrefabCollection<NetInfo>.FindLoaded("Electricity Wire")
    ?? PrefabCollection<NetInfo>.FindLoaded("Power Line");

// Encontrar o prédio de energia mais próximo
var powerBuilding = bm.FindBuilding(targetPos, 1000f,
    ItemClass.Service.Electricity, ItemClass.SubService.None,
    Building.Flags.Created, Building.Flags.Deleted);

if (powerBuilding != 0) {
    var powerPos = bm.m_buildings.m_buffer[powerBuilding].m_position;

    // Criar power line entre a usina e a área que precisa
    ushort n1, n2, seg;
    powerPos.y = tm.SampleRawHeightSmooth(powerPos);
    targetPos.y = tm.SampleRawHeightSmooth(targetPos);

    nm.CreateNode(out n1, ref sm.m_randomizer, powerLine, powerPos, sm.m_currentBuildIndex++);
    nm.CreateNode(out n2, ref sm.m_randomizer, powerLine, targetPos, sm.m_currentBuildIndex++);
    Vector3 dir = (targetPos - powerPos).normalized;
    nm.CreateSegment(out seg, ref sm.m_randomizer, powerLine,
        n1, n2, dir, -dir, sm.m_currentBuildIndex++, sm.m_currentBuildIndex++, false);
}
```

---

## 17. O QUE O MOD ATUAL JÁ TEM vs O QUE FALTA

### Já Implementado:
- [x] HTTP server na porta 8080
- [x] `/api/stats` - stats básicas (população, dinheiro, eletricidade, água, demanda)
- [x] `/api/zones` - contagem de zonas
- [x] `/api/zone` POST - zonear (mas busca por distância, não por segment)
- [x] `/api/build` POST - construir estradas/pipes/power lines
- [x] `/api/place` POST - posicionar prédios
- [x] `/api/prefabs` - listar building prefabs
- [x] `/api/nets` - listar net prefabs
- [x] `/api/map` - mapa com estradas e pontos de água
- [x] Fuzzy match de prefabs
- [x] ExecuteOnMainThread via SimulationManager

### O Que Falta / Pode Melhorar:
- [ ] **Zonear por segment**: Usar `m_blockStartLeft/Right/EndLeft/Right` do segment ao invés de busca por distância
- [ ] **Conectar estradas**: Detectar e reusar nodes existentes (via `GetClosestSegments` ou iterando `m_nodes`)
- [ ] **Water Intake posicionamento**: Usar `TerrainManager.GetShorePos()` para encontrar beira do rio
- [ ] **Orientação de water buildings**: Calcular ângulo perpendicular à margem
- [ ] **Power line automático**: Conectar automaticamente áreas sem eletricidade
- [ ] **Stats avançados**: District tem muito mais dados (crime, educação, poluição, etc)
- [ ] **Budget API**: `EconomyManager.SetBudget/SetTaxRate` para controle real
- [ ] **Transporte público**: Criar linhas de ônibus/metro
- [ ] **Áreas desbloqueáveis**: `GameAreaManager.UnlockArea`
- [ ] **Políticas**: `DistrictManager.SetCityPolicy`
