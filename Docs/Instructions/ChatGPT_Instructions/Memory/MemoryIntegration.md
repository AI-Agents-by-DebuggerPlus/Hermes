# 🧠 TASK: Fix YAML + Integrate Hermes Learning Memory System

You are a senior .NET/WPF architect.

Project already contains:

* ExternalBrainService (loads markdown, watcher, search, context)
* Obsidian vault with Memory folder

---

# 📁 ABSOLUTE PATHS (CRITICAL)

Vault:
C:\Users\busin\OneDrive\HermesBrain

Memory:
C:\Users\busin\OneDrive\HermesBrain\Memory

---

# 🚨 RULES

1. DO NOT create new folders outside this path
2. DO NOT break existing functionality
3. EXTEND current system
4. Keep performance (caching, async)

---

# 🎯 GOALS

1. Fix invalid YAML frontmatter in ALL markdown files
2. Add YAML parsing support
3. Upgrade ExternalBrainService
4. Implement MemoryExtractor (learning system)

---

# 🛠️ PART 1 — FIX YAML FRONTMATTER

Scan ALL `.md` files inside:

HermesBrain/

Find incorrect separators like:

---

or any non-standard delimiter

Replace with:

---

Ensure format:

---

type: procedural | semantic | episodic | identity
timestamp: ISO date
tags: [tag1, tag2]
project: string (optional)
importance: 1-5
---------------

Do NOT modify content body.

---

# 🧠 PART 2 — YAML PARSING

Update markdown parsing logic.

If YAML exists:

Parse fields:

* Type (string)
* Timestamp (DateTime)
* Tags (List<string>)
* Project (string)
* Importance (int)

If YAML missing:

* fallback to filename date
* fallback to file last write time

---

## Extend MemoryItem model:

Add:

* string Type
* string Project
* int Importance
* DateTime Timestamp (override existing logic)

---

# ⚙️ PART 3 — UPDATE LOADING LOGIC

In ExternalBrainService:

1. Load YAML first
2. Then content
3. Merge tags from:

   * YAML
   * #hashtags in text

---

# 🔍 PART 4 — IMPROVE SEARCH RANKING

Update BuildContext:

Ranking formula:

Score =

* relevance (text match)
* * importance * weight
* * recency boost

Sort descending

Limit results to 10–20

---

# 🧠 PART 5 — CREATE MEMORY EXTRACTOR

Create new service:

MemoryExtractorService

---

## Methods:

ExtractExperience(string task, string result)

ShouldSave(...)

GenerateMarkdown(...)

---

## Logic:

From task + result extract:

* Problem
* Solution
* Reusable knowledge

---

## Classification:

* procedural → if solution exists
* episodic → if just event
* semantic → if general knowledge

---

## Importance:

Assign 1–5 based on:

* complexity
* reusability
* uniqueness

---

# 📄 PART 6 — SAVE MEMORY

Save file to:

Memory/Procedures/
or
Memory/Projects/

Filename:

yyyy-MM-dd_HH-mm_type.md

---

# 🪟 PART 7 — WPF UI

Add:

Button:
"💾 Save Experience"

Create window:
MemoryEditorWindow

Fields:

* Type (dropdown)
* Tags
* Project
* Importance
* Content

---

# 🔄 PART 8 — INTEGRATION FLOW

After Hermes completes a task:

1. Call MemoryExtractor
2. Generate memory
3. Save to Obsidian
4. Watcher reloads memory
5. Available for next context

---

# 🧪 PART 9 — VALIDATION

After implementation:

1. Fix all YAML separators
2. Ensure parsing works
3. Ensure at least 1 memory loads correctly
4. Output parsed example
5. Confirm ranking works

---

# ⚠️ ERROR HANDLING

If no markdown files found:

OUTPUT:
USER ACTION REQUIRED: No memory files found

---

# 🚀 EXECUTE
