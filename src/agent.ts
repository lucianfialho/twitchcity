/**
 * Agente AI que usa Ollama pra decidir acoes no Cities: Skylines.
 * Recebe o estado da cidade e retorna acoes estruturadas.
 * Mantém historico entre ticks para nao repetir acoes.
 */

import { Ollama } from "ollama";
import {
  getStats,
  getMapInfo,
  zone,
  build,
  place,
  setSpeed,
  setBudget,
  connectPower,
  type CityStats,
  type MapInfo,
  type ZoneAction,
} from "./cities-client";

let _ollama: Ollama | null = null;
function getOllama() {
  if (!_ollama) {
    _ollama = new Ollama({
      host: process.env.OLLAMA_BASE_URL || "http://localhost:11434",
    });
  }
  return _ollama;
}

function getModel() {
  return process.env.OLLAMA_MODEL || "llama3.1";
}

const SYSTEM_PROMPT = `Voce e o Onoma, um prefeito AI jogando Cities: Skylines numa live da Twitch.
Voce gerencia a cidade e explica suas decisoes pro chat.

Responda SEMPRE em JSON valido neste formato:
{
  "pensamento": "explicacao em portugues do que voce esta pensando e por que (2-3 frases, como se estivesse falando pro chat da Twitch)",
  "acoes": [
    // Uma ou mais acoes da lista abaixo
  ]
}

Acoes disponiveis:

1. Construir estrada (conecta automaticamente a nos existentes perto!):
   {"tipo": "build", "build_type": "road", "startX": 100, "startZ": 100, "endX": 200, "endZ": 100}
   Tipos: road, medium_road, large_road, oneway, highway, water_pipe, powerline
   Opcional: "autoZone": "residential" - zoneia automaticamente ao construir estrada!

2. Zonear area por posicao (encontra estrada mais proxima automaticamente):
   {"tipo": "zone", "zone_type": "residential", "x": 100, "z": 100}
   Tipos: residential, residential_high, commercial, commercial_high, industrial, office
   Opcional: "segment": 123 - zoneia um segment especifico (retornado pelo build)
   Opcional: "radius": 150 - raio de busca (default 100)

3. Colocar edificio (water_pump/water_drain auto-posiciona na beira do rio!):
   {"tipo": "place", "prefab": "nome_do_prefab", "x": 100, "z": 100}
   Prefabs disponiveis:
   - Energia: coal_power_plant, wind_turbine, solar_power_plant, oil_power_plant
   - Agua: water_pump (auto-snap na margem do rio!), water_drain (auto-snap!), water_tower
   - Saude: medical_clinic, hospital
   - Seguranca: police_station, fire_station
   - Educacao: elementary_school, high_school, university
   - Lixo: landfill
   NOTA: water_pump e water_drain agora encontram a margem do rio automaticamente!
   Basta passar coordenadas PERTO do rio.

4. Conectar eletricidade (cria power line automaticamente):
   {"tipo": "connect_power", "toX": 200, "toZ": 200}
   Encontra a usina mais proxima e conecta. Opcional: "fromX", "fromZ" para especificar origem.

5. Ajustar orcamento:
   {"tipo": "budget", "service": "electricity", "budget": 120}
   Servicos: electricity, water, garbage, healthcare, fire, police, education, transport
   Opcional: "taxRate": 12 (0-29) para impostos de: residential, commercial, industrial, office

6. Mudar velocidade:
   {"tipo": "speed", "speed": 1|2|3}

REGRAS CRITICAS - SIGA TODAS:
1. TUDO precisa estar conectado a estradas! Edificios sem estrada NAO funcionam.
2. Construa estradas a partir dos nos existentes (coordenadas do mapa). O mod conecta automaticamente a nos proximos (<20 unidades).
3. water_pump e water_drain POSICIONE perto de "shore" (margens de rio do mapa). O mod ajusta a posicao exata automaticamente.
4. Use autoZone ao construir estradas para zonear de uma vez so!
5. Mantenha pelo menos $500.000 de reserva.
6. NAO construa mais usinas se producao >> consumo (>2x).
7. NAO repita acoes que falharam antes.
8. Faca no maximo 3 acoes por turno.
9. Use connect_power se buildings nao tem eletricidade e estao longe de estradas.
10. Se desemprego > 10%, zone mais commercial/industrial. Se demanda residencial alta, zone mais residential.

ORDEM DE PRIORIDADE pra cidade nova:
1. Construir estrada a partir da highway existente (use as coordenadas dos road nodes)
2. Colocar coal_power_plant PERTO da estrada
3. Colocar water_pump perto de uma posicao de "shore" (margem do rio) - ele auto-posiciona!
4. Colocar water_drain perto de outra posicao de "shore"
5. Zone residencial com autoZone ou zone separado
6. Servicos publicos quando populacao crescer (>500: escola, >1000: hospital)
7. Ajustar impostos/orcamento conforme necessario

Voce esta numa live da Twitch, entao seja divertido e explique suas decisoes de forma interessante!`;

export interface AgentAction {
  tipo: string;
  [key: string]: unknown;
}

export interface AgentResult {
  pensamento: string;
  acoes: AgentAction[];
  resultados: Array<{ acao: AgentAction; resultado: unknown }>;
}

// Historico dos ultimos ticks para contexto
const history: Array<{ role: "user" | "assistant"; content: string }> = [];
const MAX_HISTORY = 6;

// Map info cached (refreshed periodically)
let mapInfoCache: { data: MapInfo; summary: string; fetchedAt: number } | null = null;
const MAP_CACHE_TTL = 60_000; // refresh every 60s

export async function fetchMapInfo(): Promise<string> {
  const now = Date.now();
  if (mapInfoCache && now - mapInfoCache.fetchedAt < MAP_CACHE_TTL) {
    return mapInfoCache.summary;
  }

  try {
    const info = await getMapInfo();

    const roads = info.roads
      .slice(0, 10)
      .map((r) => `node${r.id}(${r.x},${r.z}) ${r.name}`)
      .join("; ");

    const water = info.waterPoints
      .slice(0, 6)
      .map((w) => `(${w.x},${w.z})`)
      .join("; ");

    const shore = (info.shore || [])
      .slice(0, 8)
      .map((s) => `(${s.x},${s.z})`)
      .join("; ");

    const buildings = (info.buildings || [])
      .slice(0, 10)
      .map((b) => `${b.name}(${b.x},${b.z})`)
      .join("; ");

    const summary = [
      `MAPA - Estradas: [${roads}]`,
      `Agua: [${water}]`,
      `Margens(shore - bom pra water_pump): [${shore}]`,
      buildings ? `Buildings: [${buildings}]` : null,
    ]
      .filter(Boolean)
      .join(" | ");

    mapInfoCache = { data: info, summary, fetchedAt: now };
    console.log(`📍 Mapa atualizado: ${info.roads.length} roads, ${(info.shore || []).length} shore, ${(info.buildings || []).length} buildings`);
    return summary;
  } catch (e) {
    console.warn("[agent] Failed to fetch map info:", e);
    return mapInfoCache?.summary || "Mapa: informacao indisponivel";
  }
}

function parseResponse(content: string): {
  pensamento: string;
  acoes: AgentAction[];
} {
  let jsonStr = content.trim();

  const fenceMatch = jsonStr.match(/```(?:json)?\s*([\s\S]*?)```/);
  if (fenceMatch) {
    jsonStr = fenceMatch[1].trim();
  }

  const jsonMatch = jsonStr.match(/\{[\s\S]*\}/);
  if (jsonMatch) {
    jsonStr = jsonMatch[0];
  }

  try {
    const parsed = JSON.parse(jsonStr);
    return {
      pensamento: parsed.pensamento || parsed.thinking || "Pensando...",
      acoes: Array.isArray(parsed.acoes || parsed.actions)
        ? parsed.acoes || parsed.actions
        : [],
    };
  } catch {
    console.warn("[agent] Failed to parse JSON response");
    return { pensamento: content.slice(0, 300), acoes: [] };
  }
}

async function executeAction(action: AgentAction): Promise<unknown> {
  switch (action.tipo) {
    case "zone":
      return zone({
        type: action.zone_type as ZoneAction["type"],
        segment: action.segment as number | undefined,
        x: action.x as number | undefined,
        z: action.z as number | undefined,
        radius: action.radius as number | undefined,
      });

    case "build":
      return build({
        type: action.build_type as string,
        startX: action.startX as number,
        startZ: action.startZ as number,
        endX: action.endX as number,
        endZ: action.endZ as number,
        autoZone: action.autoZone as string | undefined,
      });

    case "place":
      return place({
        prefab: action.prefab as string,
        x: action.x as number,
        z: action.z as number,
        angle: action.angle as number | undefined,
      });

    case "budget":
      return setBudget({
        service: action.service as string,
        budget: action.budget as number | undefined,
        taxRate: action.taxRate as number | undefined,
      });

    case "connect_power":
      return connectPower({
        toX: (action.toX ?? action.x) as number,
        toZ: (action.toZ ?? action.z) as number,
        fromX: action.fromX as number | undefined,
        fromZ: action.fromZ as number | undefined,
      });

    case "speed":
      return setSpeed(action.speed as 1 | 2 | 3);

    default:
      return { error: `Acao desconhecida: ${action.tipo}` };
  }
}

export async function think(stats: CityStats): Promise<AgentResult> {
  const mapInfo = await fetchMapInfo();

  // Build compact stats summary for the LLM
  const statsLines = [
    `Pop: ${stats.population} | Dinheiro: $${stats.money} | Felicidade: ${stats.happiness}%`,
    `Eletricidade: ${stats.electricity.production}/${stats.electricity.consumption} | Agua: ${stats.water.production}/${stats.water.consumption}`,
    `Esgoto: ${stats.sewage?.capacity || 0}/${stats.sewage?.accumulation || 0} | Lixo: ${stats.garbage?.amount || 0}/${stats.garbage?.capacity || 0}`,
    `Crime: ${stats.crime?.criminals || 0}/${stats.crime?.capacity || 0} | Desemprego: ${stats.unemployment || 0}%`,
    `Educacao: E${stats.education?.elementary || 0}% H${stats.education?.highSchool || 0}% U${stats.education?.university || 0}%`,
    `Demanda: R=${stats.demand.residential} C=${stats.demand.commercial} I=${stats.demand.industrial}`,
  ];

  const userMsg = `${mapInfo}

Estado atual da cidade:
${statsLines.join("\n")}

Decida suas proximas acoes. Use as coordenadas do mapa! Responda em JSON.`;

  const messages = [
    { role: "system" as const, content: SYSTEM_PROMPT },
    ...history,
    { role: "user" as const, content: userMsg },
  ];

  const response = await getOllama().chat({
    model: getModel(),
    messages,
    format: "json",
    options: {
      num_predict: 512,
    },
  });

  const parsed = parseResponse(response.message.content || "");
  const resultados: AgentResult["resultados"] = [];

  const acoes = parsed.acoes.slice(0, 3);

  for (const acao of acoes) {
    try {
      const resultado = await executeAction(acao);
      resultados.push({ acao, resultado });
      console.log(`  ✓ ${acao.tipo}: ${JSON.stringify(resultado)}`);
    } catch (e) {
      const erro = e instanceof Error ? e.message : String(e);
      resultados.push({ acao, resultado: { error: erro } });
      console.log(`  ✗ ${acao.tipo}: ${erro}`);
    }
  }

  // Invalidate map cache after actions (roads/buildings changed)
  if (acoes.some((a) => a.tipo === "build" || a.tipo === "place" || a.tipo === "connect_power")) {
    if (mapInfoCache) mapInfoCache.fetchedAt = 0;
  }

  history.push({ role: "user", content: userMsg });

  const resumo = resultados
    .map((r) => {
      const res = r.resultado as Record<string, unknown>;
      const status = res?.error ? `FALHOU: ${res.error}` : "OK";
      return `${r.acao.tipo}(${r.acao.prefab || r.acao.build_type || r.acao.zone_type || r.acao.service || ""}): ${status}`;
    })
    .join(", ");

  history.push({
    role: "assistant",
    content: JSON.stringify({
      pensamento: parsed.pensamento,
      acoes_executadas: resumo,
    }),
  });

  while (history.length > MAX_HISTORY) {
    history.shift();
  }

  return {
    pensamento: parsed.pensamento,
    acoes,
    resultados,
  };
}
