using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hermes.EnglishTutorClient.Models;
using Newtonsoft.Json;

namespace Hermes.EnglishTutorClient.Services;

/// <summary>Local demo session — stands in for Hermes until Supabase wire-up.</summary>
public sealed class DemoSessionRunner
{
    private TutorSessionDocument _doc = new();
    private int _index = -1;
    private readonly HashSet<string> _passed = new(StringComparer.OrdinalIgnoreCase);

    public string Title => _doc.Title;
    public int Index => _index;
    public int Count => _doc.Exercises.Count;
    public bool HasSession => _doc.Exercises.Count > 0;
    public TutorExercise? Current =>
        _index >= 0 && _index < _doc.Exercises.Count ? _doc.Exercises[_index] : null;

    public IReadOnlyCollection<string> PassedIds => _passed;

    public void LoadDefaultSample()
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SampleExercises", "level_test_demo.json");
        if (File.Exists(path))
        {
            LoadFromFile(path);
            return;
        }

        _doc = BuildBuiltIn();
        _index = -1;
        _passed.Clear();
    }

    public void LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        _doc = JsonConvert.DeserializeObject<TutorSessionDocument>(json) ?? BuildBuiltIn();
        if (_doc.Exercises == null || _doc.Exercises.Count == 0)
            _doc = BuildBuiltIn();
        _index = -1;
        _passed.Clear();
    }

    public TutorExercise Start()
    {
        if (_doc.Exercises.Count == 0)
            _doc = BuildBuiltIn();
        _index = 0;
        return _doc.Exercises[0];
    }

    public TutorExercise? Next()
    {
        if (_index + 1 >= _doc.Exercises.Count)
            return null;
        _index++;
        return _doc.Exercises[_index];
    }

    public TutorFeedback Check(string userAnswer)
    {
        var ex = Current;
        if (ex == null)
            return new TutorFeedback { IsCorrect = false, Message = "Нет активного упражнения." };

        var expected = (ex.ExpectedAnswer ?? string.Empty).Trim();
        var actual = (userAnswer ?? string.Empty).Trim();
        if (expected.Length == 0)
        {
            _passed.Add(ex.Id);
            return new TutorFeedback
            {
                IsCorrect = true,
                Message = "В демо нет эталона — засчитываю. Hermes позже проверит ответ сам."
            };
        }

        var ok = string.Equals(Normalize(actual), Normalize(expected), StringComparison.OrdinalIgnoreCase);
        if (ok)
        {
            _passed.Add(ex.Id);
            return new TutorFeedback
            {
                IsCorrect = true,
                Message = "Верно. Прогресс сохранён (локально). Можно перейти к следующему упражнению."
            };
        }

        return new TutorFeedback
        {
            IsCorrect = false,
            Message = "Есть ошибка. Ожидалось: «" + expected + "». Попробуйте ещё раз."
        };
    }

    private static string Normalize(string s) =>
        string.Join(" ", s.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));

    private static TutorSessionDocument BuildBuiltIn() => new()
    {
        Title = "Первичный тест уровня (demo)",
        Exercises = new List<TutorExercise>
        {
            new()
            {
                Id = "q1",
                Question = "Прочитайте слова вслух (или напишите перевод на русский):",
                Words = new List<string> { "apple", "water", "friend" },
                ExpectedAnswer = "яблоко вода друг",
                QuestionLang = "ru",
                WordsLang = "en"
            },
            new()
            {
                Id = "q2",
                Question = "Переведите на английский:",
                Words = new List<string> { "доброе утро" },
                ExpectedAnswer = "good morning",
                QuestionLang = "ru",
                WordsLang = "ru"
            },
            new()
            {
                Id = "q3",
                Question = "Выберите правильную форму: I ___ to school every day.",
                Words = new List<string> { "go", "goes", "going" },
                ExpectedAnswer = "go",
                QuestionLang = "en",
                WordsLang = "en"
            }
        }
    };
}
