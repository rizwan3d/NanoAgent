const config = require("./config.json");

const ok = config.git.timeoutMs === 15000 && config.git.retries === 2;
console.log(ok ? "OK" : `FAIL:${JSON.stringify(config)}`);
