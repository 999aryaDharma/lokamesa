# Document IDE — AI Coding Agent Prompt Guide

Dokumen ini adalah panduan prompt yang dipakai selama development Document IDE.

Prinsip utamanya:

```text
Kamu tidak perlu menjelaskan project dari awal setiap kali.

AGENTS.md
+ specs
+ sprint docs
= source of context agent.
```

Kamu bertindak sebagai **product owner / director**.

AI Agent bertindak sebagai:

```text
implementer
tester
reviewer
debugger
```

tetapi agent tidak boleh bebas mengubah requirement.

---

# 1. PROMPT PERTAMA BESAR — START PROJECT

Gunakan ini **sekali**, saat pertama kali Codex/AI Agent masuk ke repository kosong yang sudah berisi Agent Development Pack.

```text
You are the primary implementation agent for Document IDE.

Before modifying any code, read and understand:

1. AGENTS.md
2. README.md
3. docs/specs/00-master-product-technical-spec.md
4. docs/specs/01-system-architecture.md
5. docs/specs/12-implementation-plan.md
6. docs/agent/*
7. docs/sprints/00-sprint-index.md
8. docs/sprints/SPRINT-STATUS.md

Treat AGENTS.md and the documented specification hierarchy as authoritative.

Important rules:

- The local filesystem is the source of truth.
- Do not change product requirements on your own.
- Do not implement features outside the active sprint.
- Do not silently simplify safety/security requirements.
- Use TDD for behavioral implementation.
- Never bypass safe-write, history, scope, or security boundaries.
- Never use fuzzy/semantic matching for destructive operations.
- Canvas movement must never physically move user files.
- Word COM automation concurrency must remain exactly 1.
- Public ShareHost must remain isolated from privileged mutation/Word services.
- Live Share must never automatically expand membership.

We will develop this project sprint-by-sprint.

For this first session, implement ONLY:

FND-01 — Repository Bootstrap

Sprint document:
docs/sprints/foundation/FND-01-repository-bootstrap.md

First:

1. Verify FND-01 is READY.
2. Read all Required Reading listed by FND-01.
3. Inspect the repository state.
4. Describe the implementation boundary you are about to execute.
5. Then implement the sprint using TDD.

Do not implement FND-02 or any downstream sprint.

At completion:

1. Run every test required by the sprint.
2. Run the relevant repository verification commands.
3. Self-review the complete git diff.
4. Check every FND-01 Acceptance Criterion.
5. Check the global Definition of Done.
6. Update docs/sprints/SPRINT-STATUS.md appropriately.
7. Write the sprint handoff using the provided template.
8. Report:
   - files created/changed,
   - architecture introduced,
   - commands executed,
   - exact test results,
   - acceptance criteria status,
   - deviations from spec,
   - risks discovered,
   - next dependency-ready sprint(s).
9. Commit the completed sprint using the documented Git policy.

Do not claim completion if a required test is failing or skipped.

Begin with FND-01.
```

Ini **prompt besar pertama**.

Setelah ini, tidak perlu lagi memberi agent seluruh penjelasan panjang project.

---

# 2. PROMPT NORMAL — KERJAKAN SPRINT BERIKUTNYA

Ini yang akan paling sering kamu gunakan.

```text
Read AGENTS.md and the current sprint status.

Implement sprint:

<SPRINT-ID>

Use its sprint document under docs/sprints/.

Before editing:

- verify all dependencies are DONE,
- read every Required Reading document,
- inspect existing implementation,
- run the relevant existing tests.

Implement ONLY this sprint.

Follow:
- TDD,
- security/file-safety gates,
- Definition of Done,
- documented module boundaries.

Do not implement downstream features even if they seem easy to add.

At completion:

- run all required tests,
- run affected repository verification,
- self-review git diff,
- map every acceptance criterion to evidence,
- update SPRINT-STATUS.md,
- create/update sprint handoff,
- report deviations or newly discovered risks,
- commit the sprint.

Do not mark the sprint DONE unless all required gates pass.
```

Contoh:

```text
Implement sprint FND-02.

Follow the normal sprint execution protocol.
Do not implement FND-03 yet.
```

Bahkan setelah agent terbiasa dengan repo, kamu bisa semakin pendek:

```text
Implement FND-03 only.
Follow AGENTS.md and the sprint protocol completely.
```

---

# 3. PROMPT PALING SANTAI — "LANJUT"

Kalau agent sudah tahu workflow repository:

```text
Check SPRINT-STATUS.md.

Determine the highest-priority dependency-ready sprint.

Tell me which sprint is READY and why.

Then implement that sprint only according to AGENTS.md,
its sprint spec, TDD policy, Definition of Done,
verification, handoff, and commit protocol.

Do not start another sprint afterward.
```

Versi vibe:

```text
Gas sprint READY berikutnya.

Ikuti AGENTS.md sepenuhnya.
1 sprint saja.
Test, review, handoff, commit.
```

Ini mungkin prompt harianmu nanti.

---

# 4. PROMPT REVIEW SETELAH AGENT SELESAI

Ini penting.

Jangan selalu percaya agent yang mengatakan:

> Done, all tests pass.

Buka sesi/reviewer agent lain kalau memungkinkan.

```text
Act as a strict code reviewer for sprint <SPRINT-ID>.

Do NOT modify code yet.

Read:

- AGENTS.md
- the sprint specification
- all higher-level specifications referenced by the sprint
- Definition of Done
- security/file-safety gates
- testing policy

Then inspect the actual implementation, git diff, and tests.

Verify:

- every acceptance criterion,
- architectural boundaries,
- safety requirements,
- security requirements,
- failure behavior,
- performance implications,
- scope creep,
- missing tests,
- skipped tests,
- silent fallbacks,
- unnecessary complexity.

Report findings by severity:

CRITICAL
HIGH
MEDIUM
LOW

For every finding include:

- exact file/location,
- violated requirement,
- why it matters,
- expected correction.

Also provide:

- acceptance criteria matrix,
- tests you verified,
- final verdict:

APPROVE
CHANGES REQUIRED
or
BLOCK

Do not praise implementation generally.
Focus on evidence and actionable findings.
```

---

# 5. JIKA REVIEW BERSIH

Kalau reviewer bilang APPROVE:

```text
Finalize sprint <SPRINT-ID>.

Verify the review findings are empty or resolved.

Run final required verification.

Then:

- mark the sprint DONE,
- update SPRINT-STATUS.md,
- ensure handoff is complete,
- report newly READY dependent sprint(s).

Do not implement the next sprint yet.
```

Kemudian kamu tinggal:

```text
Gas sprint READY berikutnya.
```

---

# 6. JIKA REVIEW MENEMUKAN MASALAH

Jangan bilang:

```text
fix everything
```

Pakai:

```text
Fix the accepted review findings for sprint <SPRINT-ID> only.

Read the review findings first.

For each defect:

1. reproduce or prove the defect,
2. add or strengthen a failing regression test first,
3. verify the test fails for the expected reason,
4. implement the smallest correct fix,
5. run the regression test,
6. run the affected suite.

Do not add unrelated features or refactor unrelated modules.

After all accepted findings are fixed:

- rerun sprint verification,
- self-review git diff,
- update handoff,
- report each finding and its verification evidence,
- commit the fixes.

Do not mark DONE until review requirements pass.
```

---

# 7. PROMPT RE-REVIEW

Setelah fix:

```text
Re-review sprint <SPRINT-ID> after the review fixes.

Focus especially on the previously reported findings.

Also verify that the fixes did not introduce:

- regressions,
- scope creep,
- weakened assertions,
- architectural boundary violations.

Run or inspect the relevant tests.

Return only:

- unresolved findings,
- new findings,
- acceptance criteria status,
- APPROVE / CHANGES REQUIRED / BLOCK.
```

Kalau approve → DONE.

---

# 8. PROMPT KALAU ADA BUG SAAT VIBE CODING

Misalnya:

> Rename file malah membuat file hilang.

Gunakan:

```text
There is a bug:

<DESCRIBE ACTUAL BEHAVIOR>

Expected behavior:

<DESCRIBE EXPECTED BEHAVIOR>

Do not start by modifying code.

Follow the repository debugging protocol.

First:

1. identify the owning sprint/subsystem,
2. reproduce the issue,
3. inspect logs/state,
4. determine the smallest failing layer,
5. explain the root cause based on evidence.

Then:

6. write a failing regression test,
7. prove it fails,
8. implement the smallest safe fix,
9. run the regression,
10. run neighboring safety/integration tests.

If this bug affects:
- file corruption,
- destructive operations,
- security boundaries,
- sharing authorization,

stop unrelated feature work and determine whether a release gate/regression test must also be added.

Report root cause, changed files, and verification evidence.
```

Versi santai:

```text
Bug: rename file X menghasilkan Y.

Ikuti debugging protocol.
Jangan workaround.
Cari root cause, regression test dulu, baru fix.
```

---

# 9. PROMPT KALAU ADA ERROR TEST

```text
Investigate the failing test(s).

Do not weaken or delete assertions merely to make the suite green.

Determine whether the failure is:

- implementation defect,
- test defect,
- specification conflict,
- environment/tooling issue.

Provide evidence.

If implementation is wrong:
add/fix regression coverage and correct implementation.

If specification conflicts:
stop implementation and report the exact conflicting sections.

Run the affected suite after fixing.
```

---

# 10. PROMPT KALAU KAMU PUNYA IDE FITUR BARU

Ini sangat penting agar vibe coding tidak merusak docs.

Misalnya:

> Aku pengen sekarang PDF bisa dikasih annotation.

Jangan bilang:

```text
buat fitur annotation PDF
```

Gunakan:

```text
I want to consider a new product requirement:

<NEW IDEA>

Do NOT implement it yet.

Treat this as a Change Request.

Review it against:

- master product spec,
- subsystem specs,
- current architecture,
- sprint dependency graph,
- security/file-safety model.

Determine:

1. whether it fits the current product thesis,
2. which specifications would need revision,
3. architecture impact,
4. security/file-safety impact,
5. data model impact,
6. affected completed/future sprints,
7. whether new sprint(s) are required,
8. migration/backward-compatibility issues.

Produce a proposed Change Request using the repository template.

Do not modify implementation until I approve the requirement.
```

Ini adalah prompt yang sangat bagus untuk ide spontanmu.

---

# 11. KALAU KAMU MAU UBAH REQUIREMENT YANG SUDAH ADA

```text
I want to change an existing Document IDE requirement:

Current behavior:
<OLD>

Desired behavior:
<NEW>

Do not implement yet.

Perform spec impact analysis.

Find every master spec, subsystem spec, sprint,
test policy, and dependency affected by this change.

Propose:

- exact spec changes,
- affected sprint changes,
- newly required migration/tests,
- compatibility/security consequences.

If the change invalidates already-completed implementation,
identify exactly which completed sprints must be reopened.

Wait for requirement approval before coding.
```

---

# 12. PROMPT UNTUK SECURITY AUDIT

Bisa kamu jalankan berkala.

```text
Perform a security audit of the current Document IDE implementation.

Do not modify code.

Use the security requirements from:

- AGENTS.md
- master specification
- system architecture
- filesystem specification
- batch operation specification
- sharing/security specification
- testing strategy
- security/file-safety gate

Prioritize:

- privileged localhost API,
- Origin/Host/session validation,
- path traversal,
- junction/reparse-point escape,
- unsafe file overwrite,
- stale batch plans,
- Word lock handling,
- share token authentication,
- Live Share membership leakage,
- ShareHost privileged dependency leakage,
- XSS/CSP/sanitization,
- download authorization,
- secret leakage,
- resource exhaustion.

Report:

CRITICAL
HIGH
MEDIUM
LOW

For each finding:
- exact location,
- exploit/failure path,
- violated requirement,
- recommended fix,
- regression test needed.

Do not report theoretical issues without explaining applicability to this architecture.
```

---

# 13. PROMPT UNTUK PERFORMANCE AUDIT

```text
Perform a performance audit of the current implementation.

Do not optimize blindly.

Compare implementation with the documented target:
under 1,000 documents per workspace.

Inspect:

- SQLite transaction duration and WAL usage,
- per-keystroke search behavior,
- indexing concurrency,
- DOCX parse scheduling,
- canvas collision complexity,
- graph layout complexity,
- React rerenders,
- thumbnail/preview caching,
- Word COM queue behavior,
- PDF merge memory,
- semantic embedding concurrency.

Identify:

- actual bottlenecks,
- probable bottlenecks,
- premature optimizations.

For each real/probable bottleneck provide:
- evidence,
- complexity/resource behavior,
- benchmark to add,
- recommended correction.

Do not introduce distributed infrastructure or unnecessary architecture.
```

---

# 14. PROMPT UNTUK "BERSIHKAN CODE"

Hati-hati dengan refactor agent.

```text
Review the code touched by sprint <SPRINT-ID> for maintainability.

Do not change behavior or product scope.

Look specifically for:

- oversized classes/modules,
- duplicated business rules,
- misplaced infrastructure logic,
- hidden coupling,
- unclear interfaces,
- dead code,
- unnecessary abstraction.

Only propose/refactor issues that materially improve the implementation.

All existing behavior and tests must remain unchanged.

Do not perform broad repository-wide refactoring.
```

---

# 15. PROMPT UNTUK "CEK APA SPRINT SELANJUTNYA"

```text
Read docs/sprints/SPRINT-STATUS.md and sprint-manifest.json.

Determine which sprints are currently READY.

For each READY sprint provide:

- ID,
- title,
- priority,
- why dependencies are satisfied,
- whether it can safely run in parallel with another READY sprint.

Recommend the next sprint based on dependency critical path and risk.

Do not implement anything.
```

Versi vibe:

```text
Apa sprint READY sekarang?
Urutkan mana yang paling baik dikerjakan berikutnya.
Jangan coding dulu.
```

---

# 16. PROMPT UNTUK MULTI-AGENT PARALLEL

Jangan lakukan ini terlalu awal.

Kalau foundation sudah matang:

```text
Analyze the currently READY sprint set.

Identify sprints that can safely be implemented in parallel.

Requirements:

- no unmet dependencies,
- no substantial overlap in planned files,
- no shared unfinished architecture boundary,
- no dependency between selected sprints.

Propose a parallel execution plan:

Agent A → sprint
Agent B → sprint
Agent C → sprint

For each agent specify:
- required reading,
- branch/worktree,
- expected integration conflicts,
- verification after merge.

Do not begin implementation until the parallel plan is validated.
```

---

# 17. PROMPT UNTUK AGENT REVIEWER KHUSUS DOCX

Karena DOCX sangat sensitif:

```text
Audit the DOCX changes in sprint <ID> specifically for round-trip fidelity.

Check:

- source file modification boundaries,
- OOXML package mutation scope,
- run fragmentation handling,
- styles,
- tables,
- headers/footers,
- section properties,
- fields,
- relationships,
- validation,
- recovery behavior.

Compare pre/post document structure using the golden DOCX fixtures.

The requested property may change.
Unrelated properties must not change.

Report any unintended structural mutation.
```

---

# 18. PROMPT UNTUK AGENT REVIEWER FILE SAFETY

```text
Audit sprint <ID> specifically for user-file safety.

Trace every path from UI/API command to filesystem mutation.

Verify:

- explicit user intent,
- target scope,
- path validation,
- stable DocumentId handling,
- lock state,
- expected fingerprint,
- recovery capacity,
- recovery snapshot,
- temporary mutation,
- validation,
- atomic replace,
- reindex,
- history.

Try to find a path that can alter/delete/move a file without satisfying these gates.

Do not modify code.
```

---

# 19. PROMPT UNTUK "LANJUTKAN PROJECT BESOK"

Tidak perlu menjelaskan semuanya lagi.

```text
Resume Document IDE development.

Read:

- AGENTS.md
- docs/sprints/SPRINT-STATUS.md
- the latest sprint handoff(s)
- current git status/history

Do not assume previous chat context.

Determine:

- current project state,
- last completed sprint,
- active/incomplete sprint if any,
- currently READY next sprint,
- existing failing tests or dirty changes.

Report the state first.

If there is a clean READY sprint and no unfinished work,
proceed with that sprint only using the normal execution protocol.
```

Ini salah satu prompt paling berguna untuk Codex session baru.

---

# 20. PROMPT KALAU AGENT KEHILANGAN ARAH

```text
Stop implementation.

Re-anchor yourself to the repository specifications.

Read:

AGENTS.md
master product spec
current subsystem spec
current sprint spec

Then answer:

1. What exact sprint are you implementing?
2. What is in scope?
3. What is explicitly out of scope?
4. What higher-level requirements constrain the implementation?
5. What acceptance criteria remain incomplete?
6. What files have you changed that may be outside scope?

Do not edit code until the scope is clear again.
```

---

# 21. PROMPT "JANGAN OVERENGINEER"

Kalau agent mulai bikin Kubernetes untuk SQLite lokal:

```text
Re-evaluate this implementation for overengineering.

The target is a local-first Windows desktop application
with under 1,000 documents per workspace.

Apply YAGNI.

Identify abstractions/infrastructure that are not required
by the current sprint or documented near-term dependencies.

Prefer the simplest architecture that satisfies the specs,
safety requirements, tests, and known future interfaces.

Do not weaken architectural boundaries or file safety.
```

---

# 22. PROMPT SEBELUM MERGE

```text
Perform pre-merge verification for sprint <ID>.

Do not add new features.

Verify:

- sprint acceptance criteria,
- Definition of Done,
- security/file-safety gates,
- relevant golden tests,
- no skipped tests,
- no accidental TODO-like placeholders,
- no unrelated diff,
- no secrets,
- clean dependency boundaries.

Run the exact verification commands.

Review git diff one final time.

Return:

MERGE READY

or

NOT MERGE READY

with blocking reasons.
```

---

# 23. PROMPT SAAT MAU BUILD INSTALLER

Nanti setelah release sprint:

```text
Build a release candidate of Document IDE
according to REL-01 and the release specifications.

Do not bypass release gates.

Run release verification first.

Then build:

- frontend production assets,
- .NET release,
- self-contained win-x64 publish,
- ShareHost,
- installer package.

Verify on the documented clean-machine test path.

Report:

- commit,
- test summary,
- generated artifacts,
- hashes,
- installer smoke-test result,
- known residual risks.

Do not call the artifact a release if any required gate fails.
```

---

# 24. PROMPT RELEASE FINAL

```text
Evaluate Document IDE for V1 readiness using REL-03.

Map every locked requirement in the master specification
to implementation and verification evidence.

Check:

- filesystem source-of-truth,
- workspace/canvas,
- search/indexing,
- DOCX/Word,
- strict batch operations,
- history/undo,
- Markdown/graph,
- semantic boundaries,
- PDF,
- sharing/security,
- installer,
- offline operation.

Identify every gap.

Return:

V1 READY

or

V1 NOT READY

with exact blockers.

Do not infer readiness from number of completed sprints alone.
```

---

# 25. PROMPT HARIAN PALING PRAKTIS

Dalam praktik, 80% waktumu mungkin cuma menggunakan empat prompt ini.

### Develop

```text
Gas <SPRINT-ID>.

Ikuti AGENTS.md dan sprint spec sepenuhnya.
1 sprint saja.
TDD → implement → verify → self-review → handoff → commit.
Jangan keluar scope.
```

### Review

```text
Review <SPRINT-ID> secara ketat terhadap spec, tests,
security/file-safety gates, dan Definition of Done.

Jangan ubah code.
Kasih findings per severity dan verdict.
```

### Fix

```text
Fix accepted findings untuk <SPRINT-ID> saja.

Regression test dulu untuk setiap defect.
Jangan scope creep.
Rerun verification setelah selesai.
```

### Next

```text
Cek SPRINT-STATUS dan dependency graph.

Sprint READY apa sekarang?
Pilih yang paling tepat di critical path.
Jangan coding dulu.
```

---

# 26. GOLDEN RULE UNTUK KAMU SEBAGAI PRODUCT OWNER

Kamu bebas vibe coding:

```text
"gas"
"ubah ini"
"kok hasilnya jelek?"
"aku punya ide"
"cek security lagi"
"coba bikin lebih cepat"
```

Tetapi arahkan jenis instruksinya:

```text
FITUR YANG SUDAH DISETUJUI
→ Sprint

BUG
→ Debugging Protocol

IDE BARU
→ Change Request

ARSITEKTUR BERUBAH
→ ADR

SELESAI CODING
→ Review

SELESAI REVIEW
→ Verification

PASS
→ DONE

DONE
→ Next READY
```

Dengan begitu kamu tetap mendapat pengalaman vibe coding yang cepat, tetapi repository tidak berubah menjadi kumpulan improvisasi AI.
