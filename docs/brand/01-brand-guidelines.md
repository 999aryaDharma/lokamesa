# 01 — Brand Guidelines

**Status:** Draft v0.2 — diselaraskan dengan `ui.pen` (2026-10-18)
**Sumber sistem visual:** `D:\document-ide\ui.pen` (pen.dev). Dokumen ini mendokumentasikan, bukan menggantikan.

## 1. Logo

### 1.1 Konsep

Simbol Lokamesa adalah **tiga keping bertumpuk yang membentuk mesa** — bidang datar di atas, tempat objek diletakkan. Tiga keping itu merujuk pada **stack** (pengelompokan di canvas), **mesa/plateau** (tempat berpijak), dan **tile** (objek kecil yang membentuk satu kesatuan).

```text
        ┌──────────────────┐
        │                  │   ← bidang atas (mesa)
        └──────────────────┘
          ┌──────────────┐
          │              │       ← keping kedua
          └──────────────┘
            ┌──────────┐
            │          │           ← keping ketiga
            └──────────┘
```

### 1.2 Varian

| Varian | Pemakaian |
|---|---|
| **Wordmark** (`Lokamesa`) | header situs, dokumen, slide |
| **Symbol** (tiga keping) | ikon aplikasi, avatar, favicon |
| **Lockup horizontal** | materi utama |
| **Lockup vertikal** | sampul, stempel |
| **Monogram** (`lm`) | ruang sangat sempit |

### 1.3 Konstruksi

- Grid kelipatan **8 px**.
- Tinggi simbol = **1×** tinggi huruf kapital wordmark; jarak simbol–wordmark = **0,5×**.
- Sudut keping: **4 px** (sama dengan radius segmen di `ui.pen`).
- Ketebalan garis minimal **1,5 px** pada ukuran 24 px.

### 1.4 Ruang kosong & ukuran minimum

- Ruang kosong minimum = tinggi huruf kapital "L".
- Wordmark: 96 px (digital) / 24 mm (cetak).
- Symbol: 24 × 24 px (digital) / 8 mm (cetak).

### 1.5 Latar & penyalahgunaan

| Latar | Logo |
|---|---|
| `surface` `#FFFFFF` | `text-primary` `#0F172A` |
| `bg` `#F8FAFC` | `text-primary` |
| `accent` `#2563EB` | `#FFFFFF` |

Jangan: memiringkan, memelarkan, memberi bayangan, menambahkan gradien, atau menaruh logo di atas canvas yang ramai tanpa scrim.

## 2. Warna

### 2.1 Sumber kebenaran

Semua warna berasal dari variabel di `ui.pen`. Ada **dua set** yang berdampingan:

1. **Set utama** (dipakai layar `App — Canvas view` dan seri awal)
2. **Set macOS** (`mac-*`, dipakai seri tiga-pane: Canvas, List, Graph, Markdown, History, Share, Dialogs)

Jangan mencampur dua set dalam satu permukaan. Satu layar memakai satu set.

### 2.2 Set utama

| Token | Nilai | Peran |
|---|---|---|
| `bg` | `#F8FAFC` | latar aplikasi |
| `surface` | `#FFFFFF` | panel, kartu, kontrol |
| `border` | `#E2E8F0` | garis dan batas kontrol |
| `text-primary` | `#0F172A` | teks utama |
| `text-secondary` | `#475569` | teks sekunder |
| `text-muted` | `#64748B` | teks tersier, placeholder |
| `accent` | `#2563EB` | aksen utama, seleksi, tautan |
| `accent-weak` | `#EFF6FF` | tint seleksi |
| `canvas` | `#F1F5F9` | permukaan canvas |
| `success` | `#059669` | status berhasil |
| `warning` | `#F59E0B` | status peringatan |
| `danger` | `#DC2626` | status bahaya |
| `edge` | `#94A3B8` | garis relasi/graf |
| `shadow-soft` | `#0F172A1A` | bayangan lembut |

### 2.3 Set macOS (tiga-pane)

| Token | Nilai | Peran |
|---|---|---|
| `mac-toolbar` | `#F7F7F8` | toolbar terpadu |
| `mac-sidebar` | `#F0F0F2` | sidebar source-list |
| `mac-inspector` | `#F5F5F7` | inspector kanan |
| `mac-content` | `#FFFFFF` | konten dan kartu |
| `mac-fill-2` | `#FFFFFF` | kontrol di atas konten |
| `mac-canvas` | `#FBFBFD` | permukaan canvas/graf |
| `mac-hairline` | `#00000014` | pemisah 1 px |
| `mac-hairline-strong` | `#D9D9DE` | batas kontrol |
| `mac-text` | `#1D1D1F` | teks utama |
| `mac-text-2` | `#6E6E73` | teks sekunder |
| `mac-text-3` | `#8E8E93` | teks tersier |
| `mac-accent` | `#007AFF` | aksen sistem |
| `mac-accent-weak` | `#E7F0FF` | seleksi |
| `mac-chip` | `#E6E6E9` | track segmented control |
| `mac-success` | `#34C759` | status berhasil |
| `mac-warning` | `#FF9F0A` | status peringatan |
| `mac-danger` | `#FF3B30` | status bahaya |

### 2.4 Warna objek

| Objek | Nilai |
|---|---|
| Folder (badan) | `#3E8EF7` |
| Folder (panel depan) | `#7CB6FF` |
| Aksen `.docx` | `mac-accent` `#007AFF` |
| Aksen `.md` | `mac-success` `#34C759` |
| Trace jendela macOS | `#FF5F57` / `#FEBC2E` / `#28C840` |

### 2.5 Aturan

- **Satu aksen per layar.** `accent` dan `mac-accent` tidak dipakai bersamaan.
- Status selalu disertai teks/ikon, bukan warna saja.
- `edge` hanya untuk garis relasi, bukan teks.
- Jangan menambah warna baru tanpa menambah variabel di `ui.pen` terlebih dahulu.
- Angka kontras di sini adalah target; verifikasi tetap wajib (lihat `02-design-system.md` §7).

## 3. Tipografi

### 3.1 Keluarga

| Peran | Font | Catatan |
|---|---|---|
| UI dan body | **Inter** | dipakai di seluruh `ui.pen` |
| Mono | **JetBrains Mono** | diff, kode, path, angka teknis |

Tidak ada font ketiga. Aplikasi tidak mem-fetch web font saat runtime; gunakan fallback sistem (`"SF Pro Text"`, `"Segoe UI"`, `system-ui`, `Consolas`).

### 3.2 Skala UI (sesuai `ui.pen`)

Ukuran yang benar-benar dipakai di file desain:

| Ukuran | Pemakaian |
|---|---|
| 9 px | label mikro, badge |
| 10 px | metadata, caption |
| 11 px | body kontrol, tabel |
| 12 px | teks panel, tombol |
| 13 px | nama produk di NavBar, judul kecil |
| 14–15 px | judul bagian |
| 16–17 px | judul layar |

Weight: **400** (body), **500** (label/tombol), **600** (judul, nama produk).

### 3.3 Skala pemasaran (terpisah dari UI)

Materi brand (situs, deck, PDF) boleh memakai skala yang lebih besar dan tidak terikat grid UI:

| Peran | Ukuran | Weight |
|---|---|---|
| Display | 40–56 px | 600 |
| Heading 1 | 28 px | 600 |
| Heading 2 | 20 px | 600 |
| Body L | 18 px | 400 |
| Body | 16 px | 400 |
| Caption | 14 px | 400 |

### 3.4 Aturan

- Body materi brand minimal **16 px**; UI produk tetap mengikuti skala §3.2.
- Jangan memakai kapital semua untuk kalimat panjang.
- Maksimum dua weight per blok.
- Path, angka, dan diff selalu mono.

## 4. Tone of voice

### 4.1 Prinsip

1. **Tenang.** Tidak perlu bersuara keras.
2. **Konkret.** Sebut apa yang terjadi.
3. **Manusiawi.** Sapa pengguna sebagai orang.
4. **Jujur.** Akui batasan; jangan menjual yang belum ada.

### 4.2 Sapaan

| Konteks | Sapaan |
|---|---|
| UI produk, pemasaran | **kamu** |
| Legal, penagihan, keamanan | **Anda** |
| Error | netral, tanpa menyalahkan |

### 4.3 Contoh

| Hindari | Pakai |
|---|---|
| "Supercharge your workflow with AI!" | "Tulis, atur, dan temukan dokumenmu di satu tempat." |
| "Seamless cloud collaboration" | "Bagikan lewat tautan eksplisit. File tetap di mesinmu." |
| "Oops! Something went wrong :(" | "File tidak bisa disimpan. Cek apakah folder masih bisa ditulis, lalu coba lagi." |
| "Revolutionary second brain" | "Tempat untuk ide yang belum jadi." |
| "Unlimited everything" | "Tanpa batas jumlah dokumen di workspace lokalmu." |

### 4.4 Kata yang dihindari

`revolutionary`, `seamless`, `supercharge`, `AI-powered` (tanpa penjelasan), `game-changer`, `unlimited` (tanpa syarat), `effortless`, `magic`.

## 5. Imagery

- **Foto:** permukaan nyata — meja, kertas, cahaya alami, tangan yang bekerja.
- **Tangkapan layar:** UI asli dari `ui.pen` atau aplikasi, bukan mockup fitur fiktif.
- **Ilustrasi:** garis tipis + satu bentuk aksen; motif keping/tile dan bidang datar.
- **Ikon:** **Lucide**, ukuran 14 px di UI (sesuai NavBar), sudut 2 px, grid 24 px.

## 6. Motion

| Durasi | Pemakaian |
|---|---|
| 120 ms | hover, fokus, toggle |
| 200 ms | panel, dialog, transisi tampilan |
| 300 ms | perpindahan canvas besar |

- Easing: `cubic-bezier(0.2, 0.8, 0.2, 1)`.
- Tidak ada bounce atau animasi berulang.
- Wajib menghormati `prefers-reduced-motion`.

## 7. Do / Don't

| Do | Don't |
|---|---|
| Satu pesan utama per layar | Menumpuk banyak ajakan |
| Memakai token dari `ui.pen` | Menambah hex baru di kode |
| Screenshot UI asli | Mockup fitur fiktif |
| Sebut batasan (sync belum ada) | Menyembunyikan batasan |
| Nama mode konsisten: Mesa, Loka | Menulis "Loka Mesa" |
