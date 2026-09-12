// Eine Probe des Leuchtturms auf dem eigenen Rechner.
//
// Genau das, was Caddy später tut: /w/ liefert die Seite aus, alles andere geht an
// den Relay - WebSocket-Aufstieg inbegriffen. Dadurch liegen Seite und Relay im
// selben Herkunftsgebiet, und "connect-src 'self'" in der Inhaltsregel greift hier
// wie dort. Ohne diesen Gleichlauf prüfte die Vorschau etwas anderes als den Betrieb.
//
//   node vorschau.mjs <watch.html> <port> <relay-host:port>

import fs from "node:fs";
import http from "node:http";
import net from "node:net";

const [, , PAGE, PORT = "8090", RELAY = "127.0.0.1:8080"] = process.argv;
const [relayHost, relayPort] = RELAY.split(":");

const server = http.createServer((request, response) => {
  if (request.url === "/w" ) {
    response.writeHead(302, { Location: "/w/" });
    response.end();
    return;
  }

  if (request.url.startsWith("/w/")) {
    // Frisch von der Platte lesen: Beim Anschauen wird oft nachgebessert, und ein
    // Neustart für jede Änderung wäre die falsche Art von Sorgfalt.
    const page = fs.readFileSync(PAGE);

    response.writeHead(200, {
      "Content-Type": "text/html; charset=utf-8",
      "Cache-Control": "no-store",
      "X-Content-Type-Options": "nosniff",
      "Referrer-Policy": "no-referrer"
    });

    response.end(page);
    return;
  }

  // Gewöhnliche Aufrufe an den Relay weiterreichen, etwa /health.
  const onward = http.request(
    { host: relayHost, port: Number(relayPort), path: request.url, method: request.method, headers: request.headers },
    upstream => {
      response.writeHead(upstream.statusCode || 502, upstream.headers);
      upstream.pipe(response);
    });

  onward.on("error", () => { response.writeHead(502); response.end("Leuchtturm antwortet nicht.\n"); });
  request.pipe(onward);
});

// Der Aufstieg zum WebSocket geht roh weiter: Die Anfrage noch einmal schreiben,
// dann beide Richtungen aneinanderlegen. Mehr ist ein Durchgang nicht.
server.on("upgrade", (request, socket, head) => {
  const upstream = net.connect(Number(relayPort), relayHost, () => {
    const lines = [`GET ${request.url} HTTP/1.1`];

    for (let i = 0; i < request.rawHeaders.length; i += 2) {
      lines.push(`${request.rawHeaders[i]}: ${request.rawHeaders[i + 1]}`);
    }

    upstream.write(lines.join("\r\n") + "\r\n\r\n");
    if (head && head.length) upstream.write(head);

    upstream.pipe(socket);
    socket.pipe(upstream);
  });

  const give = () => { try { socket.destroy(); } catch { } try { upstream.destroy(); } catch { } };

  upstream.on("error", give);
  socket.on("error", give);
});

server.listen(Number(PORT), "127.0.0.1", () => {
  console.log(`Vorschau auf http://127.0.0.1:${PORT}/w/  (Relay: ${RELAY})`);
});
