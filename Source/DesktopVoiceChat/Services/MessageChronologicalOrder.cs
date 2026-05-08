using DesktopVoiceChat.Models;

namespace DesktopVoiceChat.Services;

/// <summary>
/// Старые сообщения первыми (меньший индекс в списке), новее — ниже (больший индекс).
/// </summary>
public static class MessageChronologicalOrder
{
    /// <summary>Пустое время считаем «самым новым», чтобы сообщение realtime без даты попало вниз списка.</summary>
    public static long UtcTicksKey(DateTimeOffset createdAt)
    {
        if (createdAt == default)
        {
            return long.MaxValue;
        }

        return createdAt.UtcTicks;
    }

    /// <summary>Сравнение по возрастанию времени (старое меньше нуля).</summary>
    public static int Compare(Message a, Message b)
    {
        var c = UtcTicksKey(a.CreatedAt).CompareTo(UtcTicksKey(b.CreatedAt));
        if (c != 0)
        {
            return c;
        }

        return a.Id.CompareTo(b.Id);
    }

    /// <summary>Индекс вставки в отсортированный по возрастанию список.</summary>
    public static int InsertIndex(IReadOnlyList<Message> list, Message message)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (Compare(list[i], message) > 0)
            {
                return i;
            }
        }

        return list.Count;
    }
}
