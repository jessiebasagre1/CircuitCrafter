# Tier 0 — Assessment & Competency Layer

Drop-in package. **Nothing in your existing project is modified.** Every script here is new; the two places that touch existing systems (`LegacyAssessmentBridge`, and the optional `SaveManager` patch at the end) are opt-in.

Put the whole folder at `Assets/Scripts/Assessment/Core/`.

---

## What each script is for

| Script | Kind | Where it goes |
|---|---|---|
| `CompetencyTypes.cs` | enums | — |
| `TrainingMode.cs` | static | — |
| `CompetencyDefinition.cs` | ScriptableObject | Project window, one asset per module |
| `AssessmentResult.cs` | data | — |
| `ProfileManager.cs` | MonoBehaviour | Persistent **Managers** object |
| `CompetencyLedger.cs` | MonoBehaviour | Persistent **Managers** object |
| `ResultsRepository.cs` | static | — |
| `AssessmentRecorder.cs` | MonoBehaviour | Managers object in **COC3 / COC4** scenes |
| `LegacyAssessmentBridge.cs` | MonoBehaviour | Managers object in **COC1 / COC2** scenes |
| `OSInstallTaskBridge.cs` | MonoBehaviour | Managers object in the **COC1** scene |
| `AssessmentManagerBase.cs` | abstract | Base class for new COC3/COC4 managers |

> **Evidence comes from two sources.** A `CompetencyDefinition` accepts either a TaskManager task id *or* an `AssessmentRecorder` manual stage id in its evidence list, and doesn't need to say which. COC3 doesn't use TaskManager at all and COC4 names its repair tasks per ticket at runtime, so without the second source those two modules could never evidence an outcome.
>
> **`OSInstallTaskBridge` exists because the OS install is not a TaskManager task.** `OSInstallManager` announces itself with its own `OnInstallComplete` event, which is why `COC1CompletionManager` checks `osInstallDone` separately. A `CompetencyDefinition` can only name ids, so without this bridge "install the operating system" is an outcome with no possible evidence.

> **Do not put `AssessmentRecorder` and `LegacyAssessmentBridge` in the same scene.** Both listen to `TaskManager` and you'd get two results per run.

---

## Step 1 — Persistent managers (5 minutes)

On the same `DontDestroyOnLoad` GameObject that already holds `SaveManager` and `ScoreManager`, add two components:

1. **`ProfileManager`** — set *Default Profile Name* to `Trainee`. Nothing else to configure. On first launch it creates one profile automatically, so a build with no profile-picker UI behaves exactly as it does today.

2. **`CompetencyLedger`** — leave *Definitions* empty for now (Step 2 fills it). *Pass Threshold Percent* defaults to **80**; this is the score an Assessment run must reach before any outcome can be marked Competent.

That's it. The ledger loads the active profile's records in `Start()` and reloads automatically whenever the profile changes.

---

## Step 2 — Competency definition assets (the real work)

**Create → CircuitCrafter → Competency Definition**, once per module. Name them `Competency_COC1` … `Competency_COC4` and drag all four into `CompetencyLedger.Definitions`.

For each asset set `moduleId` (`COC1`…`COC4`), `unitTitle`, then list the learning outcomes. Under each outcome, list the **exact** TaskManager task id strings that evidence it.

> Inspector fields can't reference C# consts. Copy the strings from `SystemConfigTaskIds`, `PCDisassemblySetup` and `COC2TaskSetup` rather than retyping from memory — a typo doesn't throw, it just silently never satisfies the outcome.

### A starting point for COC1

Based on the task ids already in your code:

| Outcome | Title | Evidence task ids |
|---|---|---|
| LO1 | Assemble computer hardware | `disassemble_pc`, `assemble_pc` |
| LO2 | Prepare installer | *(your bootable-USB task id)* |
| LO3 | Install operating system and drivers | `install_drivers` |
| LO4 | Install application software | `install_app_software` |
| LO5 | Conduct testing and documentation | `install_updates`, `test_computer`, `document_work` |

`requiredEvidenceCount` left at **0** means *all* listed tasks are required — the right default. Set it to a lower number only when several tasks are alternative routes to the same skill.

Verify the outcome numbering and titles against the current Training Regulations before you finalise these — the codes end up in learner records and shouldn't change afterwards.

---

## Step 3a — COC1 and COC2: the bridge (2 minutes each)

Add **`LegacyAssessmentBridge`** to the Managers object in each scene. It auto-finds the assessment manager; assign it explicitly if you prefer.

- Leave *Contribute To Global Score* **off** — COC1/COC2 currently keep their assessment score to themselves, and turning it on would double-count.

It watches `IsFinished` flip false→true and writes a result. `COC1AssessmentManager` and `COC2AssessmentManager` are not touched and don't know it exists.

**What a bridged result can't include:** those managers keep their mistake log private and track no per-attempt data, so you get the mistake *count* but not the entries, and first-attempt accuracy reports as "n/a" rather than a fabricated number. COC2 is the exception — `FailedCables` is public, so it comes through as a categorised entry. New modules built on `AssessmentRecorder` get the full picture.

---

## Step 3b — COC3 and COC4: the recorder

Add **`AssessmentRecorder`** to the Managers object.

1. Set *Module Id* to `COC3` / `COC4`.
2. Fill the **Stages** list. Each entry needs either a `taskId` (closes itself when that TaskManager task completes) or a `manualId` (you close it with `CompleteStage("<id>")`), plus a display name and points.
3. Leave *Begin On Start* and *Finish When All Stages Complete* on.

For a module whose stages are all TaskManager tasks, **that's the entire setup** — it starts a timer, closes stages, finishes, writes the result and updates the ledger with no further wiring.

### Getting real data out of COC3

Three one-line calls in `COC3Manager.HandleStepResult` turn the existing step loop into recorded evidence. They're static and no-op when no recorder exists, so they're safe to add now:

```csharp
using CircuitCrafter.Assessment;

// in HandleStepResult, right after you know `success`:
AssessmentRecorder.Attempt(step.stepData.stepId, success);

if (!success)
    AssessmentRecorder.Mistake("configuration", message);
```

`Attempt` on every submit is what produces first-attempt accuracy — the number actually worth graphing, because a retry loop with a small penalty mostly measures persistence rather than knowledge.

To hook COC3's completion screen, either add a stage per step group, or call `AssessmentRecorder.Active.Finish()` from `COC3CompletionNotifier.OnEnable` beside the existing `MarkCompleted()`.

---

## Step 4 — Optional: per-profile save files

This is the one change to an existing file. Without it, everything above still works — results and ledgers are already per-profile — but the **save file** stays shared, so on a lab PC the second student still overwrites the first's in-progress scene state.

In `SaveManager.cs`, replace the one-line `SavePath` property:

```csharp
// BEFORE
private string SavePath => Path.Combine(Application.persistentDataPath, "savegame.json");

// AFTER
private string SavePath => Path.Combine(
    Application.persistentDataPath,
    CircuitCrafter.Assessment.ProfileManager.Instance != null
        ? CircuitCrafter.Assessment.ProfileManager.SaveFileNameFor(
              CircuitCrafter.Assessment.ProfileManager.Instance.ActiveProfileId)
        : "savegame.json");
```

The null check matters: `SaveManager.Awake()` reads the save file, and there's no guaranteed ordering between two components' `Awake()` even on the same GameObject. The fallback keeps the old behaviour if `ProfileManager` hasn't initialised yet.

**Existing saves:** the old `savegame.json` is not read once this is in. Either rename it to `savegame_<id>.json` for the auto-created profile, or accept that existing testers start fresh.

---

## Step 5 — Optional: the three training tiers

`TrainingMode` adds a **Practice** tier between campaign and assessment, and works read-only over what you already have — if nothing calls `Begin()`, it derives the mode from `AssessmentMode`, so today's menus stay correct.

To enable Practice, change one line in `AssessmentMenuController.OnModuleClicked`:

```csharp
// BEFORE
AssessmentMode.Begin(m.modeId);

// AFTER — pass whichever tier the button represents
TrainingMode.Begin(m.modeId, SessionMode.Assessment);
```

`TrainingMode.Begin` keeps `AssessmentMode` in sync automatically, so every existing `IsAssessing()` check stays correct without being touched.

Also add `TrainingMode.End();` to `MenuNavigator.Start()`, beside the `AssessmentMode.End()` and `TicketSessionState.Clear()` calls already there.

Then in new code: `TrainingMode.IsGraded` (timers, no hints), `TrainingMode.HintsAllowed`, `TrainingMode.IsUnassisted` (show the HUD).

---

## Where the data lands

Under `Application.persistentDataPath`:

```
profiles.json                          the learner index
ledger_{profileId}.json                competency standing, one per learner
Results/{profileId}/{resultId}.json    one full record per run
Results/{profileId}/results.csv        one summary row per run, appended
Exports/outcomes-{timestamp}.csv       on demand, one row per outcome per run
```

`results.csv` opens straight in Excel. For the long-format per-outcome file:

```csharp
ResultsRepository.ExportOutcomesCsv(ProfileManager.Instance.ActiveProfileId);
```

Wire that to a button in a settings or instructor panel. On Windows `persistentDataPath` is `%userprofile%\AppData\LocalLow\<Company>\<Product>`.

---

## Promotion rules

| Situation | Result |
|---|---|
| Any completed run | at least **Practised** |
| Assessment mode + run completed + outcome fully evidenced + score ≥ threshold | **Competent** |
| A worse run later | **no change** — promotion is one-way |

A **Guided** run can never award Competent. That's deliberate: a run where the game led the learner through every step is evidence of exposure, not of independent competence — which is also how the real assessment treats it.

---

## Verifying it works

1. Play COC1 in assessment mode and finish a run.
2. Check the Console for `[ResultsRepository] Saved result …`.
3. Open `Results/<profileId>/results.csv` — one row.
4. Open `ledger_<profileId>.json` — one record per outcome, with statuses.
5. `CompetencyLedger.Instance.IsModeCompetent("COC1")` should now answer meaningfully. This is the check to gate COC4 tickets on later: a cabling fault should require COC2 *competence*, not merely that COC2 was finished once.

---

## What comes next

- **Competency profile screen** — a UI over `CompetencyLedger.GetRecords()` / `GetModuleProgress()`. All the data is already there; it just needs a view.
- **Profile picker** — a UI over `ProfileManager.AllProfiles` / `CreateProfile` / `SelectProfile`.
- **Migrate COC1 and COC2 onto `AssessmentManagerBase`** — when you next have reason to open those scenes. Delete the bridge at that point or you'll record twice.
