import http from "k6/http";
import { check, sleep } from "k6";
import { Rate } from "k6/metrics";

const baseUrl = __ENV.BASE_URL || "http://localhost:5000";
const successFlow = new Rate("success_flow");

export const options = {
  vus: 30,
  duration: "2m",
  thresholds: {
    // With Serializable isolation + jittered retry, failed rate should be low.
    http_req_failed: ["rate<0.05"],
    // p95 target: 2 s is realistic for 30 concurrent VUs on a single-node Postgres.
    http_req_duration: ["p(95)<2000"],
    // ≥90 % of full entry→exit→pay flows should succeed.
    success_flow: ["rate>0.90"],
  },
};

function pickLotId() {
  const res = http.get(`${baseUrl}/api/lots`);
  if (res.status !== 200) return null;

  const lots = res.json();
  if (!Array.isArray(lots) || lots.length === 0) return null;

  // Pick the lot with the most available spaces so we don't exhaust it fast.
  const sorted = lots.slice().sort((a, b) => b.availableSpaces - a.availableSpaces);
  return sorted[0].id;
}

// Each VU generates a globally unique plate using VU number + iteration counter.
function uniquePlate() {
  return `K6L-${__VU}-${__ITER}`;
}

export function setup() {
  const lotId = pickLotId();
  if (!lotId) throw new Error("No lot available for performance test.");
  return { lotId };
}

export default function (data) {
  // Stagger VU starts: spread the initial burst across 0–2 s.
  if (__ITER === 0) {
    sleep((__VU / 30) * 2);
  }

  const payload = JSON.stringify({
    licensePlate: uniquePlate(),
    vehicleType: "Car",
  });

  // ── Entry ──────────────────────────────────────────────────────────────────
  const entryRes = http.post(
    `${baseUrl}/api/lots/${data.lotId}/entry`,
    payload,
    { headers: { "Content-Type": "application/json" } }
  );

  const entryOk = check(entryRes, {
    "entry is 201": (r) => r.status === 201,
  });

  if (!entryOk) {
    successFlow.add(false);
    // Back off before retrying the next iteration so the server can recover.
    sleep(0.5 + Math.random() * 0.5);
    return;
  }

  const ticket = entryRes.json();
  const ticketId = ticket?.id;

  // ── Exit ───────────────────────────────────────────────────────────────────
  const exitRes = http.post(`${baseUrl}/api/tickets/${ticketId}/exit`);
  const exitOk = check(exitRes, {
    "exit is 200": (r) => r.status === 200,
  });

  if (!exitOk) {
    successFlow.add(false);
    sleep(0.3);
    return;
  }

  // ── Pay ────────────────────────────────────────────────────────────────────
  const payRes = http.post(`${baseUrl}/api/tickets/${ticketId}/pay`);
  const payOk = check(payRes, {
    "pay is 200": (r) => r.status === 200,
  });

  successFlow.add(entryOk && exitOk && payOk);

  // Short think-time between successful full cycles.
  sleep(0.2 + Math.random() * 0.3);
}
