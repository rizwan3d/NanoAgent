"use client";

import { comparisonHeaders, comparisonRows } from "@/lib/data";

export default function ComparisonTable() {
  return (
    <>
      <div className="overflow-x-auto border border-[var(--color-border)] rounded-[var(--radius)] bg-[var(--color-surface)]">
        <table className="w-full border-collapse min-w-[760px] text-[14px]">
          <thead>
            <tr>
              {comparisonHeaders.map((header, i) => (
                <th
                  key={header}
                  className={`sticky top-0 px-[10px] py-3 text-center text-[13px] font-bold border-b border-[var(--color-border-2)] ${
                    i === 0
                      ? "text-left text-[var(--color-text)] bg-[var(--color-bg-2)]"
                      : i === 1
                      ? "text-[var(--color-text)] bg-gradient-to-b from-[rgba(124,140,255,0.22)] to-[rgba(124,140,255,0.06)]"
                      : "text-[var(--color-text-mut)] bg-[var(--color-bg-2)]"
                  }`}
                  scope="col"
                >
                  {header}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {comparisonRows.map((row) => (
              <tr key={row.feature} className="hover:bg-[rgba(255,255,255,0.025)]">
                <th
                  scope="row"
                  className="text-left font-semibold text-[var(--color-text)] pl-[18px] whitespace-nowrap px-[10px] py-3 border-b border-[var(--color-border)]"
                >
                  {row.feature}
                </th>
                {row.values.map((val, i) => (
                  <td
                    key={`${row.feature}-${i}`}
                    className={`px-[10px] py-3 text-center border-b border-[var(--color-border)] text-[16px] ${
                      i === 0 ? "bg-[rgba(124,140,255,0.07)]" : ""
                    } ${
                      val === "✓"
                        ? "text-[var(--color-acc-1)] font-bold"
                        : val === "◐"
                        ? "text-[#febc2e] font-bold"
                        : val === "–"
                        ? "text-[var(--color-text-dim)]"
                        : "text-[var(--color-text-dim)]"
                    }`}
                  >
                    {val}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className="mt-4 text-center text-[13px] text-[var(--color-text-mut)]">
        <span className="text-[var(--color-acc-1)] font-bold">✓</span> Yes ·{" "}
        <span className="text-[#febc2e] font-bold">◐</span> Partial ·{" "}
        <span className="text-[var(--color-text-dim)] font-bold">–</span> No
      </p>
    </>
  );
}
