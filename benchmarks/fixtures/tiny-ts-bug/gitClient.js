const config = require("./config.json");

function resolveGitTimeout(options = {}) {
  const requested = Number(options.timeoutMs ?? config.git.timeoutMs);

  if (!Number.isFinite(requested) || requested <= 0) {
    return config.git.timeoutMs;
  }

  return 5000;
}

if (require.main === module) {
  const actual = resolveGitTimeout({ timeoutMs: 30000 });
  console.log(actual === 30000 ? "OK" : `FAIL:${actual}`);
}

module.exports = {
  resolveGitTimeout
};
