/**
 * TwitchCity - Onoma joga Cities: Skylines
 *
 * Loop principal:
 * 1. Conecta no mod do Cities Skylines (Windows)
 * 2. Le estado da cidade
 * 3. Ollama decide acoes
 * 4. Executa acoes no jogo
 * 5. Repete a cada 15s
 */

import { getStats, ping, setSpeed } from "./cities-client";
import { think } from "./agent";

// Load .env.local
import { readFileSync } from "fs";
import { resolve } from "path";

try {
  const envPath = resolve(process.cwd(), ".env.local");
  const envContent = readFileSync(envPath, "utf-8");
  for (const line of envContent.split("\n")) {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith("#")) continue;
    const [key, ...rest] = trimmed.split("=");
    const value = rest.join("=");
    if (key && value && !process.env[key]) {
      process.env[key] = value;
    }
  }
} catch {
  // No .env.local, use defaults
}

const TICK_INTERVAL = 15_000; // 15 segundos entre decisoes

async function waitForConnection() {
  const bridgeUrl = process.env.CITIES_BRIDGE_URL || "http://localhost:8080";
  console.log(`\n🏙️  TwitchCity - Onoma joga Cities: Skylines`);
  console.log(`━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━`);
  console.log(`🔌 Conectando no mod: ${bridgeUrl}`);
  console.log(`🤖 Ollama: ${process.env.OLLAMA_BASE_URL || "http://localhost:11434"}`);
  console.log(`🧠 Modelo: ${process.env.OLLAMA_MODEL || "llama3.1"}`);
  console.log();

  while (true) {
    try {
      await ping();
      console.log("✅ Conectado ao Cities: Skylines!\n");
      return;
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e);
      if (!msg.includes("ECONNREFUSED") && !msg.includes("fetch failed")) {
        console.log(`\n⚠️  Erro: ${msg}`);
      } else {
        process.stdout.write(".");
      }
      await new Promise((r) => setTimeout(r, 3000));
    }
  }
}

async function gameLoop() {
  let tick = 0;

  // Coloca velocidade 3 pra cidade crescer mais rapido
  try {
    await setSpeed(3);
    console.log("⏩ Velocidade: 3x\n");
  } catch {
    // Mod pode nao suportar
  }

  while (true) {
    tick++;
    console.log(`\n━━━ Tick ${tick} ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━`);

    try {
      // 1. Le estado
      const stats = await getStats();
      console.log(
        `📊 Pop: ${stats.population} | 💰 $${stats.money.toLocaleString()} | 😊 ${stats.happiness}%`
      );
      console.log(
        `⚡ Energia: ${stats.electricity.production}/${stats.electricity.consumption} | 💧 Agua: ${stats.water.production}/${stats.water.consumption}`
      );
      console.log(
        `📈 Demanda: R=${stats.demand.residential} C=${stats.demand.commercial} I=${stats.demand.industrial}`
      );

      // 2. Ollama pensa
      console.log("\n🤖 Onoma pensando...");
      const result = await think(stats);

      // 3. Mostra pensamento
      console.log(`\n💬 "${result.pensamento}"`);
      console.log(`\n🎯 ${result.acoes.length} acao(oes) executada(s)`);
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e);
      console.error(`\n❌ Erro: ${msg}`);

      // Se perdeu conexao, tenta reconectar
      if (msg.includes("fetch failed") || msg.includes("ECONNREFUSED")) {
        console.log("🔌 Conexao perdida. Tentando reconectar...");
        await waitForConnection();
      }
    }

    // Espera pro proximo tick
    await new Promise((r) => setTimeout(r, TICK_INTERVAL));
  }
}

async function main() {
  await waitForConnection();
  await gameLoop();
}

main().catch((e) => {
  console.error("Fatal:", e);
  process.exit(1);
});
