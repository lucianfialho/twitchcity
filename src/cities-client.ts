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
  type: "residential" | "commercial" | "industrial" | "office";
  x: number;
  z: number;
  width?: number;
  depth?: number;
}

export async function zone(action: ZoneAction) {
  return request("POST", "/api/zone", action as unknown as Record<string, unknown>);
}

export interface BuildAction {
  type: string; // road, powerline, water_pipe, etc
  startX: number;
  startZ: number;
  endX: number;
  endZ: number;
}

export async function build(action: BuildAction) {
  return request("POST", "/api/build", action as unknown as Record<string, unknown>);
}

export interface PlaceAction {
  prefab: string; // coal_power_plant, police_station, fire_station, etc
  x: number;
  z: number;
}

export async function place(action: PlaceAction) {
  return request("POST", "/api/place", action as unknown as Record<string, unknown>);
}

export async function setBudget(service: string, percentage: number) {
  return request("POST", "/api/budget", { service, percentage });
}

export async function setSpeed(speed: 1 | 2 | 3) {
  return request("POST", "/api/speed", { speed });
}

// --- Utility ---

export async function getMapInfo(): Promise<{
  roads: Array<{ id: number; x: number; z: number; name: string }>;
  waterPoints: Array<{ x: number; z: number; waterLevel: number; terrainHeight: number }>;
}> {
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
