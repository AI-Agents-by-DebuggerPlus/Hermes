In the Hermes WPF application, the FlashcardSkill posts messages to Supabase 
where the `content` field contains Unicode escape sequences instead of 
readable Cyrillic text.

The required format is:
  {"type":"flashcard","en":"neural network","ru":"нейронная сеть"}

The actual content being written to Supabase is:
  {"type":"flashcard","en":"neural network","ru":"\u043D\u0435\u0439\u0440\u043E\u043D\u043D\u0430\u044F \u0441\u0435\u0442\u044C"}

### Fix
Find where the flashcard JSON string is assembled before calling 
InsertMessage() (search for: "flashcard", SerializeObject, JsonSerializer.Serialize).

If using System.Text.Json:

  // WRONG — escapes non-ASCII by default
  string content = JsonSerializer.Serialize(obj);

  // CORRECT
  var options = new JsonSerializerOptions
  {
      Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
  };
  string content = JsonSerializer.Serialize(obj, options);

If using Newtonsoft.Json:

  // WRONG
  string content = JsonConvert.SerializeObject(obj);

  // CORRECT
  string content = JsonConvert.SerializeObject(obj, new JsonSerializerSettings
  {
      StringEscapeHandling = StringEscapeHandling.Default
  });

Apply the fix only at the serialization call site inside FlashcardSkill. 
Do not change any other serialization in the project.

After the fix, verify in the Supabase Table Editor that the `content` column 
shows readable Cyrillic characters, not \uXXXX sequences.