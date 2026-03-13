/**
 * Client HTTP que fala com o mod C# rodando dentro do Cities: Skylines.
 * O mod expoe endpoints REST no Windows.
 */

function getBridgeUrl() {
  return process.env.CITIES_BRIDGE_URL || "http://localhost:8080";
}

async function request(
  method: string,
  path: string,
  body?: Record<string, unknown>
) {
  const res = await fetch(`${getBridgeUrl()}${path}`, {
    method,
    headers: { "Content-Type": "application/json" },
    body: body ? JSON.stringify(body) : undefined,
  });

  if (!res.ok) {
    const text = await res.text();
    throw new Error(`Bridge ${method} ${path} failed (${res.status}): ${text}`);
  }

  return res.json();
}

// --- Game State ---

export interface CityStats {
  population: number;
  money: number;
  cityName: string;
  happiness: number;
  electricity: { production: number; consumption: number };
  water: { production: number; consumption: number };
  sewage: { capacity: number; accumulation: number };
  garbage: { amount: number; capacity: number };
  crime: { criminals: number; capacity: number };
  education: { elementary: number; highSchool: number; university: number };
  unemployment: number;
  landValue: number;
  demand: { residential: number; commercial: number; industrial: number };
}

export async function getStats(): Promise<CityStats> {
  return request("GET", "/api/stats");
}

export async function getZones(): Promise<{
  residential: number;
  commercial: number;
  industrial: number;
  office: number;
}> {
  return request("GET", "/api/zones");
}

// --- Actions ---

export interface ZoneAction {
  type:
    | "residential"
    | "residential_low"
    | "residential_high"
    | "commercial"
    | "commercial_low"
    | "commercial_high"
    | "industrial"
    | "office";
  segment?: number; // zone by segment ID (preferred)
  x?: number; // zone by position
  z?: number;
  radius?: number;
}

export async function zone(action: ZoneAction) {
  return request("POST", "/api/zone", action as unknown as Record<string, unknown>);
}

export interface BuildAction {
  type: string; // road, medium_road, large_road, oneway, highway, powerline, water_pipe
  startX: number;
  startZ: number;
  endX: number;
  endZ: number;
  autoZone?: string; // "residential", "commercial", etc - auto-zones after building road
}

export async function build(action: BuildAction) {
  return request("POST", "/api/build", action as unknown as Record<string, unknown>);
}

export interface PlaceAction {
  prefab: string; // coal_power_plant, water_pump, etc
  x: number;
  z: number;
  angle?: number; // optional, auto-calculated for water buildings
}

export async function place(action: PlaceAction) {
  return request("POST", "/api/place", action as unknown as Record<string, unknown>);
}

export interface BudgetAction {
  service: string; // electricity, water, garbage, healthcare, fire, police, education, transport, road, residential, commercial, industrial, office
  budget?: number; // 50-150 (default 100)
  taxRate?: number; // 0-29
}

export async function setBudget(action: BudgetAction) {
  return request("POST", "/api/budget", action as unknown as Record<string, unknown>);
}

export async function setSpeed(speed: 1 | 2 | 3) {
  return request("POST", "/api/speed", { speed });
}

export interface ConnectPowerAction {
  toX: number;
  toZ: number;
  fromX?: number; // optional, auto-finds nearest power plant
  fromZ?: number;
}

export async function connectPower(action: ConnectPowerAction) {
  return request("POST", "/api/connect-power", action as unknown as Record<string, unknown>);
}

// --- Utility ---

export interface ShorePoint {
  x: number;
  z: number;
  waterHeight: number;
  dirX: number;
  dirZ: number;
}

export interface MapBuilding {
  id: number;
  name: string;
  service: string;
  x: number;
  z: number;
}

export interface MapInfo {
  roads: Array<{ id: number; x: number; z: number; name: string }>;
  waterPoints: Array<{ x: number; z: number; waterLevel: number; terrainHeight: number }>;
  shore: ShorePoint[];
  buildings: MapBuilding[];
}

export async function getMapInfo(): Promise<MapInfo> {
  return request("GET", "/api/map");
}

export async function ping(): Promise<{ ok: boolean }> {
  return request("GET", "/api/ping");
}

export async function screenshot(): Promise<string> {
  const res = await fetch(`${getBridgeUrl()}/api/screenshot`);
  if (!res.ok) throw new Error("Failed to get screenshot");
  const buffer = await res.arrayBuffer();
  return `data:image/png;base64,${Buffer.from(buffer).toString("base64")}`;
}
