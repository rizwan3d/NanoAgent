const config = require("./config.json");

const profiles = Array.isArray(config.profiles) ? config.profiles : [];
const dev = profiles.find((profile) => profile.name === "dev");
const prod = profiles.find((profile) => profile.name === "prod");

const ok =
  dev?.timeoutMs === 5000 &&
  dev?.retries === 2 &&
  prod?.timeoutMs === 15000 &&
  prod?.retries === 2;

console.log(ok ? "OK" : `FAIL:${JSON.stringify(config)}`);
