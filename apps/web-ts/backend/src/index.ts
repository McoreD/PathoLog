
import { app, HttpRequest, HttpResponseInit, InvocationContext } from "@azure/functions";
import http from "http";

let appExpress: any;

async function getExpressApp() {
  if (appExpress) return appExpress;
  
  try {
    // Dynamic import of the compiled Express app
    const mod = await import("./server.js");
    appExpress = mod.app || mod.default;
    return appExpress;
  } catch (err: any) {
    throw new Error(`Failed to load Express app: ${err.message}`);
  }
}

app.http('api', {
  methods: ['GET', 'POST', 'PUT', 'DELETE', 'PATCH', 'OPTIONS'],
  authLevel: 'anonymous',
  route: '{*path}',
  handler: async (request: HttpRequest, context: InvocationContext): Promise<HttpResponseInit> => {
    try {
      context.log(`Http function processed request for url "${request.url}"`);
      
      const distinctApp = await getExpressApp();

      // IMPORTANT: Azure Functions v4 passes a Request object (fetch standard based),
      // but Express expects Node.js http.IncomingMessage and http.ServerResponse.
      // We must bridge them.

      // 1. Create a dummy Node.js request
      const url = new URL(request.url);
      const req = new http.IncomingMessage(null as any);
      req.url = url.pathname + url.search;
      req.method = request.method;
      req.headers = {};
      for (const [key, value] of request.headers) {
        req.headers[key] = value;
      }
      
      // Handle Body
      // For binary/json bodies, we need to read it. 
      // Express body-parser will try to read from the stream if we don't read it here.
      // However, creating a readable stream from request.body might be cleaner.
      // BUT simplify: read as arrayBuffer and push to req.
      const bodyBlob = await request.blob();
      const bodyBuffer = Buffer.from(await bodyBlob.arrayBuffer());
      
      req.push(bodyBuffer);
      req.push(null);

      // 2. Create a dummy Node.js response that captures the output
      return new Promise((resolve, reject) => {
        const res = new http.ServerResponse(req);
        
        let statusCode = 200;
        const headers: Record<string, string> = {};
        const chunks: Buffer[] = [];

        // Override methods to capture status and headers
        res.writeHead = (code: number, ...args: any[]) => {
            statusCode = code;
            const headersObj = args.length > 0 && typeof args[args.length - 1] === 'object' ? args[args.length - 1] : {};
            for(const k in headersObj) headers[k] = String(headersObj[k]);
            return res;
        };

        res.setHeader = (name: string, value: string | number | readonly string[]) => {
            headers[name.toLowerCase()] = String(value);
            return res;
        };
        
        res.getHeader = (name: string) => headers[name.toLowerCase()];

        // Capture body
        res.write = (chunk: any) => {
            chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk));
            return true;
        };

        res.end = (chunk: any) => {
            if (chunk) chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk));
            
            const responseBody = Buffer.concat(chunks);
            
            resolve({
                status: statusCode,
                headers: headers,
                body: responseBody
            });
            return res;
        };

        // Pass to Express
        distinctApp(req, res);
      });

    } catch (err: any) {
      context.error('Function execution error', err);
      return {
        status: 500,
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          error: "Function Handler Error",
          message: err.message,
          stack: err.stack
        })
      };
    }
  }
});
