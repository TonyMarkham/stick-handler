# Efficient Prompt Workflow

### Plan

Use Claude Code's Panning Mode and allow it to create and itterate on the original plan

### Plan > PM
```text
- use the /pm skill to transfer the `unity-client-plan.md` plan to the PONE project on the Agile Board.
- Use proper Agile Techniques to organize Epics > Stories > Tasks 
- Use the tasks to show a concrete implementation complete with production-grade code snippets
- The tasks should also be written with well thought out explanations above each code block to give humans well structured context

**CRITICAL** MUST USE /pm !!! 
```

### Review
```text
- use /pm to find PONE-15
- Review all descendant tasks.
- Write the review as a comment on PONE-15

PONE-14 and 15 epics have already been implemented, so there is production code in the repo to reference if needed
```

### Address Review
```text
- use /pm to find PONE-15
- Address the review in the latest comment
- use the /pro-rust skill to align with my preferred patterns

Make sure everything complies with /pro-rust

**CRITICAL** MUST USE /pm !!!
```

### Implement
```text
- use /pm to find PONE-15
- Review all descendant tasks.
- read `CRITICAL_OPERATING_CONSTRAINTS.md`
- Teach me by presenting bite-sized chunks for me to write/edit keeping the commentary separate from the code snippets to make the code snippets easier for me to follow
- As each `work-item` is finished, use `./pm` to move it to `review`

**CRITICAL** MUST USE /pm !!!
```

### Validate
```text
- use /pm to find PONE-15
- read `CRITICAL_OPERATING_CONSTRAINTS.md`
- Review all descendant tasks.
- Identify any gaps that might have been skipped when implementing all of the work items in this repo.

**CRITICAL** MUST USE /pm !!!
```

### Commit
```text
I have staged all files, please commit without a byline.
```
---

---

---

---

### Super-Audit
```markdown
Use /pm to audit PONE-15 and all its descendants.

## RULES — READ THESE BEFORE DOING ANYTHING ELSE

1. **DO NOT USE SUB-AGENTS** Fetch each item yourself. Sub-Agents summarize and that breaks your task.
2. **ONE TASK AT A TIME.** Fetch each work item individually with `$PM work-item get`. Do NOT bulk-export. Do NOT process multiple tasks in one step.
3. Use the `--output-toml` flag when getting each work item.
4. **YOU ARE AN AUDITOR, NOT A PLANNER.** Your only job is to find bugs in the implementation code inside each task's description. Not praise. Not planning feedback. BUGS.
5. **WRITE FINDINGS TO A FILE.** Create `/tmp/pone15_audit.md`. After auditing each task, APPEND that task's bugs to the file. Do not post anything to the PM system until ALL tasks are done.
6. **POST ONE COMMENT ON THE EPIC ONLY.** When every task has been audited, post the entire `/tmp/pone15_audit.md` content as a single comment on PONE-15 using `$PM comment create`.

## HOW TO AUDIT EACH TASK

For every task with code in the description:
— Read EVERY line of code. Not a summary — every line.
— Check for: compile errors, missing imports, wrong API signatures, incorrect types, race conditions, silent failures, wrong table/field names, inconsistencies with peer tasks
— Cross-reference signatures against other tasks (e.g. if Task A calls a function defined in Task B, verify the call matches the definition)
— For Rust: check `.len()` vs `.chars().count()`, missing `#[derive]`, blocking in async, `query_scalar!` macro on runtime DBs, missing PRAGMAs, `unwrap_or_default()` swallowing errors
— For tests: check that assertions actually test what they claim, that setup inserts into the correct tables, that RAII guards use the correct APIs
— Audit the entire task, do not only criticize the low-hanging fruit. Remember you are THE Gordon Ramsay!
— Do not invent gap(s)/bug(s), be honest.

## FOR EACH BUG FOUND

Document with: Task ID, severity (Critical/Serious/Minor), exact code quote, explanation of why it's wrong, fix.

## SEVERITY GUIDE
— **Critical (-15)**: Won't compile, data corruption, assertion always passes/fails regardless of behaviour
— **Serious (-10)**: Race condition, wrong table/field, silent failure, API mismatch
— **Minor (-3)**: Style, redundant code, missing index

## SCORING
Start at 100. Add +5 good structure, +10 if code exists in tasks. Subtract all bug deductions. Show the maths.

## START
1. List all descendants: `$PM work-item list PONE --descendants-of PONE-15 --include-done`
2. Record the full list of IDs
3. Fetch and audit EACH ONE individually, appending findings to `/tmp/pone15_audit.md`
4. When ALL tasks are done, post the file contents as a comment on PONE-15

## Previous Reviews
— Use /pm to read the last comment on PONE-15. 
— Recognize that your previous reviews are previous comments on PONE-15
— Recognize that each task that has been updated per one of your previous reviews is documented as a commont on that Task.
```


### Address Super-Audit
```markdown
— Use /pm to read the last comment on PONE-15.
— The review is of the implementation outlined in each Task's Description.
— Audit the review and build a todo list of Agile Board Task Descriptions that need to address the identified bug(s)/gap(s)

## RULES — READ THESE BEFORE DOING ANYTHING ELSE

1. **DO NOT USE SUB-AGENTS** Fetch each item yourself. Sub-Agents summarize and that breaks your task.
2. **ONE TASK AT A TIME.** Fetch each work item individually with `$PM work-item get`. Do NOT bulk-export. Do NOT process multiple tasks in one step.
3. Use the `--output-toml` flag when getting each work item.
4. Modify the work item description in the toml.
5. Update each work item using the modified toml and the `--from-toml` flag.
6. Identify if **THIS** Task was flagged as having any issue(s) in the review.
7. While addressing the issue(s) ensure that everything complies with /pro-rust
8. **POST ONE COMMENT ON THE TASK ONLY IF IT WAS MODIFIED** If you chose to modify **THIS** Task, add a comment to it identifying what you changed and why.
9. **DO NOT REMOVE CODE BLOCKS WITH EXPLICITLY GETTING HUMAN INVOLVED**
```

### Individual
```text
— Use /pm to audit PONE-18 (Task)
— Do **NOT** use a sub-agent
— Strip out any commentary about fixing issues.
— Leave in commentary about what this task is for and what and code blocks are doing.
```
---

---

---

---

---

---

---

---

---

### Init
```txt
Read `docs/implementation-plan-v2.md.` We just finished impl'ing session 60
```

### Plan
```txt
Create a step-by-step implementation plan complete with proposed code snippets for how to implement Session 100.
```

### Challenge
```txt
Evaluate your plan and rate it based on how production grade it is.
```

### Product-Grade
```text
My quality bar is to be at least 9.25 out of 10 on a production grade score.

Iterating on your plan until you meet the target level.
```

### ordering
```text
Verify that the `80-Session-Plan-Teaching.md` has proper dependency ordering so that when implementing sequentially, nothing references code that hasn't been written yet.
```

```text
please stop thinking in terms of sub-sessions and instead in dependency order
```

```text
Can you show a dependency graph?
```

### x
```text
before continuing, can I get you to perform a fresh-eyes review of the plan.

Identify strengths/weaknesses/issues and re-grade x.x/10 for production-grade
```

### pm
```text
- use the /pm skill to transfer the plan to the Agile Board.
- Use proper Agile Techniques to organize Epics > Stories > Tasks 
- Use the tasks to show a concrete implementation complete with production-grade code snippets
- The tasks should also be written with well thought out explanations above each code block to give humans well structured context
- Do not try to automate this, perform each interaction 1 at a time 
```

### Sub-Process
```text
- Read `docs/session-plans/121-Session-Plan-Initial.md`
- Create a step-by-step implementation plan complete with production-grade code snippets for how an LLM can teacher the content to a human.
- Break it down into multiple sub-sessions targeting <50k tokens each.
- Take inspiration for the plan/sub-plans strategy from `docs/session-plans/template/`
```

### Summarize
```text
Restructure this session plan as teaching material:

  1. Add a "Teaching Focus" section (3-4 bullet points of concepts learned)
  2. Frame the work with "The Problem" and "The Solution"
  3. Add "Why X?" explanations after major code blocks
  4. Include a "Prerequisites Check" with verification commands
  5. End with "Key Concepts Learned" summary
  6. Add "Next Session" link if applicable

Follow the structure in docs/session-plans/42.1-Session-Plan.md as a template.
```

### Gordon Ramsay
```text
Read:
- `CRITICAL_OPERATING_CONSTRAINTS.md`
- `docs/session-plans/121.3.2-Session-Plan.md`
- `docs/session-plans/121.3.2.1-Session-Plan.md`
- `docs/session-plans/121.3.2.2-Session-Plan.md`
- `docs/session-plans/121.3.2.3-Session-Plan.md`
- `docs/session-plans/121.3.2.4-Session-Plan.md`
- `docs/session-plans/121.3.2.5-Session-Plan.md`
- `docs/session-plans/121.3.2.6-Session-Plan.md`

- Audit these and cross reference the contents against the current state of the project to avoid making assumptions.
- The audit should identify gaps in the plan.
- Raise issues where the repo is missing something that the plan needs/assumes already exists.
- Identify when the plan deviates from patterns that already exist in the repo.

Review the intended implementation quality of ONLY:
- `docs/session-plans/121.3.2.1-Session-Plan.md`
- `docs/session-plans/121.3.2.2-Session-Plan.md`
- `docs/session-plans/121.3.2.3-Session-Plan.md`
- `docs/session-plans/121.3.2.4-Session-Plan.md`
- `docs/session-plans/121.3.2.5-Session-Plan.md`
- `docs/session-plans/121.3.2.6-Session-Plan.md`
```

```text
Read:
- `CRITICAL_OPERATING_CONSTRAINTS.md`
- `docs/session-plans/121.3.2-Session-Plan.md`
- `docs/session-plans/121.3.2.1-Session-Plan.md`
- `docs/session-plans/121.3.2.2-Session-Plan.md`
- `docs/session-plans/121.3.2.3-Session-Plan.md`
- `docs/session-plans/121.3.2.4-Session-Plan.md`
- `docs/session-plans/121.3.2.5-Session-Plan.md`
- `docs/session-plans/121.3.2.6-Session-Plan.md`

Here is a review of the plan:
- `.reviews/20260208-083134.md`
```

### Other
```text
- Read `CRITICAL_OPERATING_CONSTRAINTS.md`
- use the /pm SKILL to audit the `PONE-39` Epic
- Cross reference it's contents against the current state of the project to avoid making poor assumptions.
- Teach me by presenting bite-sized chunks for me to write/edit keeping the commentary separate from the code snippets to make the code snippets easier for me to follow
- **The audit is important, but teaching is the ultimate goal of this session**
```

### Other
```text
Read `CRITICAL_OPERATING_CONSTRAINTS.md`, `docs/session-plans/120.2-Session-Plan.md` and `docs/session-plans/120-Session-Plan.md`.

Please audit `docs/session-plans/120.2-Session-Plan.md` and cross reference it's contents against the current state of the project to avoid making poor assumptions.

I want you to teach me by presenting me with bite-sized chunks for me to write/edit keeping the commentary separate from the code snippets to make the code snippets easier for me to follow

**The audit is important, but teaching is the ultimate goal of this session**
```

### End of Session Sanity Check
```text
Builds clean and all tests pass. Please sanity check that everything in `docs/session-plans/120.2-Session-Plan.md` was implemented as expected.
```

### Update Docs
```text
Please update `docs/session-plans/120.2-Session-Plan.md` and `docs/session-plans/120-Session-Plan.md`
```

### Commit
```text
I have staged all files, please commit without a byline.
```

---
