using Hermes.SpotTerminal.Core.Domain;

namespace Hermes.SpotTerminal.Core.Abstractions;

public interface ILearningJournalStore
{
    string Location { get; }
    void Append(LearningJournalEntry entry);
    IReadOnlyList<LearningJournalEntry> LoadAll();
}
