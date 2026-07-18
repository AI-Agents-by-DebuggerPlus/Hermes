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

// Путь: папка ui_vN и имя DLL должны совпадать (ui_v29 + HermesWpfTerminalUi29.dll).
input string InpWpfUi29       = "D:/Programming/AI_Agents/Hermes/Hermes.MT5/WpfGuiControllerTest/WpfTestApp/bin/Release/ui_v29/HermesWpfTerminalUi29.dll";
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

   SymbolSelect(_Symbol, true);

   if(!FileIsExist(InpWpfUi29, 0) && !FileIsExist(InpWpfUi29, FILE_COMMON))
     {
      // FileIsExist для абсолютных путей вне MQL5 sandbox часто false —
      // проверяем через WinAPI-стиль: просто печатаем путь и пробуем LoadFrom.
      Print("WPF DLL path (must exist on disk): ", InpWpfUi29);
     }
   else
      Print("WPF DLL path OK: ", InpWpfUi29);

   ResetLastError();
   bool ok = GuiController::ShowWindow(InpWpfUi29, InpWpfWindow);
   if(!ok || GetLastError() != 0)
     {
      Print("ShowWindow FAILED path=", InpWpfUi29, " error=", GetLastError());
      Alert("WPF ShowWindow failed. Check DLL path in EA inputs:\n", InpWpfUi29);
      return(INIT_FAILED);
     }

   g_window_ready = true;
   EventSetMillisecondTimer(InpTimerMs);
   PushGuiState();
   EchoToGui("panel started / " + InpWpfWindow);
   Print("WPF panel started: ", InpWpfUi29, " / ", InpWpfWindow);
   return(INIT_SUCCEEDED);
  }

//+------------------------------------------------------------------+
void OnDeinit(const int reason)
  {
   EventKillTimer();
   if(g_window_ready)
      GuiController::HideWindow(InpWpfUi29, InpWpfWindow);
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
   int total = GuiController::EventsTotal(InpWpfUi29, InpWpfWindow);
   if(total <= 0)
      return;

   for(int i = 0; i < total; i++)
     {
      string el_name = "";
      int    id      = 0;
      long   lparam  = 0;
      double dparam  = 0.0;
      string sparam  = "";
      GuiController::GetEvent(InpWpfUi29, InpWpfWindow, i, el_name, id, lparam, dparam, sparam);
      HandleEvent(el_name, (ENUM_GUI_EVENT)id, lparam, dparam, sparam);
     }
   GuiController::ClearEvents(InpWpfUi29, InpWpfWindow);
  }

//+------------------------------------------------------------------+
void EchoToGui(const string msg)
  {
   if(!g_window_ready)
      return;
   string line = TimeToString(TimeLocal(), TIME_MINUTES) + "  [MQL5]  " + msg;
   GuiController::SendEvent(InpWpfUi29, InpWpfWindow, "txtMqlLog",
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
   if(el_name == "tabMarket" || el_name == "tabLimit" || el_name == "tabStop" || el_name == "tabStopLimit")
     {
      EchoToGui("mode tab " + el_name + " (UI only ack)");
      return;
     }
   if(el_name == "btnSessionsCalendar")
     {
      EchoToGui("Sessions calendar opened on WPF side");
      return;
     }
   if(el_name == "btnSettings")
     {
      EchoToGui("Settings opened on WPF side");
      return;
     }
   if(el_name == "btnSettingsClosed")
     {
      EchoToGui("Settings closed on WPF side");
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
   datetime tickTime = (datetime)SymbolInfoInteger(_Symbol, SYMBOL_TIME);
   int tickAge = (tickTime > 0) ? (int)(TimeCurrent() - tickTime) : -1;

   GuiController::SendEvent(InpWpfUi29, InpWpfWindow, "txtSymbol",
                            (int)GUI_TEXT_CHANGE, 0, 0, _Symbol);
   GuiController::SendEvent(InpWpfUi29, InpWpfWindow, "txtBid",
                            (int)GUI_TEXT_CHANGE, 0, 0, DoubleToString(bid, digits));
   GuiController::SendEvent(InpWpfUi29, InpWpfWindow, "txtAsk",
                            (int)GUI_TEXT_CHANGE, 0, 0, DoubleToString(ask, digits));

   // Mini-panel button captions with live prices
   GuiController::SendEvent(InpWpfUi29, InpWpfWindow, "btnQuickSell",
                            (int)GUI_TEXT_CHANGE, 0, 0, "SELL  " + DoubleToString(bid, digits));
   GuiController::SendEvent(InpWpfUi29, InpWpfWindow, "btnQuickBuy",
                            (int)GUI_TEXT_CHANGE, 0, 0, "BUY  " + DoubleToString(ask, digits));

   GuiController::SendEvent(InpWpfUi29, InpWpfWindow, "txtAccount",
                            (int)GUI_TEXT_CHANGE, 0, 0, BuildAccountLine());
   GuiController::SendEvent(InpWpfUi29, InpWpfWindow, "txtMarketStatus",
                            (int)GUI_TEXT_CHANGE, 0, 0, BuildMarketStatusLine(tickAge, tickTime));
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
// US Pacific DST: 2nd Sunday March 02:00 PST → UTC-7; 1st Sunday Nov 02:00 PDT → UTC-8.
datetime NthWeekdayOfMonth(const int year, const int month, const int weekday, const int nth)
  {
   MqlDateTime dt;
   ZeroMemory(dt);
   dt.year = year;
   dt.mon  = month;
   dt.day  = 1;
   datetime first = StructToTime(dt);
   TimeToStruct(first, dt);
   int add = (weekday - dt.day_of_week + 7) % 7;
   dt.day = 1 + add + (nth - 1) * 7;
   return StructToTime(dt);
  }

//+------------------------------------------------------------------+
int PacificUtcOffsetSec(const datetime utcDt)
  {
   MqlDateTime dt;
   TimeToStruct(utcDt, dt);
   datetime dstStart = NthWeekdayOfMonth(dt.year, 3, 0, 2) + 10 * 3600; // 02:00 PST = 10:00 UTC
   datetime dstEnd   = NthWeekdayOfMonth(dt.year, 11, 0, 1) + 9 * 3600;  // 02:00 PDT = 09:00 UTC
   if(utcDt >= dstStart && utcDt < dstEnd)
      return -7 * 3600;
   return -8 * 3600;
  }

//+------------------------------------------------------------------+
datetime ServerToPacific(const datetime serverDt)
  {
   datetime utc = ServerToUtc(serverDt);
   return utc + PacificUtcOffsetSec(utc);
  }

//+------------------------------------------------------------------+
datetime UtcToPacific(const datetime utcDt)
  {
   return utcDt + PacificUtcOffsetSec(utcDt);
  }

//+------------------------------------------------------------------+
string FormatPacific(const datetime serverDt)
  {
   datetime utc = ServerToUtc(serverDt);
   string abbr = (PacificUtcOffsetSec(utc) == -7 * 3600) ? " PDT" : " PST";
   return TimeToString(ServerToPacific(serverDt), TIME_DATE|TIME_MINUTES) + abbr;
  }

//+------------------------------------------------------------------+
string FormatPacificUtc(const datetime utcDt)
  {
   string abbr = (PacificUtcOffsetSec(utcDt) == -7 * 3600) ? " PDT" : " PST";
   return TimeToString(UtcToPacific(utcDt), TIME_DATE|TIME_MINUTES) + abbr;
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
bool IsFxWeekendUtc(const datetime utcNow)
  {
   MqlDateTime utc;
   TimeToStruct(utcNow, utc);
   int mins = utc.hour * 60 + utc.min;
   // Типичное FX-окно: вс 22:00 UTC → пт 22:00 UTC
   if(utc.day_of_week == 6) // Saturday
      return true;
   if(utc.day_of_week == 0 && mins < 22 * 60) // Sunday before open
      return true;
   if(utc.day_of_week == 5 && mins >= 22 * 60) // Friday after close
      return true;
   return false;
  }

//+------------------------------------------------------------------+
string FormatTickAge(const int tickAge, const datetime tickTime)
  {
   if(tickTime <= 0)
      return "котировка: нет данных";
   if(tickAge < 0)
      return "котировка: " + TimeToString(tickTime, TIME_DATE|TIME_SECONDS);
   if(tickAge < 5)
      return "котировка: live (" + IntegerToString(tickAge) + "с)";
   if(tickAge < 3600)
      return StringFormat("котировка: %s (%s назад)",
                          TimeToString(tickTime, TIME_SECONDS), FormatDuration(tickAge));
   return StringFormat("котировка: %s (%s назад)",
                       TimeToString(tickTime, TIME_DATE|TIME_MINUTES), FormatDuration(tickAge));
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
string BuildMarketStatusLine(const int tickAge, const datetime tickTime)
  {
   datetime nowServer = TimeTradeServer();
   datetime nowUtc    = TimeGMT();
   string clockSession = DetectMarketSessionUtc(nowUtc);
   string tickInfo     = FormatTickAge(tickAge, tickTime);

   datetime sessFrom, sessTo;
   if(FindCurrentQuoteSession(nowServer, sessFrom, sessTo))
     {
      int left = (int)(sessTo - nowServer);
      string line = StringFormat(
         "Рынок: ОТКРЫТ | Сессия: %s | До закрытия: %s (до %s) | %s",
         clockSession, FormatDuration(left), FormatPacific(sessTo), tickInfo);
      datetime nextFrom, nextTo;
      if(FindNextQuoteSessionStart(sessTo, nextFrom, nextTo))
        {
         string openSess = OpeningSessionNameUtc(ServerToUtc(nextFrom));
         line += StringFormat(" | След.: %s (с %s)", FormatPacific(nextFrom), openSess);
        }
      return line;
     }

   datetime nextFrom, nextTo;
   if(FindNextQuoteSessionStart(nowServer, nextFrom, nextTo))
     {
      int wait = (int)(nextFrom - nowServer);
      string openSess = OpeningSessionNameUtc(ServerToUtc(nextFrom));
      string why = IsFxWeekendUtc(nowUtc)
                   ? "выходные (гео-сессия по часам: " + clockSession + ")"
                   : "вне сессии котировок (по часам: " + clockSession + ")";
      return StringFormat(
         "Рынок: ЗАКРЫТ | %s | Откроется: %s (через %s) | с сессии: %s | %s",
         why, FormatPacific(nextFrom), FormatDuration(wait), openSess, tickInfo);
     }

   if(!IsFxWeekendUtc(nowUtc))
     {
      MqlDateTime utc;
      TimeToStruct(nowUtc, utc);
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
         "Рынок: ОТКРЫТ | Сессия: %s | До закрытия: %s (до %s) | %s",
         clockSession, FormatDuration((int)(friClose - nowUtc)), FormatPacificUtc(friClose), tickInfo);
     }

   MqlDateTime utc;
   TimeToStruct(nowUtc, utc);
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
      "Рынок: ЗАКРЫТ | выходные (гео-сессия по часам: %s) | Откроется: %s (через %s) | с сессии: Sydney | %s",
      clockSession, FormatPacificUtc(sunOpen), FormatDuration((int)(sunOpen - nowUtc)), tickInfo);
  }
//+------------------------------------------------------------------+
