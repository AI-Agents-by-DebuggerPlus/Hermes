# 06. Реализация learning loop (2026-05-28)

## Новые классы

| Файл | Назначение |
|------|------------|
| `Models/LocalAutomationKind.cs` | Тип локальной автоматизации |
| `Models/LocalExecutionRecord.cs` | Контекст post-hook |
| `Models/AgentRole.cs` | + `UtilitiesManager` |
| `Services/ExternalBrainWriteService.cs` | Запись `.md` и скриншотов в vault |
| `Services/LocalExecutionLearningService.cs` | Post-local hook (memory + role + WSL + crystallize) |
| `Services/ReniWaterExecutionCoordinator.cs` | Единый path submit + learning |
| `Services/ReniWaterExperienceBuilder.cs` | Structured MemoryDraft для Reni |
| `Services/ReniWaterSkillCrystallizer.cs` | Auto `builtin_reni_water` skill |
| `Services/BuiltInSkillsPromptInstructions.cs` | Outbound prompt для LLM |
| `Services/ReniWaterStatusTriggers.cs` | Локальные Q&A «ты передавал?» |
| `Services/LocalCaptureOptions.cs` | Relaxed RoleCapture для local |

## Изменённые классы

| Файл | Изменение |
|------|-----------|
| `MemoryExtractorService` | `ExtractFromLocalExecution`, `ExtractAndSaveAsync` |
| `RoleExperienceCapture` | `CaptureIfNeededAsync` + local options |
| `SkillGenerationService` | `TrySaveBuiltInAutomationAsync`, `skipSandbox` |
| `HermesSettings` | `LocalLearningLoopEnabled`, `ReniWaterLearningSuccessCount`, … |
| `MainViewModel` | Coordinator, status handler, prompt block |
| `ReniWaterSubmitTriggers` | + «reni water», «введи показания» |
| `RoleManager`, `RoleAwareMemoryRouter`, `VaultInitializer` | UtilitiesManager |

## Поток после успешного submit

```
RunReniWaterSubmitUiAsync
  → ReniWaterExecutionCoordinator.RunSubmitAsync
      → ReniWaterScriptService (Playwright)
      → LocalExecutionLearningService.ProcessAsync
          → ExternalBrainWriteService (Procedures/Utilities/ReniWater + screenshot)
          → RoleExperienceCapture (UtilitiesManager)
          → ReniWaterLearningSuccessCount++
          → ReniWaterSkillCrystallizer (after N successes)
          → WslAgentMemorySyncService.TrySync
```

## Settings

| Key | Default |
|-----|---------|
| `LocalLearningLoopEnabled` | true |
| `ReniWaterAutoCrystallizeEnabled` | true |
| `ReniWaterAutoCrystallizeAfterSuccesses` | 2 |
| `ReniWaterLearningSuccessCount` | 0 (persisted) |

## Требования для полного loop

1. `ExternalBrainMemoryPath` — путь к vault (иначе vault write пропускается, лог `[local-learning]`)
2. `SkillGenerationEnabled` — для auto-crystallize
3. `RoleAutoCapture` — или force capture при успешном submit
