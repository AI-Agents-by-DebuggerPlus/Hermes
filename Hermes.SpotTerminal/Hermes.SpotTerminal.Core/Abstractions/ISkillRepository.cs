using Hermes.SpotTerminal.Core.Domain;

namespace Hermes.SpotTerminal.Core.Abstractions;

public interface ISkillRepository
{
    IReadOnlyList<Skill> LoadAll();
    void SaveAll(IReadOnlyList<Skill> skills);
    void Save(Skill skill);
}
