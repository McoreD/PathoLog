process.env.PATHOLOG_LISTEN = "false";

const { createHandler } = require("azure-function-express");

let handler;

module.exports = async function (context, req) {
  if (!handler) {
    const mod = await import("../dist/server.js");
    const app = mod.app || mod.default;
    handler = createHandler(app);
  }
  return handler(context, req);
};
