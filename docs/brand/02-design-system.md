# 02 — Design System

**Status:** Draft v0.2 — diekstrak langsung dari `ui.pen`
**Sumber kebenaran desain:** `D:\document-ide\ui.pen` (pen.dev)
**Cermin implementasi:** `src/DocumentIDE.Web/src/index.css` (Tailwind v4 `@theme`)
**Stack:** React 19 + Tailwind CSS v4 + `motion`

> Dokumen ini **merekam** sistem yang sudah ada di `ui.pen`, bukan mengarang sistem baru. Setiap nilai di bawah berasal dari variabel dan properti node di file tersebut.

## 1. Prinsip

1. **Permukaan dulu.** UI adalah bidang tenang; konten yang berwarna.
2. **Satu aksen.** Satu warna aksen aktif per layar.
3. **Padat.** Kontrol kecil (10–13 px), jarak antar kelompok lega.
4. **Jujur secara visual.** Status yang terlihat harus benar.
5. **Bisa dipakai tanpa mouse.** Setiap aksi punya jalur keyboard.

## 2. Token

### 2.1 Set utama

```css
--color-bg:            #f8fafc;
--color-surface:       #ffffff;
--color-border:        #e2e8f0;
--color-text-primary:  #0f172a;
--color-text-secondary:#475569;
--color-text-muted:    #64748b;
--color-accent:        #2563eb;
--color-accent-weak:   #eff6ff;
--color-canvas:        #f1f5f9;
--color-success:       #059669;
--color-warning:       #f59e0b;
--color-danger:        #dc2626;
--color-edge:          #94a3b8;
--color-shadow-soft:   #0f172a1a;
--font-body:           Inter;
--font-mono:           JetBrains Mono;
```

### 2.2 Set macOS

```css
--color-mac-toolbar:        #f7f7f8;
--color-mac-sidebar:        #f0f0f2;
--color-mac-inspector:      #f5f5f7;
--color-mac-content:        #ffffff;
--color-mac-fill-2:         #ffffff;
--color-mac-canvas:         #fbfbfd;
--color-mac-hairline:       #00000014;
--color-mac-hairline-strong:#d9d9de;
--color-mac-text:           #1d1d1f;
--color-mac-text-2:         #6e6e73;
--color-mac-text-3:         #8e8e93;
--color-mac-accent:         #007aff;
--color-mac-accent-weak:    #e7f0ff;
--color-mac-chip:           #e6e6e9;
--color-mac-success:        #34c759;
--color-mac-warning:        #ff9f0a;
--color-mac-danger:         #ff3b30;
--color-mac-folder:         #3e8ef7;
--color-mac-folder-front:   #7cb6ff;
```

### 2.3 Aturan pemakaian set

- Satu layar memakai **satu set**. Jangan mencampur `accent` dan `mac-accent`.
- Layar seri tiga-pane memakai `mac-*`; layar seri awal memakai set utama.
- Warna baru harus ditambahkan sebagai variabel di `ui.pen` lebih dulu.

## 3. Tipografi

### 3.1 Keluarga

| Peran | Font | Fallback |
|---|---|---|
| UI/body | Inter | `"SF Pro Text"`, `"Segoe UI"`, `system-ui` |
| Mono | JetBrains Mono | `ui-monospace`, `Consolas` |

### 3.2 Ukuran yang dipakai (terukur di `ui.pen`)

| Ukuran | Jumlah node | Pemakaian |
|---:|---:|---|
| 9 px | 52 | label mikro |
| 10 px | 431 | metadata, caption |
| 11 px | 554 | body kontrol, tabel |
| 12 px | 494 | tombol, teks panel |
| 13 px | 26 | nama produk, judul kecil |
| 14 px | 15 | judul bagian |
| 15 px | 12 | judul bagian besar |
| 16–17 px | 2 | judul layar |

Weight: **400 / 500 / 600**. Line height mengikuti default font kecuali ditimpa.

### 3.3 Aturan

- Jangan memakai ukuran di luar daftar tanpa alasan; tambahkan ke sistem bila perlu.
- Teks wajib punya `fill`; jangan mengandalkan warna default.
- Path, diff, dan angka teknis memakai mono.

## 4. Spacing, radius, dan ukuran

### 4.1 Spacing

Basis 4 px: `2 · 4 · 6 · 8 · 10 · 12 · 16 · 20 · 24 · 32 · 40 · 48`.

Pemakaian terukur:

| Konteks | Nilai |
|---|---|
| Padding NavBar | `[0, 12]` |
| Gap NavBar kiri | `12` |
| Gap NavBar kanan | `8` |
| Padding segmen | `[4, 10]` |
| Gap track segmented | `2`, padding `2` |
| Padding tombol ikon | `6` |
| Padding tombol berlabel | `[5, 10]` |
| Padding panel | `16` |

### 4.2 Radius

| Nilai | Dipakai |
|---:|---|
| 2 | chip kecil |
| 4 | segmen, kontrol kecil |
| 5 | sheet dokumen |
| 6 | tombol, panel, track |
| 7 | folder |
| 10 | dialog |
| 999 | pill |

### 4.3 Tinggi & lebar

| Elemen | Nilai |
|---|---|
| NavBar | 44 px tinggi, 1440 px lebar layar |
| Toolbar (seri macOS) | 52 px |
| Body | `fill_container` |
| Panel kanan | maksimum `max-w-xl` |
| Ikon di NavBar | 14 × 14 |
| Folder | 56 × 48 (badan), 56 × 36 (panel depan) |
| Sheet | 52 × 66 |

### 4.4 Elevasi

| Level | Bayangan | Dipakai |
|---|---|---|
| 0 | none | permukaan dasar |
| 1 | `0 1px 2px #0F172A0F` | kartu, node |
| 2 | `0 2px 6px #0000001F` | folder, sheet, popover |
| 3 | `0 12px 32px #00000024` | dialog |

## 5. Komponen

### 5.1 NavBar (komponen reusable `T2Fm9Z`)

```text
NavBar            1440 × 44 · fill surface · stroke border bottom 1
                  gap 12 · padding [0,12] · space-between · center

├─ NavLeft        gap 12
│  ├─ Product     Inter 13 / 600 · text-primary · "Lokamesa"
│  └─ View switch fill #F1F5F9 · radius 6 · stroke border · gap 2 · padding 2
│     ├─ Segmen aktif    fill surface · radius 4 · stroke border · padding [4,10]
│     └─ Segmen nonaktif radius 4 · padding [4,10]
└─ NavRight       gap 8
   ├─ Tombol ikon fill surface · radius 6 · stroke border · padding 6 · ikon 14
   └─ Tombol berlabel radius 6 · stroke border · gap 6 · padding [5,10] · teks 12/500
```

Ikon memakai **Lucide** (`search`, `git-branch`, `share-2`, …).

### 5.2 Tombol

| Varian | Latar | Teks | Border |
|---|---|---|---|
| Primary | `accent` | `#FFFFFF` | none |
| Secondary | `surface` | `text-primary` | `border` |
| Ghost | transparan | `text-secondary` | none |
| Danger | `danger` | `#FFFFFF` | none |

Tinggi 26/30/34 px. State: hover (gelap 6%), active (gelap 12%), disabled (opacity 0,4), loading (spinner + label tetap).

### 5.3 Input

- Tinggi 26/30 px, radius 4, border 1 px `border`.
- Focus: ring 2 px `accent` + offset 2 px.
- Error: border `danger` + pesan di bawah; placeholder bukan satu-satunya label.

### 5.4 Panel

- Latar `surface`, border `border`, padding 16.
- Header: judul 12–13 px weight 600 + aksi di kanan.
- Panel kanan maksimum `max-w-xl`, dapat di-scroll.

### 5.5 Node canvas

- Latar `surface`, border `border`, radius 6, elevasi 1–2.
- Terpilih: ring 2 px `accent` + `accent-weak`.
- `missing`: garis putus-putus + ikon + label teks (bukan hanya warna).

### 5.6 Chip & filter

- Tinggi 22–24 px, radius 999, latar `mac-chip`/`#F1F5F9`.
- Aktif: `accent-weak` + teks `text-primary` + ikon hapus.
- Dapat dihapus dengan keyboard.

### 5.7 Dialog / Sheet

- `role="dialog"`, `aria-modal="true"`, fokus masuk, `Escape` menutup.
- Footer: sekunder kiri, primer kanan.
- Destruktif: tombol danger + ringkasan dampak (jumlah file, jalur).

### 5.8 Tabel / list

- Header 11 px weight 500 `text-secondary`, dapat di-sort.
- Baris 32 px; seleksi `accent-weak`.
- Kolom status memakai teks + ikon.

### 5.9 Empty state

- Satu kalimat + satu aksi.
- Tidak ada ilustrasi besar yang mendorong aksi keluar layar.

## 6. State

| State | Aturan |
|---|---|
| Loading | skeleton/spinner di tempat konten akan muncul |
| Empty | pesan + aksi |
| Error | apa yang gagal + langkah berikutnya + coba lagi |
| Partial | sebut bagian yang berhasil dan gagal |
| Disabled | opacity 0,4 + alasan lewat `aria-describedby` |

## 7. Aksesibilitas

Target **WCAG 2.2 AA**.

| Item | Target |
|---|---|
| Teks normal | ≥ 4,5:1 |
| Teks besar (≥ 24 px, atau ≥ 18,66 px bold) | ≥ 3:1 |
| Komponen UI & grafik | ≥ 3:1 |
| Target sentuh | ≥ 24 × 24 px |
| Zoom | tetap usable pada 200% dan reflow 400% |
| Fokus | terlihat, 2 px, ≥ 3:1 |

Aturan:

- Jangan menghapus outline tanpa pengganti.
- Tombol ikon wajib `aria-label`.
- Hasil async memakai `aria-live` yang tepat.
- Status tidak boleh disampaikan hanya lewat warna.
- Canvas wajib punya alternatif non-canvas (list view).

**Angka kontras di dokumen ini adalah target desain, bukan hasil uji.** Verifikasi dengan axe DevTools / WebAIM sebelum publikasi.

## 8. Motion

| Durasi | Pemakaian |
|---|---|
| 120 ms | hover, fokus, toggle |
| 200 ms | panel, dialog |
| 300 ms | perpindahan besar |

Easing `cubic-bezier(0.2, 0.8, 0.2, 1)`. Tanpa bounce. Hormati `prefers-reduced-motion`.

## 9. Inventaris layar di `ui.pen`

| Layar | Status |
|---|---|
| App — Canvas view | ada |
| App — List view | ada |
| App — Graph view | ada |
| App — Batch operations | ada |
| App — Source control | ada |
| App — Markdown editor (macOS) | ada |
| App — History & undo (macOS) | ada |
| App — Share manager (macOS) | ada |
| Dialogs & overlays (macOS) | ada |
| NavBar (component) | ada |

Layar lain (worktrees, search, DOCX diff, dsb.) tercantum di `docs/design/UI-DESIGN-BRIEF.md`.

## 10. Implementasi

- Tailwind v4: daftarkan token di blok `@theme` (lihat `index.css`).
- Jangan menambah web font runtime; gunakan fallback sistem.
- Komponen baru mengikuti pola yang ada (`SheetShell`, `NavBar`, `ViewSwitcher`).
- Ekspor frame dari `ui.pen` dengan `Export([frameId], "png", …)` untuk dokumentasi.
- Perubahan token produk = perubahan UI = butuh sprint + spec.
