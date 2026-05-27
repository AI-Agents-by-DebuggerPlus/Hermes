using Hermes.Wpf.Services;
using Xunit;

namespace Hermes.Wpf.Tests;

public sealed class BiohackerIntentParserTests
{
    private readonly BiohackerIntentParser _parser = new();

    [Fact]
    public void TryParseAll_returns_empty_for_empty_input()
    {
        var (intents, clean) = _parser.TryParseAll(string.Empty);

        Assert.Empty(intents);
        Assert.Equal(string.Empty, clean);
    }

    [Fact]
    public void TryParseAll_extracts_single_log_supplement_block_and_removes_it()
    {
        const string response =
            "Зафиксировал утренний приём.\n" +
            "{\"bio\":\"log_supplement\",\"name\":\"Альфа-GPC\",\"dose_mg\":300,\"timing\":\"morning\",\"date\":\"2026-05-25\"}\n";

        var (intents, clean) = _parser.TryParseAll(response);

        var intent = Assert.Single(intents);
        var supplement = Assert.IsType<LogSupplementIntent>(intent);
        Assert.Equal("Альфа-GPC", supplement.Name);
        Assert.Equal(300, supplement.DoseMg);
        Assert.Equal("morning", supplement.Timing);
        Assert.DoesNotContain("\"bio\"", clean);
        Assert.Contains("Зафиксировал утренний приём", clean);
    }

    [Fact]
    public void TryParseAll_handles_multiple_blocks_in_one_response()
    {
        const string response = """
        Записал.

        {"bio":"log_supplement","name":"Магний","dose_mg":400,"timing":"before_sleep","date":"2026-05-25"}

        {"bio":"log_metrics","date":"2026-05-25","sleep_quality":7,"energy_morning":6,"focus_day":8,"mood":7,"productivity":8,"stress":3,"notes":"хорошо"}

        Готово.
        """;

        var (intents, clean) = _parser.TryParseAll(response);

        Assert.Equal(2, intents.Count);
        Assert.IsType<LogSupplementIntent>(intents[0]);
        var metrics = Assert.IsType<LogMetricsIntent>(intents[1]);
        Assert.Equal(7, metrics.SleepQuality);
        Assert.Equal(3, metrics.Stress);
        Assert.DoesNotContain("\"bio\"", clean);
        Assert.Contains("Записал", clean);
        Assert.Contains("Готово", clean);
    }

    [Fact]
    public void TryParseAll_tolerates_surrounding_noise_text()
    {
        const string response = """
        Краткий ответ. Дальше JSON, его прятать. {"bio":"update_stock","name":"Альфа-GPC","doses_used":2} Конец фразы.
        """;

        var (intents, clean) = _parser.TryParseAll(response);

        var intent = Assert.Single(intents);
        var stock = Assert.IsType<UpdateStockIntent>(intent);
        Assert.Equal("Альфа-GPC", stock.Name);
        Assert.Equal(2, stock.DosesUsed);
        Assert.DoesNotContain("\"bio\"", clean);
        Assert.Contains("Краткий ответ", clean);
        Assert.Contains("Конец фразы", clean);
    }

    [Fact]
    public void TryParseAll_ignores_malformed_json_without_throwing()
    {
        const string response = "Поломанный блок: {\"bio\":\"log_supplement\",\"dose_mg\": } и текст.";

        var (intents, clean) = _parser.TryParseAll(response);

        Assert.Empty(intents);
        Assert.Contains("Поломанный блок", clean);
    }

    [Fact]
    public void TryParseAll_returns_empty_when_no_bio_present()
    {
        const string response = "Просто текст без JSON-блоков.";

        var (intents, clean) = _parser.TryParseAll(response);

        Assert.Empty(intents);
        Assert.Equal(response, clean);
    }

    [Fact]
    public void TryParseAll_extracts_update_supplement_card()
    {
        const string response = """
        Сохраняю карточку.
        {"bio":"update_supplement","name":"Магний глицинат","dose_mg":400,"timing":"before_sleep",
         "status":"active","stock_units":60,"reorder_threshold":14,
         "observed_effects":["улучшение сна","снижение тревожности"],
         "stack_compatibility":"совместим с L-теанин"}
        """;

        var (intents, clean) = _parser.TryParseAll(response);

        var intent = Assert.Single(intents);
        var upd = Assert.IsType<UpdateSupplementIntent>(intent);
        Assert.Equal("Магний глицинат", upd.Card.Name);
        Assert.Equal(400, upd.Card.DoseMg);
        Assert.Equal(60, upd.Card.StockUnits);
        Assert.Equal(14, upd.Card.ReorderThreshold);
        Assert.Contains("улучшение сна", upd.Card.ObservedEffects);
        Assert.DoesNotContain("\"bio\"", clean);
    }

    [Fact]
    public void TryParseAll_extracts_optimize_schedule()
    {
        const string response =
            "{\"bio\":\"optimize_schedule\",\"schedule_type\":\"workday\"," +
            "\"reason\":\"deep work приоритет\"," +
            "\"changes\":[{\"time_from\":\"08:15\",\"time_to\":\"07:00\",\"block\":\"Deep work блок 1\"}]}";

        var (intents, _) = _parser.TryParseAll(response);

        var intent = Assert.Single(intents);
        var opt = Assert.IsType<OptimizeScheduleIntent>(intent);
        Assert.Equal("workday", opt.ScheduleType);
        Assert.Single(opt.Changes);
        Assert.Equal("08:15", opt.Changes[0].TimeFrom);
        Assert.Equal("07:00", opt.Changes[0].TimeTo);
    }
}
