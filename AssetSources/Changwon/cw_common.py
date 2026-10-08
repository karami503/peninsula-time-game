# Shared world definition for the Changwon open world data. Must match ChangwonGeo in C#.
import math
ORIGIN_LON, ORIGIN_LAT = 128.62, 35.20
SX = 111320.0 * math.cos(math.radians(ORIGIN_LAT)); SZ = 111320.0
LON0, LON1, LAT0, LAT1 = 128.33, 128.86, 35.01, 35.41
CELL = 16.0  # terrain/land grid spacing in metres
def to_xz(lon, lat): return ((lon - ORIGIN_LON) * SX, (lat - ORIGIN_LAT) * SZ)
def to_lonlat(x, z): return (ORIGIN_LON + x / SX, ORIGIN_LAT + z / SZ)
X0, Z0 = to_xz(LON0, LAT0); X1, Z1 = to_xz(LON1, LAT1)
X0, Z0 = math.floor(X0 / CELL) * CELL, math.floor(Z0 / CELL) * CELL
NX = int(math.ceil((X1 - X0) / CELL)) + 1; NZ = int(math.ceil((Z1 - Z0) / CELL)) + 1
CHUNK = 512.0
