# 03 — User Personas

**Status:** Draft v0.1
**Dasar:** arsitektur produk (local-first, canvas, DOCX/Word, Markdown, Git, batch) dan masalah yang dijelaskan di `00-brand-brief.md`
**Catatan:** persona ini adalah hipotesis kerja, bukan hasil riset lapangan. Validasi dengan 5–8 wawancara sebelum dipakai untuk keputusan besar.

## Ringkasan

| Persona | Peran | Mode utama | Pemicu utama |
|---|---|---|---|
| **Rani** | Konsultan proposal & tender | Mesa | Dokumen berulang, versi, bukti perubahan |
| **Budi** | Technical lead / penulis teknis | Mesa | Markdown + Git + dokumen yang harus akurat |
| **Sari** | Peneliti / analis | Loka + Mesa | Sumber berserakan, referensi visual, sintesis |
| **Dimas** | Operasi dokumen | Mesa | Pekerjaan batch berulang dan aman |

---

## 1. Rani — Konsultan Proposal & Tender

**34 tahun · konsultan independen · Jakarta · Windows · bekerja sendiri dengan 2–3 klien sekaligus**

### Hari biasa

Rani menulis 3–5 proposal per bulan untuk klien korporat dan pemerintah. Semua dalam DOCX, dengan template, lampiran, dan revisi dari klien. Ia menyimpan file di folder proyek, mengirim versi lewat email, dan sering ditanya: *"yang mana versi terakhir?"*

### Tujuan

- Menghasilkan proposal yang konsisten dan cepat dari template.
- Tahu persis apa yang berubah antar versi sebelum dikirim.
- Menemukan kembali klausul atau angka dari proposal lama.

### Frustrasi

- Menyalin-tempel antar file dan kehilangan format.
- Nama file `proposal_final_v3_revisi_final.docx`.
- Tidak ada catatan mengapa sebuah bagian diubah.
- Alat catatan tidak bisa menangani DOCX; alat dokumen tidak punya tempat untuk ide.

### Jobs to be done

1. *Saat klien meminta revisi,* saya ingin melihat perubahan teks antar versi, supaya saya bisa menjawab tanpa membuka dua file berdampingan.
2. *Saat memulai proposal baru,* saya ingin memakai bagian dari proposal lama, supaya saya tidak menulis dari nol.
3. *Saat mengirim,* saya ingin yakin tidak ada placeholder atau angka lama yang tertinggal.

### Bagaimana Lokamesa membantu

| Kebutuhan | Fitur |
|---|---|
| Lihat perubahan | Git diff DOCX yang sadar dokumen (`SCM-05`) |
| Pakai ulang isi | Pencarian terstruktur + batch replace dengan pratinjau |
| Catatan di samping dokumen | **Loka**: sticky note dan sheet di sekitar dokumen |
| Kirim versi bersih | Ekspor PDF |

### Sinyal sukses

- Waktu menyiapkan revisi turun ≥ 30%.
- Tidak ada lagi `final_v3`.
- Ia bisa menjelaskan satu perubahan ke klien dalam < 1 menit.

### Kutipan

> "Saya tidak butuh aplikasi baru. Saya butuh satu tempat di mana saya bisa melihat dokumen dan alasan saya mengubahnya."

### Yang membuat ia pergi

- Harus upload ke cloud untuk bisa review.
- DOCX-nya berubah format setelah dibuka di aplikasi lain.
- Tidak ada riwayat perubahan.

---

## 2. Budi — Technical Lead / Penulis Teknis

**40 tahun · lead engineer di perusahaan produk · Bandung · Windows + WSL · menulis dokumentasi arsitektur dan ADR**

### Hari biasa

Budi menulis Markdown di repo, me-review PR, dan menjaga dokumentasi tetap sinkron dengan kode. Ia memakai Git setiap hari dan menolak alat yang menyembunyikan file.

### Tujuan

- Dokumentasi yang benar, bisa di-diff, dan tinggal di repo.
- Menemukan keputusan lama (ADR) dengan cepat.
- Menghubungkan dokumen dengan kode dan catatan desain.

### Frustrasi

- Aplikasi catatan menyimpan isi di database proprietary.
- Wiki terpisah dari repo, jadi selalu ketinggalan.
- Mencari relasi antar dokumen secara manual.

### Jobs to be done

1. *Saat menulis ADR,* saya ingin melihat dokumen terkait dan backlink-nya, supaya keputusan tidak berdiri sendiri.
2. *Saat me-review perubahan,* saya ingin membaca diff Markdown yang rapi, supaya saya bisa menyetujui tanpa membuka terminal.
3. *Saat ide muncul di tengah coding,* saya ingin menangkapnya tanpa meninggalkan konteks.

### Bagaimana Lokamesa membantu

| Kebutuhan | Fitur |
|---|---|
| File tetap di repo | Markdown source-first, tanpa format proprietary |
| Riwayat & diff | Git source control + diff Markdown |
| Relasi | Wikilink, backlink, graph |
| Tangkap ide | **Loka** + Quick Capture |

### Sinyal sukses

- Dokumentasi di repo tetap jadi satu-satunya sumber kebenaran.
- Ia bisa menelusuri satu keputusan dari ADR ke dokumen terkait dalam 2 klik.
- Ia tidak perlu membuka aplikasi catatan terpisah.

### Kutipan

> "Kalau saya tidak bisa `grep` isinya, itu bukan milik saya."

### Yang membuat ia pergi

- Isi tersimpan di database yang tidak bisa di-diff.
- Aplikasi memaksa commit otomatis.
- Markdown diubah diam-diam.

---

## 3. Sari — Peneliti / Analis

**29 tahun · analis riset di lembaga riset · Yogyakarta · macOS + Windows · mengerjakan 2 proyek riset sekaligus**

### Hari biasa

Sari mengumpulkan PDF, artikel, tangkapan layar, dan catatan wawancara. Ia menyusun sintesis di dokumen panjang. Referensinya berserakan di folder, bookmark, dan aplikasi catatan.

### Tujuan

- Melihat seluruh bahan satu proyek sekaligus, secara visual.
- Menghubungkan temuan dengan sumbernya.
- Menulis sintesis tanpa kehilangan jejak referensi.

### Frustrasi

- Menemukan kembali "gambar yang saya simpan minggu lalu" sangat sulit.
- Catatan dan PDF terpisah.
- Struktur folder memaksa keputusan sebelum ia siap.

### Jobs to be done

1. *Saat mengumpulkan bahan,* saya ingin menaruhnya di satu bidang tanpa membuat folder, supaya saya bisa mengelompokkan nanti.
2. *Saat menulis sintesis,* saya ingin melihat kutipan dan gambar di samping dokumen.
3. *Saat menyiapkan presentasi,* saya ingin mengambil referensi visual dengan cepat.

### Bagaimana Lokamesa membantu

| Kebutuhan | Fitur |
|---|---|
| Kumpulkan tanpa struktur | **Loka**: sticky, sheet, stack, gambar (1.9.0) |
| Lihat semua sekaligus | Canvas + list view |
| Hubungkan temuan | Wikilink, graph, backlink |
| Cari cepat | Pencarian + filter metadata |
| Rapikan nanti | Multi-select, group drag, tidy |

### Sinyal sukses

- Tidak ada lagi folder "temp" yang tak pernah dibuka.
- Ia bisa menemukan satu referensi dalam < 30 detik.
- Bahan pindah dari Loka ke dokumen sintesis tanpa copy-paste berantakan.

### Kutipan

> "Saya tidak mau memilih folder sebelum saya tahu isinya apa."

### Yang membuat ia pergi

- Harus menyusun struktur sebelum mulai.
- Gambar tidak bisa ditempel.
- Pencarian tidak menemukan isi catatan.

---

## 4. Dimas — Operasi Dokumen

**27 tahun · staf operasional di tim legal/administrasi · Surabaya · Windows · menangani ratusan file per bulan**

### Hari biasa

Dimas mengganti nama file sesuai nomor kontrak, mengganti nama klien di banyak dokumen, dan mengekspor PDF untuk arsip. Kesalahan kecil bisa berdampak besar.

### Tujuan

- Melakukan perubahan massal dengan aman dan bisa diperiksa.
- Tidak ada file yang berubah tanpa ia tahu.
- Hasil yang konsisten dan bisa diulang.

### Frustrasi

- Find & replace yang tidak bisa di-review sebelum diterapkan.
- Takut merusak file.
- Tidak ada catatan apa yang sudah diubah.

### Jobs to be done

1. *Saat mengganti teks di 80 file,* saya ingin melihat pratinjau per file dan per kemunculan, supaya saya bisa membatalkan yang salah.
2. *Saat rename massal,* saya ingin aturan yang deterministik, supaya hasilnya bisa diprediksi.
3. *Saat terjadi kesalahan,* saya ingin mengembalikan ke keadaan sebelumnya.

### Bagaimana Lokamesa membantu

| Kebutuhan | Fitur |
|---|---|
| Pratinjau aman | Batch plan + review per occurrence |
| Deterministik | Aturan rename eksplisit, tanpa fuzzy match |
| Bisa dibatalkan | Recovery snapshot + undo |
| Riwayat | Batch results & history |

### Sinyal sukses

- Nol file berubah tanpa pratinjau.
- Semua perubahan bisa di-undo.
- Waktu pengerjaan turun tanpa menambah risiko.

### Kutipan

> "Saya tidak takut pekerjaannya banyak. Saya takut ada satu file yang salah dan tidak ketahuan."

### Yang membuat ia pergi

- Perubahan langsung diterapkan tanpa pratinjau.
- Tidak ada undo.
- Aturan rename tidak bisa diprediksi.

---

## 5. Kebutuhan lintas persona

| Kebutuhan | Rani | Budi | Sari | Dimas |
|---|---|---|---|---|
| File tetap lokal & format terbuka | ●●● | ●●● | ●● | ●● |
| Canvas untuk melihat keseluruhan | ●● | ●● | ●●● | ● |
| Pencarian kuat | ●●● | ●●● | ●●● | ●● |
| Riwayat & undo | ●●● | ●●● | ●● | ●●● |
| Pratinjau sebelum perubahan massal | ●● | ● | ● | ●●● |
| Tempat untuk ide lepas | ●● | ●●● | ●●● | ● |
| Git/diff | ●● | ●●● | ● | ● |

Skala: ● rendah · ●● sedang · ●●● tinggi

## 6. Persona yang **bukan** target sekarang

**Pembeli enterprise kolaborasi** — membutuhkan SSO, audit organisasi, kontrol admin terpusat, dan kolaborasi real-time. Kebutuhannya nyata, tetapi belum menjadi fokus produk ini. Menargetkannya sekarang akan mendorong fitur cloud-first yang bertentangan dengan posisi merek.
