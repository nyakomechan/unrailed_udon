#!/usr/bin/env python3
# pacing_sim.py - pacing model for the Unrailed-like loop.
# Models: prep phase (stockpile + pre-lay), legs between stations,
# rail recycling from behind the train, crafting wagon throughput.
# Distances in tiles (1 tile = 1 m), times in seconds.

import argparse

DEFAULTS = dict(
    baseSpeed=1.2,        # train tiles/s at leg 1 (TrainController.baseSpeed)
    speedPerStation=0.12, # added per station reached
    stationTiles=40,      # tiles between stations (GameManager.stationTiles)
    craftSeconds=4.0,     # wagon busy time per craft batch
    railsPerCraft=1,      # rails produced per batch (wood1+iron1 per batch)
    stockCap=8,           # railStockMax
    carryRails=1,         # rails a player can carry per trip (design lever)
    starterTiles=30,      # TrackManager.StarterLength
    mineWood=1.0,         # treeHp 2 * 0.5s tick
    mineIron=2.0,         # rockHp 4 * 0.5s tick
    carry=5.0,            # one-way carry time with an item (estimate)
    pull=1.0,             # rail pull-up interact time
    place=1.0,            # rail place use time
    withdraw=1.0,         # take rail from stack wagon
    stopSeconds=10.0,     # station stop (GameManager.stationStopSeconds)
    prepSeconds=120.0,    # free prep time before lever pull
    maxStations=12,
    dt=0.05,
)

ROLES = ("idle", "craft_feed", "place_behind", "place_stock")


class Player:
    __slots__ = ("freeAt", "action")

    def __init__(self):
        self.freeAt = 0.0
        self.action = "idle"


class Sim:
    def __init__(self, cfg, players):
        self.c = dict(DEFAULTS)
        self.c.update(cfg)
        self.players = [Player() for _ in range(players)]
        self.t = 0.0
        self.speed = self.c["baseSpeed"]
        self.stock = 0
        self.ahead = self.c["starterTiles"]
        self.behind = 0            # recoverable tiles behind the train (2-tile safety margin applied)
        self.tilesPassed = 0
        self.totalDist = 0.0
        self.legDist = 0.0
        self.stations = 0
        self.running = False       # train moving (leg in progress)
        self.prepLeft = self.c["prepSeconds"]
        self.stopLeft = 0.0
        self.wagonFreeAt = 0.0
        self.wagonOuts = []        # (time, qty) pending craft outputs
        self.feeders = 0           # players currently feeding the wagon
        self.failed = False
        self.trace = []

    def feed_time(self):
        c = self.c
        return c["mineWood"] + c["mineIron"] + 2.0 * c["carry"]

    def behind_cycle(self):
        c = self.c
        return c["pull"] * c["carryRails"] + 2.0 * c["carry"] + c["place"] * c["carryRails"]

    def stock_cycle(self):
        c = self.c
        return c["withdraw"] * c["carryRails"] + c["carry"] + c["place"] * c["carryRails"]

    def decide(self):
        c = self.c
        n = c["carryRails"]
        if self.running:
            horizon = max(6.0, self.speed * 15.0)
            if self.ahead < horizon:
                if self.stock >= 1:
                    return "place_stock"
                if self.behind >= 1:
                    return "place_behind"
                return "craft_feed"
            if self.feeders < 1 and self.stock < c["stockCap"]:
                return "craft_feed"
            if self.behind >= 1:
                return "place_behind"
            if self.stock >= 1:
                return "place_stock"
            return "idle"
        if self.stock < c["stockCap"]:
            return "craft_feed"
        if self.stock >= 1:
            return "place_stock"
        return "idle"

    def step(self):
        c = self.c
        dt = c["dt"]
        self.t += dt

        # wagon outputs
        i = 0
        while i < len(self.wagonOuts):
            wt, qty = self.wagonOuts[i]
            if wt <= self.t:
                self.stock = min(c["stockCap"], self.stock + qty)
                self.wagonOuts.pop(i)
            else:
                i += 1

        # train
        if self.running:
            self.legDist += self.speed * dt
            self.totalDist += self.speed * dt
            while int(self.totalDist) > self.tilesPassed:
                self.tilesPassed += 1
                self.ahead -= 1
                if self.ahead < 0:
                    self.failed = True
                    self.trace.append(
                        (self.t, "FAIL tile %d" % self.tilesPassed, self.speed,
                         self.stock, self.ahead, self.behind))
                    return
                if self.tilesPassed > 2:
                    self.behind += 1
            if self.legDist >= c["stationTiles"]:
                self.legDist -= c["stationTiles"]
                self.stations += 1
                self.speed += c["speedPerStation"]
                self.running = False
                self.stopLeft = c["stopSeconds"]
                self.trace.append(
                    (self.t, "STATION %d" % self.stations, self.speed,
                     self.stock, self.ahead, self.behind))
                if self.stations >= c["maxStations"]:
                    return
        elif self.stopLeft > 0.0:
            self.stopLeft -= dt
            if self.stopLeft <= 0.0:
                self.running = True
                self.trace.append(
                    (self.t, "DEPART", self.speed,
                     self.stock, self.ahead, self.behind))
        else:
            self.prepLeft -= dt
            if self.prepLeft <= 0.0:
                self.running = True
                self.trace.append(
                    (self.t, "LEVER", self.speed,
                     self.stock, self.ahead, self.behind))

        # players
        for p in self.players:
            if self.t < p.freeAt:
                continue
            if p.action == "craft_feed":
                start = max(self.t, self.wagonFreeAt)
                self.wagonFreeAt = start + c["craftSeconds"]
                self.wagonOuts.append((self.wagonFreeAt, c["railsPerCraft"]))
                self.feeders -= 1
            elif p.action == "place_behind":
                self.ahead += c["carryRails"]
            elif p.action == "place_stock":
                self.ahead += c["carryRails"]
            p.action = self.decide()
            if p.action == "craft_feed":
                if self.feeders >= 3:  # wagon saturates; extra feeders go place
                    p.action = "place_stock" if self.stock >= 1 else "idle"
                else:
                    self.feeders += 1
                    p.freeAt = self.t + self.feed_time()
            if p.action == "place_behind":
                self.behind -= min(c["carryRails"], self.behind)
                p.freeAt = self.t + self.behind_cycle()
            elif p.action == "place_stock":
                self.stock -= min(c["carryRails"], self.stock)
                p.freeAt = self.t + self.stock_cycle()
            elif p.action == "idle":
                p.freeAt = self.t + 1.0

    def run(self):
        guard = 0
        while not self.failed and self.stations < self.c["maxStations"]:
            self.step()
            guard += 1
            if guard > 50_000_000:
                break
        return self.stations


def simulate(cfg, players, trace=False):
    sim = Sim(cfg, players)
    stations = sim.run()
    if trace:
        for row in sim.trace:
            print("  t=%6.1fs %-10s v=%.2f stock=%d ahead=%d behind=%d" % row)
        print("  -> stations reached: %d%s" %
              (stations, " (capped)" if stations >= sim.c["maxStations"] else ""))
    return stations


def sweep():
    results = []
    for baseSpeed in (0.2, 0.25, 0.3, 0.4, 0.5, 0.7):
        for carryRails in (1, 2):
            for stationTiles in (30, 40):
                cfg = dict(baseSpeed=baseSpeed, carryRails=carryRails,
                           stationTiles=stationTiles,
                           craftSeconds=3.0, railsPerCraft=2)
                r = [simulate(cfg, p) for p in (1, 2, 4, 8)]
                results.append((r, cfg))
    print("=== stations reached (craft3s x2, +0.12/station, prep120s) ===")
    print("speed carry tiles |  1p  2p  4p  8p")
    for r, cfg in sorted(results, key=lambda x: (x[1]["baseSpeed"], x[1]["carryRails"], x[1]["stationTiles"])):
        print(" %.2f   x%d    %3d  | %3d %3d %3d %3d" %
              (cfg["baseSpeed"], cfg["carryRails"], cfg["stationTiles"],
               r[0], r[1], r[2], r[3]))
    print()
    print("=== current values (v1.2 craft4s x1 tiles40) ===")
    for p in (1, 2, 4, 8):
        print("players=%d -> %d stations" % (p, simulate(dict(), p)))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--trace", action="store_true")
    ap.add_argument("--players", type=int, default=1)
    for k, v in DEFAULTS.items():
        ap.add_argument("--" + k, type=type(v), default=None)
    args = ap.parse_args()
    if args.trace:
        cfg = {k: getattr(args, k) for k in DEFAULTS if getattr(args, k) is not None}
        print("cfg:", {k: v for k, v in sorted(cfg.items())}, "players:", args.players)
        simulate(cfg, args.players, trace=True)
    else:
        sweep()


if __name__ == "__main__":
    main()
