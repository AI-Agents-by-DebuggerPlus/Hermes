using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace Hermes.Wpf.Models;

/// <summary>Maps to Postgres table <c>messages</c> used by DesktopVoiceChat / Android client.</summary>
[Table("messages")]
public sealed class SupabaseMessageRow : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("sender_id")]
    public string SenderId { get; set; } = string.Empty;

    [Column("sender_name")]
    public string SenderName { get; set; } = string.Empty;

    [Column("content")]
    public string Content { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
