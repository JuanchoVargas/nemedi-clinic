// Levantar una instancia adicional del API para un spec (con otra configuración que la de :5055).
// Usa el build más reciente: pnpm test:unit compila a .artifacts/; dotnet run, a bin/Debug.
import { spawn, type ChildProcess } from "node:child_process";
import fs from "node:fs";
import path from "node:path";

export function apiExecutable() {
  const candidates = [
    ".artifacts/api-tests/bin/NemediClinic.Api/debug/NemediClinic.Api.exe",
    "apps/api/src/NemediClinic.Api/bin/Debug/net10.0/NemediClinic.Api.exe",
  ].map((p) => path.resolve(p)).filter((p) => fs.existsSync(p));
  if (candidates.length === 0) throw new Error("No hay un NemediClinic.Api.exe compilado: corre pnpm test:unit o dotnet build.");
  const mtime = (exe: string) => fs.statSync(exe.replace(/\.exe$/, ".dll")).mtimeMs;
  return candidates.sort((a, b) => mtime(b) - mtime(a))[0];
}

export interface ApiProcess {
  process: ChildProcess;
  log: () => string;
  stop: () => void;
}

/**
 * Arranca el API en Development con las variables dadas (sin Hangfire) y espera a que
 * `healthUrl` responda 200 con `headers`. Falla con el final del log si no arranca en 60 s.
 */
export async function startApi(env: Record<string, string>, healthUrl: string, headers: Record<string, string> = {}, args: string[] = []): Promise<ApiProcess> {
  let log = "";
  const child = spawn(apiExecutable(), args, {
    cwd: path.resolve("apps/api/src/NemediClinic.Api"), // appsettings.json + Development
    env: { ...process.env, ASPNETCORE_ENVIRONMENT: "Development", Jobs__SchedulerEnabled: "false", ...env },
    stdio: ["ignore", "pipe", "pipe"],
  });
  child.stdout?.on("data", (b) => { log += b; });
  child.stderr?.on("data", (b) => { log += b; });

  for (let i = 0; i < 60; i++) {
    const ok = await fetch(healthUrl, { headers }).then((r) => r.ok).catch(() => false);
    if (ok) return { process: child, log: () => log, stop: () => child.kill() };
    if (child.exitCode !== null) break;
    await new Promise((r) => setTimeout(r, 1000));
  }
  child.kill();
  throw new Error(`El API no arrancó (${healthUrl}):\n${log.slice(-2000)}`);
}
