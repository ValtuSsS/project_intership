import http from "k6/http";
import { check } from "k6";

const hosty = ["http://localhost:5126", "http://localhost:5127"];
const sciez = "/lokale/lok1/pracownicy/p1/wolne?data=2026-12-16";

export const options = {
  scenarios: {
    odczyt: {
      executor: "constant-arrival-rate",
      rate: 250,
      timeUnit: "1s",
      duration: "40s",
      preAllocatedVUs: 100,
      maxVUs: 1000
    }
  },
  thresholds: {
    checks: ["rate==1"]
  }
};

export default function () {
  const host = hosty[__VU % 2];
  const inst = __VU % 2 === 0 ? "A" : "B";
  const odp = http.get(host + sciez, {
    headers: { "X-Klient-Id": `k${__VU}` },
    tags: { inst: inst }
  });
  check(odp, {
    "status 200": o => o.status === 200,
    "lista godzin": o => o.status === 200 && Array.isArray(o.json())
  });
}
