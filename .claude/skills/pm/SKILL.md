# pm-cli - Blazor Agile Board CLI

Command-line interface for programmatic interaction with the Blazor Agile Board project management system. All operations sync in real-time with the Blazor UI through WebSocket broadcasts.

## CRITICAL: Use `$PM` for Every Command

`PM` is pre-set as an environment variable in `.claude/settings.local.json` and points directly to the correct binary for this project. **Do not set it yourself. Do not guess the path. Do not use `./pm` or any other path.**

Just use it:

```bash
$PM <command>
```

**If `$PM` is empty or unset**, stop immediately and tell the user — do not attempt to locate or construct the path yourself.

## Quick Reference

**Binary:** `$PM` (pre-set via `.claude/settings.local.json`)

**Invocation:** `$PM <command> [options] [--pretty]`

**Global options:**
- `--server <URL>` — Server URL (default: auto-discovered from `.pm/server.json`)
- `--user-id <UUID>` — User ID (default: LLM user from `.pm/config.toml`)
- `--pretty` — Pretty-print JSON output (recommended)
- `--output-toml <PATH>` — Write response to a TOML file (stdout still receives JSON)

## STOP — Read This Before Running Commands

**`work-item create --parent-id` does NOT resolve display keys.** It requires a UUID. The recommended pattern for creating hierarchies is to capture UUIDs from previous commands:

```bash
EPIC=$($PM work-item create --project-id "$PROJECT_ID" --type epic --title "My Epic" --pretty)
EPIC_ID=$(echo "$EPIC" | jq -r '.work_item.id')

$PM work-item create --project-id "$PROJECT_ID" --type task \
  --parent-id "$EPIC_ID" --title "My Task" --pretty
```

**If you get `Invalid UUID format` errors**, you are passing a display key (like `PONE-123`) or project key (like `PONE`) where a UUID is required. Check the "What accepts display keys" table below — if your command/flag is not listed, use a UUID.

## Display Key Resolution

**You can use human-readable keys instead of UUIDs in read/update/delete commands.**

The CLI automatically resolves:
- **Display keys** (like `PONE-123`) for work items
- **Project keys** (like `PONE`) for projects

**This does NOT work everywhere.** Check the tables below for exactly which commands and flags support key resolution.

### Examples

```bash
# Get a work item by display key instead of UUID
$PM work-item get PONE-123 --pretty

# List work items using project key
$PM work-item list PONE --pretty

# Update work item status using display key
$PM work-item update PONE-123 \
  --version 1 \
  --status in_progress --pretty

# Create dependency between work items using display keys
$PM dependency create \
  --blocking PONE-123 \
  --blocked PONE-124 \
  --type blocks --pretty

# Export a work item using display key
$PM sync export work-item PONE-123 \
  --descendant-levels 2 --comments --pretty
```

**What accepts display keys (work item keys like `PONE-123`):**
- Work item: `get`, `update`, `delete` — positional ID argument
- Work item `list`: `--parent-id`, `--descendants-of` flags
- Comment: `list`, `create` — `--work-item-id` flag
- Dependency: `list`, `create` — `--blocking`, `--blocked` flags
- Time entry: `list`, `create` — `--work-item-id` flag
- Sync: `export work-item` — positional ID argument

**What accepts project keys (like `PONE`):**
- Project: `get`, `update`, `delete` — positional ID argument
- Work item: `list` — positional project-id argument
- Sprint: `list` — positional project-id argument
- Swim lane: `list` — positional project-id argument

**What does NOT accept display keys or project keys (UUID ONLY):**
- **`work-item create --parent-id`** — requires UUID
- **`work-item create --project-id`** — requires UUID
- **`sprint create --project-id`** — requires UUID
- **`work-item update --parent-id`** — requires UUID
- Any `--assignee-id`, `--sprint-id` flag — requires UUID

**UUIDs always work:** All commands that accept keys also accept UUIDs.

## When to Use This Skill

Use the pm-cli when you need to:

1. **Manage projects** — Create, read, update, delete, and list projects
2. **Manage work items** — Full CRUD on tasks, stories, and epics
3. **Manage sprints** — Full CRUD on sprint planning cycles
4. **Manage comments** — Create, update, delete, and list comments on work items
5. **Track time** — Start/stop timers and manage time entries on work items
6. **Manage dependencies** — Create and delete dependency links between work items
7. **Query data** — Read-only access to swim lanes, or filtered queries on work items
8. **Bulk operations** — Export/import entire project data as JSON
9. **Launch desktop app** — Start the Tauri desktop application

**DO NOT use this skill for:**
- Building or compiling code
- Modifying source code or configuration files
- Tasks that are better suited for direct file manipulation

## Available Commands

### Project Commands

```bash
# List all projects
$PM project list [--pretty]

# Get a specific project by ID
$PM project get <project-id> [--pretty]

# Create a new project
$PM project create \
  --title "Project Title" \
  --key "PROJ" \
  [--description "Project description"] \
  [--pretty]

# Update a project
$PM project update <project-id> \
  --expected-version <current-version> \
  [--title "New title"] \
  [--description "New description"] \
  [--status <active|archived>] \
  [--pretty]

# Delete a project
$PM project delete <project-id> [--pretty]
```

**Valid statuses:** `active`, `archived`

### Work Item Commands

```bash
# List work items in a project (with optional filters)
$PM work-item list <project-id> \
  [--type <epic|story|task>] \
  [--status <status>] \
  [--parent-id <uuid>] \
  [--orphaned] \
  [--descendants-of <uuid>] \
  [--include-done] \
  [--pretty]

# Get a specific work item
$PM work-item get <work-item-id> [--pretty]

# Create a new work item
$PM work-item create \
  [--project-id <uuid>] \
  [--type <epic|story|task>] \
  [--title "Title"] \
  [--description "Description"] \
  [--parent-id <uuid>] \
  [--status <backlog|todo|in_progress|review|done|blocked>] \
  [--priority <low|medium|high|critical>] \
  [--from-toml <PATH>] \
  [--pretty]

# Note: --project-id, --type, and --title are required but can come
# from the --from-toml file instead of the CLI.

# Update a work item
$PM work-item update <work-item-id> \
  --version <current-version> \
  [--title "New title"] \
  [--description "New description"] \
  [--status <status>] \
  [--priority <priority>] \
  [--assignee-id <uuid>] \
  [--sprint-id <uuid>] \
  [--story-points <0-100>] \
  [--parent-id <uuid>] \
  [--update-parent] \
  [--position <int>] \
  [--from-toml <PATH>] \
  [--pretty]

# Note: --version must always be on the CLI (not in the TOML file).

# Delete a work item
$PM work-item delete <work-item-id> [--pretty]
```

**Valid statuses:** `backlog`, `todo`, `in_progress`, `review`, `done`, `blocked`
**Valid priorities:** `low`, `medium`, `high`, `critical`
**Valid types:** `epic`, `story`, `task`

**List filters:** `--parent-id`, `--orphaned`, and `--descendants-of` are mutually exclusive (enforced by the CLI). All three can combine with `--type`, `--status`, and `--include-done`.

### Sprint Commands

```bash
# List sprints in a project
$PM sprint list <project-id> [--pretty]

# Get a specific sprint
$PM sprint get <sprint-id> [--pretty]

# Create a new sprint
$PM sprint create \
  --project-id <uuid> \
  --name "Sprint Name" \
  --start-date <unix-timestamp> \
  --end-date <unix-timestamp> \
  [--goal "Sprint goal"] \
  [--pretty]

# Update a sprint
$PM sprint update <sprint-id> \
  --expected-version <current-version> \
  [--name "New name"] \
  [--goal "New goal"] \
  [--start-date <unix-timestamp>] \
  [--end-date <unix-timestamp>] \
  [--status <planned|active|completed>] \
  [--pretty]

# Delete a sprint
$PM sprint delete <sprint-id> [--pretty]
```

### Comment Commands

```bash
# List comments on a work item
$PM comment list <work-item-id> [--pretty]

# Create a comment
$PM comment create \
  --work-item-id <uuid> \
  --content "Comment text" \
  [--pretty]

# Update a comment
$PM comment update <comment-id> \
  --content "Updated text" \
  [--pretty]

# Delete a comment
$PM comment delete <comment-id> [--pretty]
```

### Dependency Commands

```bash
# List dependencies for a work item
$PM dependency list <work-item-id> [--pretty]

# Create a dependency link
$PM dependency create \
  --blocking <work-item-id> \
  --blocked <work-item-id> \
  --type <blocks|relates_to> \
  [--pretty]

# Delete a dependency
$PM dependency delete <dependency-id> [--pretty]
```

### Time Entry Commands

```bash
# List time entries for a work item
$PM time-entry list <work-item-id> [--pretty]

# Get a specific time entry
$PM time-entry get <time-entry-id> [--pretty]

# Start a timer on a work item
$PM time-entry create \
  --work-item-id <uuid> \
  [--description "What you're working on"] \
  [--pretty]

# Stop a running timer or update description
$PM time-entry update <time-entry-id> \
  [--stop] \
  [--description "Updated description"] \
  [--pretty]

# Delete a time entry
$PM time-entry delete <time-entry-id> [--pretty]
```

### Swim Lane Commands

```bash
# List swim lanes for a project (read-only)
$PM swim-lane list <project-id> [--pretty]
```

**Note:** Swim lanes are read-only via CLI. Use the Blazor UI to create/update/delete swim lanes.

### Sync Commands

```bash
# Export all data to JSON
$PM sync export [-o|--output <file>] [--pretty]

# Export a specific work item (scoped export)
$PM sync export [-o|--output <file>] work-item <work-item-id> \
  [--descendant-levels <0-2>] \
  [--comments] \
  [--sprints] \
  [--dependencies] \
  [--time-entries] \
  [--pretty]

# Import data from JSON file
$PM sync import -f|--file <json-file> [--pretty]
```

**Scoped export flags:**
- `--descendant-levels <N>` — Include N levels of children (0=just item, 1=children, 2=grandchildren)
- `--comments` — Include comments for matched work items
- `--sprints` — Include sprint data referenced by matched work items
- `--dependencies` — Include dependency links involving matched work items
- `--time-entries` — Include time entries for matched work items

Without flags, scoped export returns only the work item itself. The response uses the same `ExportData` format as full export (compatible with `sync import`).

### Desktop Command

```bash
# Launch the Tauri desktop application
$PM desktop
```

## Usage Patterns

### Project Setup Workflow

```bash
# 1. Create project
PROJECT=$($PM project create \
  --title "My Project" --key "MP" \
  --description "Project description" --pretty)
PROJECT_ID=$(echo "$PROJECT" | jq -r '.project.id')

# 2. Create a 2-week sprint
SPRINT=$($PM sprint create \
  --project-id "$PROJECT_ID" \
  --name "Sprint 1" \
  --start-date $(date +%s) \
  --end-date $(($(date +%s) + 1209600)) \
  --goal "Initial setup and core features" --pretty)
SPRINT_ID=$(echo "$SPRINT" | jq -r '.sprint.id')

# 3. Create epic → task hierarchy
EPIC=$($PM work-item create \
  --project-id "$PROJECT_ID" --type epic \
  --title "User Authentication" \
  --description "Implement complete auth system" \
  --priority high --pretty)
EPIC_ID=$(echo "$EPIC" | jq -r '.work_item.id')

TASK=$($PM work-item create \
  --project-id "$PROJECT_ID" --type task \
  --parent-id "$EPIC_ID" \
  --title "Implement OAuth2 login" \
  --status todo --priority high --pretty)
TASK_ID=$(echo "$TASK" | jq -r '.work_item.id')
TASK_VERSION=$(echo "$TASK" | jq -r '.work_item.version')

# 4. Assign to sprint, start work
TASK=$($PM work-item update "$TASK_ID" \
  --version $TASK_VERSION \
  --sprint-id "$SPRINT_ID" \
  --status in_progress \
  --story-points 5 --pretty)
TASK_VERSION=$(echo "$TASK" | jq -r '.work_item.version')

# 5. Timer + comment
TIMER=$($PM time-entry create \
  --work-item-id "$TASK_ID" \
  --description "Working on OAuth implementation" --pretty)
TIMER_ID=$(echo "$TIMER" | jq -r '.time_entry.id')

$PM comment create \
  --work-item-id "$TASK_ID" \
  --content "Started implementation, setting up OAuth provider" --pretty

# 6. Finish up
$PM time-entry update "$TIMER_ID" --stop --pretty
$PM work-item update "$TASK_ID" --version $TASK_VERSION --status done --pretty
```

### Reparenting Work Items

Move work items between parents or orphan them using `--parent-id` with `--update-parent`:

```bash
# Move an orphan task under a story
$PM work-item update "$TASK_ID" \
  --version "$VERSION" \
  --parent-id "$STORY_ID" \
  --update-parent --pretty

# Move a task from one story to another
$PM work-item update "$TASK_ID" \
  --version "$VERSION" \
  --parent-id "$NEW_STORY_ID" \
  --update-parent --pretty

# Orphan a task (remove its parent)
$PM work-item update "$TASK_ID" \
  --version "$VERSION" \
  --parent-id "" \
  --update-parent --pretty
```

**Why `--update-parent` is required:** Without the flag, omitting `--parent-id` means "don't change the parent." With the flag, omitting `--parent-id` (or passing empty string) means "clear the parent." The flag disambiguates intent.

### Filtering and Querying

```bash
# Children of a specific parent
$PM work-item list "$PROJECT_ID" --parent-id "$EPIC_ID" --pretty

# Orphaned items (no parent) — useful for finding misplaced work items
$PM work-item list "$PROJECT_ID" --orphaned --pretty

# Orphaned stories specifically
$PM work-item list "$PROJECT_ID" --orphaned --type story --pretty

# All descendants recursively (stories + their tasks)
$PM work-item list "$PROJECT_ID" --descendants-of "$EPIC_ID" --pretty

# Just the tasks in an epic's entire tree
$PM work-item list "$PROJECT_ID" --descendants-of "$EPIC_ID" --type task --pretty

# Combine with status filter
$PM work-item list "$PROJECT_ID" --parent-id "$EPIC_ID" --status in_progress --pretty
```

### Scoped Export

```bash
# Epic with full tree and all related data
$PM sync export -o epic.json work-item "$EPIC_ID" \
  --descendant-levels 2 \
  --comments --sprints --dependencies --time-entries --pretty

# Story with child tasks and comments
$PM sync export -o story.json work-item "$STORY_ID" \
  --descendant-levels 1 --comments --pretty

# Single work item only (no related data)
$PM sync export work-item "$WORK_ITEM_ID" --pretty
```

### Saving Output to a TOML File

Use `--output-toml <PATH>` on **any command** to write the response as TOML to a file.
Stdout still receives JSON — jq pipelines are unaffected.

```bash
# Save project list to TOML
$PM project list --output-toml /tmp/projects.toml

# Save and pipe to jq simultaneously
$PM work-item list PONE --output-toml /tmp/items.toml | jq '.work_items | length'

# Save a full scoped export
$PM sync export work-item PONE-42 \
  --descendant-levels 2 --comments \
  --output-toml /tmp/epic-context.toml --pretty
```

**Why TOML?** TOML handles markdown content (descriptions with backticks, code blocks)
as multiline strings. Null fields (unset assignee, sprint, etc.) are automatically omitted.

### Creating and Updating Work Items from TOML

Use `--from-toml <PATH>` to load work item fields from a TOML file.
CLI flags always override TOML values. Best for descriptions with markdown.

**TOML file schema (`item.toml`):**

```toml
# Required for create (can be on CLI instead)
project_id = "8d96310e-1e69-4dc5-9529-5c173674ab90"
type = "task"
title = "My Work Item"

# Multi-line description — backticks and code blocks work natively
description = """
## Summary

Implement the handler in `src/api.rs`.

```rust
pub async fn handle(State(db): State<DbPool>) -> Result<Json<Item>> {
    let item = db.get().await?;
    Ok(Json(item))
}
```

All existing tests must pass.
"""

# Optional fields
status = "todo"
priority = "high"

# Update-only fields (silently ignored when used with work-item create)
assignee_id  = "uuid-here"   # omit entirely to leave unchanged
sprint_id    = "uuid-here"   # omit entirely to leave unchanged
story_points = 5             # integer 0-100
position     = 3             # non-negative integer for ordering
```

**Create from TOML file:**

```bash
$PM work-item create --from-toml ./item.toml --pretty

# CLI flag overrides TOML field
$PM work-item create --from-toml ./item.toml --priority critical --pretty
```

**Round-trip edit workflow (get → edit → update, no jq needed):**

`--output-toml` produces a file you can edit directly and feed straight back
into `--from-toml`. The `[work_item]` wrapper and all server-only fields
(`id`, `created_at`, `version`, etc.) are automatically handled — just edit
the fields you want to change and pass the same file to update.

```bash
# 1. Fetch — saves full work item as TOML (version is in the file)
$PM work-item get PONE-42 --output-toml /tmp/pone42.toml

# 2. Edit /tmp/pone42.toml — change title, description, status, etc.
#    (Use the Edit tool — no shell permissions needed)

# 3. Push — read version from the file, pass the same file back
$PM work-item update PONE-42 --version N --from-toml /tmp/pone42.toml --pretty
```

**`--version` must always be on the CLI** — read it from the `version` field
in the TOML file. The file itself is not re-written by update; fetch again
if you need the new version for a subsequent update.

## Response Format

All commands return JSON with consistent structure:

**Success response:**
```json
{
  "work_item": { ... },
  "comment": { ... },
  "sprint": { ... }
}
```

**Error response:**
```json
{
  "error": {
    "code": "NOT_FOUND | VALIDATION_ERROR | CONFLICT | INTERNAL_ERROR",
    "message": "Human-readable error message"
  }
}
```

**Exit codes:** `0` success, `1` error (check stderr and JSON response).

## Critical Rules

### NEVER Delete and Recreate Work Items — Always Update

**NEVER use `work-item delete` followed by `work-item create` to fix a work item.** Always use `work-item update` instead.

Deleting destroys: dependency links, comments, time entries, sprint assignments, activity log references. It also leaves gaps in display key numbering (e.g., PROJ-124 disappears, replacement becomes PROJ-125).

```bash
# WRONG — destroys all associated data
$PM work-item delete "$TASK_ID"
$PM work-item create --project-id "$PROJECT_ID" --type task --title "Fixed title" ...

# CORRECT — preserves all relationships and history
$PM work-item update "$TASK_ID" --version "$VERSION" --title "Fixed title" --description "Fixed description"
```

Only delete a work item if it was created in error and has no dependencies, comments, or other references.

## Important Notes

### Done Items Excluded by Default

`work-item list` excludes items with status `done` by default. Add `--include-done` to see them:
```bash
$PM work-item list "$PROJECT_ID" --pretty              # active items only
$PM work-item list "$PROJECT_ID" --include-done --pretty  # include completed
```

### Optimistic Locking

Work item, project, and sprint updates require a version parameter (`--version` or `--expected-version`) matching the current version. On mismatch you get a `CONFLICT` error. Always fetch the latest version before updating:

```bash
VERSION=$($PM work-item get "$TASK_ID" --pretty | jq -r '.work_item.version')
$PM work-item update "$TASK_ID" --version "$VERSION" --status in_progress --pretty
```

### Quoting Descriptions

Descriptions containing backticks, quotes, or code blocks require careful shell quoting:

- **No backticks:** double quotes are fine
- **With backticks/code blocks:** use single quotes around `--description`
- **Backticks AND shell variables:** use heredoc with single-quoted delimiter:

````bash
$PM work-item create \
  --project-id "$PROJECT_ID" \
  --type task \
  --title "My task" \
  --description "$(cat <<'EOF'
Implement the handler in `src/api.rs`:

```rust
pub fn handle_request(req: Request) -> Response {
    Response::ok()
}
```

Must pass all existing tests.
EOF
)" \
  --pretty
````

The `<<'EOF'` (single-quoted delimiter) prevents bash from interpreting backticks and `$variables` inside the heredoc. The `$(cat ...)` wrapper captures the heredoc as a string for `--description`.

### Known Issues

1. **Swim lanes are read-only** — No create/update/delete via CLI; use the Blazor UI.
2. **Dependencies are immutable** — Can only be created or deleted, not updated.

### Real-time Synchronization

All CLI operations trigger WebSocket broadcasts. Changes appear instantly in the Blazor UI, Tauri desktop app, and any connected WebSocket clients.
