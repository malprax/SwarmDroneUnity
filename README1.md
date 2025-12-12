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


metode yang mau di riset subuh?
✅ Versi simpel: Flood Fill room-by-room

✅ Versi advance: BFS/Dijkstra pathfinding antar ruangan

✅ Versi full: SLAM indoor mini (graph slam)


metode simple Flood Fill room by room
Dengan update di atas:
	•	✅ Stop button memanggil SimManager.StopSimulation() → tiap Drone StopDrone() → mode Idle → Rigidbody velocity 0 → drone benar-benar berhenti.
	•	✅ Grid mapping disimpan ke _maze dengan arah dunia (Up/Down/Left/Right).
	•	✅ BuildFloodFillFromHome() membuat matriks jarak ke home (flood-fill, gaya micromouse).
	•	✅ Mode ReturnHome di Drone menggunakan GetReturnDirectionFor() untuk berjalan mengikuti gradient flood-fill.
	•	🔜 Tinggal finetuning:
	•	gridOrigin & cellSize di Inspector biar pas dengan arena kuning,
	•	kecepatan return,
	•	posisi homeBase di tengah HomeZone.

Kalau mau, habis ini kita bisa:
	•	Tambah gizmo visual flood-fill di Scene View (lihat angka/jalur),
	•	Ekspor maze map + flood matrix ke CSV untuk lampiran artikel ICRES.

Kalau ada bagian Drone.FixedUpdate yang kawan bingung mau dipindah ke HandleSearchMode(), kirim saja potongan FixedUpdate() sekarang, nanti saya rapikan langsung.











Betul, dari log kelihatan jelas kenapa dia “bolak-balik di ruangan lain” dan kesulitan masuk Room 2.

1. Apa yang terjadi dari log
	•	Hampir semua GridStep seperti ini:

[GridStep] Drone 1 cell=(2,5) walls L=False,R=False,F=False,B=False decision=FWD

Artinya semua sensor (Left/Right/Front/Back) selalu False → drone merasa tidak ada dinding dekat dia, jadi dia cuma terus maju sampai nabrak collider lalu mantul (lihat banyak [Collision]).

	•	Karena dia hanya ganti arah saat tabrakan, tidak ada “logika belok ke pintu”. Masuk ke Room 2 hanya terjadi kalau kebetulan arah dan posisinya pas dengan pintu. Itu yang membuat dia lama berputar-putar dulu.

Jadi masalah utama:
➡️ Sensor jarak tidak “melihat” dinding / pintu dengan benar.

⸻

2. Perbaiki dulu sensor RangeSensor2D

Lakukan untuk ke-4 sensor (Front, Back, Left, Right) di prefab Drone:
	1.	Cek posisi sensor:
	•	Klik Drone di Hierarchy → buka child SensorFront, SensorBack, SensorLeft, SensorRight (atau nama sejenis).
	•	Pastikan posisi lokalnya:
	•	SensorFront di depan badan drone (sumbu +Y atau +X tergantung orientasi),
	•	SensorBack di belakang,
	•	SensorLeft di kiri,
	•	SensorRight di kanan.
	•	Jangan ditempel di tengah badan, geser sedikit keluar (± setengah diameter drone).
	2.	Cek Range / Distance di RangeSensor2D:
	•	Di Inspector, ubah maxDistance / detectionDistance (nama variabel bisa sedikit beda) ke nilai yang cukup besar, misalnya 0.8 – 1.2.
	•	Saat ini kemungkinan terlalu pendek → drone sudah nabrak duluan sebelum sinar menyentuh dinding.
	3.	Cek Layer Mask:
	•	Di komponen RangeSensor2D, pastikan LayerMask hanya/termasuk layer Wall.
	•	Pastikan semua objek tembok (wall_left, wall_right, wall_room2, dll.) benar-benar berada di layer Wall (lihat di Inspector, bagian Layer).
	4.	Aktifkan gizmos sensor (kalau ada):
	•	Kalau di RangeSensor2D ada OnDrawGizmos, aktifkan tombol Gizmos di Scene view.
	•	Pastikan garis raycast sensor terlihat dan mengenai tembok saat drone dekat dinding.

Setelah ini jalan lagi simulasi → perhatikan di Console:
kalau sensor sudah benar, log akan berubah jadi misalnya:

[GridStep] Drone 1 cell=(8,6) walls L=True,R=False,F=False,B=True decision=TURN_LEFT

Tidak selalu False semua.

⸻

3. Sedikit tweak agar lebih mudah masuk Room 2

Kalau sensor sudah bekerja tapi drone masih “malas” masuk ruangan kecil, ada dua tweak ringan yang bisa kamu lakukan:
	1.	Perbesar pintu Room 2 sedikit:
	•	Di Scene, seleksi dinding yang jadi sisi pintu (vertical wall dekat pintu).
	•	Geser beberapa piksel (0.1–0.2 unit) supaya celah pintu sedikit lebih lebar dari diameter collider drone + margin.
	•	Pastikan collider dinding tidak menutup celah.
	2.	Tambah logika “lebih suka belok ke ruang yang belum dikunjungi” (opsional, di kode Drone):
Di fungsi keputusan arah (bagian yang sekarang memilih FWD, TURN_LEFT, dll.), tambahkan prioritas:
	•	Kalau ada cabang ke arah cell yang belum pernah dikunjungi, pilih arah itu dulu.
	•	Baru kalau semua sudah visited, boleh lanjut maju / random.
Secara konsep (pseudo):

// misalnya di Drone/SimManager, saat pindah grid cell
List<Direction> candidates = GetAvailableDirectionsFromSensors();
Direction best = ChooseDirectionToUnvisitedCell(candidates);
if (best == Direction.None) best = ChooseRandom(candidates);
currentDirection = best;

Ini akan membuat drone lebih cepat “masuk ke gang” yang menuju Room 2 begitu dia lewat di depan pintu.

⸻

4. Ringkas langkah praktis
	1.	Perbaiki sensor:
	•	Posisi sensor di ujung badan drone.
	•	Range/distance ≈ 0.8–1.2.
	•	LayerMask → Wall.
	2.	Tes lagi:
	•	Lihat di Console apakah walls L/R/F/B sudah kadang True, bukan selalu False.
	•	Amati apakah drone sudah bisa menghindari tembok tanpa banyak tabrakan.
	3.	Kalau masih sering muter di luar Room 2:
	•	Sedikit lebarkan pintu Room 2.
	•	(Opsional) Tambah logika pilih arah ke cell yang belum dikunjungi.

Coba dulu langkah sensor + layer mask ini, biasanya begitu sensor “melek”, perilaku eksplorasi langsung jauh lebih masuk akal dan pintu Room 2 jadi lebih mudah dijelajahi. Kalau sudah kamu tes dan masih ada pola aneh tertentu, kirimkan screenshot Scene + Inspector salah satu sensor, nanti kita bedah lagi bareng 👍




Betul, GridOrigin-nya sudah pas di HomeBase 👍
Masalah yang kamu lihat sekarang bukan lagi di origin, tapi di cara “pulang” drone-nya.

🔍 Apa yang sedang terjadi?

Begitu target ditemukan di Room 1, log kita kira-kira begini:
	•	targetFound = true
	•	returning = true
	•	Drone masuk mode return.
	•	Di mode ini, di script yang kita buat, arah drone dihitung langsung:

Vector2 dirToHome = (homeBasePos - rb.position).normalized;
rb.AddForce(dirToHome * throttle);

Artinya:

Drone selalu mendorong dirinya ke arah garis lurus dari posisinya sekarang ke HomeBase.

Di layout arena kamu, HomeBase berada di balik dinding (di bawah koridor).
Secara geometri, garis lurus dari Room 1 ke HomeBase menembus dinding → makanya:
	•	drone maju ke arah home,
	•	nabrak dinding,
	•	pantul / geser sedikit,
	•	lalu maju lagi ke arah garis lurus itu,
	•	dan terus berulang → terlihat seperti “nyundul dinding” di sebelah homezone.

Jadi:
	•	GridOrigin sudah benar,
	•	tapi algoritma pulangnya masih “straight line”, belum pakai path / lorong.

⸻

🎯 Tujuan: Drone pulang lewat pintu, bukan nembus tembok

Untuk pulang dengan benar, drone harus:
	1.	Keluar dulu dari Room 1 lewat DoorR1,
	2.	Masuk koridor,
	3.	Baru turun ke HomeZone.

Ada dua opsi perbaikan:

⸻

🅰 Opsi Praktis (pakai Door Waypoint)

Kita sudah punya DoorR1, DoorR2, DoorR3 di Hierarchy.
Sekarang kita pakai itu sebagai waypoint pulang.

1. Tambah field di Drone.cs

Di bagian atas (fields), tambahkan:

[Header("Return Home Waypoints")]
public Transform doorR1;
public Transform doorR2;
public Transform doorR3;
public Transform homeBase;    // assign HomeBase transform

2. Tambah state kecil untuk return

Masih di Drone.cs:

private enum ReturnPhase { None, ToDoor, ToHome }
private ReturnPhase returnPhase = ReturnPhase.None;

3. Fungsi untuk mulai pulang

Dipanggil dari SimManager saat target ditemukan:

public int currentRoomId = -1; // sudah kita update lewat SimManager.OnDroneEnterRoom

public void StartReturnMission()
{
    returningHome = true;
    atHome = false;

    // Tentukan waypoint pertama berdasarkan room saat ini
    if (currentRoomId == 0)      // Room1
        currentWaypoint = doorR1;
    else if (currentRoomId == 1) // Room2
        currentWaypoint = doorR2;
    else if (currentRoomId == 2) // Room3
        currentWaypoint = doorR3;
    else                         // sudah di Home room
        currentWaypoint = homeBase;

    returnPhase = (currentWaypoint == homeBase) 
        ? ReturnPhase.ToHome 
        : ReturnPhase.ToDoor;
}

Jangan lupa deklarasi:

private Transform currentWaypoint;

4. Update gerakan di FixedUpdate

Di FixedUpdate (atau fungsi update movement-mu), saat returningHome == true:

private void HandleReturnHome()
{
    if (homeBase == null) return;

    Vector2 targetPos;

    if (returnPhase == ReturnPhase.ToDoor && currentWaypoint != null)
    {
        targetPos = currentWaypoint.position;
    }
    else
    {
        targetPos = homeBase.position;
    }

    Vector2 dir = (targetPos - rb.position).normalized;
    rb.AddForce(dir * throttle);

    float dist = Vector2.Distance(rb.position, targetPos);

    // Threshold boleh kamu sesuaikan
    if (returnPhase == ReturnPhase.ToDoor && dist < 0.2f)
    {
        // Sudah sampai pintu → lanjut ke HomeBase
        returnPhase = ReturnPhase.ToHome;
    }
    else if (returnPhase == ReturnPhase.ToHome && dist < 0.2f)
    {
        // Sudah sampai Home
        returningHome = false;
        atHome = true;
        rb.velocity = Vector2.zero;
    }
}

Lalu di FixedUpdate:

void FixedUpdate()
{
    if (returningHome)
    {
        HandleReturnHome();
        return;
    }

    // ... logika searching seperti biasa
}

5. Assign reference di Inspector

Untuk setiap Drone:
	•	doorR1 → drag DoorR1
	•	doorR2 → drag DoorR2
	•	doorR3 → drag DoorR3
	•	homeBase → drag HomeBase

Sekarang, setelah target ketemu:
	•	SimManager panggil drone.StartReturnMission();
	•	Drone akan:
	•	dari Room 1 → ke DoorR1 dulu,
	•	lalu dari DoorR1 → langsung ke HomeBase,
	•	tanpa nyundul dinding.

⸻

🅱 Opsi Lanjutan (pakai Grid / Micromouse Pathfinding)

Ini yang lebih “ilmiah” untuk disertasi:
	•	GridOrigin + cell log yang sudah kita buat dipakai untuk:
	•	bangun peta occupancy,
	•	jalankan BFS / flood-fill dari homeCell,
	•	simpan jalur terpendek,
	•	saat pulang drone mengikuti jalur cell itu.

Ini kita bisa kerjakan nanti sebagai versi lanjutan begitu behavior dasar sudah stabil.

⸻

Ringkasannya
	•	Bukan salah GridOrigin — sudah pas di HomeBase ✅
	•	Drone nabrak karena algoritma pulang masih “garis lurus ke HomeBase” ❌
	•	Solusi cepat: gunakan DoorR1/R2/R3 sebagai waypoint saat return, seperti kode di atas.
	•	Solusi ilmiah berikutnya: pakai pathfinding di grid (micromouse style).

Kalau kamu mau, langkah berikutnya saya bisa bantu:
	1.	Menunjukkan contoh pemanggilan StartReturnMission() dari SimManager setelah targetFound.
	2.	Atau, lanjut langsung ke versi BFS flood-fill supaya bisa kamu pakai sebagai novelty di bab metode 🌱



    Semua rencana tahapan SwarmDroneSim sudah aku tampilkan di sebelah kanan sebagai dokumen:
	•	Tahap 0 – Baseline & bereskan proyek ✅
	•	Tahap 1 – Single drone gerak & hindari dinding ✅
	•	Tahap 2 – Misi Search → Return + distance field ✅ (tinggal uji target)
	•	Tahap 3 – Micromouse cell memory ✅ (sudah tersambung ke ReportGridStep)
	•	Tahap 4 – Room/region identification tanpa RoomZone 🔄
	•	Tahap 5 – Multi-drone & role (leader/member) 🔄
	•	Tahap 6 – Statistik & visualisasi untuk disertasi 🔄
	•	Plus bagian Catatan Konsistensi dan Catatan Revisi supaya setiap perubahan besar kita tulis dulu di sana.



aduuh kacau kawanku, ganti saja ini metode semuanya. Saya mau begini pada saat awal start drone melihat semua ruangan dan menandai posisi nya di homebase terhadap sumbu x dan y, kemudian  bergerak menuju tengah ruangan dan memetakan yang mana ada pintu menuju keruangan lain, terus memutuskan pintu mana yang akan dimasuki, drone bergerak langsung dari posisi tengah ruangan menuju pintu yang akan dimasukinya, pada saat sampai depan pintu juga drone otomatis menandai koordinat pintu tadi terhadap sumbu x dan y, drone masuk kedalam pintu ruangan tersebut serta berusaha mengambil posisi tengah ruangan dan menscan apakah ada pintu atau tidak, jika drone tidak menemukan pintu, maka drone kembali ke pintu sebelumnya langsung karena sudah ada koordinat yang disimpannya. Dan misalnya dalam satu kali scan ruangan, drone mendapati ada tiga pintu, drone menandai dari jauh dulu memastikan ada tiga pintu yang akan dilalui, dan memasukinya satu persatu bergiliran semuanya dan kembali ke home base


Saat StartSimulation(), lakukan:

STEP WAJIB: 1 — Build grid occupancy map

Drone memang belum bergerak, tapi arena static bisa kita raycast secara otomatis.

STEP WAJIB: 2 — Flood fill untuk room clustering

STEP WAJIB: 3 — Deteksi doorway

STEP WAJIB: 4 — Buat graph antar room

STEP WAJIB: 5 — Buat route drone → waypoints pertama

Baru drone boleh StartSearch.
