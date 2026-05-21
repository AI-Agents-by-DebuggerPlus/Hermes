namespace Hermes.TradingPlatform.Core.Domain;

public enum PositionSide
{
    Long,
    Short,
}

public enum OrderSide
{
    Buy,
    Sell,
}

public enum OrderType
{
    Market,
    Limit,
    Stop,
}

public enum OrderStatus
{
    Open,
    Filled,
    Cancelled,
    Rejected,
}

public enum StrategyRunStatus
{
    Idle,
    Running,
    Halted,
}

public enum HermesOrchestrationState
{
    Offline,
    Monitoring,
    Reviewing,
    Halted,
}

public enum RiskLevel
{
    Low,
    Medium,
    High,
    Critical,
}
