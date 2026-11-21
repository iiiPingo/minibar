"""Writes minibar.ico from the picked logo (I Stocked): 16..256 (32-bit DIB below 256, PNG at 256).
python3 make_ico.py minibar.ico"""
import io, struct, sys
from logos import tile_color, paint_fridge

A, B = '#26262B', '#0E0E10'
sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
entries = []
for s in sizes:
    im = tile_color(s, A, B, paint_fridge).convert('RGBA')
    if s >= 256:
        b = io.BytesIO(); im.save(b, 'PNG'); data = b.getvalue()
    else:
        px = im.tobytes('raw', 'BGRA')
        rows = [px[y * s * 4:(y + 1) * s * 4] for y in range(s)][::-1]
        mask_row = ((s + 31) // 32) * 4
        hdr = struct.pack('<IiiHHIIiiII', 40, s, s * 2, 1, 32, 0, s * s * 4 + mask_row * s, 0, 0, 0, 0)
        data = hdr + b''.join(rows) + b'\x00' * (mask_row * s)
    entries.append((s, data))
out = io.BytesIO()
out.write(struct.pack('<HHH', 0, 1, len(entries)))
off = 6 + 16 * len(entries)
for s, d in entries:
    out.write(struct.pack('<BBBBHHII', s % 256, s % 256, 0, 0, 1, 32, len(d), off)); off += len(d)
for s, d in entries:
    out.write(d)
open(sys.argv[1] if len(sys.argv) > 1 else 'minibar.ico', 'wb').write(out.getvalue())
tile_color(256, A, B, paint_fridge).save('logo256.png')
print('ok', len(out.getvalue()))
