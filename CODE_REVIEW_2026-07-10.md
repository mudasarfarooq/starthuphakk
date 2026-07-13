# OpenMono Code Review — Findings & Improvement Plan

**Created:** 2026-07-10 (Fri) · **Resume:** 2026-07-13 (Mon)
**Status:** Review complete. **Nothing implemented yet.** All items below are proposed, non-breaking for legitimate inputs.
**Scope reviewed:** ~22.5k LOC across `src/OpenMono.Cli/` — Session/ACP, Tools, core loop/LLM/config, Rendering/Commands/Permissions.

> How to continue: this is a **Claude Code** work doc. The openmono `/resume` command only restores openmono-agent chat sessions, not this review. Point Claude Code at this file to pick up where we left off.

---

## Baseline (already good — do not regress)
- `Nullable` enabled, `TreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild`, 398 tests / 55 files.
- Well-designed `Utils/RetryPolicy.cs` (jitter, honors `Retry-After`).
- Solid `PathGuard` core (UNC, device files, `.env*`, cross-platform casing).
- User preference: **do NOT add code comments** in any changes.

---

## ⭐ Top 6 (highest value)
1. Resolve symlinks in `PathGuard` — closes workspace escape. `Permissions/PathGuard.cs:57-71`
2. Move permission deny-checks BEFORE allow short-circuits. `Permissions/PermissionEngine.cs:87-110`
3. Delete duplicated ACP pause-resume path (~250 LOC). `Acp/AcpTurnRunner.cs` vs `Acp/AcpPauseResponseHandler.cs`
4. Stop swallowing session save/load errors. `Program.cs:607`, `Session/SessionManager.cs:97-117`
5. Add SSRF guard to `WebFetch`. `Tools/WebFetchTool.cs:60-78`
6. Fix Anthropic retry gap (no timeout retry). `Llm/AnthropicClient.cs:87-91`

---

## Theme 1 — Security & sandboxing
- **HIGH** `PathGuard` containment is prefix-only; symlinks inside workspace escape. Fix: resolve real path (`ResolveLinkTarget`) before prefix check. `PathGuard.cs:57-71` *(sibling-prefix case already handled)*
- **HIGH** Playbook-scope / session-allow-all return allow before deny loop → allow-all'd Bash bypasses destructive-command denies. Fix: run deny loop first in `CheckCapabilitiesAsync` AND `CheckAsync`. `PermissionEngine.cs:87-110`
- **HIGH** `FileReadCap` auto-allow uses raw `StartsWith(WorkingDirectory)` and skips `PathGuard` → auto-read of `~/.ssh/id_rsa`/`.env` in workspace. Fix: route through `PathGuard.Validate`. `PermissionEngine.cs:256`
- **HIGH** `WebFetch` direct path checks only URL scheme — no SSRF guard (`169.254.169.254`, localhost, private ranges); forwards caller headers verbatim. `WebFetchTool.cs:60-78,120-161`
- **HIGH** `BashParser` gate doesn't split on newline or `&`, caps subshells at 50 → `foo\nrm -rf /` slips through. Treat parse-truncation as "ask". `BashParser.cs:306,37`, `SanityCheck.cs:96-137`
- **MED** Denial-escalation counter can turn explicit config-deny / session-deny-all into a prompt. Only escalate on implicit "ask" fall-through. `PermissionEngine.cs:143-184`
- **MED** `ApplyPatch` path strip `TrimStart('b','/')` mangles filenames (`bbc.cs`→`c.cs`), no `/dev/null` special-case. Strip exactly `a/`|`b/`. `ApplyPatchTool.cs:134`
- **MED** `/export` writes user path with no `PathGuard`. Validate or document as trusted. `ExportCommand.cs:51-71`
- **MED** Dead deny branch: git `push`/`force-push` matches then returns `null` (no-op). `PermissionEngine.cs:235-237`

## Theme 2 — ACP concurrency & lifetime
- **HIGH** `AcpSessionStore` uses `.GetAwaiter().GetResult()` under `lock` on Kestrel threads → pool starvation. Make async + `SemaphoreSlim`. `AcpSessionStore.cs:67-171`
- **HIGH** `PostTurn` `finally` releases lock + saves even when turn only PAUSED for permission → race on unsynchronized `List<Message>`. Release/save only on terminal states. `AcpEndpoints.cs:247-252`
- **HIGH** Permission `Queue<T>` + `_currentPermissionId` mutated with no lock. Use `ConcurrentQueue` + lock. `AcpSession.cs:47-112`
- **HIGH** Queued-permission throws `PendingUserResponseException` with unregistered id → orphaned 2nd concurrent permission. `AcpUserInteractionForwarder.cs:53-68`
- **HIGH** `ILlmClient` is `using var llm` AND a DI singleton the ACP container disposes → double-dispose / disposal mid-stream. Pick one owner. `Program.cs:150,274`
- **MED** Reaper timer can delete a session file out from under an in-flight turn. Skip sessions whose `TurnLock` is held. `AcpSessionStore.cs:104-119`

## Theme 3 — Silent error swallowing (add `Log.Debug/Warn(ex)`, keep recovery)
- `Program.cs:607` — session SAVE failure lost (data loss).
- `Session/SessionManager.cs:97-117` — corrupt line silently DROPS a message (invisible history loss).
- `Tools/RoslynTool.cs:142,150` — file load failure silently dropped from analysis; surface skipped count.
- Others: `Program.cs:413,633`, `Session/TurnJournal.cs:165`, `Tools/FileEditTool.cs:112`, `Tools/FileWriteTool.cs:98`, `Lsp/LspClient.cs:246`.

## Theme 4 — Correctness bugs
- **HIGH** `AnthropicClient` retry catches only `HttpRequestException`, not `TaskCanceledException` → timeout doesn't retry (OpenAI client does). `AnthropicClient.cs:87-91`
- **HIGH** Terminal not restored on unhandled exception (no `AppDomain.UnhandledException` handler) → wedged terminal. `AnsiTuiRenderer.cs`
- **MED** Warmup reads key from `OPENMONO_API_KEY`/`LLAMA_API_KEY` but client uses `config.Llm.ApiKey` → key set only in settings.json → warmup 401 silently. `Program.cs:640-642`
- **MED** Config `MergeFrom` guards numeric overrides with `>0`/`!=0` → deliberate `temperature:0`/`top_p:0` ignored. Use nullable/key-present checks. `Config/AppConfig.cs:76-88`
- **MED** `ExecuteToolCallsWithInflightAsync` uses O(n²) `IndexOf(call)` → mis-maps results for two identical tool calls (records compare equal). Use known loop index. `Session/ConversationLoop.cs:870-902`
- **MED** Background input thread never `Join`ed on stop → two readers steal keystrokes around permission prompts. `AnsiInputReader.cs:145-165`
- **MED** Playbook-approval prompt has no Ctrl+C/cancellation. `AnsiInputReader.cs:386-397`
- **MED** `AnthropicClient` SSE uses culture-sensitive `StartsWith("data: ")`, no `ct.ThrowIfCancellationRequested()`, no malformed-chunk ceiling (OpenAI has all 3). `AnthropicClient.cs:109-116`
- **MED** No shared command error handling — `/export`,`/init`,`/resume` throw raw exceptions to loop. Wrap dispatch in one try/catch → `Renderer.WriteError`. `Commands/*`

## Theme 5 — Design / maintainability (safe extractions)
- Duplicated ACP pause-resume: `AcpPauseResponseHandler` was meant to replace `AcpTurnRunner.ResumeWith*`; both exist & drifted. Delete one. *(biggest smell)*
- God classes: `Rendering/AnsiPainter.cs` (1687), `Program.cs` (964, `RunAgentAsync` ~500), `Session/ConversationLoop.cs` (957). Pure-extraction splits (`AnsiText`/`ThroughputMeter`/`MessageRenderer`; `BuildToolRegistry`/`ReplRunner`/`ConfigureAndStartAcp`; `ContextWindowManager`/`ToolDispatcher`).
- Dead code: `ConversationLoop.ExecuteToolCallsAsync` (superseded by inflight variant).
- Duplicated file-mutation boilerplate: `DiagnoseWriteFailure` byte-identical in `FileWriteTool.cs:87` & `FileEditTool.cs:101`; hoist guard→scan→history→write to `FileMutationBase`.
- Duplicated ANSI escapes across 4 files; centralize in one `Ansi` constants class.
- `ToolResult.Error()` always maps to `InvalidInput` → runtime failures look like bad args. Use `Crash`/`StateConflict`. `Tools/ToolResult.cs:35`

## Theme 6 — Performance (lower)
- `FileReadTool` loads whole file then `Skip/Take`. Use `File.ReadLinesAsync` + short-circuit + size guard. `FileReadTool.cs:94`
- `Checkpointer.BuildContextWindow` re-allocates ~5×/iteration; cache once per iteration. `ConversationLoop.cs:272-326`
- Several `new HttpClient()` probe calls; `GatewayCapabilities` caches transient failures forever & ignores `ct`. `Program.cs:623-863`, `GatewayCapabilities.cs:38-49`

---

## Execution plan (sequencing)
- [x] **Phase 1 — security floor:** DONE 2026-07-13. PathGuard symlink resolution (real-path for workspace+target; ValidateDirectory too), deny-before-allow in CheckCapabilitiesAsync, FileReadCap auto-allow via PathGuard, denial-escalation only on implicit ask fall-through, WebFetch SSRF (host pre-check + connection-time IP validation via SocketsHttpHandler.ConnectCallback — closes redirect + DNS-rebinding bypass). +6 regression tests. Build clean, no new test failures.
- [x] **Phase 2 — silent failures:** DONE 2026-07-13. Log.Warn/Debug added to Program.cs (autosave/warmth/ACP-stop/code-graph), SessionManager (msg+checkpoints), TurnJournal, RoslynTool x2, FileEdit/FileWrite diagnose probes, LspClient dispose. Recovery unchanged.
- [ ] **Phase 3 — correctness bugs:** Anthropic retry/SSE parity, warmup key, config-merge zeros, `IndexOf` mapping, terminal-restore handler. NOTE: warmup-key + config-merge-ApiKey are already worked around live via OPENMONO_API_KEY env var; the underlying MergeFrom drop of ApiKey is still unfixed here.
- [ ] **Phase 4 — ACP concurrency:** async store + pause/save lifetime + queue sync. Needs new tests, care.
- [ ] **Phase 5 — refactors:** delete duplicate pause-resume, then god-class extractions.

**Recommended first batch:** Phase 1 + Phase 2 together (best safety-to-risk, test-backed). Run `dotnet build` + test suite after each phase.

## Already done this session (context)
- Removed `/model` slash command (registration, help text, `Commands/ModelCommand.cs`) and rebuilt the `openmono-agent:latest` Docker image.
- Verified `/resume` works correctly (no bug — earlier confusion was a garbled query to the local model).
