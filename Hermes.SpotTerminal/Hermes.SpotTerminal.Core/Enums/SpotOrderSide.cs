namespace Hermes.SpotTerminal.Core.Enums;

public enum SpotOrderSide
{
    Buy,
    Sell,
}

public enum SpotOrderType
{
    Market,
    Limit,
}

public enum SpotOrderStatus
{
    Open,
    Filled,
    Cancelled,
    Rejected,
}
