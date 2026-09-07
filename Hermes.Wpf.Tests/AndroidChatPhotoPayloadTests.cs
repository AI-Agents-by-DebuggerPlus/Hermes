using Hermes.Wpf.Services;
using Xunit;

namespace Hermes.Wpf.Tests;

public sealed class AndroidChatPhotoPayloadTests
{
    private const string PhotoJson = """
        {
          "type": "file",
          "kind": "photo",
          "name": "photo_20260817_201530.jpg",
          "bucket": "chat-files",
          "path": "uid/folder/photo_20260817_201530.jpg",
          "mime": "image/jpeg",
          "size": 184320,
          "nonce": "7bef9d6f97e345a89ac0e156eb9c5bac"
        }
        """;

    [Fact]
    public void Parses_KindPhoto()
    {
        Assert.True(AndroidChatPhotoPayload.TryParse(PhotoJson, "AndroidChat", out var p));
        Assert.Equal("photo_20260817_201530.jpg", p.Name);
        Assert.Equal("chat-files", p.Bucket);
        Assert.Equal("uid/folder/photo_20260817_201530.jpg", p.Path);
        Assert.Equal("image/jpeg", p.Mime);
        Assert.Equal("7bef9d6f97e345a89ac0e156eb9c5bac", p.Nonce);
        Assert.Equal(184320, p.Size);
    }

    [Fact]
    public void Parses_AndroidChatImageMime_WithoutKind()
    {
        const string json = """{"type":"file","name":"cam.jpg","bucket":"chat-files","path":"a/b.jpg","mime":"image/jpeg"}""";
        Assert.True(AndroidChatPhotoPayload.TryParse(json, "AndroidChat", out var p));
        Assert.Equal("cam.jpg", p.Name);
    }

    [Fact]
    public void Rejects_XmlFileWithoutPhotoKind()
    {
        const string json = """{"type":"file","name":"backup.xml","bucket":"chat-files","path":"a/backup.xml","mime":"application/xml"}""";
        Assert.False(AndroidChatPhotoPayload.TryParse(json, "AndroidChat", out _));
        Assert.False(AndroidChatPhotoPayload.TryParse(json, "Tasker", out _));
    }

    [Fact]
    public void Rejects_HwtScreenshot()
    {
        const string json = """{"type":"hwt_screenshot","name":"shot.png","bucket":"chat-files","path":"a/shot.png","mime":"image/png"}""";
        Assert.False(AndroidChatPhotoPayload.TryParse(json, "Hermes", out _));
    }

    [Fact]
    public void Rejects_PlainTextAndVoice()
    {
        Assert.False(AndroidChatPhotoPayload.TryParse("hello from phone", "AndroidChat", out _));
        Assert.False(AndroidChatPhotoPayload.TryParse("[Voice]{\"ru\":\"hi\"}[/Voice]", "AndroidChat", out _));
    }

    [Fact]
    public void EncodeStoragePath_EncodesSegmentsOnly()
    {
        var encoded = AndroidChatPhotoPayload.EncodeStorageObjectPath("uid/folder uuid/photo.jpg");
        Assert.Equal("uid/folder%20uuid/photo.jpg", encoded);
    }
}
