import http from "k6/http";
import { check } from "k6";
import { Counter } from "k6/metrics";

const hosty = ["http://localhost:5126", "http://localhost:5127"];
const dni = 28;

const st201 = new Counter("rez_201_zapisana");
const st409 = new Counter("rez_409_zajete");
const st503 = new Counter("rez_503_blokada");
const inne = new Counter("rez_inny_kod");

http.setResponseCallback(http.expectedStatuses(201, 409));

export const options = {
  scenarios: {
    rezerwacje: {
      executor: "constant-arrival-rate",
      rate: 100,
      timeUnit: "1s",
      duration: "100s",
      preAllocatedVUs: 100,
      maxVUs: 400
    }
  },
  thresholds: {
    rez_inny_kod: ["count==0"]
  }
};

export default function () {
  const host = hosty[__VU % 2];
  const dzien = new Date(Date.UTC(2026, 11, 1 + Math.floor(Math.random() * dni)));
  const data = dzien.toISOString().slice(0, 10);
  const prac = Math.random() < 0.5 ? "p1" : "p2";
  const godz = String(9 + Math.floor(Math.random() * 8)).padStart(2, "0") + ":00";

  const cialo = JSON.stringify({ LokalId: "lok1", PracownikId: prac, Data: data, Godzina: godz });
  const odp = http.post(host + "/rezerwacje", cialo, {
    headers: { "Content-Type": "application/json", "X-Klient-Id": `k${__VU}` },
    tags: { inst: __VU % 2 === 0 ? "A" : "B" }
  });

  if (odp.status === 201) st201.add(1);
  else if (odp.status === 409) st409.add(1);
  else if (odp.status === 503) st503.add(1);
  else inne.add(1);

  check(odp, { "201, 409 albo 503": o => [201, 409, 503].includes(o.status) });
}
