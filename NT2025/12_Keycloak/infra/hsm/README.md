### Utimaco Security Server Evalution V6.2.0.0

### Base image

Why Debian-slim over Ubuntu?

Smaller and cleaner runtime than Ubuntu, still glibc (your sim needs glibc ≥ 2.17 — Debian 12 easily satisfies that).

Fewer background packages → faster pulls, smaller attack surface.

You don’t need Ubuntu-specific tooling here; you’re just running a vendor binary + a shell script.

Why not Alpine?

Alpine uses musl, and vendor PKCS#11 bits/simulators are almost always built for glibc. You’d end up adding a glibc shim (brittle).

Mixing bases is fine

Your other containers can be Alpine/UBI/etc. Containers talk over the network; they don’t need the same OS base.

### Steps

- files copied from 'Software/Linux/Simulator/sim5_linux/bin' to 'utimaco-sim' folder
