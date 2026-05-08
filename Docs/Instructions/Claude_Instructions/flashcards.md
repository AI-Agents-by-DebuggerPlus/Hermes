You are working on a WPF desktop application called "Hermes" — an AI-powered voice/text chat client 
that connects to a Supabase database (table: `messages`) and communicates with an AI backend.

## Context

The `messages` table schema:
  id          uuid        DEFAULT gen_random_uuid() PRIMARY KEY
  sender_id   uuid        NOT NULL REFERENCES auth.users(id)
  sender_name text        NOT NULL
  content     text        NOT NULL
  created_at  timestamptz DEFAULT now()

A WordPress plugin ("English Flashcards") reads the `messages` table and displays the most recent 
message where `content` is valid JSON matching:
  {"type":"flashcard","en":"<english word or phrase>","ru":"<russian translation>"}

All other message formats are ignored by the WordPress plugin.

## Task: Implement the "English Flashcards" skill in Hermes

### 1. Trigger detection

Hermes must detect when the user's message is a flashcard generation request.
Detection is handled by the existing AI backend — add a system prompt fragment that instructs 
the AI to recognize commands like:

  "генерируй карточки по теме X с интервалом Y минут, начни через Z минут"
  "stop", "закончить просмотр карточек", "стоп", "хватит"

When the AI detects a start command it must respond with a structured JSON tool call:

  {
    "skill": "flashcard_start",
    "topic": "<topic string>",
    "interval_minutes": <number>,
    "delay_minutes": <number>
  }

When the AI detects a stop command while the skill is active, it must respond with:

  {
    "skill": "flashcard_stop"
  }

### 2. FlashcardSkill class

Create `Skills/FlashcardSkill.cs`:

  public class FlashcardSkill : IDisposable
  {
      // Dependencies injected via constructor:
      //   - ISupabaseService  (existing service for inserting messages)
      //   - IAnthropicService (existing service for AI completions)
      //   - Action<FlashcardStatus> onStatusChanged

      public enum FlashcardStatus
      {
          Idle,
          WaitingToStart,   // delay timer running
          Generating,       // actively posting cards
          Stopped
      }

      // Public API
      public void Start(string topic, int intervalMinutes, int delayMinutes);
      public void Stop();
      public FlashcardStatus Status { get; private set; }
      public void Dispose();
  }

  Internal logic:
  
  Start():
    1. Set Status = WaitingToStart
    2. Fire onStatusChanged
    3. After delayMinutes — set Status = Generating, fire onStatusChanged
    4. Immediately generate and post the first card
    5. Start a System.Timers.Timer with interval = intervalMinutes * 60 * 1000
    6. On each tick: generate and post one card

  GenerateAndPostCard(topic):
    1. Call IAnthropicService with this exact system prompt:
       ---
       You are an English vocabulary teacher. Generate ONE flashcard for the topic: "{topic}".
       Respond ONLY with a JSON object, no markdown, no explanation:
       {"type":"flashcard","en":"<word or short phrase in English>","ru":"<translation in Russian>"}
       Rules:
       - "en" must be a single word or short phrase (max 5 words)
       - "ru" must be a natural Russian translation
       - Do not repeat cards already sent in this session
       - Vary difficulty: mix common and advanced vocabulary
       ---
    2. Parse the JSON from the AI response
    3. Validate: must have type=="flashcard", non-empty "en" and "ru"
    4. Call ISupabaseService.InsertMessage(content: <json string>)
       with sender_name = "Hermes" (or the configured bot name)
    5. If parsing fails — retry once with a stricter prompt

  Stop():
    1. Cancel timers
    2. Set Status = Stopped
    3. Fire onStatusChanged

### 3. UI status indicator

In the main chat window (MainWindow.xaml or ChatView.xaml), add a status bar element 
that is visible only when FlashcardSkill.Status != Idle:

  <!-- Flashcard skill status bar -->
  <Border x:Name="FlashcardStatusBar" Visibility="Collapsed"
          Background="#1a3a6b" CornerRadius="8" Padding="12,8" Margin="8,4">
      <StackPanel Orientation="Horizontal" Spacing="10">
          <Ellipse x:Name="FlashcardDot" Width="8" Height="8" Fill="#c9a84c">
              <!-- Pulse animation when Status == Generating -->
          </Ellipse>
          <TextBlock x:Name="FlashcardStatusText" 
                     Foreground="#b0c8ff" FontSize="12"
                     Text="Flashcards: ожидание запуска…" />
          <Button Content="✕" Click="StopFlashcards_Click"
                  Background="Transparent" Foreground="#b0c8ff"
                  BorderThickness="0" Cursor="Hand" />
      </StackPanel>
  </Border>

Status text logic:
  WaitingToStart → "🃏 Flashcards: запуск через {remaining} мин  •  тема: {topic}"
  Generating     → "🃏 Flashcards: активно  •  тема: {topic}  •  каждые {interval} мин"
  Stopped/Idle   → hide the bar

Pulse animation on the dot (WPF DoubleAnimation on Opacity, RepeatBehavior="Forever") 
must play only when Status == Generating, and stop when WaitingToStart.

### 4. Integration into MainViewModel (or code-behind)

- Instantiate FlashcardSkill when the app starts (singleton, lives for app lifetime)
- In the message processing pipeline, after the AI returns a tool call JSON:
    if skill == "flashcard_start" → call FlashcardSkill.Start(topic, interval, delay)
    if skill == "flashcard_stop"  → call FlashcardSkill.Stop()
- Subscribe to onStatusChanged → update UI on Dispatcher thread
- On app shutdown → call FlashcardSkill.Dispose()

### 5. Session deduplication (optional but recommended)

FlashcardSkill should keep a HashSet<string> of "en" values posted in the current session.
Pass the list as context to the AI prompt:
  "Already sent in this session: {string.Join(", ", sentWords)}"
This prevents the AI from repeating the same word within one session.

### 6. Error handling

- If Supabase insert fails: log the error, skip this card, try again on next tick
- If AI returns invalid JSON twice in a row: log warning, skip tick
- Never crash the timer — all exceptions inside the tick handler must be caught

### 7. Files to create / modify

  CREATE  Skills/FlashcardSkill.cs
  CREATE  Skills/IFlashcardSkill.cs          (interface, for testability)
  MODIFY  ViewModels/MainViewModel.cs         (instantiate, wire tool call handling)
  MODIFY  Views/MainWindow.xaml               (add status bar)
  MODIFY  Views/MainWindow.xaml.cs            (StopFlashcards_Click handler)
  MODIFY  Services/IAnthropicService.cs       (add overload with custom system prompt if needed)

### 8. Example end-to-end flow

  User:   "генерируй карточки по теме ИИ-агенты с интервалом 10 минут, начни через 30 минут"
  
  AI:     {"skill":"flashcard_start","topic":"AI agents","interval_minutes":10,"delay_minutes":30}
  
  App:    FlashcardSkill.Start("AI agents", 10, 30)
  UI:     "🃏 Flashcards: запуск через 30 мин  •  тема: AI agents"
  
  [30 min later]
  App:    GenerateAndPostCard("AI agents")
  AI:     {"type":"flashcard","en":"autonomous agent","ru":"автономный агент"}
  App:    Supabase INSERT content = '{"type":"flashcard","en":"autonomous agent","ru":"автономный агент"}'
  UI:     "🃏 Flashcards: активно  •  тема: AI agents  •  каждые 10 мин"
  WP:     WordPress plugin picks up the new message and displays the card
  
  [10 min later — next card posted automatically]
  
  User:   "закончить просмотр карточек"
  AI:     {"skill":"flashcard_stop"}
  App:    FlashcardSkill.Stop()
  UI:     status bar hidden