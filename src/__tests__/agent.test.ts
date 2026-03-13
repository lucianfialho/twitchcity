import { describe, it, expect, vi, beforeEach } from "vitest";

const {
  mockChat,
  mockGetStats,
  mockZone,
  mockBuild,
  mockPlace,
  mockSetSpeed,
} = vi.hoisted(() => ({
  mockChat: vi.fn(),
  mockGetStats: vi.fn(),
  mockZone: vi.fn(),
  mockBuild: vi.fn(),
  mockPlace: vi.fn(),
  mockSetSpeed: vi.fn(),
}));

vi.mock("ollama", () => ({
  Ollama: class {
    chat = mockChat;
  },
}));

vi.mock("../cities-client", () => ({
  getStats: (...args: unknown[]) => mockGetStats(...args),
  zone: (...args: unknown[]) => mockZone(...args),
  build: (...args: unknown[]) => mockBuild(...args),
  place: (...args: unknown[]) => mockPlace(...args),
  setSpeed: (...args: unknown[]) => mockSetSpeed(...args),
}));

import { think, type AgentResult } from "../agent";
import type { CityStats } from "../cities-client";

const fakeStats: CityStats = {
  population: 1000,
  money: 50000,
  cityName: "TwitchCity",
  happiness: 70,
  electricity: { production: 100, consumption: 80 },
  water: { production: 50, consumption: 40 },
  demand: { residential: 60, commercial: 30, industrial: 20 },
};

describe("agent", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockZone.mockResolvedValue({ success: true });
    mockBuild.mockResolvedValue({ success: true });
    mockPlace.mockResolvedValue({ success: true, buildingId: 1 });
    mockSetSpeed.mockResolvedValue({ success: true });
  });

  it("parses JSON response and executes zone action", async () => {
    mockChat.mockResolvedValueOnce({
      message: {
        content: JSON.stringify({
          pensamento: "Preciso de mais casas!",
          acoes: [
            { tipo: "zone", zone_type: "residential", x: 100, z: 100 },
          ],
        }),
      },
    });

    const result = await think(fakeStats);

    expect(result.pensamento).toBe("Preciso de mais casas!");
    expect(result.acoes).toHaveLength(1);
    expect(mockZone).toHaveBeenCalledWith({
      type: "residential",
      x: 100,
      z: 100,
      width: 4,
      depth: 4,
    });
  });

  it("executes build action", async () => {
    mockChat.mockResolvedValueOnce({
      message: {
        content: JSON.stringify({
          pensamento: "Conectando com estradas.",
          acoes: [
            {
              tipo: "build",
              build_type: "road",
              startX: 100,
              startZ: 100,
              endX: 200,
              endZ: 100,
            },
          ],
        }),
      },
    });

    await think(fakeStats);

    expect(mockBuild).toHaveBeenCalledWith({
      type: "road",
      startX: 100,
      startZ: 100,
      endX: 200,
      endZ: 100,
    });
  });

  it("executes place action", async () => {
    mockChat.mockResolvedValueOnce({
      message: {
        content: JSON.stringify({
          pensamento: "Cidade precisa de energia!",
          acoes: [
            { tipo: "place", prefab: "coal_power_plant", x: 50, z: 50 },
          ],
        }),
      },
    });

    await think(fakeStats);

    expect(mockPlace).toHaveBeenCalledWith({
      prefab: "coal_power_plant",
      x: 50,
      z: 50,
    });
  });

  it("executes speed action", async () => {
    mockChat.mockResolvedValueOnce({
      message: {
        content: JSON.stringify({
          pensamento: "Vamos acelerar!",
          acoes: [{ tipo: "speed", speed: 3 }],
        }),
      },
    });

    await think(fakeStats);

    expect(mockSetSpeed).toHaveBeenCalledWith(3);
  });

  it("handles multiple actions", async () => {
    mockChat.mockResolvedValueOnce({
      message: {
        content: JSON.stringify({
          pensamento: "Construindo infraestrutura basica.",
          acoes: [
            { tipo: "place", prefab: "coal_power_plant", x: 50, z: 50 },
            {
              tipo: "build",
              build_type: "road",
              startX: 50,
              startZ: 60,
              endX: 150,
              endZ: 60,
            },
            { tipo: "zone", zone_type: "residential", x: 100, z: 70 },
          ],
        }),
      },
    });

    const result = await think(fakeStats);

    expect(result.resultados).toHaveLength(3);
    expect(mockPlace).toHaveBeenCalled();
    expect(mockBuild).toHaveBeenCalled();
    expect(mockZone).toHaveBeenCalled();
  });

  it("handles malformed JSON gracefully", async () => {
    mockChat.mockResolvedValueOnce({
      message: { content: "Nao consigo decidir agora..." },
    });

    const result = await think(fakeStats);

    expect(result.pensamento).toContain("Nao consigo decidir");
    expect(result.acoes).toHaveLength(0);
  });

  it("handles JSON in code fences", async () => {
    mockChat.mockResolvedValueOnce({
      message: {
        content:
          '```json\n{"pensamento": "Vamos la!", "acoes": [{"tipo": "speed", "speed": 2}]}\n```',
      },
    });

    const result = await think(fakeStats);

    expect(result.pensamento).toBe("Vamos la!");
    expect(mockSetSpeed).toHaveBeenCalledWith(2);
  });

  it("handles action execution errors gracefully", async () => {
    mockPlace.mockRejectedValueOnce(new Error("prefab_not_found"));

    mockChat.mockResolvedValueOnce({
      message: {
        content: JSON.stringify({
          pensamento: "Tentando construir.",
          acoes: [{ tipo: "place", prefab: "invalid_thing", x: 0, z: 0 }],
        }),
      },
    });

    const result = await think(fakeStats);

    expect(result.resultados[0].resultado).toEqual({
      error: "prefab_not_found",
    });
  });

  it("handles unknown action type", async () => {
    mockChat.mockResolvedValueOnce({
      message: {
        content: JSON.stringify({
          pensamento: "Testando.",
          acoes: [{ tipo: "fly_to_mars" }],
        }),
      },
    });

    const result = await think(fakeStats);

    expect(result.resultados[0].resultado).toEqual({
      error: "Acao desconhecida: fly_to_mars",
    });
  });

  it("uses 'thinking' as fallback for 'pensamento'", async () => {
    mockChat.mockResolvedValueOnce({
      message: {
        content: JSON.stringify({
          thinking: "English thinking fallback",
          acoes: [],
        }),
      },
    });

    const result = await think(fakeStats);

    expect(result.pensamento).toBe("English thinking fallback");
  });
});
