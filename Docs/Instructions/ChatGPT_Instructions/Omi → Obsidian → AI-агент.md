# Вкладка 1

**Omi → Obsidian → AI-агент**

**[Hermes AI Agent + Obsidian + Omi is Insane (FREE\!)](https://www.youtube.com/watch?v=RytNZ_5JLQo)**

Вот краткое и понятное резюме 👇

---

## **📌 О чём видео**

Видео показывает, как сделать AI-агентов (например Hermes или OpenClaw) **намного “умнее” за счёт памяти**, используя связку:

👉 **Omi → Obsidian → AI-агент**

Ключевая идея:

* не обучать AI вручную  
* а дать ему **постоянный доступ к вашим реальным данным и истории**

### **💡 Как это работает:**

1. **Omi** записывает всё, что вы делаете:  
   * разговоры (микрофон)  
   * действия на экране  
   * активность в течение дня  
2. Omi автоматически:  
   * превращает это в **структурированные “воспоминания”**  
   * группирует их (задачи, идеи, привычки и т.д.)  
3. Эти данные экспортируются в **Obsidian**:  
   * создаётся база знаний (vault)  
   * все заметки обновляются ежедневно  
4. AI (Hermes, OpenClaw и др.):  
   * подключается к этой папке  
   * читает ваши “воспоминания”  
   * использует их как **контекст**

👉 В итоге AI:

* знает ваши привычки  
* понимает задачи  
* помнит прошлые действия  
* даёт более точные советы

---

## **🧠 Что такое Omi**

**Omi** — это инструмент для создания **автоматической “памяти” о вашей жизни и работе**.

Проще говоря:

Omi \= система, которая превращает вашу повседневную активность в данные для AI

### **🔧 Основные функции:**

* 🎤 Запись разговоров  
* 🖥️ Захват экрана  
* 🧠 Автоматическое создание “воспоминаний”  
* 📊 Организация в структуру (как mind map)  
* 📁 Экспорт в Obsidian / Notion  
* 🔗 Интеграции (календарь, файлы, email)

---

## **🚀 Главная идея видео**

Вместо:

“обучать AI”

Ты:

“даёшь ему доступ к своей жизни”

И получаешь:

* персонального ассистента с памятью  
* без сложных RAG-систем  
* без обучения моделей

---

## **⚠️ Важный момент**

Это мощно, но есть нюанс:

* система **записывает почти всё**, включая экран и голос  
* значит, есть вопросы приватности и безопасности

---

## **🧩 Итог**

Связка:

**Omi \+ Obsidian \= внешний мозг**  
**AI \+ этот мозг \= персональный агент с памятью**

---

Если хочешь, могу:

* разобрать, как тебе собрать такую систему локально (как в твоих проектах с Ollama / OpenClaw)  
* или упростить это до стабильной архитектуры под твои задачи 👌

# Вкладка 2

Я создал для тебя полноценный `.md` файл в canvas — там уже есть:

* архитектура внешнего мозга  
* чёткое разделение: **что делать вручную / что делает Cursor**  
* структура проекта  
* сервисы, модели, UI  
* и отдельное окно **ExternalBrainWindow** (как ты и просил)

Если хочешь дальше — могу:

* сгенерировать **готовый prompt для Cursor**, который реализует всё это автоматически  
* или сразу накидать **production-ready WPF код (MVVM \+ UI \+ сервисы)**

# Вкладка 3

Вот готовый **one-shot prompt для Cursor**, который максимально автоматизирует всё 👇  
Просто вставь его в Cursor (в Chat/Agent режим) и запусти.

---

# **🧠 TASK: Implement External Brain System for Hermes (WPF)**

You are a senior .NET/WPF architect.

Your goal is to fully implement an **External Brain system** for a Hermes AI client using:

* Local file-based memory (Obsidian vault)  
* Markdown parsing  
* Real-time updates  
* Dedicated WPF UI window

---

# **🎯 OBJECTIVE**

Build a complete module that allows Hermes to:

1. Read user memory from Markdown files  
2. Search and filter memory  
3. Build context for AI prompts  
4. Visualize and manage memory via a dedicated UI

---

# **⚙️ REQUIREMENTS**

## **1\. Core Service Layer**

Create:

### **`ExternalBrainService`**

Responsibilities:

* Load all markdown files from a directory  
* Parse them into structured objects  
* Provide search & filtering  
* Build AI context

Methods:

Task\<List\<MemoryItem\>\> GetAllMemoriesAsync();  
Task\<List\<MemoryItem\>\> SearchAsync(string query);  
Task\<List\<MemoryItem\>\> GetRecentAsync(TimeSpan timeSpan);  
Task\<List\<MemoryItem\>\> GetByTagAsync(string tag);  
string BuildContext(string userQuery, int maxItems \= 10);

---

## **2\. Data Model**

class MemoryItem  
{  
    DateTime Timestamp;  
    string Content;  
    List\<string\> Tags;  
    string SourceFile;  
}

---

## **3\. Markdown Parsing**

Requirements:

* Extract timestamp (from filename or content)  
* Extract tags (\#tag)  
* Clean content

---

## **4\. File Monitoring**

Implement:

FileSystemWatcher

Behavior:

* Watch memory folder  
* Auto-reload on changes  
* Notify UI

---

## **5\. WPF UI (MANDATORY)**

Create a new window:

### **`ExternalBrainWindow.xaml`**

Features:

### **🔍 Search Panel**

* TextBox with debounce  
* Real-time filtering

### **📜 Memory List**

* ListView  
* Sorted by Timestamp DESC

### **📄 Viewer Panel**

* Display selected memory  
* Render markdown (basic formatting is enough)

### **🏷️ Filters**

* By tag  
* By time (Today / Week / All)

### **📊 Visualization (simple)**

* Timeline OR grouped list by date

---

## **6\. MVVM**

Create:

* `ExternalBrainViewModel`  
* Commands:  
  * SearchCommand  
  * RefreshCommand  
  * FilterCommand

Use proper binding.

---

## **7\. Integration with Hermes**

Create method:

string BuildContext(string query)

Logic:

* Find relevant memories  
* Prioritize recent  
* Return formatted context string

---

## **8\. Configuration**

Create config:

{  
  "MemoryPath": "C:\\\\HermesBrain\\\\Memory"  
}

Allow easy override.

---

## **9\. Performance**

* Cache memory in RAM  
* Avoid reloading all files every time  
* Use async/await  
* Debounce search input

---

## **10\. Code Quality**

* Clean architecture  
* Separation of concerns  
* No hardcoding paths  
* Logging where useful

---

# **🚨 IMPORTANT: USER ACTIONS REQUIRED**

If any of the following is missing, STOP and clearly instruct the user:

1. Memory folder path (Obsidian vault)  
2. Markdown files structure  
3. Permissions to access folder

When stopping, output:

USER ACTION REQUIRED:  
\<clear step-by-step instructions\>

---

# **🧠 OPTIONAL (IF TIME)**

* Add simple tagging system UI  
* Add pin/favorite memory  
* Add basic scoring for relevance

---

# **📦 FINAL OUTPUT**

You must:

1. Generate ALL required files  
2. Create WPF window  
3. Wire everything together  
4. Ensure it builds and runs

Do NOT leave placeholders.

---

# **🚀 START IMPLEMENTATION**

---

## **💡 Как использовать**

1. Открой проект в Cursor  
2. Вставь этот prompt  
3. Запусти Agent / Chat  
4. Дай ему выполнить всё

---

## **⚠️ Что он у тебя спросит (нормально)**

Скорее всего:

* путь к Obsidian vault  
* структура markdown

👉 Просто ответь один раз — дальше он всё доделает сам.

---

Если хочешь следующий шаг — могу:

* усилить это до **RAG с embeddings (Ollama)**  
* или сделать **multi-agent shared memory архитектуру**

# CursorPrompt

# **🧠 TASK: Implement External Brain System for Hermes (WPF)**

You are a senior .NET/WPF architect.

Your goal is to fully implement an **External Brain system** for a Hermes AI client using:

* Local file-based memory (Obsidian vault)  
* Markdown parsing  
* Real-time updates  
* Dedicated WPF UI window

---

# **🎯 OBJECTIVE**

Build a complete module that allows Hermes to:

1. Read user memory from Markdown files  
2. Search and filter memory  
3. Build context for AI prompts  
4. Visualize and manage memory via a dedicated UI

---

# **⚙️ REQUIREMENTS**

## **1\. Core Service Layer**

Create:

### **`ExternalBrainService`**

Responsibilities:

* Load all markdown files from a directory  
* Parse them into structured objects  
* Provide search & filtering  
* Build AI context

Methods:

Task\<List\<MemoryItem\>\> GetAllMemoriesAsync();  
Task\<List\<MemoryItem\>\> SearchAsync(string query);  
Task\<List\<MemoryItem\>\> GetRecentAsync(TimeSpan timeSpan);  
Task\<List\<MemoryItem\>\> GetByTagAsync(string tag);  
string BuildContext(string userQuery, int maxItems \= 10);

---

## **2\. Data Model**

class MemoryItem  
{  
    DateTime Timestamp;  
    string Content;  
    List\<string\> Tags;  
    string SourceFile;  
}

---

## **3\. Markdown Parsing**

Requirements:

* Extract timestamp (from filename or content)  
* Extract tags (\#tag)  
* Clean content

---

## **4\. File Monitoring**

Implement:

FileSystemWatcher

Behavior:

* Watch memory folder  
* Auto-reload on changes  
* Notify UI

---

## **5\. WPF UI (MANDATORY)**

Create a new window:

### **`ExternalBrainWindow.xaml`**

Features:

### **🔍 Search Panel**

* TextBox with debounce  
* Real-time filtering

### **📜 Memory List**

* ListView  
* Sorted by Timestamp DESC

### **📄 Viewer Panel**

* Display selected memory  
* Render markdown (basic formatting is enough)

### **🏷️ Filters**

* By tag  
* By time (Today / Week / All)

### **📊 Visualization (simple)**

* Timeline OR grouped list by date

---

## **6\. MVVM**

Create:

* `ExternalBrainViewModel`  
* Commands:  
  * SearchCommand  
  * RefreshCommand  
  * FilterCommand

Use proper binding.

---

## **7\. Integration with Hermes**

Create method:

string BuildContext(string query)

Logic:

* Find relevant memories  
* Prioritize recent  
* Return formatted context string

---

## **8\. Configuration**

Create config:

{  
  "MemoryPath": "C:\\\\HermesBrain\\\\Memory"  
}

Allow easy override.

---

## **9\. Performance**

* Cache memory in RAM  
* Avoid reloading all files every time  
* Use async/await  
* Debounce search input

---

## **10\. Code Quality**

* Clean architecture  
* Separation of concerns  
* No hardcoding paths  
* Logging where useful

---

# **🚨 IMPORTANT: USER ACTIONS REQUIRED**

If any of the following is missing, STOP and clearly instruct the user:

1. Memory folder path (Obsidian vault)  
2. Markdown files structure  
3. Permissions to access folder

When stopping, output:

USER ACTION REQUIRED:  
\<clear step-by-step instructions\>

---

# **🧠 OPTIONAL (IF TIME)**

* Add simple tagging system UI  
* Add pin/favorite memory  
* Add basic scoring for relevance

---

# **📦 FINAL OUTPUT**

You must:

1. Generate ALL required files  
2. Create WPF window  
3. Wire everything together  
4. Ensure it builds and runs

Do NOT leave placeholders.

---

# **🚀 START IMPLEMENTATION**

