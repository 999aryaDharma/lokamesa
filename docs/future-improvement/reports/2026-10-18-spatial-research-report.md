# Report — Riset Spatial, Instalasi REA, dan Pola Canvas Interaktif

**Tanggal:** 2026-10-18
**Label:** `future-improvement` (planning artifact)
**Status:** Informational — tidak mengubah board V1
**Terkait:** `docs/future-improvement/specs/1.7.0-wildboard-core.md`, `docs/decisions/ADR-003-wildboard-workspace-kind-and-vault.md`
**Branch saat dibuat:** `integration/parallel-batches`

---

## 0. Ringkasan eksekutif

1. **REA terpasang** (`rea-agents@6.5.0`) sebagai MCP server untuk OpenCode + skill `reverse-engineer-anything`.
2. **Spatial dianalisis** dari informasi produk publik (bukan reverse-engineering biner — lihat §2.2).
3. **Tiga fitur diusulkan** untuk Document IDE sebagai roadmap `1.7.0` (status `CANDIDATE`): Quick Capture → Inbox, Canvas Stacks, Canvas Markdown Sheets.
4. **Canvas interaktif di repo ini sudah ada** dan memakai pola hand-rolled (viewport transform + pure selection controller + persistensi per-identitas stabil). Report ini mendokumentasikan resepnya agar bisa direplikasi/diperluas.
5. **Tidak ada kode produksi yang diubah.** Tidak ada commit.

---

## 1. Instalasi REA

### 1.1 Perintah

```powershell
npx rea-agents@latest setup --client opencode --yes
```

`setup` tanpa flag menghasilkan `plannedActions: []` (scope kosong), sehingga harus menyebut client secara eksplisit.

### 1.2 Hasil

| Item | Nilai |
|---|---|
| Versi | `rea-agents@6.5.0` |
| `appliedActions` | `configured_opencode`, `installed_skill` |
| Config OpenCode | `C:\Users\User\.config\opencode\opencode.json` |
| Backup config | `C:\Users\User\.config\opencode\opencode.json.rea.backup` |
| Perintah MCP | `cmd.exe /d /c npx -y rea-agents@6.5.0 mcp` |
| Skill | `C:\Users\User\.agents\skills\reverse-engineer-anything` |
| Skill state | `aligned` (v42, 207 tools) |
| Doctor | `healthy: true` |

### 1.3 Catatan / caveat

- **Perlu restart OpenCode** agar MCP server terbaca oleh sesi.
- Provider analisis **belum dikonfigurasi**:
  - `GHIDRA_INSTALL_DIR` belum diset,
  - `REA_IDA_MCP_CONFIG` belum diset,
  - `REA_JADX_MCP_JAR` belum diset.
- REA tetap bisa melakukan analisis statis JavaScript/web/Electron **tanpa** provider tersebut.
- `environment_healthy: false` karena provider di atas; ini bukan kegagalan instalasi.

---

## 2. Analisis Spatial (get-spatial.com)

### 2.1 Cara kerja produk

Spatial adalah aplikasi catatan **macOS** berbentuk canvas visual ("digital shoebox").

| Kapabilitas | Perilaku | Keputusan desain |
|---|---|---|
| **Notes** | Dua jenis: *sticky note* kecil yang bisa di-resize, dan *paper sheet* untuk tulisan panjang | Keduanya disimpan sebagai **Markdown biasa** |
| **Visuals** | Gambar, video, GIF, animasi ditempatkan di canvas | Canvas berfungsi sebagai mood-board |
| **Webclips** | Snapshot "magazine-style" layout desktop penuh dari sebuah URL | Bookmark visual, bukan halaman live |
| **Stacks** | Tumpukan organik; klik untuk expand, drag keluar | **Pengelompokan tanpa struktur** |
| **Scratch Pad** | Hotkey global (`⌥Space`, dapat dikustom) membuka panel satu kolom | Capture **tanpa** pindah konteks |
| **Storage** | Database lokal saja; disiapkan untuk iCloud sync | Local-first; **search masih "coming"** (Summer 2026) di Spatial, bukan fitur yang sudah ada |

Fitur yang **direncanakan** Spatial (belum ada): label pada space, sync/backup, import PDF, sidebar layer view, search, aplikasi iPhone/iPad.

**Batas parity.** Spatial **tidak** mendukung `.docx`/`.doc` sama sekali, dan PDF-nya masih *coming*. Karena itu "parity 1:1" hanya berlaku untuk **permukaan wildboard** (sticky, sheet, stack, capture, visual); integrasi Word tetap milik workspace `documents` dan tidak boleh ikut ke wildboard.

### 2.2 Batasan analisis (penting)

- Spatial adalah **aplikasi macOS App Store**; lingkungan kerja saat ini **Windows**.
- Tidak ada artefak (`.app`, APK, bundle Electron, bundle web) yang diberikan.
- Provider analisis biner (Ghidra/IDA/jadx) belum terpasang.

Karena itu, **REA tidak dipakai untuk membongkar Spatial**. Analisis di §2.1 berasal dari informasi produk publik (situs, changelog, daftar fitur mendatang). REA akan berguna bila nanti tersedia artefak nyata.

---

## 3. Canvas dengan object interaktif — pola di repo ini

Lokasi kode: `src/DocumentIDE.Web/src/features/canvas/`

| File | Peran |
|---|---|
| `types.ts` | `Viewport`, `CanvasObject`, `CanvasState` |
| `canvasStore.ts` | External store + aksi, kompatibel `useSyncExternalStore` |
| `DocumentCanvas.tsx` | Komponen utama: pan, zoom, drag, klik |
| `nodes/DocumentNode.tsx` | Kartu objek dokumen |
| `selection/SelectionController.ts` | Pure function: klik, rect-select, group drag |
| `usePersistedPosition.ts` | Persistensi posisi per workspace |
| `layout/AssistedPlacement.ts`, `SpatialIndex.ts`, `TidyLayout.ts` | Penempatan, indeks spasial, tidy |
| `FolderPicker.tsx` | Popover isi folder |

### 3.1 Struktur tiga lapis (pola kamera)

```text
container (overflow: hidden)        <- wheel + pan
 └─ surface (dot-grid background)   <- data-testid="canvas-surface"
    └─ transform container          <- translate(x,y) scale(zoom)
       └─ object 1..N               <- position:absolute, left:x, top:y
```

```tsx
transform: `translate(${viewport.x}px, ${viewport.y}px) scale(${viewport.zoom})`,
transformOrigin: '0 0',
```

Semua objek memakai koordinat **canvas-space**; objek tidak perlu tahu zoom.

### 3.2 Viewport dan zoom ke kursor

- `Viewport { x, y, zoom }`, `zoom` di-clamp `[0.1, 3.0]` (`ZOOM_MIN`, `ZOOM_MAX`).
- Agar titik di bawah kursor tetap diam saat zoom:

```ts
const scaleFactor = newZoom / viewport.zoom;
const newX = mouseX - scaleFactor * (mouseX - viewport.x);
const newY = mouseY - scaleFactor * (mouseY - viewport.y);
```

### 3.3 Pan

Tiga jalur: middle-click, `Space` + drag, dan drag pada background kosong. State pan disimpan di `useRef` (`isPanning`, `panStart`).

### 3.4 Object interaktif

```ts
interface CanvasObject {
  id: string;
  x: number; y: number;
  width: number; height: number;
  title: string;
  type: 'folder' | 'document';
  docType?: 'docx' | 'md' | 'unknown';
  thumbnailState?: 'loading' | 'ready' | 'stale' | 'missing' | 'failed';
  thumbnailUrl?: string;
  thumbnailContent?: string;
  isMissing?: boolean;
  statusText?: string;
}
```

Poin implementasi yang mudah salah:

1. **Listener drag di `window`**, bukan di elemen — supaya drag tetap berjalan saat kursor keluar kartu.
2. **Group drag** = translasi seragam; posisi relatif terjaga; objek `pinned` tidak ikut bergerak.
3. **Seleksi sebagai pure function** (`applyClick`, `rectSelect`, `groupDrag`) → mudah diuji tanpa DOM.
4. **Gesture folder (pasca UI-01)**: klik tunggal **membuka folder**, klik berikutnya menutup; picker CAN-04 pindah ke affordance `⌄`. Timer arbitrase `FOLDER_CLICK_OPEN_DELAY_MS` sudah **tidak ada** — jangan direintroduksi. Urutan DOM yang benar-benar terjadi: `click, click, dblclick`.
5. **Aksesibilitas**: `role="button"`, `tabIndex={0}`, `aria-label`, **`aria-pressed`** (bukan `aria-selected` — itu state yang tidak didukung untuk role button), handler `Enter`/`Space`. `FolderObject` dan `DocumentNode` sudah memakai `aria-pressed`. Sisa sejenis yang belum diperbaiki: `SearchPanel.tsx:227` dan `SourceControlPanel.tsx:266` (`aria-selected` pada `<div>`/`<li>` tanpa role pendukung) — follow-up a11y terpisah, bukan bagian wildboard.

### 3.5 Persistensi

- **Viewport** → `localStorage` (`documentide_canvas_viewport`).
- **Posisi objek** → `localStorage` per workspace, **dikunci oleh DocumentId stabil, bukan path** (`documentide_canvas_positions:<workspaceId>`).
  - Konsekuensi: rename/pindah file tidak merusak memori spasial.
- **Restore** saat bentrok: probe spiral 32px hingga radius `MAX_RESTORE_DISPLACEMENT = 600`; jika tidak ada sel kosong → **throw error bertipe** (tidak menumpuk diam-diam).

### 3.6 Aturan yang tidak boleh dilanggar

- Drag di canvas **tidak pernah** memindahkan file fisik — hanya metadata.
- `getSnapshot()` pada store **wajib** mengembalikan referensi yang sama bila state tidak berubah (kalau tidak → infinite render). Implementasi memakai `cachedSnapshot`.
- Kartu di-`memo`; ada tes 100 objek dan gate performa `QA-06`.

### 3.7 Menambah interaktivitas baru (resep)

1. Tambah tipe/field di `types.ts`.
2. Tambah aksi di `canvasStore.ts` (referensi aksi harus stabil).
3. Tulis **pure controller** untuk logika interaksi + unit test (Red → Green → Refactor).
4. Tambah/ubah komponen node; pastikan listener global untuk drag.
5. Tambah tes komponen + tes persistensi.
6. Cek gate performa.

Alternatif library bila tidak ingin hand-roll: `react-zoom-pan-pinch`, `konva`/`react-konva`, `tldraw`, `reactflow`. Repo ini sengaja hand-roll demi testability — ikuti pola yang ada.

---

## 4. Fitur Spatial yang bisa diadopsi

| Fitur Spatial | Status adopsi | Bentuk di Document IDE | Sprint / versi |
|---|---|---|---|
| Scratch Pad (`⌥Space`) | ✅ Diadopsi | Quick Capture | `WB-04` (1.7.0) |
| Stacks | ✅ Diadopsi | Canvas Stacks (metadata-only) | `WB-03` (1.7.0) |
| Sticky vs Sheet | ✅ Diadopsi | objek `text` + `sheet` | `WB-03` (1.7.0) |
| Markdown sebagai storage | ✅ Sudah ada | `.md` tetap format penyimpanan | — |
| Writing announced (slash, divider, numbered list, tabel, internal link, font, inline media) | ✅ Diadopsi | Editor blok ala Medium + toggle Source | `RW-01…RW-03` (1.8.0) |
| Labels on spaces | ✅ Diadopsi | objek `label` (metadata-only) | `RW-04` (1.8.0) |
| Sidebar layer view | ✅ Diadopsi | layer view (z-order saja) | `RW-04` (1.8.0) |
| Search | ✅ Diadopsi | indeks khusus wildboard | `RW-05` (1.8.0) |
| Images | ✅ Diadopsi | objek `image` | `MR-02` (1.9.0) |
| Video | ✅ Diadopsi | objek `video` (tanpa autoplay) | `MR-03` (1.9.0) |
| Webclips | ✅ Diadopsi (dibatasi) | `webclip` + SSRF/sanitasi | `MR-05` (1.9.0) |
| PDF import/view | ✅ Diadopsi | viewer lokal | `MR-04` (1.9.0) |
| Sync iCloud | ❌ Ditolak | bertentangan dengan local-first | — |
| iPhone/iPad | ❌ Di luar cakupan | produk desktop | — |
| X posts/bookmarks import | ⏸ Ditunda | integrasi jaringan | fase 3+ |

### 4.1 Yang benar-benar bisa "copy-paste"

Bukan kodenya (Spatial Swift/macOS; project ini React + Electron), melainkan **pola interaksinya**:

- kamera transform (translate + scale, origin `0 0`),
- dualitas sticky/sheet dengan mode edit berbeda,
- hotkey capture global,
- collapse/expand stack,
- Markdown-as-storage (sudah dimiliki).

### 4.2 Yang tidak boleh diadopsi

- cloud-first storage / sync backend,
- format konten proprietary,
- pembuatan folder fisik dari pengelompokan canvas.

---

## 5. Deliverable dan status

| Artefak | Status |
|---|---|
| `docs/future-improvement/specs/1.7.0-wildboard-core.md` | **Baru**, status `SPECIFIED` (governed by ADR-003 `Accepted`) |
| `docs/future-improvement/specs/1.7.0-wildboard-core.id.md` | **Baru**, terjemahan Indonesia (versi EN = sumber kebenaran) |
| `docs/future-improvement/specs/1.8.0-rich-writing-and-organization.md` | **Baru**, status `CANDIDATE` |
| `docs/future-improvement/specs/1.9.0-media-and-references.md` | **Baru**, status `CANDIDATE` |
| `docs/decisions/ADR-003-wildboard-workspace-kind-and-vault.md` | **Baru**, status `Accepted` (owner decision, 2026-10-18) |
| `docs/future-improvement/README.md` | Diubah (range → `1.9.x`, daftar → sembilan baris) |
| `docs/future-improvement/01-roadmap.md` | Diubah (baris tabel + filosofi `1.7.0`) |
| `docs/future-improvement/FUTURE-STATUS.md` | Diubah (baris status `CANDIDATE`) |
| `docs/future-improvement/specs/00-shared-future-contracts.md` | Diubah (range → `1.1.0–1.9.x`) |
| `docs/future-improvement/reports/2026-10-18-spatial-research-report.md` | **Baru** (dokumen ini) |
| Manifest / future sprint index / dependency graph / execution waves | **Diubah**: `WB-01`…`WB-05` terdaftar dengan status `SPECIFIED`; `SPRINT-STATUS.md` (board V1) **tidak** diubah |
| Kode produksi | **Tidak diubah** |

---

## 6. Risiko dan tindak lanjut

**Risiko**

- Repo belum hijau: `REL-03` **BLOCKED** (6 gate gagal). Status lane per 2026-10-18 sore: `UI-01`, `UI-02`, `REL-04` **DONE** (merged), `SCM-08` menunggu merge; fitur baru antre setelah V1 GO.
- **Webclips (`1.9.0`, `MR-05`) adalah adopsi paling berisiko** di seluruh roadmap: butuh fetch jaringan, penyimpanan HTML arbitrer, dan rendering. Ini permukaan SSRF + sanitasi + kuota, dan **wajib punya ADR keamanan sendiri sebelum `MR-05`**, bukan hanya satu paragraf di ADR-003.
- Vault berada di `%LOCALAPPDATA%`: tanpa aksi "Export vault" (zip) isi wildboard tidak discoverable dan tidak ter-backup. Aksi itu sudah dimasukkan ke scope `1.7.0`.
- REA belum bisa dipakai pada Spatial tanpa artefak + provider analisis.
- Belum ada commit; perubahan berada di branch `integration/parallel-batches`.

**Tindak lanjut yang disarankan**

1. Review spec `1.7.0` dan tentukan tiga open question:
   - target default capture (file `.md` vs note object),
   - model data stack (tipe objek vs properti grup),
   - batas note (`1.6.0`) vs sheet (`1.7.0`).
2. Pilih status: tetap `CANDIDATE`, atau naikkan ke `SPECIFIED` (butuh dekomposisi sprint + update manifest/index/waves).
3. Bila ingin implementasi: buat spec sprint, branch, lalu TDD (Red → Green → Refactor) + verifikasi repo.
4. Restart OpenCode agar MCP REA aktif.

---

## 7. Revisi 2026-10-18 — dari "Capture & Canvas Composition" ke "Wildboard"

Permintaan owner berubah selama sesi: bukan sekadar tiga fitur tambahan, melainkan **sub-app canvas liar untuk second brain**, dengan target **parity fungsional 1:1 terhadap Spatial** dan **editor formatting ala Medium**.

Konsekuensi yang sudah didokumentasikan:

- Proposal `1.7.0` lama (`Capture & Canvas Composition`) **digantikan** oleh `1.7.0 — Wildboard Core`.
- Roadmap diperluas menjadi tiga versi: `1.7.0` (core), `1.8.0` (rich writing + organisasi), `1.9.0` (media + referensi).
- Karena perubahan menyentuh **source-of-truth**, **database identity model**, dan **product scope**, dibuat `ADR-003` berstatus **Proposed** sesuai change-control policy.
- "Source-first" pada spec Markdown perlu **amandemen eksplisit** (rich mode + Source toggle), bukan perubahan diam-diam.
- Parity yang **tidak** diadopsi: sync/iCloud (melanggar local-first) dan aplikasi mobile (di luar cakupan produk desktop).

