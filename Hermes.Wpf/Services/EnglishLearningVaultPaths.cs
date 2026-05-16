using System.IO;

namespace Hermes.Wpf.Services;

/// <summary>
/// Canonical subfolders under the External Brain (Obsidian) root for English study and exported/generated flashcards.
/// </summary>
public static class EnglishLearningVaultPaths
{
    /// <summary>Knowledge / English — общая зона заметок по английскому.</summary>
    public static string RelativeEnglishKnowledgeRoot =>
        Path.Combine("Knowledge", "English");

    /// <summary>Сгенерированные карточки (Hermes Flashcards skill, экспорт и т.п.).</summary>
    public static string RelativeGeneratedFlashcards =>
        Path.Combine("Knowledge", "English", "GeneratedFlashcards");

    public static string ResolveEnglishKnowledgeRoot(string memoryVaultRoot) =>
        Path.Combine(memoryVaultRoot, RelativeEnglishKnowledgeRoot);

    public static string ResolveGeneratedFlashcardsDirectory(string memoryVaultRoot) =>
        Path.Combine(memoryVaultRoot, RelativeGeneratedFlashcards);

    /// <summary>Creates <see cref="RelativeEnglishKnowledgeRoot"/> and nested <see cref="RelativeGeneratedFlashcards"/>.</summary>
    public static void EnsureLayout(string memoryVaultRoot)
    {
        var root = (memoryVaultRoot ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            return;
        }

        Directory.CreateDirectory(ResolveEnglishKnowledgeRoot(root));
        Directory.CreateDirectory(ResolveGeneratedFlashcardsDirectory(root));
    }
}
