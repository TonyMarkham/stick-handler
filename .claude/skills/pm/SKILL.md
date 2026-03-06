# pm-cli - Blazor Agile Board CLI

Command-line interface for programmatic interaction with the Blazor Agile Board project management system. All operations sync in real-time with the Blazor UI through WebSocket broadcasts.

## Quick Reference

**Binary location:** `pm` (macOS/Linux shell script) or `pm.bat` (Windows batch file), in the **repository root**.

Examples use `./pm` (bash). On Windows use `pm.bat` instead.

**Invocation:** `./pm <command> [options] [--pretty]`

**Global options:**
- `--server <URL>` — Server URL (default: auto-discovered from `.pm/server.json`)
- `--user-id <UUID>` — User ID (default: LLM user from `.pm/config.toml`)
- `--pretty` — Pretty-print JSON output (recommended)

**IMPORTANT:** The `pm` and `pm.bat` scripts are in the **repository root**, not in the skill directory. The skill directory only contains this documentation.

## STOP — Read This Before Running Commands

**`work-item create --parent-id` does NOT resolve display keys.** It requires a UUID. The recommended pattern for creating hierarchies is to capture UUIDs from previous commands:

```bash
EPIC=$(./pm work-item create --project-id "$PROJECT_ID" --type epic --title "My Epic" --pretty)
EPIC_ID=$(echo "$EPIC" | jq -r '.work_item.id')

./pm work-item create --project-id "$PROJECT_ID" --type task \
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
./pm work-item get PONE-123 --pretty

# List work items using project key
./pm work-item list PONE --pretty

# Update work item status using display key
./pm work-item update PONE-123 \
  --version 1 \
  --status in_progress --pretty

# Create dependency between work items using display keys
./pm dependency create \
  --blocking PONE-123 \
  --blocked PONE-124 \
  --type blocks --pretty

# Export a work item using display key
./pm sync export work-item PONE-123 \
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
./pm project list [--pretty]

# Get a specific project by ID
./pm project get <project-id> [--pretty]

# Create a new project
./pm project create \
  --title "Project Title" \
  --key "PROJ" \
  [--description "Project description"] \
  [--pretty]

# Update a project
./pm project update <project-id> \
  --expected-version <current-version> \
  [--title "New title"] \
  [--description "New description"] \
  [--status <active|archived>] \
  [--pretty]

# Delete a project
./pm project delete <project-id> [--pretty]
```

**Valid statuses:** `active`, `archived`

### Work Item Commands

```bash
# List work items in a project (with optional filters)
./pm work-item list <project-id> \
  [--type <epic|story|task>] \
  [--status <status>] \
  [--parent-id <uuid>] \
  [--orphaned] \
  [--descendants-of <uuid>] \
  [--include-done] \
  [--pretty]

# Get a specific work item
./pm work-item get <work-item-id> [--pretty]

# Create a new work item
./pm work-item create \
  --project-id <uuid> \
  --type <epic|story|task> \
  --title "Title" \
  [--description "Description"] \
  [--parent-id <uuid>] \
  [--status <backlog|todo|in_progress|review|done|blocked>] \
  [--priority <low|medium|high|critical>] \
  [--pretty]

# Update a work item
./pm work-item update <work-item-id> \
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
  [--pretty]

# Delete a work item
./pm work-item delete <work-item-id> [--pretty]
```

**Valid statuses:** `backlog`, `todo`, `in_progress`, `review`, `done`, `blocked`
**Valid priorities:** `low`, `medium`, `high`, `critical`
**Valid types:** `epic`, `story`, `task`

**List filters:** `--parent-id`, `--orphaned`, and `--descendants-of` are mutually exclusive (enforced by the CLI). All three can combine with `--type`, `--status`, and `--include-done`.

### Sprint Commands

```bash
# List sprints in a project
./pm sprint list <project-id> [--pretty]

# Get a specific sprint
./pm sprint get <sprint-id> [--pretty]

# Create a new sprint
./pm sprint create \
  --project-id <uuid> \
  --name "Sprint Name" \
  --start-date <unix-timestamp> \
  --end-date <unix-timestamp> \
  [--goal "Sprint goal"] \
  [--pretty]

# Update a sprint
./pm sprint update <sprint-id> \
  --expected-version <current-version> \
  [--name "New name"] \
  [--goal "New goal"] \
  [--start-date <unix-timestamp>] \
  [--end-date <unix-timestamp>] \
  [--status <planned|active|completed>] \
  [--pretty]

# Delete a sprint
./pm sprint delete <sprint-id> [--pretty]
```

### Comment Commands

```bash
# List comments on a work item
./pm comment list <work-item-id> [--pretty]

# Create a comment
./pm comment create \
  --work-item-id <uuid> \
  --content "Comment text" \
  [--pretty]

# Update a comment
./pm comment update <comment-id> \
  --content "Updated text" \
  [--pretty]

# Delete a comment
./pm comment delete <comment-id> [--pretty]
```

### Dependency Commands

```bash
# List dependencies for a work item
./pm dependency list <work-item-id> [--pretty]

# Create a dependency link
./pm dependency create \
  --blocking <work-item-id> \
  --blocked <work-item-id> \
  --type <blocks|relates_to> \
  [--pretty]

# Delete a dependency
./pm dependency delete <dependency-id> [--pretty]
```

### Time Entry Commands

```bash
# List time entries for a work item
./pm time-entry list <work-item-id> [--pretty]

# Get a specific time entry
./pm time-entry get <time-entry-id> [--pretty]

# Start a timer on a work item
./pm time-entry create \
  --work-item-id <uuid> \
  [--description "What you're working on"] \
  [--pretty]

# Stop a running timer or update description
./pm time-entry update <time-entry-id> \
  [--stop] \
  [--description "Updated description"] \
  [--pretty]

# Delete a time entry
./pm time-entry delete <time-entry-id> [--pretty]
```

### Swim Lane Commands

```bash
# List swim lanes for a project (read-only)
./pm swim-lane list <project-id> [--pretty]
```

**Note:** Swim lanes are read-only via CLI. Use the Blazor UI to create/update/delete swim lanes.

### Sync Commands

```bash
# Export all data to JSON
./pm sync export [-o|--output <file>] [--pretty]

# Export a specific work item (scoped export)
./pm sync export [-o|--output <file>] work-item <work-item-id> \
  [--descendant-levels <0-2>] \
  [--comments] \
  [--sprints] \
  [--dependencies] \
  [--time-entries] \
  [--pretty]

# Import data from JSON file
./pm sync import -f|--file <json-file> [--pretty]
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
./pm desktop
```

## Usage Patterns

### Project Setup Workflow

```bash
# 1. Create project
PROJECT=$(./pm project create \
  --title "My Project" --key "MP" \
  --description "Project description" --pretty)
PROJECT_ID=$(echo "$PROJECT" | jq -r '.project.id')

# 2. Create a 2-week sprint
SPRINT=$(./pm sprint create \
  --project-id "$PROJECT_ID" \
  --name "Sprint 1" \
  --start-date $(date +%s) \
  --end-date $(($(date +%s) + 1209600)) \
  --goal "Initial setup and core features" --pretty)
SPRINT_ID=$(echo "$SPRINT" | jq -r '.sprint.id')

# 3. Create epic → task hierarchy
EPIC=$(./pm work-item create \
  --project-id "$PROJECT_ID" --type epic \
  --title "User Authentication" \
  --description "Implement complete auth system" \
  --priority high --pretty)
EPIC_ID=$(echo "$EPIC" | jq -r '.work_item.id')

TASK=$(./pm work-item create \
  --project-id "$PROJECT_ID" --type task \
  --parent-id "$EPIC_ID" \
  --title "Implement OAuth2 login" \
  --status todo --priority high --pretty)
TASK_ID=$(echo "$TASK" | jq -r '.work_item.id')
TASK_VERSION=$(echo "$TASK" | jq -r '.work_item.version')

# 4. Assign to sprint, start work
TASK=$(./pm work-item update "$TASK_ID" \
  --version $TASK_VERSION \
  --sprint-id "$SPRINT_ID" \
  --status in_progress \
  --story-points 5 --pretty)
TASK_VERSION=$(echo "$TASK" | jq -r '.work_item.version')

# 5. Timer + comment
TIMER=$(./pm time-entry create \
  --work-item-id "$TASK_ID" \
  --description "Working on OAuth implementation" --pretty)
TIMER_ID=$(echo "$TIMER" | jq -r '.time_entry.id')

./pm comment create \
  --work-item-id "$TASK_ID" \
  --content "Started implementation, setting up OAuth provider" --pretty

# 6. Finish up
./pm time-entry update "$TIMER_ID" --stop --pretty
./pm work-item update "$TASK_ID" --version $TASK_VERSION --status done --pretty
```

### Reparenting Work Items

Move work items between parents or orphan them using `--parent-id` with `--update-parent`:

```bash
# Move an orphan task under a story
./pm work-item update "$TASK_ID" \
  --version "$VERSION" \
  --parent-id "$STORY_ID" \
  --update-parent --pretty

# Move a task from one story to another
./pm work-item update "$TASK_ID" \
  --version "$VERSION" \
  --parent-id "$NEW_STORY_ID" \
  --update-parent --pretty

# Orphan a task (remove its parent)
./pm work-item update "$TASK_ID" \
  --version "$VERSION" \
  --parent-id "" \
  --update-parent --pretty
```

**Why `--update-parent` is required:** Without the flag, omitting `--parent-id` means "don't change the parent." With the flag, omitting `--parent-id` (or passing empty string) means "clear the parent." The flag disambiguates intent.

### Filtering and Querying

```bash
# Children of a specific parent
./pm work-item list "$PROJECT_ID" --parent-id "$EPIC_ID" --pretty

# Orphaned items (no parent) — useful for finding misplaced work items
./pm work-item list "$PROJECT_ID" --orphaned --pretty

# Orphaned stories specifically
./pm work-item list "$PROJECT_ID" --orphaned --type story --pretty

# All descendants recursively (stories + their tasks)
./pm work-item list "$PROJECT_ID" --descendants-of "$EPIC_ID" --pretty

# Just the tasks in an epic's entire tree
./pm work-item list "$PROJECT_ID" --descendants-of "$EPIC_ID" --type task --pretty

# Combine with status filter
./pm work-item list "$PROJECT_ID" --parent-id "$EPIC_ID" --status in_progress --pretty
```

### Scoped Export

```bash
# Epic with full tree and all related data
./pm sync export -o epic.json work-item "$EPIC_ID" \
  --descendant-levels 2 \
  --comments --sprints --dependencies --time-entries --pretty

# Story with child tasks and comments
./pm sync export -o story.json work-item "$STORY_ID" \
  --descendant-levels 1 --comments --pretty

# Single work item only (no related data)
./pm sync export work-item "$WORK_ITEM_ID" --pretty
```

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
./pm work-item delete "$TASK_ID"
./pm work-item create --project-id "$PROJECT_ID" --type task --title "Fixed title" ...

# CORRECT — preserves all relationships and history
./pm work-item update "$TASK_ID" --version "$VERSION" --title "Fixed title" --description "Fixed description"
```

Only delete a work item if it was created in error and has no dependencies, comments, or other references.

## Important Notes

### Done Items Excluded by Default

`work-item list` excludes items with status `done` by default. Add `--include-done` to see them:
```bash
./pm work-item list "$PROJECT_ID" --pretty              # active items only
./pm work-item list "$PROJECT_ID" --include-done --pretty  # include completed
```

### Optimistic Locking

Work item, project, and sprint updates require a version parameter (`--version` or `--expected-version`) matching the current version. On mismatch you get a `CONFLICT` error. Always fetch the latest version before updating:

```bash
VERSION=$(./pm work-item get "$TASK_ID" --pretty | jq -r '.work_item.version')
./pm work-item update "$TASK_ID" --version "$VERSION" --status in_progress --pretty
```

### Quoting Descriptions

Descriptions containing backticks, quotes, or code blocks require careful shell quoting:

- **No backticks:** double quotes are fine
- **With backticks/code blocks:** use single quotes around `--description`
- **Backticks AND shell variables:** use heredoc with single-quoted delimiter:

````bash
./pm work-item create \
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
