using Hermes.SpotTerminal.Core.Abstractions;
using Hermes.SpotTerminal.Core.Domain;
using Hermes.SpotTerminal.Shared.Bridge;

namespace Hermes.SpotTerminal.Data.Persistence;

public sealed class SkillJsonRepository : ISkillRepository
{
    private readonly string _path;
    private readonly object _sync = new();

    public SkillJsonRepository()
    {
        SpotBridgePaths.EnsureRoot();
        _path = Path.Combine(SpotBridgePaths.DataRoot, "skills", "catalog.json");
    }

    public IReadOnlyList<Skill> LoadAll()
    {
        lock (_sync)
        {
            if (!AtomicJsonFileStore.TryLoad(_path, out SkillCatalogFile? file) || file?.Skills is null)
            {
                return [];
            }

            return file.Skills;
        }
    }

    public void Save(Skill skill)
    {
        var all = LoadAll().ToList();
        var idx = all.FindIndex(s => s.Id == skill.Id);
        if (idx >= 0)
        {
            all[idx] = skill;
        }
        else
        {
            all.Add(skill);
        }

        SaveAll(all);
    }

    public void SaveAll(IReadOnlyList<Skill> skills)
    {
        lock (_sync)
        {
            AtomicJsonFileStore.Save(_path, new SkillCatalogFile { Skills = skills.ToList() });
        }
    }

    private sealed class SkillCatalogFile
    {
        public List<Skill> Skills { get; set; } = [];
    }
}
