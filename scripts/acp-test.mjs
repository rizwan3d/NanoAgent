// ACP smoke test for NanoAgent — exercises tool calls over the Agent Client Protocol.
//
// Drives the JSON-RPC handshake over stdio:
//   initialize -> session/new -> session/prompt (x N) -> session/close
//
// It also ANSWERS the requests the agent sends back to the client:
//   session/request_permission  (tool approval AND ask_question choices)
//   session/request_text        (free-form / "Other…" answers)
// and renders streamed session/update notifications (messages, reasoning,
// tool_call / tool_call_update, and plan entries).
//
// Usage (from repo root):
//   node scripts/acp-test.mjs                 # runs the 3 built-in scenarios
//   node scripts/acp-test.mjs "your prompt"   # runs a single custom prompt
//
// Provider config comes from the environment (NANOAGENT_PROVIDER, NANOAGENT_MODEL,
// NANOAGENT_THINKING, NANOAGENT_REASONING, NANOAGENT_API_KEY) — export those first.

import { spawn } from "node:child_process";
import { existsSync } from "node:fs";
import path from "node:path";
import process from "node:process";

const cwd = process.cwd();

// The three tools to exercise. Prompts are explicit so a small model reliably
// reaches for the right tool. Override with a single CLI arg.
const SCENARIOS = [
  {
    name: "write file",
    prompt:
      "Use the file_write tool to create a file named acp-tool-test.txt in the " +
      "current directory containing exactly: hello from acp. Then confirm in one line.",
  },
  {
    name: "planning tool",
    prompt:
      "Use the planning tool (update_plan) to lay out a 3-step plan for adding a " +
      "search feature to an app, then stop.",
  },
  {
    name: "ask_question",
    prompt:
      "Use the ask_question tool to ask me whether I prefer 'tabs' or 'spaces' " +
      "(give those two options), then tell me what I chose.",
  },
];

const custom = process.argv.slice(2).join(" ").trim();
const scenarios = custom ? [{ name: "custom", prompt: custom }] : SCENARIOS;

// ---------------------------------------------------------------------------
// Launch: prefer the built exe (clean stdout) and fall back to `dotnet run`.
// ---------------------------------------------------------------------------
const exe =
  [
    path.join(cwd, "NanoAgent.CLI", "bin", "Release", "net10.0", "NanoAgent.CLI.exe"),
    path.join(cwd, "NanoAgent.CLI", "bin", "Debug", "net10.0", "NanoAgent.CLI.exe"),
  ].find(existsSync) ?? null;

const [command, args] = exe
  ? [exe, ["--acp"]]
  : ["dotnet", ["run", "--project", path.join(cwd, "NanoAgent.CLI"), "--", "--acp"]];

console.log(`launching: ${command} ${args.join(" ")}\n`);
const child = spawn(command, args, { stdio: ["pipe", "pipe", "inherit"] });

let nextId = 1;
const pending = new Map();
let buffer = "";

function write(obj) {
  child.stdin.write(JSON.stringify(obj) + "\n");
}

function request(method, params) {
  const id = nextId++;
  write({ jsonrpc: "2.0", id, method, params });
  return new Promise((resolve, reject) => pending.set(id, { resolve, reject }));
}

function respond(id, result) {
  write({ jsonrpc: "2.0", id, result });
}

// ---------------------------------------------------------------------------
// Answer requests the agent makes back to us (the client).
// ---------------------------------------------------------------------------
function handleServerRequest(msg) {
  const { id, method, params = {} } = msg;

  if (method === "session/request_permission") {
    const options = params.options ?? [];
    const title = params.toolCall?.title ?? "(permission)";
    // Prefer an "allow" option; otherwise take the default; otherwise the first.
    const allow = options.find((o) => /allow/i.test(o.kind ?? ""));
    const chosen =
      allow ??
      options.find((o) => o.optionId === params.defaultOptionId) ??
      options[0];
    console.log(
      `\n  [request_permission] ${title}\n` +
        `    options: ${options.map((o) => `${o.optionId}:${o.name}`).join(" | ")}\n` +
        `    -> answering "${chosen?.name}" (${chosen?.optionId})`,
    );
    respond(id, { outcome: { outcome: "selected", optionId: chosen?.optionId } });
    return;
  }

  if (method === "session/request_text") {
    const value = "spaces"; // canned answer for free-text / "Other…"
    console.log(`\n  [request_text] ${params.label} -> answering "${value}"`);
    respond(id, { outcome: { outcome: "submitted", value } });
    return;
  }

  // Unknown request: reply method-not-found so the agent can degrade gracefully.
  console.log(`\n  [unhandled request] ${method}`);
  write({ jsonrpc: "2.0", id, error: { code: -32601, message: "unsupported" } });
}

// ---------------------------------------------------------------------------
// Render streamed notifications.
// ---------------------------------------------------------------------------
function handleNotification(msg) {
  if (msg.method !== "session/update") return;
  const u = msg.params?.update ?? {};
  switch (u.sessionUpdate) {
    case "agent_message_chunk":
      process.stdout.write(u.content?.text ?? "");
      break;
    case "agent_reasoning_chunk":
      process.stdout.write(`\x1b[2m${u.content?.text ?? ""}\x1b[0m`);
      break;
    case "tool_call":
      console.log(`\n  [tool_call] ${u.title}  (kind=${u.kind}, id=${u.toolCallId})`);
      break;
    case "tool_call_update":
      console.log(`  [tool ${u.status}] ${u.toolCallId}`);
      break;
    case "plan":
      console.log("\n  [plan]");
      for (const e of u.entries ?? []) console.log(`    - [${e.status}] ${e.content}`);
      break;
  }
}

// ---------------------------------------------------------------------------
// Stdout pump: split newline-delimited JSON, route each message.
// ---------------------------------------------------------------------------
child.stdout.on("data", (chunk) => {
  buffer += chunk.toString("utf8");
  let nl;
  while ((nl = buffer.indexOf("\n")) >= 0) {
    const line = buffer.slice(0, nl).trim();
    buffer = buffer.slice(nl + 1);
    if (!line) continue;

    let msg;
    try {
      msg = JSON.parse(line);
    } catch {
      console.error("non-JSON:", line); // build noise when using `dotnet run`
      continue;
    }

    if (msg.method && msg.id !== undefined) {
      handleServerRequest(msg); // request FROM the agent
    } else if (msg.id !== undefined && (msg.result !== undefined || msg.error)) {
      const waiter = pending.get(msg.id); // response TO our request
      pending.delete(msg.id);
      if (msg.error) waiter?.reject(new Error(`${msg.error.code}: ${msg.error.message}`));
      else waiter?.resolve(msg.result);
    } else {
      handleNotification(msg); // notification
    }
  }
});

child.on("exit", (code) => console.log(`\n--- CLI exited (${code}) ---`));

// ---------------------------------------------------------------------------
// Drive the session.
// ---------------------------------------------------------------------------
try {
  const init = await request("initialize", { protocolVersion: 1, clientCapabilities: {} });
  console.log(`initialize ok: ${init.agentInfo?.name} ${init.agentInfo?.version}`);

  const { sessionId } = await request("session/new", { cwd, mcpServers: [] });
  console.log(`session: ${sessionId}`);

  for (const s of scenarios) {
    console.log(`\n===== scenario: ${s.name} =====`);
    console.log(`>>> ${s.prompt}\n`);
    const result = await request("session/prompt", {
      sessionId,
      prompt: [{ type: "text", text: s.prompt }],
    });
    console.log(`\n--- stopReason: ${result.stopReason} ---`);
  }

  await request("session/close", { sessionId });
} catch (err) {
  console.error("\nACP error:", err.message);
} finally {
  child.stdin.end();
}
