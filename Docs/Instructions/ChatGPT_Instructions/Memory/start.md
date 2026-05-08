# 🧠 TASK: Upgrade Hermes External Brain to Learning System

You are a senior .NET/WPF architect.

The project already has:

* ExternalBrainService (loads markdown, watcher, search, BuildContext)
* WPF UI for browsing memory

Your task is to UPGRADE the system into a **learning memory system**.

---

# 🎯 GOAL

Make Hermes:

* learn from completed tasks
* store structured experience
* reuse knowledge in future prompts

---

# 📁 MEMORY LOCATION

Path:
C:\Users<USER>\Google Drive\HermesBrain\Memory

---

# 🧩 PART 1 — YAML FRONTMATTER SUPPORT

Update markdown parsing:

Support:

---

type: procedural | episodic | semantic | identity
timestamp: ISO datetime
tags: [tag1, tag2]
project: string
importance: 1-5
---------------

Rules:

1. Parse YAML first
2. Fallback to filename timestamp
3. Fallback to file last write time
4. Tags:

   * first from YAML
   * then from #tags in content

Extend MemoryItem:

* Type
* Project
* Importance

---

# 🧠 PART 2 — MEMORY EXTRACTOR

Create new service:

MemoryExtractorService

Methods:

ExtractExperience(string task, string result)
ShouldSave(...)
GenerateMemoryMarkdown(...)

Logic:

1. Analyze task + result
2. Extract:

   * problem
   * solution
   * reusable knowledge
3. Classify type (procedural / episodic / semantic)
4. Assign importance (1–5)

---

# 📄 PART 3 — SAVE MEMORY

Write .md file into:

Memory/Procedures/
Memory/Projects/

Filename:
yyyy-MM-dd_HH-mm_<type>.md

---

# 🪟 PART 4 — WPF UI

Add:

## Button:

"💾 Save Experience"

## New Window:

MemoryEditorWindow

Fields:

* Type (dropdown)
* Tags
* Project
* Importance
* Content

---

# 🔍 PART 5 — CONTEXT IMPROVEMENT

Update BuildContext:

Ranking priority:

1. relevance score
2. importance
3. recency

Limit:
max 10–20 items

---

# ⚙️ PART 6 — PERFORMANCE

* Keep caching
* Do not break watcher
* Async operations

---

# 🚨 USER ACTION REQUIRED

If path does not exist or no markdown files:

Output:

USER ACTION REQUIRED:

1. Provide valid MemoryPath
2. Ensure markdown files exist

---

# 📦 FINAL

* Do NOT rewrite existing system
* Extend it
* Ensure build success
* Clean architecture

---

# 🚀 START IMPLEMENTATION
