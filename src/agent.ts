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
  type CityStats,
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

1. Zonear area (DEVE ser adjacente a uma estrada existente!):
   {"tipo": "zone", "zone_type": "residential|commercial|industrial|office", "x": 100, "z": 100, "width": 4, "depth": 4}

2. Construir estrada (use coordenadas de nós de estrada existentes como ponto de partida!):
   {"tipo": "build", "build_type": "road", "startX": 100, "startZ": 100, "endX": 200, "endZ": 100}

3. Construir cano de agua:
   {"tipo": "build", "build_type": "water_pipe", "startX": 100, "startZ": 100, "endX": 200, "endZ": 100}

4. Colocar edificio (DEVE ser perto de uma estrada!):
   {"tipo": "place", "prefab": "nome_do_prefab", "x": 100, "z": 100}
   Prefabs disponiveis:
   - Energia: coal_power_plant, wind_turbine, solar_power_plant, oil_power_plant
   - Agua: water_pump (captacao - DEVE ficar na beira do rio!), water_drain (despejo - DEVE ficar na beira do rio!), water_tower
   - Saude: medical_clinic, hospital
   - Seguranca: police_station, fire_station
   - Educacao: elementary_school, high_school, university
   - Lixo: landfill

5. Mudar velocidade:
   {"tipo": "speed", "speed": 1|2|3}

REGRAS CRITICAS - SIGA TODAS:
1. TUDO precisa estar conectado a estradas! Edificios sem estrada NAO funcionam.
2. Primeiro SEMPRE construa estradas a partir dos nós de estrada existentes (dados no mapa).
3. water_pump e water_drain DEVEM ficar em coordenadas de agua (waterPoints do mapa). Sem agua por perto, nao produzem nada.
4. Zone APENAS em areas adjacentes a estradas que voce ja construiu.
5. Mantenha pelo menos $500.000 de reserva.
6. NAO construa mais usinas de energia se producao >> consumo.
7. NAO repita acoes que falharam antes.
8. Faca no maximo 3 acoes por turno.

ORDEM DE PRIORIDADE pra cidade nova:
1. Construir estrada a partir da highway existente (use as coordenadas dos road nodes)
2. Colocar coal_power_plant PERTO da estrada
3. Construir estrada ate a beira do rio (waterPoints)
4. Colocar water_pump e water_drain nas coordenadas de agua
5. Construir canos de agua conectando water_pump ate a area residencial
6. Zone residencial ao longo das estradas
7. Servicos publicos quando populacao crescer

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
const MAX_HISTORY = 6; // Ultimos 3 ticks (user + assistant)

// Map info cached (fetched once)
let mapInfoCache: string | null = null;

export async function fetchMapInfo(): Promise<string> {
  if (mapInfoCache) return mapInfoCache;
  try {
    const info = await getMapInfo();
    // Summarize: first few road nodes + water points
    const roads = info.roads.slice(0, 10).map(r => `(${r.x},${r.z}) ${r.name}`).join("; ");
    const water = info.waterPoints.slice(0, 8).map(w => `(${w.x},${w.z})`).join("; ");
    mapInfoCache = `MAPA - Estradas existentes: [${roads}] | Pontos de agua: [${water}]`;
    console.log(`📍 ${mapInfoCache}`);
    return mapInfoCache;
  } catch (e) {
    console.warn("[agent] Failed to fetch map info:", e);
    return "Mapa: informacao indisponivel";
  }
}

function parseResponse(content: string): {
  pensamento: string;
  acoes: AgentAction[];
} {
  let jsonStr = content.trim();

  // Remove code fences
  const fenceMatch = jsonStr.match(/```(?:json)?\s*([\s\S]*?)```/);
  if (fenceMatch) {
    jsonStr = fenceMatch[1].trim();
  }

  // Find JSON object
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
        type: action.zone_type as "residential" | "commercial" | "industrial" | "office",
        x: action.x as number,
        z: action.z as number,
        width: (action.width as number) || 4,
        depth: (action.depth as number) || 4,
      });

    case "build":
      return build({
        type: action.build_type as string,
        startX: action.startX as number,
        startZ: action.startZ as number,
        endX: action.endX as number,
        endZ: action.endZ as number,
      });

    case "place":
      return place({
        prefab: action.prefab as string,
        x: action.x as number,
        z: action.z as number,
      });

    case "speed":
      return setSpeed(action.speed as 1 | 2 | 3);

    default:
      return { error: `Acao desconhecida: ${action.tipo}` };
  }
}

export async function think(stats: CityStats): Promise<AgentResult> {
  // Fetch map info on first call
  const mapInfo = await fetchMapInfo();

  const userMsg = `${mapInfo}

Estado atual da cidade:
${JSON.stringify(stats, null, 2)}

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

  // Limit to 3 actions max
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

  // Salva no historico para o proximo tick
  history.push({ role: "user", content: userMsg });

  // Resumo compacto dos resultados para o historico
  const resumo = resultados.map(r => {
    const res = r.resultado as Record<string, unknown>;
    const status = res?.error ? `FALHOU: ${res.error}` : "OK";
    return `${r.acao.tipo}(${r.acao.prefab || r.acao.build_type || r.acao.zone_type || ""}): ${status}`;
  }).join(", ");

  history.push({
    role: "assistant",
    content: JSON.stringify({
      pensamento: parsed.pensamento,
      acoes_executadas: resumo,
    }),
  });

  // Mantem historico limitado
  while (history.length > MAX_HISTORY) {
    history.shift();
  }

  return {
    pensamento: parsed.pensamento,
    acoes,
    resultados,
  };
}
