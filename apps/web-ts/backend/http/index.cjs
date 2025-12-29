process.env.PATHOLOG_LISTEN = "false";

const { createHandler } = require("azure-function-express");

let handler;

module.exports = async function (context, req) {
  try {
    if (!handler) {
      const mod = await import("../dist/server.js");
      const app = mod.app || mod.default;
      handler = createHandler(app);
    }
    return await handler(context, req);
  } catch (err) {
    const message = err && err.message ? err.message : "Unknown error";
    context.res = {
      status: 500,
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ error: "Function initialization failed", detail: message }),
    };
  }
};
