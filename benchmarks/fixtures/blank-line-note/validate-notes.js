const fs = require("fs");

const actual = fs.readFileSync("notes.md", "utf8");
const expected = "# Deployment Notes\n\nTODO: ship cache\n\n## Checks\n\n- keep spacing\n- keep headings\n";

console.log(actual === expected ? "OK" : `FAIL:${JSON.stringify(actual)}`);
