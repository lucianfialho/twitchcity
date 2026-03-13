import { describe, it, expect, vi, beforeEach } from "vitest";

const mockFetch = vi.fn();
vi.stubGlobal("fetch", mockFetch);
vi.stubEnv("CITIES_BRIDGE_URL", "http://10.0.0.5:8080");

describe("cities-client", () => {
  beforeEach(() => {
    vi.resetModules();
    mockFetch.mockReset();
  });

  it("ping sends GET to /api/ping", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: () => Promise.resolve({ ok: true }),
    });

    const { ping } = await import("../cities-client");
    const result = await ping();

    expect(mockFetch).toHaveBeenCalledWith(
      "http://10.0.0.5:8080/api/ping",
      expect.objectContaining({ method: "GET" })
    );
    expect(result).toEqual({ ok: true });
  });

  it("getStats returns city stats", async () => {
    const stats = {
      population: 5000,
      money: 70000,
      cityName: "TestCity",
      happiness: 75,
      electricity: { production: 100, consumption: 80 },
      water: { production: 50, consumption: 40 },
      demand: { residential: 60, commercial: 30, industrial: 20 },
    };

    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: () => Promise.resolve(stats),
    });

    const { getStats } = await import("../cities-client");
    const result = await getStats();

    expect(result.population).toBe(5000);
    expect(result.demand.residential).toBe(60);
  });

  it("zone sends POST to /api/zone", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: () => Promise.resolve({ success: true }),
    });

    const { zone } = await import("../cities-client");
    await zone({ type: "residential", x: 100, z: 200 });

    expect(mockFetch).toHaveBeenCalledWith(
      "http://10.0.0.5:8080/api/zone",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({
          type: "residential",
          x: 100,
          z: 200,
        }),
      })
    );
  });

  it("build sends POST to /api/build", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: () => Promise.resolve({ success: true, segment: 42 }),
    });

    const { build } = await import("../cities-client");
    await build({
      type: "road",
      startX: 100,
      startZ: 100,
      endX: 200,
      endZ: 100,
    });

    expect(mockFetch).toHaveBeenCalledWith(
      "http://10.0.0.5:8080/api/build",
      expect.objectContaining({ method: "POST" })
    );
  });

  it("place sends POST to /api/place", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: () => Promise.resolve({ success: true, buildingId: 7 }),
    });

    const { place } = await import("../cities-client");
    await place({ prefab: "coal_power_plant", x: 50, z: 50 });

    expect(mockFetch).toHaveBeenCalledWith(
      "http://10.0.0.5:8080/api/place",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ prefab: "coal_power_plant", x: 50, z: 50 }),
      })
    );
  });

  it("setSpeed sends POST to /api/speed", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: () => Promise.resolve({ success: true, speed: 3 }),
    });

    const { setSpeed } = await import("../cities-client");
    await setSpeed(3);

    expect(mockFetch).toHaveBeenCalledWith(
      "http://10.0.0.5:8080/api/speed",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ speed: 3 }),
      })
    );
  });

  it("throws on HTTP error", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 500,
      text: () => Promise.resolve("Internal error"),
    });

    const { getStats } = await import("../cities-client");
    await expect(getStats()).rejects.toThrow("Bridge GET /api/stats failed (500)");
  });
});
