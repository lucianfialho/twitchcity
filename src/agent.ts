/**
 * Agente AI que usa Ollama pra decidir acoes no Cities: Skylines.
 * Recebe o estado da cidade e retorna acoes estruturadas.
 * Mantém historico entre ticks para nao repetir acoes.
 */

import { Ollama } from "ollama";
import {
  getStats,
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

1. Zonear area:
   {"tipo": "zone", "zone_type": "residential|commercial|industrial|office", "x": 100, "z": 100, "width": 4, "depth": 4}

2. Construir estrada:
   {"tipo": "build", "build_type": "road", "startX": 100, "startZ": 100, "endX": 200, "endZ": 100}

3. Construir linha de energia:
   {"tipo": "build", "build_type": "powerline", "startX": 100, "startZ": 100, "endX": 200, "endZ": 100}

4. Construir cano de agua:
   {"tipo": "build", "build_type": "water_pipe", "startX": 100, "startZ": 100, "endX": 200, "endZ": 100}

5. Colocar edificio:
   {"tipo": "place", "prefab": "nome_do_prefab", "x": 100, "z": 100}
   Prefabs: coal_power_plant, wind_turbine, water_pump, water_drain,
   police_station, fire_station, hospital, elementary_school, high_school,
   landfill, cemetery

6. Mudar velocidade:
   {"tipo": "speed", "speed": 1|2|3}

REGRAS IMPORTANTES:
- NAO repita acoes que ja fizeram efeito. Se ja tem energia suficiente, NAO construa mais usinas.
- Se uma acao falhou antes (ex: prefab_not_found), NAO tente de novo com o mesmo nome.
- Mantenha pelo menos $500.000 de reserva. Se o dinheiro esta baixo, NAO construa nada caro.
- Siga esta ordem de prioridade pra cidade nova:
  1. UMA usina de energia (coal_power_plant)
  2. Agua (water_pump + water_drain)
  3. Estradas conectando tudo
  4. Zonas residenciais (a demanda R=100 pede moradores)
  5. Servicos publicos quando populacao crescer
- Se a producao de energia ja e maior que o consumo, NAO construa mais usinas!
- Varie suas acoes a cada turno. Faca coisas DIFERENTES do turno anterior.

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
  const userMsg = `Estado atual da cidade:\n${JSON.stringify(stats, null, 2)}\n\nDecida suas proximas acoes. Lembre do historico acima e NAO repita acoes desnecessarias. Responda em JSON.`;

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

  for (const acao of parsed.acoes) {
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
    const status = (r.resultado as Record<string, unknown>)?.error ? "FALHOU" : "OK";
    return `${r.acao.tipo}: ${status}`;
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
    acoes: parsed.acoes,
    resultados,
  };
}
