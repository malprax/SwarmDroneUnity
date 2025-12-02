# Swarm Drone Simulation Copyright Mechanical Engineering Hasanuddin University
## Unity 6.2 Project

swarm drone simulation dissertation

## Scripts

- `Drone.cs` — basic random-movement drone with start/search/return/reset.
- `SearchTarget.cs` — renamed target trigger (NOT `Target.cs` to avoid conflicts).
- `SimManager.cs` — manages leader/member assignment, timer, object randomization.

## How to set up the scene



poin penting:
A. Buat visual overlay visited-room

Misal ruangan berubah warna jika sudah dikunjungi.

B. Buat logic “skip ruangan yang sudah dikunjungi”

Drone akan otomatis hanya masuk ke ruangan baru → mempercepat pencarian.

C. Buat “ruangan kosong” skip cepat

Jika ruangan sudah full scanned → drone keluar lewat pintu berikutnya.

D. Visualisasi log kunjungan ruangan

Untuk laporan ICRES / disertasi.

E. Debug garis batas ruangan

Agar mudah melihat kapan trigger aktif.