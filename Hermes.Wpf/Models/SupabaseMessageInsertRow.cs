using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace Hermes.Wpf.Models;

/// <summary>
/// Insert-only row for <c>messages</c>.
/// Omits <c>created_at</c> so Postgres can apply <c>DEFAULT now()</c>.
/// </summary>
[Table("messages")]
public sealed class SupabaseMessageInsertRow : BaseModel
{
    [Column("sender_id")]
    public string SenderId { get; set; } = string.Empty;

    [Column("sender_name")]
    public string SenderName { get; set; } = string.Empty;

    [Column("content")]
    public string Content { get; set; } = string.Empty;

    // Table schema in this project uses NOT NULL without DEFAULT; client must provide created_at.
    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}

