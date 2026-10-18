# 06 — Governance

**Status:** Draft v0.1

## 1. Kepemilikan

| Area | Pemilik keputusan |
|---|---|
| Posisi, nama, tagline | project owner |
| Sistem visual (`ui.pen`) | project owner + desainer |
| Implementasi token di kode | pemilik sprint UI |
| Nada tulisan produk | pemilik fitur + review owner |
| Materi pemasaran | owner |

## 2. Hierarki sumber kebenaran

1. `ui.pen` — desain visual
2. `docs/brand/01` dan `docs/brand/02` — aturan tertulis
3. `src/DocumentIDE.Web/src/index.css` — implementasi token
4. komponen di `src/DocumentIDE.Web/**`
5. materi pemasaran

Bila ada konflik: **`ui.pen` menang** untuk visual; **spec produk menang** untuk perilaku; **kode menang** untuk apa yang benar-benar dikirim.

## 3. Proses perubahan

```text
usul (issue/diskusi)
  → dampak (visual, kode, materi)
  → review owner
  → perbarui ui.pen lebih dulu (jika visual)
  → perbarui docs/brand
  → perbarui token/kode lewat sprint
  → catat di §5
```

Aturan:

- Jangan mengubah token di kode sebelum variabelnya ada di `ui.pen`.
- Jangan menambah warna/font baru hanya untuk satu layar.
- Perubahan besar (nama, logo, palet) butuh persetujuan owner secara eksplisit.

## 4. Versioning dokumen

| Versi | Perubahan |
|---|---|
| v0.1 (2026-10-18) | struktur awal: brief, guideline, design system, persona, messaging, aset |
| v0.2 (2026-10-18) | warna & tipografi diselaraskan dengan `ui.pen` (slate + blue, Inter/JetBrains Mono) |

Format versi: `vMAJOR.MINOR`.

- **MAJOR** — perubahan posisi, nama, atau logo.
- **MINOR** — penambahan aturan, token, atau dokumen.

## 5. Log perubahan

| Tanggal | Versi | Isi |
|---|---|---|
| 2026-10-18 | v0.1 | Dokumen brand pertama dibuat |
| 2026-10-18 | v0.2 | Palet hangat diganti token asli `ui.pen`; tipografi mengikuti ukuran 9–17 px; NavBar 44 px didokumentasikan |

## 6. Kadens review

| Review | Kapan |
|---|---|
| Konsistensi nama | setiap rilis |
| Kontras & aksesibilitas | sebelum setiap rilis UI |
| Token vs `ui.pen` | setiap sprint UI |
| Pesan & klaim | setiap peluncuran materi |

## 7. Catatan legal

- Nama **Lokamesa** belum diverifikasi mereknya. Lihat `05-assets-and-launch.md` §7.
- Jangan memakai tanda ™ atau ® sebelum pendaftaran selesai.
- Font: Inter dan JetBrains Mono memakai lisensi SIL Open Font License; aman untuk self-host. Jangan mendistribusikan font berlisensi komersial (mis. SF Pro) di dalam paket.
- Aset pihak ketiga (ikon Lucide) wajib menyertakan lisensi di `THIRD-PARTY-NOTICES`.
- Screenshot yang memuat data pengguna harus dianonimkan.

## 8. Pertanyaan terbuka

1. Apakah `Mesa` dan `Loka` akan tampil sebagai label UI, atau hanya di materi pemasaran?
2. Apakah brand memerlukan simbol yang benar-benar digambar (SVG) atau cukup wordmark + keping sederhana?
3. Apakah situs akan memakai Inter saja (konsisten dengan produk) atau menambah font editorial?
4. Siapa yang akan memverifikasi kontras dan aksesibilitas sebelum rilis?
