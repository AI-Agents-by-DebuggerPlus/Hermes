using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace DesktopVoiceChat.Models;

[Table("messages")]
public class Message : BaseModel
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
    public DateTimeOffset CreatedAt { get; set; }
}
