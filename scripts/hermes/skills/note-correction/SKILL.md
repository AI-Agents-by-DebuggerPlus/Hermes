---
name: note-correction
description: >
  When the user corrects a factual error (wrong bank, amount, role), optionally emit
  note_correction JSON so Hermes.Wpf appends hermes/corrections.jsonl. Does not create skills.
version: 1.0.0
metadata:
  hermes:
    tags: [correction, learning, accountant, harness]
---

# note_correction

After acknowledging a user correction, you may emit:

```json
{"skill":"note_correction","what":"bank_name","actual":"TD","expected":"RBC Chequing","task_type":"extract_screenshot"}
```

Fields: `what`, `actual` (wrong), `expected` (right), optional `task_type`.

WPF writes `hermes/corrections.jsonl`. Do not invent corrections. Do not call memory for this.
