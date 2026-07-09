"use client";

import { useState } from "react";

// ── VS Code Dark+ theme colors ──
const C = {
  keyword: "#c586c0",
  keyword2: "#569cd6",
  string: "#ce9178",
  number: "#b5cea8",
  function: "#dcdcaa",
  type: "#4ec9b0",
  comment: "#6a9955",
  variable: "#9cdcfe",
  property: "#9cdcfe",
};

const TS_KW = new Set([
  "const","let","var","function","return","async","await","new","throw",
  "try","catch","finally","if","else","for","of","in","while","do",
  "switch","case","break","continue","typeof","instanceof","void",
  "delete","class","extends","super","this","as","any","null","undefined",
  "true","false","type","interface","enum","implements","abstract",
  "static","private","protected","public","readonly","keyof","satisfies",
  "declare","namespace","module","global","infer","never","unknown",
  "symbol","object","bigint",
]);

const TS_TYPE = new Set([
  "Promise","Record","Partial","Required","Pick","Omit","Exclude",
  "Extract","NonNullable","Readonly","Map","Set","Array","Date",
  "Error","RegExp","Buffer","NodeJS","ReadableStream","WritableStream",
  "Response","Request","Headers","URL","URLSearchParams",
]);

const PY_KW = new Set([
  "import","from","def","return","class","if","elif","else","for","in",
  "while","break","continue","try","except","finally","raise","with",
  "as","pass","yield","async","await","True","False","None","not","and",
  "or","is","lambda","global","nonlocal","del","assert",
]);

const PY_TYPE = new Set([
  "str","int","float","bool","list","dict","tuple","set","Optional",
  "List","Dict","Tuple","Set","Union","Any","Callable","Iterator",
  "Generator","Type","Self",
]);

const BLUE_WORDS = new Set(["import","from","as","export","default","process","os","env"]);

const BASH_COMMANDS = new Set([
  "curl","echo","cd","ls","cat","grep","npm","node","dotnet","git",
  "docker","export","source","python","pip","bash","sh","mkdir","rm",
  "cp","mv","chmod","chown","nano","vim","code","open","nanoai",
  "irm","iex","curl","bun","pnpm","npx","yarn","sudo","brew",
]);

function esc(s: string): string {
  return s.replace(/&/g,"&amp;").replace(/</g,"&lt;").replace(/>/g,"&gt;").replace(/"/g,"&quot;");
}

function highlightHtml(code: string, lang: "typescript" | "python" | "bash"): string {
  const kw = lang === "python" ? PY_KW : TS_KW;
  const tp = lang === "python" ? PY_TYPE : TS_TYPE;

  interface Token { text: string; color?: string }

  const out: Token[] = [];
  let i = 0;

  while (i < code.length) {
    // whitespace
    if (/\s/.test(code[i])) {
      let s = "";
      while (i < code.length && /\s/.test(code[i])) s += code[i++];
      out.push({ text: s });
      continue;
    }

    // comment: //
    if (lang !== "python" && code[i] === "/" && code[i + 1] === "/") {
      let s = "";
      while (i < code.length && code[i] !== "\n") s += code[i++];
      out.push({ text: s, color: C.comment });
      continue;
    }
    // comment: #
    if ((lang === "python" || lang === "bash") && code[i] === "#") {
      let s = "";
      while (i < code.length && code[i] !== "\n") s += code[i++];
      out.push({ text: s, color: C.comment });
      continue;
    }

    // string
    const quote = code[i];
    if (quote === '"' || quote === "'" || (lang === "typescript" && quote === "`")) {
      let s = quote;
      i++;
      while (i < code.length) {
        if (code[i] === "\\") {
          s += code[i++];
          if (i < code.length) s += code[i++];
          continue;
        }
        s += code[i];
        if (code[i] === quote) { i++; break; }
        if (code[i] === "\n") { i++; break; }
        i++;
      }
      out.push({ text: s, color: C.string });
      continue;
    }

    // number
    if (/\d/.test(code[i]) && (i === 0 || /[\s,=+\-*/([{}:;!<>?|&^%]/.test(code[i - 1]))) {
      let s = "";
      while (i < code.length && /[\d.]/.test(code[i])) s += code[i++];
      out.push({ text: s, color: C.number });
      continue;
    }

    // identifier / word
    if (/[a-zA-Z_$\xAA-\uFFFF]/.test(code[i])) {
      let s = "";
      while (i < code.length && /[a-zA-Z0-9_$\xAA-\uFFFF]/.test(code[i])) s += code[i++];

      if (kw.has(s)) {
        out.push({ text: s, color: BLUE_WORDS.has(s) ? C.keyword2 : C.keyword });
        continue;
      }
      if (lang === "typescript" && (s === "string" || s === "number" || s === "boolean" || s === "bigint" || s === "symbol")) {
        out.push({ text: s, color: C.type });
        continue;
      }
      if (tp.has(s)) {
        out.push({ text: s, color: C.type });
        continue;
      }
      if (/^[A-Z][A-Z0-9_]+$/.test(s)) {
        out.push({ text: s, color: C.variable });
        continue;
      }
      if (lang !== "bash" && /^[A-Z]/.test(s) && s.length > 1) {
        out.push({ text: s, color: C.type });
        continue;
      }
      // function call?
      {
        let p = i;
        while (p < code.length && /\s/.test(code[p])) p++;
        if (p < code.length && code[p] === "(") {
          out.push({ text: s, color: C.function });
          continue;
        }
      }
      // bash command at line start
      if (lang === "bash" && BASH_COMMANDS.has(s) && (out.length === 0 || out[out.length - 1].text === "\n" || /^\n$/.test(out[out.length - 1]?.text ?? ""))) {
        out.push({ text: s, color: C.keyword2 });
        continue;
      }
      // env var reference in bash (after $, already handled below)
      out.push({ text: s });
      continue;
    }

    // bash-specific
    if (lang === "bash") {
      // $VAR or ${VAR}
      if (code[i] === "$") {
        let s = "$";
        i++;
        if (i < code.length && code[i] === "{") {
          s += "{"; i++;
          while (i < code.length && code[i] !== "}") s += code[i++];
          if (i < code.length) s += code[i++];
        } else {
          while (i < code.length && /[a-zA-Z0-9_]/.test(code[i])) s += code[i++];
        }
        out.push({ text: s, color: C.keyword2 });
        continue;
      }
      // flag -H, -d, -X etc.
      if (code[i] === "-" && i + 1 < code.length && /[a-zA-Z]/.test(code[i + 1]) && /[\s]/.test(code[i - 1] ?? " ")) {
        let s = "-";
        i++;
        while (i < code.length && /[a-zA-Z]/.test(code[i])) s += code[i++];
        out.push({ text: s, color: C.function });
        continue;
      }
    }

    // property access: .foo
    if (code[i] === "." && i + 1 < code.length && /[a-zA-Z_]/.test(code[i + 1])) {
      let dot = ".";
      i++;
      let prop = "";
      while (i < code.length && /[a-zA-Z0-9_]/.test(code[i])) prop += code[i++];
      if (prop === "env" || prop === "environ") {
        out.push({ text: dot + prop, color: C.keyword2 });
      } else {
        out.push({ text: dot + prop, color: C.property });
      }
      continue;
    }

    // everything else
    out.push({ text: code[i++] });
  }

  return out.map(t => t.color
    ? `<span style="color:${t.color}">${esc(t.text)}</span>`
    : esc(t.text)
  ).join("");
}

// ── Types ──

interface CodeTab {
  id: string;
  label: string;
  code: string;
}

interface CodeBlockProps {
  /** Tabbed code view (Quickstart-style) */
  tabs?: CodeTab[];
  /** Single code string (Gateway-style) */
  code?: string;
  /** Language for syntax highlighting */
  language?: "typescript" | "python" | "bash";
}

// ── Tabbed variant ──

function TabbedCodeBlock({ tabs }: { tabs: CodeTab[] }) {
  const [active, setActive] = useState(tabs[0]?.id ?? "");
  const activeTab = tabs.find((t) => t.id === active) ?? tabs[0];

  if (!activeTab) return null;

  return (
    <div className="border border-[rgba(255,255,255,0.07)] bg-gradient-to-b from-[rgba(29,32,42,0.98)] to-[rgba(24,27,36,0.98)] shadow-[inset_0_1px_0_rgba(255,255,255,0.03)] overflow-hidden rounded-xl">
      <div className="flex border-b border-[rgba(255,255,255,0.06)]" role="tablist">
        {tabs.map((t) => (
          <button
            key={t.id}
            onClick={() => setActive(t.id)}
            className="code-tab"
            role="tab"
            aria-selected={t.id === active}
          >
            {t.label}
          </button>
        ))}
      </div>
      <pre className="m-0 p-[20px_24px] border-0 bg-transparent text-[14px] leading-[1.75] font-[var(--font-mono)] overflow-x-auto" role="tabpanel">
        <code dangerouslySetInnerHTML={{ __html: highlightHtml(activeTab.code, "bash") }} />
      </pre>
    </div>
  );
}

// ── Main export ──

export default function CodeBlock({ tabs, code, language = "bash" }: CodeBlockProps) {
  if (tabs) {
    return <TabbedCodeBlock tabs={tabs} />;
  }
  const html = highlightHtml(code ?? "", language);
  return <code dangerouslySetInnerHTML={{ __html: html }} />;
}
