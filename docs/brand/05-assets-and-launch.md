# 05 — Aset & Peluncuran

**Status:** Draft v0.1

## 1. Inventaris aset

| Aset | Format | Ukuran | Status |
|---|---|---|---|
| Logo primer (lockup horizontal) | SVG | vektor | belum |
| Logo sekunder (vertikal) | SVG | vektor | belum |
| Symbol / ikon | SVG | 24, 32, 48 | belum |
| Monogram `lm` | SVG | vektor | belum |
| Wordmark | SVG | vektor | belum |
| Ikon aplikasi Windows | ICO + PNG | 16–256 | belum |
| Ikon aplikasi macOS | ICNS + PNG | 16–1024 | belum |
| Favicon | SVG + PNG | 32, 180 | belum |
| Gambar OG | PNG | 1200 × 630 | belum |
| Banner sosial | PNG | 1500 × 500 | belum |
| Screenshot produk | PNG | 2× skala | sebagian (dari `ui.pen`) |
| Deck ringkas | PDF | 16:9 | belum |
| One-pager | PDF | A4 | belum |
| Press kit | ZIP | — | belum |

Screenshot wajib berasal dari UI asli (`ui.pen` atau aplikasi berjalan). Dilarang memakai mockup fitur yang belum ada.

## 2. Konvensi nama file

```text
lokamesa-<jenis>-<varian>-<ukuran>.<ext>

lokamesa-logo-horizontal.svg
lokamesa-symbol-48.png
lokamesa-og-1200x630.png
lokamesa-screenshot-canvas-2x.png
```

- Huruf kecil, tanda hubung, tanpa spasi.
- Varian: `horizontal`, `vertical`, `mono`, `inverse`.
- Jangan memakai kata `final`, `v2`, `new`.

## 3. Spesifikasi ekspor

| Media | Catatan |
|---|---|
| SVG | outline teks; tanpa font yang tidak di-embed |
| PNG | latar transparan untuk logo; 2× untuk screenshot |
| PDF | font ter-embed; warna RGB |
| Deck | 16:9; aman di zona 5% tepi |

## 4. Kanal & pesan

| Kanal | Fokus | Aset minimum |
|---|---|---|
| Situs | janji + bukti visual | hero, 3 screenshot, 1 diagram |
| GitHub | kejelasan teknis | README, arsitektur, lisensi |
| Product Hunt | satu kalimat + 3 bullet | logo, OG, 4 screenshot |
| Komunitas developer Indonesia | cerita lokal-first | 1 unggahan + tautan |
| Email | personal | 3 paragraf + 1 tautan |

## 5. Checklist peluncuran

### Sebelum

- [ ] nama & ejaan konsisten di semua materi (`Lokamesa`, `Mesa`, `Loka`)
- [ ] logo dan warna sudah diverifikasi kontras
- [ ] screenshot dari build nyata, bukan mockup
- [ ] README menyebut batasan (sync belum ada, backup manual)
- [ ] tidak ada klaim terlarang (`04-messaging.md` §7)
- [ ] tautan unduhan berfungsi
- [ ] halaman privasi/keamanan tersedia bila ada telemetri

### Saat

- [ ] satu pesan utama per kanal
- [ ] balasan cepat untuk pertanyaan "apakah cloud?"
- [ ] pantau crash/issue pertama

### Setelah

- [ ] kumpulkan 5–8 wawancara persona (`03-personas.md`)
- [ ] catat keberatan berulang → perbarui `04-messaging.md`
- [ ] tinjau metrik §6

## 6. Metrik

| Tahap | Metrik | Target awal |
|---|---|---|
| Aktivasi | workspace dibuat + folder source terpasang | 60% dalam sesi pertama |
| Aktivasi | wildboard pertama dibuat | 40% |
| Aha | dokumen dibuka/diedit dalam 24 jam | 50% |
| Retensi | kembali di hari ke-7 | 25% |
| Retensi | kembali di hari ke-30 | 12% |
| Kepercayaan | tiket "file saya di mana?" | 0 |
| Brand | ejaan nama benar di sebutan pengguna | 90% |

Angka ini adalah hipotesis, bukan janji. Perbarui setelah data nyata.

## 7. Status nama & domain

| Item | Status |
|---|---|
| `lokamesa.com` | tidak ada A record (indikasi tersedia; **belum** diverifikasi di registrar) |
| `lokamesa.app` | tidak ada A record |
| `lokamesa.id` | tidak ada A record |
| `lokamesa.dev` | tidak ada A record |
| `lokamesa.io` | tidak ada A record |
| `lokamesa.co` | tidak ada A record |
| Repositori GitHub `lokamesa` | 0 hasil |
| Pemakaian web "lokamesa" | tidak ditemukan (hanya frasa tak terkait) |

Verifikasi wajib sebelum publikasi:

- [ ] registrar (Cloudflare/Namecheap) untuk `.com`, `.app`, `.id`
- [ ] merek dagang DJKI kelas 9 & 42
- [ ] USPTO/EUIPO bila go-international
- [ ] handle sosial dan organisasi GitHub
- [ ] tes ucap: 3 orang menuliskan ejaan setelah mendengar nama
