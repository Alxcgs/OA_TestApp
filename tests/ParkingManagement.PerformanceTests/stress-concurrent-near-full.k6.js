import http from "k6/http";
import { check, sleep } from "k6";
import { Rate } from "k6/metrics";

const baseUrl = __ENV.BASE_URL || "http://localhost:5000";
const acceptableStatus = new Rate("acceptable_status");

export const options = {
  stages: [
    { duration: "30s", target: 40 },
    { duration: "1m", target: 80 },
    { duration: "30s", target: 0 },
  ],
  thresholds: {
    http_req_failed: ["rate<0.05"],
    http_req_duration: ["p(95)<2000"],
    acceptable_status: ["rate>0.98"],
  },
};

function getFirstLotId() {
  const lotsRes = http.get(`${baseUrl}/api/lots`);
  if (lotsRes.status !== 200) {
    return null;
  }

  const lots = lotsRes.json();
  if (!Array.isArray(lots) || lots.length === 0) {
    return null;
  }

  return lots[0].id;
}

function getAvailableSpaces(lotId) {
  const detailsRes = http.get(`${baseUrl}/api/lots/${lotId}`);
  if (detailsRes.status !== 200) {
    return 0;
  }

  const details = detailsRes.json();
  return details?.availableSpaces ?? 0;
}

function prefillNearFull(lotId) {
  const maxAttempts = 2000;

  for (let i = 0; i < maxAttempts; i += 1) {
    const available = getAvailableSpaces(lotId);
    if (available <= 1) {
      return;
    }

    const payload = JSON.stringify({
      licensePlate: `PREFILL-${i}`,
      vehicleType: "Car",
    });

    const res = http.post(`${baseUrl}/api/lots/${lotId}/entry`, payload, {
      headers: { "Content-Type": "application/json" },
    });

    if (res.status !== 201 && res.status !== 409) {
      return;
    }
  }
}

function uniquePlate() {
  return `K6S-${__VU}-${__ITER}`;
}

export function setup() {
  const lotId = getFirstLotId();
  if (!lotId) {
    throw new Error("No lot available for stress test.");
  }

  prefillNearFull(lotId);
  return { lotId };
}

export default function (data) {
  const payload = JSON.stringify({
    licensePlate: uniquePlate(),
    vehicleType: "Car",
  });

  const entryRes = http.post(`${baseUrl}/api/lots/${data.lotId}/entry`, payload, {
    headers: { "Content-Type": "application/json" },
  });

  const ok = check(entryRes, {
    "status is 201 or 409": (r) => r.status === 201 || r.status === 409,
  });

  acceptableStatus.add(ok);
  sleep(0.05);
}
