Sip. Kalau Step-1 (1 drone, non-physics, random + wall avoidance) sudah jalan stabil, urutan step selanjutnya secara keseluruhan begini (biar kamu simpan jadi roadmap):

Roadmap Besar Simulasi Swarm Drone (Non-Physics)

Step-1 — ✅ DONE

1 drone bergerak random + hindar tembok (tanpa target, tanpa room, tanpa leader).

⸻

Step-2 — Target Object (Red Box) + Deteksi

Goal: drone bisa mendeteksi kotak merah.
	•	Buat targetObject (Sprite merah + Collider2D)
	•	Drone cek:
	•	Trigger (OnTriggerEnter2D) atau
	•	Raycast / CircleCast ke arah depan untuk detect target
	•	Saat kena target → log: FOUND TARGET

Output: drone bisa “nemu target” kapan pun.

⸻

Step-3 — Arena Room 1–3 + HomeBase Marker

Goal: ada struktur HomeBase + 3 ruangan yang jelas.
	•	Tambah HomeBase (Transform)
	•	Tambah RoomZone (Collider2D trigger) untuk Room1/2/3
	•	Saat drone masuk room → log: Enter Room X

Output: sistem tahu drone sedang di ruangan mana.

⸻

Step-4 — 3 Drone Spawn + Assignment Room (Random)

Goal: 3 drone muncul, lalu tiap drone ditugaskan ke room berbeda.
	•	SimManager punya list drones
	•	Random assign: Drone1→Room?, Drone2→Room?, Drone3→Room?
	•	Drone punya assignedRoomId

Output: 3 drone punya tujuan area masing-masing.

⸻

Step-5 — Search Behavior Per Room (lebih “cerdas” dari random)

Goal: drone menjelajah ruangan bukan cuma muter nabrak.
Implementasi sederhana yang stabil:
	•	“wall follow” + “random turn timer”
	•	“anti-stuck escape”
	•	Optional: “zigzag sweep” di dalam room

Output: drone bisa cover area ruangan dengan lebih merata.

⸻

Step-6 — Leader Election + Komunikasi Member→Leader

Goal: salah satu drone jadi leader.
	•	Pilih leader (misal Drone1 default)
	•	Jika member yang menemukan target:
	•	member kirim sinyal ke leader (event / callback)
	•	leader yang menulis log sistem

Output: aturan notifikasi leader/member sesuai skenario kamu berjalan.

⸻

Step-7 — Wait 10 Seconds (Freeze Position)

Goal: setelah target ditemukan:
	•	semua drone stop 10 detik (tahan posisi)
	•	tampilkan “found marker” (opsional)

Output: sesuai tahapan misi.

⸻

Step-8 — Return Home (Semua Drone Pulang)

Goal: setelah 10 detik, semua drone:
	•	navigasi kembali ke HomeBase
	•	saat semua sudah sampai → End

Output: satu siklus misi lengkap.

⸻

Step-9 — UI Interaksi (Radio Room & Kill Leader)

Goal: user bisa:
	1.	pilih ruangan mana yang berisi red box
	2.	“disable leader” → leader mati total → auto pilih leader baru

Output: sistem robust jika leader gagal (novelty kamu juga di sini).

⸻

Step-10 — Logging + Data untuk Riset

Goal: siap untuk disertasi/paper:
	•	log step per drone
	•	waktu temuan
	•	jarak tempuh
	•	jumlah tabrakan/escape
	•	perbandingan performa (Monte Carlo nanti)

⸻

Kita lanjut ke “Step-2” sekarang ya

Step-2 paling kecil tapi krusial: targetObject + deteksi + event FOUND.

Kalau kamu mau, kirim:
	•	apakah target pakai Collider Trigger atau mau Raycast detect (lebih “robotic”)
Aku langsung tulis file Drone.Target.cs + update minimal SimManager untuk Step-2.