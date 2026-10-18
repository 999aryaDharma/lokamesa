# Lokamesa — Brand System

**Status:** Draft v0.2 (2026-10-18) — diselaraskan dengan `ui.pen`
**Produk:** Lokamesa
**Mode:** `Mesa` (dokumen) · `Loka` (wildboard / otak kedua)
**Sumber kebenaran visual:** `D:\document-ide\ui.pen` (pen.dev)

Folder ini adalah sumber acuan untuk identitas, bahasa, dan sistem desain Lokamesa.

## Isi

| File | Untuk apa |
|---|---|
| `00-brand-brief.md` | Posisi, janji, audiens, diferensiasi, alasan nama |
| `01-brand-guidelines.md` | Logo, warna, tipografi, tone of voice, do/don't |
| `02-design-system.md` | Token, komponen, motion, aksesibilitas |
| `03-personas.md` | Empat persona + jobs-to-be-done + skenario |
| `04-messaging.md` | Value props, tagline, boilerplate, objection handling |
| `05-assets-and-launch.md` | Checklist aset, kanal, metrik peluncuran |
| `06-governance.md` | Kepemilikan, versi, proses perubahan, catatan legal |

## Aturan penting

1. **Sumber kebenaran visual adalah `ui.pen`.** Dokumen di folder ini merekam token dan aturan yang sudah ada di file desain, bukan mengarang palet baru. Palet yang dipakai adalah **slate + blue** (`accent` `#2563EB`, `mac-accent` `#007AFF`), font **Inter** dan **JetBrains Mono**.
2. **Token di kode mencerminkan file desain.** `src/DocumentIDE.Web/src/index.css` memakai set `mac-*`. Perubahan visual wajib dimulai di `ui.pen`, baru diturunkan ke kode.
3. **Tidak ada web font yang di-fetch saat runtime.** Aplikasi harus tetap jalan offline. Font brand hanya dipakai di materi yang dapat menyertakan font sendiri (situs, PDF, deck).
4. Semua angka warna/ukuran di sini adalah **target**, bukan hasil verifikasi kontras otomatis. Verifikasi akhir wajib dilakukan sebelum publikasi (lihat `02-design-system.md` §7).

## Ringkasan satu paragraf

Lokamesa adalah ruang kerja lokal untuk dokumen serius dan pikiran yang belum jadi. Di **Mesa** kamu mengelola DOCX, Markdown, Git, dan pencarian seperti di IDE. Di **Loka** kamu menuangkan ide, referensi visual, dan tulisan acak tanpa struktur. Semuanya tinggal di mesinmu sendiri, dalam file yang bisa kamu buka tanpa Lokamesa.
