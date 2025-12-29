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

    // azure-function-express is callback-based, so we must wrap it in a Promise
    // to prevent the async function from returning before the response is ready.
    return await new Promise((resolve, reject) => {
      // Hijack context.done to resolve the promise
      const originalDone = context.done;
      context.done = (err, result) => {
        if (originalDone) originalDone(err, result);
        
        if (err) {
          context.res = {
            status: 500,
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
              error: "Adapter Error",
              message: err instanceof Error ? err.message : String(err),
              stack: err instanceof Error ? err.stack : undefined
            })
          };
          resolve(context.res);
          return;
        }

        // Azure Functions v4 async model expects the response to be returned.
        // azure-function-express might pass it as 'result' or set 'context.res'.
        const response = result || context.res;
        
        if (!response) {
          // Fallback if the library failed to produce a response object
          context.res = {
            status: 500,
            body: "Debug: handler completed but no response object found (result or context.res)"
          };
          resolve(context.res);
        } else {
          resolve(response);
        }
      };
      
      // Execute the handler
      handler(context, req);
    });
  } catch (err) {
    const message = err && err.message ? err.message : "Unknown error";
    context.res = {
      status: 500,
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ error: "Function initialization failed", detail: message }),
    };
  }
};
