//+------------------------------------------------------------------+
//|                                HermesWpfGuiControllerTest.mq5    |
//| MQL5 <=> HermesWpfGuiController.dll <=> HermesWpfTerminal        |
//+------------------------------------------------------------------+
#property strict

enum ENUM_GUI_EVENT
  {
   GUI_EXCEPTION       = 0,
   GUI_CLICK           = 1,
   GUI_TEXT_CHANGE     = 2,
   GUI_CHECKBOX_CHANGE = 3,
   GUI_COMBOBOX_CHANGE = 4,
   GUI_SLIDER_CHANGE   = 5,
   GUI_ELEMENT_ENABLE  = 6,
   GUI_ELEMENT_HIDE    = 7,
   GUI_SELECTION_CHANGE= 8
  };

// Новое имя input + новая DLL (HermesWpfTerminalUi) — обход кэша Assembly.LoadFrom в MT5.
input string InpWpfUi3       = "D:/Programming/AI_Agents/Hermes/Hermes.MT5/WpfGuiControllerTest/WpfTestApp/bin/Release/ui_v3/HermesWpfTerminalUi3.dll";
input string InpWpfWindow    = "HermesWpfTerminal";
input int    InpTimerMs      = 200;
input double InpDefaultLot   = 0.10;

double g_lot          = 0.0;
bool   g_autotrade     = false;
bool   g_real_trade    = false;
bool   g_window_ready  = false;
string g_order_type    = "Market";
string g_fill          = "Fill or Kill";
string g_comment       = "";
double g_volume        = 0.10;
double g_price         = 0.0;
double g_sl            = 0.0;
double g_tp            = 0.0;

#import "HermesWpfGuiController.dll"
#import

//+------------------------------------------------------------------+
int OnInit()
  {
   g_lot = InpDefaultLot;
   g_volume = InpDefaultLot;

   ResetLastError();
   bool ok = GuiController::ShowWindow(InpWpfUi3, InpWpfWindow);
   if(!ok || GetLastError() != 0)
     {
      Print("ShowWindow failed, error=", GetLastError());
      return(INIT_FAILED);
     }

   g_window_ready = true;
   EventSetMillisecondTimer(InpTimerMs);
   PushGuiState();
   EchoToGui("panel started / " + InpWpfWindow);
   Print("WPF panel started: ", InpWpfUi3, " / ", InpWpfWindow);
   return(INIT_SUCCEEDED);
  }

//+------------------------------------------------------------------+
void OnDeinit(const int reason)
  {
   EventKillTimer();
   if(g_window_ready)
      GuiController::HideWindow(InpWpfUi3, InpWpfWindow);
  }

//+------------------------------------------------------------------+
void OnTimer()
  {
   if(!g_window_ready)
      return;

   PushGuiState();
   DrainGuiEvents();
  }

//+------------------------------------------------------------------+
void OnTick()
  {
   PushGuiState();
  }

//+------------------------------------------------------------------+
void DrainGuiEvents()
  {
   int total = GuiController::EventsTotal(InpWpfUi3, InpWpfWindow);
   if(total <= 0)
      return;

   for(int i = 0; i < total; i++)
     {
      string el_name = "";
      int    id      = 0;
      long   lparam  = 0;
      double dparam  = 0.0;
      string sparam  = "";
      GuiController::GetEvent(InpWpfUi3, InpWpfWindow, i, el_name, id, lparam, dparam, sparam);
      HandleEvent(el_name, (ENUM_GUI_EVENT)id, lparam, dparam, sparam);
     }
   GuiController::ClearEvents(InpWpfUi3, InpWpfWindow);
  }

//+------------------------------------------------------------------+
void EchoToGui(const string msg)
  {
   if(!g_window_ready)
      return;
   string line = TimeToString(TimeLocal(), TIME_MINUTES) + "  [MQL5]  " + msg;
   GuiController::SendEvent(InpWpfUi3, InpWpfWindow, "txtMqlLog",
                            (int)GUI_TEXT_CHANGE, 0, 0, line);
   Print(line);
  }

//+------------------------------------------------------------------+
void HandleEvent(const string el_name, const ENUM_GUI_EVENT id,
                  const long lparam, const double dparam, const string sparam)
  {
   switch(id)
     {
      case GUI_EXCEPTION:
         EchoToGui("EXCEPTION " + sparam);
         break;

      case GUI_CLICK:
         EchoToGui("recv CLICK " + el_name);
         HandleClick(el_name);
         break;

      case GUI_TEXT_CHANGE:
         EchoToGui("recv TEXT " + el_name + " = " + sparam);
         if(el_name == "txtLot" || el_name == "txtVolume")
           {
            double v = StringToDouble(sparam);
            if(v > 0)
              {
               g_lot = v;
               g_volume = v;
              }
           }
         else if(el_name == "txtPrice")
            g_price = StringToDouble(sparam);
         else if(el_name == "txtSL")
            g_sl = StringToDouble(sparam);
         else if(el_name == "txtTP")
            g_tp = StringToDouble(sparam);
         else if(el_name == "txtComment")
            g_comment = sparam;
         break;

      case GUI_CHECKBOX_CHANGE:
         if(el_name == "chkAutoTrade")
           {
            g_autotrade = (lparam != 0);
            EchoToGui("recv Auto-trade = " + (string)g_autotrade);
           }
         else if(el_name == "chkRealTrade")
           {
            g_real_trade = (lparam != 0);
            EchoToGui("recv Real trading (test) = " + (string)g_real_trade);
           }
         break;

      case GUI_COMBOBOX_CHANGE:
         EchoToGui("recv COMBO " + el_name + " idx=" + IntegerToString((int)lparam) + " " + sparam);
         if(el_name == "cmbOrderType")
            g_order_type = sparam;
         else if(el_name == "cmbFill")
            g_fill = sparam;
         break;

      default:
         EchoToGui("recv event id=" + IntegerToString((int)id) + " el=" + el_name);
         break;
     }
  }

//+------------------------------------------------------------------+
void HandleClick(const string el_name)
  {
   if(el_name == "btnQuickBuy" || el_name == "btnBuyMarket" || el_name == "btnBuy")
     {
      DoTradeStub(ORDER_TYPE_BUY, "Market");
      return;
     }
   if(el_name == "btnQuickSell" || el_name == "btnSellMarket" || el_name == "btnSell")
     {
      DoTradeStub(ORDER_TYPE_SELL, "Market");
      return;
     }
   if(el_name == "btnPlacePending")
     {
      DoPendingStub();
      return;
     }
   if(StringFind(el_name, "btnMode") == 0)
     {
      EchoToGui("mode button " + el_name + " (UI only ack)");
      return;
     }
   if(el_name == "btnSessionsCalendar")
     {
      EchoToGui("Sessions calendar opened on WPF side");
      return;
     }
  }

//+------------------------------------------------------------------+
void DoTradeStub(ENUM_ORDER_TYPE type, const string kind)
  {
   double price = (type == ORDER_TYPE_BUY) ? SymbolInfoDouble(_Symbol, SYMBOL_ASK)
                                            : SymbolInfoDouble(_Symbol, SYMBOL_BID);
   string side = (type == ORDER_TYPE_BUY) ? "BUY" : "SELL";
   string msg = StringFormat(
      "ACK %s %s %s lot=%.2f price=%s SL=%s TP=%s fill=%s real=%s comment=%s",
      kind, side, _Symbol, g_volume,
      DoubleToString(price, (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS)),
      DoubleToString(g_sl, (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS)),
      DoubleToString(g_tp, (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS)),
      g_fill,
      (string)g_real_trade,
      g_comment);
   EchoToGui(msg);
   // Заглушка: реальный OrderSend не вызывается (даже при Real trading test).
  }

//+------------------------------------------------------------------+
void DoPendingStub()
  {
   string msg = StringFormat(
      "ACK PENDING type=%s %s vol=%.2f price=%s SL=%s TP=%s fill=%s real=%s",
      g_order_type, _Symbol, g_volume,
      DoubleToString(g_price, (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS)),
      DoubleToString(g_sl, (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS)),
      DoubleToString(g_tp, (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS)),
      g_fill,
      (string)g_real_trade);
   EchoToGui(msg);
  }

//+------------------------------------------------------------------+
void PushGuiState()
  {
   if(!g_window_ready)
      return;

   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   int digits = (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS);

   GuiController::SendEvent(InpWpfUi3, InpWpfWindow, "txtSymbol",
                            (int)GUI_TEXT_CHANGE, 0, 0, _Symbol);
   GuiController::SendEvent(InpWpfUi3, InpWpfWindow, "txtBid",
                            (int)GUI_TEXT_CHANGE, 0, 0, DoubleToString(bid, digits));
   GuiController::SendEvent(InpWpfUi3, InpWpfWindow, "txtAsk",
                            (int)GUI_TEXT_CHANGE, 0, 0, DoubleToString(ask, digits));

   // Mini-panel button captions with live prices
   GuiController::SendEvent(InpWpfUi3, InpWpfWindow, "btnQuickSell",
                            (int)GUI_TEXT_CHANGE, 0, 0, "SELL  " + DoubleToString(bid, digits));
   GuiController::SendEvent(InpWpfUi3, InpWpfWindow, "btnQuickBuy",
                            (int)GUI_TEXT_CHANGE, 0, 0, "BUY  " + DoubleToString(ask, digits));

   GuiController::SendEvent(InpWpfUi3, InpWpfWindow, "txtAccount",
                            (int)GUI_TEXT_CHANGE, 0, 0, BuildAccountLine());
   GuiController::SendEvent(InpWpfUi3, InpWpfWindow, "txtMarketStatus",
                            (int)GUI_TEXT_CHANGE, 0, 0, BuildMarketStatusLine());
  }

//+------------------------------------------------------------------+
string BuildAccountLine()
  {
   return StringFormat(
      "Balance: %s   Equity: %s   Margin: %s   Free: %s   Profit: %s   %s",
      DoubleToString(AccountInfoDouble(ACCOUNT_BALANCE), 2),
      DoubleToString(AccountInfoDouble(ACCOUNT_EQUITY), 2),
      DoubleToString(AccountInfoDouble(ACCOUNT_MARGIN), 2),
      DoubleToString(AccountInfoDouble(ACCOUNT_MARGIN_FREE), 2),
      DoubleToString(AccountInfoDouble(ACCOUNT_PROFIT), 2),
      AccountInfoString(ACCOUNT_CURRENCY));
  }

//+------------------------------------------------------------------+
string FormatDuration(const int totalSec)
  {
   int sec = MathMax(0, totalSec);
   int d = sec / 86400;
   int h = (sec % 86400) / 3600;
   int m = (sec % 3600) / 60;
   if(d > 0)
      return StringFormat("%dд %02d:%02d", d, h, m);
   return StringFormat("%02d:%02d", h, m);
  }

//+------------------------------------------------------------------+
datetime ServerToUtc(const datetime serverDt)
  {
   return serverDt - (TimeTradeServer() - TimeGMT());
  }

//+------------------------------------------------------------------+
datetime ServerToPacific(const datetime serverDt)
  {
   return ServerToUtc(serverDt) - 8 * 3600;
  }

//+------------------------------------------------------------------+
datetime UtcToPacific(const datetime utcDt)
  {
   return utcDt - 8 * 3600;
  }

//+------------------------------------------------------------------+
string FormatPacific(const datetime serverDt)
  {
   return TimeToString(ServerToPacific(serverDt), TIME_DATE|TIME_MINUTES) + " PT";
  }

//+------------------------------------------------------------------+
string FormatPacificUtc(const datetime utcDt)
  {
   return TimeToString(UtcToPacific(utcDt), TIME_DATE|TIME_MINUTES) + " PT";
  }

//+------------------------------------------------------------------+
string DetectMarketSessionUtc(const datetime utcNow)
  {
   MqlDateTime dt;
   TimeToStruct(utcNow, dt);
   int mins = dt.hour * 60 + dt.min;

   bool sydney = (mins >= 21 * 60) || (mins < 6 * 60);
   bool tokyo  = (mins >= 0) && (mins < 9 * 60);
   bool london = (mins >= 7 * 60) && (mins < 16 * 60);
   bool ny     = (mins >= 12 * 60) && (mins < 21 * 60);

   string parts = "";
   if(sydney) parts += (StringLen(parts) > 0 ? "/" : "") + "Sydney";
   if(tokyo)  parts += (StringLen(parts) > 0 ? "/" : "") + "Tokyo";
   if(london) parts += (StringLen(parts) > 0 ? "/" : "") + "London";
   if(ny)     parts += (StringLen(parts) > 0 ? "/" : "") + "New York";
   if(StringLen(parts) == 0)
      return "Off-hours";
   return parts;
  }

//+------------------------------------------------------------------+
//| Первая (ведущая) сессия в момент открытия.                       |
//+------------------------------------------------------------------+
string OpeningSessionNameUtc(const datetime utcOpen)
  {
   MqlDateTime dt;
   TimeToStruct(utcOpen, dt);
   int mins = dt.hour * 60 + dt.min;
   // Порядок старта по UTC: Sydney(21), Tokyo(0), London(7), New York(12)
   if(mins >= 21 * 60 || mins < 0)
      return "Sydney";
   if(mins < 7 * 60)
      return "Tokyo";
   if(mins < 12 * 60)
      return "London";
   if(mins < 21 * 60)
      return "New York";
   return "Sydney";
  }

//+------------------------------------------------------------------+
datetime DayStartServer(const datetime t)
  {
   MqlDateTime dt;
   TimeToStruct(t, dt);
   dt.hour = 0; dt.min = 0; dt.sec = 0;
   return StructToTime(dt);
  }

//+------------------------------------------------------------------+
bool SessionAbsBounds(const datetime dayBase, const datetime fromTod, const datetime toTod,
                      datetime &absFrom, datetime &absTo)
  {
   MqlDateTime f, t;
   TimeToStruct(fromTod, f);
   TimeToStruct(toTod, t);
   absFrom = dayBase + f.hour * 3600 + f.min * 60 + f.sec;
   absTo   = dayBase + t.hour * 3600 + t.min * 60 + t.sec;
   if(absTo <= absFrom)
      absTo += 86400;
   return true;
  }

//+------------------------------------------------------------------+
bool FindCurrentQuoteSession(const datetime now, datetime &sessFrom, datetime &sessTo)
  {
   MqlDateTime dt;
   TimeToStruct(now, dt);
   ENUM_DAY_OF_WEEK dow = (ENUM_DAY_OF_WEEK)dt.day_of_week;
   datetime dayBase = DayStartServer(now);
   for(int i = 0; i < 16; i++)
     {
      datetime fromTod, toTod;
      if(!SymbolInfoSessionQuote(_Symbol, dow, i, fromTod, toTod))
         break;
      datetime absFrom, absTo;
      SessionAbsBounds(dayBase, fromTod, toTod, absFrom, absTo);
      if(now >= absFrom && now < absTo)
        {
         sessFrom = absFrom;
         sessTo   = absTo;
         return true;
        }
     }
   return false;
  }

//+------------------------------------------------------------------+
bool FindNextQuoteSessionStart(const datetime now, datetime &nextFrom, datetime &nextTo)
  {
   for(int dayOffset = 0; dayOffset <= 8; dayOffset++)
     {
      datetime probe = now + dayOffset * 86400;
      MqlDateTime dt;
      TimeToStruct(probe, dt);
      ENUM_DAY_OF_WEEK dow = (ENUM_DAY_OF_WEEK)dt.day_of_week;
      datetime dayBase = DayStartServer(probe);
      for(int i = 0; i < 16; i++)
        {
         datetime fromTod, toTod;
         if(!SymbolInfoSessionQuote(_Symbol, dow, i, fromTod, toTod))
            break;
         datetime absFrom, absTo;
         SessionAbsBounds(dayBase, fromTod, toTod, absFrom, absTo);
         if(absFrom > now)
           {
            nextFrom = absFrom;
            nextTo   = absTo;
            return true;
           }
        }
     }
   return false;
  }

//+------------------------------------------------------------------+
string BuildMarketStatusLine()
  {
   datetime nowServer = TimeTradeServer();
   datetime nowUtc    = TimeGMT();
   string session     = DetectMarketSessionUtc(nowUtc);

   datetime sessFrom, sessTo;
   if(FindCurrentQuoteSession(nowServer, sessFrom, sessTo))
     {
      int left = (int)(sessTo - nowServer);
      string line = StringFormat(
         "Рынок: ОТКРЫТ | Сессия: %s | До закрытия: %s (до %s)",
         session, FormatDuration(left), FormatPacific(sessTo));
      datetime nextFrom, nextTo;
      if(FindNextQuoteSessionStart(sessTo, nextFrom, nextTo))
        {
         string openSess = OpeningSessionNameUtc(ServerToUtc(nextFrom));
         line += StringFormat(" | След. сессия: %s (с %s)", FormatPacific(nextFrom), openSess);
        }
      return line;
     }

   datetime nextFrom, nextTo;
   if(FindNextQuoteSessionStart(nowServer, nextFrom, nextTo))
     {
      int wait = (int)(nextFrom - nowServer);
      string openSess = OpeningSessionNameUtc(ServerToUtc(nextFrom));
      return StringFormat(
         "Рынок: ЗАКРЫТ | Сейчас: %s | Откроется: %s (через %s) | Открытие с сессии: %s",
         session, FormatPacific(nextFrom), FormatDuration(wait), openSess);
     }

   MqlDateTime utc;
   TimeToStruct(nowUtc, utc);
   bool weekend = (utc.day_of_week == 6) ||
                  (utc.day_of_week == 0 && (utc.hour * 60 + utc.min) < 22 * 60) ||
                  (utc.day_of_week == 5 && (utc.hour * 60 + utc.min) >= 22 * 60);

   if(!weekend)
     {
      datetime friClose = nowUtc;
      MqlDateTime c = utc;
      int addDays = (5 - utc.day_of_week + 7) % 7;
      friClose = nowUtc + addDays * 86400;
      TimeToStruct(friClose, c);
      c.hour = 22; c.min = 0; c.sec = 0;
      friClose = StructToTime(c);
      if(friClose <= nowUtc)
         friClose += 7 * 86400;
      return StringFormat(
         "Рынок: ОТКРЫТ | Сессия: %s | До закрытия: %s (до %s)",
         session, FormatDuration((int)(friClose - nowUtc)), FormatPacificUtc(friClose));
     }

   int daysToSun = (7 - utc.day_of_week) % 7;
   if(utc.day_of_week == 0 && (utc.hour * 60 + utc.min) < 22 * 60)
      daysToSun = 0;
   else if(utc.day_of_week == 0)
      daysToSun = 7;
   datetime utcDay = nowUtc - (utc.hour * 3600 + utc.min * 60 + utc.sec);
   datetime sunOpen = utcDay + daysToSun * 86400 + 22 * 3600;
   if(sunOpen <= nowUtc)
      sunOpen += 7 * 86400;

   return StringFormat(
      "Рынок: ЗАКРЫТ | Сейчас: %s | Откроется: %s (через %s) | Открытие с сессии: Sydney",
      session, FormatPacificUtc(sunOpen), FormatDuration((int)(sunOpen - nowUtc)));
  }
//+------------------------------------------------------------------+
