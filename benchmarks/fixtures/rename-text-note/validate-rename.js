const fs = require("fs");

const oldExists = fs.existsSync("old.txt");
const newExists = fs.existsSync("new.txt");
const content = newExists ? fs.readFileSync("new.txt", "utf8") : "";

const ok = !oldExists && newExists && content === "hello there\n";
console.log(ok ? "OK" : `FAIL:${JSON.stringify({ oldExists, newExists, content })}`);
