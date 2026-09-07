//+------------------------------------------------------------------+
//|                                HermesWpfGuiControllerTest.mq5    |
//| MQL5 <=> HermesWpfGuiController.dll <=> HermesWpfTerminal        |
//+------------------------------------------------------------------+
#property strict

#include <Trade/Trade.mqh>

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

// Path: ui_vN folder and DLL name must match (ui_v48 + HermesWpfTerminalUi48.dll).
input string InpWpfUi48       = "D:/Programming/AI_Agents/Hermes/Hermes.MT5/WpfGuiControllerTest/WpfTestApp/bin/Release/ui_v48/HermesWpfTerminalUi48.dll";
input string InpWpfWindow    = "HermesWpfTerminal";
input string InpIpcDir       = "D:/Programming/AI_Agents/HermesProjects/Mt5Terminal/hermes/ipc";
input int    InpTimerMs      = 200;
input double InpDefaultLot   = 0.10;
input ulong  InpMagic        = 260804;

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
double g_stoplimit     = 0.0;
string g_order_symbol  = "";
CTrade g_trade;

#define POS_SLOTS 8
ulong  g_pos_ticket[POS_SLOTS];
int    g_pos_shown = 0;

#import "HermesWpfGuiController.dll"
#import

//+------------------------------------------------------------------+
int OnInit()
  {
   g_lot = InpDefaultLot;
   g_volume = InpDefaultLot;

   SymbolSelect(_Symbol, true);

   if(!FileIsExist(InpWpfUi48, 0) && !FileIsExist(InpWpfUi48, FILE_COMMON))
     {
      // FileIsExist often false for absolute paths outside MQL5 sandbox �
      // print path and try LoadFrom anyway.
      Print("WPF DLL path (must exist on disk): ", InpWpfUi48);
     }
   else
      Print("WPF DLL path OK: ", InpWpfUi48);

   ResetLastError();
   bool ok = GuiController::ShowWindow(InpWpfUi48, InpWpfWindow);
   if(!ok || GetLastError() != 0)
     {
      Print("ShowWindow FAILED path=", InpWpfUi48, " error=", GetLastError());
      Alert("WPF ShowWindow failed. Check DLL path in EA inputs:\n", InpWpfUi48);
      return(INIT_FAILED);
     }

   g_window_ready = true;
   EventSetMillisecondTimer(InpTimerMs);
   PushGuiState();
   EchoToGui("panel started / " + InpWpfWindow);
   Print("WPF panel started: ", InpWpfUi48, " / ", InpWpfWindow);
   return(INIT_SUCCEEDED);
  }

//+------------------------------------------------------------------+
void OnDeinit(const int reason)
  {
   EventKillTimer();
   if(g_window_ready)
      GuiController::HideWindow(InpWpfUi48, InpWpfWindow);
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
   int total = GuiController::EventsTotal(InpWpfUi48, InpWpfWindow);
   if(total <= 0)
      return;

   for(int i = 0; i < total; i++)
     {
      string el_name = "";
      int    id      = 0;
      long   lparam  = 0;
      double dparam  = 0.0;
      string sparam  = "";
      GuiController::GetEvent(InpWpfUi48, InpWpfWindow, i, el_name, id, lparam, dparam, sparam);
      HandleEvent(el_name, (ENUM_GUI_EVENT)id, lparam, dparam, sparam);
     }
   GuiController::ClearEvents(InpWpfUi48, InpWpfWindow);
  }

//+------------------------------------------------------------------+
void EchoToGui(const string msg)
  {
   if(!g_window_ready)
      return;
   string line = TimeToString(TimeLocal(), TIME_MINUTES) + "  [MQL5]  " + msg;
   GuiController::SendEvent(InpWpfUi48, InpWpfWindow, "txtMqlLog",
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
         else if(el_name == "txtStopLimit")
            g_stoplimit = StringToDouble(sparam);
         else if(el_name == "txtSL")
            g_sl = StringToDouble(sparam);
         else if(el_name == "txtTP")
            g_tp = StringToDouble(sparam);
         else if(el_name == "txtComment")
            g_comment = sparam;
         else if(el_name == "txtOrderSymbol")
            g_order_symbol = sparam;
         else if(el_name == "txtOrderTypeLabel")
            g_order_type = NormalizeOrderType(sparam);
         break;

      case GUI_CHECKBOX_CHANGE:
         if(el_name == "chkAutoTrade")
           {
            g_autotrade = (lparam != 0);
            EchoToGui("recv Auto-trade = " + (string)g_autotrade +
                      (g_autotrade ? " (agent/auto OrderSend allowed when Real ON)" : " (manual UI clicks still OK when Real ON)"));
           }
         else if(el_name == "chkRealTrade")
           {
            g_real_trade = (lparam != 0);
            EchoToGui("recv Real trading = " + (string)g_real_trade +
                      (g_real_trade ? " (OrderSend ENABLED)" : " (stub ACK only)"));
           }
         break;

      case GUI_COMBOBOX_CHANGE:
         EchoToGui("recv COMBO " + el_name + " idx=" + IntegerToString((int)lparam) + " " + sparam);
         if(el_name == "cmbOrderType")
            g_order_type = NormalizeOrderType(sparam);
         else if(el_name == "cmbFill")
            g_fill = NormalizeOrderType(sparam);
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
      RequestSideTrade(true);
      return;
     }
   if(el_name == "btnQuickSell" || el_name == "btnSellMarket" || el_name == "btnSell")
     {
      RequestSideTrade(false);
      return;
     }
   if(el_name == "btnPlacePending")
     {
      RequestPendingTrade();
      return;
     }
   if(el_name == "tabMarket" || el_name == "tabLimit" || el_name == "tabStop" || el_name == "tabStopLimit")
     {
      EchoToGui("mode tab " + el_name + " (UI only ack)");
      return;
     }
   if(StringFind(el_name, "btnClosePos") == 0)
     {
      int slot = (int)StringToInteger(StringSubstr(el_name, 11));
      ClosePositionSlot(slot);
      return;
     }
   if(el_name == "btnCloseAllPositions")
     {
      CloseAllPositions();
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
   if(el_name == "btnScreenshot" || el_name == "btnCaptureScreenshot")
     {
      CaptureChartScreenshot();
      return;
     }
   if(el_name == "btnListSymbols")
     {
      ExportSymbolsList();
      return;
     }
   if(el_name == "tabScreenshots")
     {
      EchoToGui("mode tab Screenshots (UI only ack)");
      return;
     }
  }

//+------------------------------------------------------------------+
void CaptureChartScreenshot()
  {
   string fileName = "hermes_chart_" +
                     IntegerToString(TimeLocal()) + "_" +
                     IntegerToString((int)GetTickCount()) + ".png";
   // ChartScreenShot writes into MQL5\Files\
   if(!ChartScreenShot(0, fileName, 1280, 720, ALIGN_RIGHT))
     {
      EchoToGui("SCREENSHOT FAIL ChartScreenShot err=" + IntegerToString(GetLastError()));
      GuiController::SendEvent(InpWpfUi48, InpWpfWindow, "txtScreenshotPath",
                               GUI_TEXT_CHANGE, 0, 0.0, "");
      return;
     }

   string absPath = TerminalInfoString(TERMINAL_DATA_PATH) + "\\MQL5\\Files\\" + fileName;
   EchoToGui("SCREENSHOT OK " + absPath);
   GuiController::SendEvent(InpWpfUi48, InpWpfWindow, "txtScreenshotPath",
                            GUI_TEXT_CHANGE, 0, 0.0, absPath);
  }

//+------------------------------------------------------------------+
bool IsTradeableSymbol(const string sym)
  {
   if(StringLen(sym) == 0)
      return false;
   long mode = SymbolInfoInteger(sym, SYMBOL_TRADE_MODE);
   if(mode == SYMBOL_TRADE_MODE_DISABLED || mode == SYMBOL_TRADE_MODE_CLOSEONLY)
      return false;
   return true;
  }

//+------------------------------------------------------------------+
//+------------------------------------------------------------------+
string NormalizeOrderType(const string raw)
  {
   string t = raw;
   // GuiController may send "System.Windows.Controls.ComboBoxItem: Buy Limit"
   int p = StringFind(t, ": ");
   if(p >= 0)
      t = StringSubstr(t, p + 2);
   StringTrimLeft(t);
   StringTrimRight(t);
   return t;
  }

//+------------------------------------------------------------------+
void ExportSymbolsList()
  {
   // MT5 FileOpen cannot write outside sandbox (err 5002). Write under MQL5\Files\,
   // then HWT copies into hermes/ipc/symbols.json.
   string relName = "hermes_symbols.json";

   string syms[];
   int total = SymbolsTotal(false);
   int added = 0;
   ArrayResize(syms, 0);

   for(int i = 0; i < total && added < 4000; i++)
     {
      string sym = SymbolName(i, false);
      if(!IsTradeableSymbol(sym))
         continue;
      ArrayResize(syms, added + 1);
      syms[added] = sym;
      added++;
     }

   int h = FileOpen(relName, FILE_WRITE|FILE_TXT|FILE_ANSI);
   if(h == INVALID_HANDLE)
     {
      EchoToGui("SYMBOLS FAIL FileOpen err=" + IntegerToString(GetLastError()) + " name=" + relName);
      GuiController::SendEvent(InpWpfUi48, InpWpfWindow, "txtSymbolsPath",
                               GUI_TEXT_CHANGE, 0, 0.0, "");
      return;
     }

   FileWriteString(h, "{\r\n\"source\":\"Mt5Terminal\",\r\n\"utc\":\"");
   FileWriteString(h, TimeToString(TimeGMT(), TIME_DATE|TIME_SECONDS) + "Z");
   FileWriteString(h, "\",\r\n\"chart_symbol\":\"");
   FileWriteString(h, _Symbol);
   FileWriteString(h, "\",\r\n\"count\":");
   FileWriteString(h, IntegerToString(added));
   FileWriteString(h, ",\r\n\"symbols\":[");

   for(int j = 0; j < added; j++)
     {
      if(j > 0)
         FileWriteString(h, ",");
      FileWriteString(h, "\"" + syms[j] + "\"");
     }

   FileWriteString(h, "]\r\n}");
   FileClose(h);

   string absPath = TerminalInfoString(TERMINAL_DATA_PATH) + "\\MQL5\\Files\\" + relName;
   EchoToGui("SYMBOLS OK count=" + IntegerToString(added) + " path=" + absPath);
   GuiController::SendEvent(InpWpfUi48, InpWpfWindow, "txtSymbolsPath",
                            GUI_TEXT_CHANGE, 0, 0.0, absPath);
  }

//+------------------------------------------------------------------+
bool IsMarketOrderType(const string t)
  {
   return(t == "" || t == "Market");
  }

//+------------------------------------------------------------------+
void RequestSideTrade(const bool buy)
  {
   if(IsMarketOrderType(g_order_type))
     {
      DoMarketTrade(buy ? ORDER_TYPE_BUY : ORDER_TYPE_SELL);
      return;
     }
   RequestPendingTrade();
  }

//+------------------------------------------------------------------+
string ResolveTradeSymbol()
  {
   string sym = (StringLen(g_order_symbol) > 0) ? g_order_symbol : _Symbol;
   if(SymbolSelect(sym, true))
      return sym;

   // Broker aliases (NAS100 often USTEC / NAS100m)
   string alts[];
   ArrayResize(alts, 0);
   if(StringFind(sym, "NAS") == 0 || sym == "USTEC")
     {
      ArrayResize(alts, 4);
      alts[0] = "NAS100";
      alts[1] = "USTEC";
      alts[2] = "NAS100m";
      alts[3] = "NASDAQ";
     }
   else if(StringFind(sym, "XAU") == 0 || sym == "GOLD")
     {
      ArrayResize(alts, 3);
      alts[0] = "XAUUSD";
      alts[1] = "XAUUSDm";
      alts[2] = "GOLD";
     }

   for(int i = 0; i < ArraySize(alts); i++)
     {
      if(alts[i] == sym)
         continue;
      if(SymbolSelect(alts[i], true))
        {
         EchoToGui("SymbolSelect alias " + sym + " → " + alts[i]);
         return alts[i];
        }
     }

   EchoToGui("WARN SymbolSelect failed for " + sym + ", fallback _Symbol=" + _Symbol);
   return _Symbol;
  }

//+------------------------------------------------------------------+
bool TradeAllowedNow(string &reason)
  {
   return TradeAllowedForSymbol(_Symbol, reason);
  }

//+------------------------------------------------------------------+
bool TradeAllowedForSymbol(const string sym, string &reason)
  {
   if(!TerminalInfoInteger(TERMINAL_TRADE_ALLOWED))
     {
      reason = "terminal trade disabled (check connection / investor password)";
      return false;
     }
   if(!MQLInfoInteger(MQL_TRADE_ALLOWED))
     {
      reason = "Algo Trading OFF in MT5 toolbar - turn on AutoTrading";
      return false;
     }
   if(!AccountInfoInteger(ACCOUNT_TRADE_ALLOWED))
     {
      reason = "account trade not allowed";
      return false;
     }
   long mode = SymbolInfoInteger(sym, SYMBOL_TRADE_MODE);
   if(mode == SYMBOL_TRADE_MODE_DISABLED)
     {
      reason = "symbol trade mode disabled for " + sym;
      return false;
     }
   return true;
  }

//+------------------------------------------------------------------+
double NormalizeVolume(double vol)
  {
   return NormalizeVolumeFor(_Symbol, vol);
  }

//+------------------------------------------------------------------+
double NormalizeVolumeFor(const string sym, double vol)
  {
   double vmin = SymbolInfoDouble(sym, SYMBOL_VOLUME_MIN);
   double vmax = SymbolInfoDouble(sym, SYMBOL_VOLUME_MAX);
   double step = SymbolInfoDouble(sym, SYMBOL_VOLUME_STEP);
   if(step <= 0)
      step = 0.01;
   if(vol < vmin)
      vol = vmin;
   if(vol > vmax)
      vol = vmax;
   vol = MathFloor(vol / step + 1e-8) * step;
   int digits = 0;
   double s = step;
   while(digits < 8 && MathAbs(s - MathRound(s)) > 1e-8)
     {
      s *= 10.0;
      digits++;
     }
   return NormalizeDouble(vol, digits);
  }

//+------------------------------------------------------------------+
double NormalizePricePx(double price)
  {
   return NormalizePriceFor(_Symbol, price);
  }

//+------------------------------------------------------------------+
double NormalizePriceFor(const string sym, double price)
  {
   int digits = (int)SymbolInfoInteger(sym, SYMBOL_DIGITS);
   return NormalizeDouble(price, digits);
  }

//+------------------------------------------------------------------+
ENUM_ORDER_TYPE_FILLING ResolveFilling()
  {
   long modes = SymbolInfoInteger(_Symbol, SYMBOL_FILLING_MODE);
   if(StringFind(g_fill, "Immediate") >= 0 || StringFind(g_fill, "IOC") >= 0)
     {
      if((modes & SYMBOL_FILLING_IOC) != 0)
         return ORDER_FILLING_IOC;
     }
   if(StringFind(g_fill, "Return") >= 0)
      return ORDER_FILLING_RETURN;
   if((modes & SYMBOL_FILLING_FOK) != 0)
      return ORDER_FILLING_FOK;
   if((modes & SYMBOL_FILLING_IOC) != 0)
      return ORDER_FILLING_IOC;
   return ORDER_FILLING_RETURN;
  }

//+------------------------------------------------------------------+
void ConfigureTrade()
  {
   g_trade.SetExpertMagicNumber(InpMagic);
   g_trade.SetDeviationInPoints(30);
   g_trade.SetTypeFilling(ResolveFilling());
   g_trade.SetAsyncMode(false);
  }

//+------------------------------------------------------------------+
void EchoTradeResult(const bool ok, const string action)
  {
   if(ok)
     {
      EchoToGui(StringFormat(
         "FILLED %s %s order=%s deal=%s vol=%.2f price=%s ret=%u %s",
         action, _Symbol,
         IntegerToString(g_trade.ResultOrder()),
         IntegerToString(g_trade.ResultDeal()),
         g_trade.ResultVolume(),
         DoubleToString(g_trade.ResultPrice(), (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS)),
         g_trade.ResultRetcode(),
         g_trade.ResultRetcodeDescription()));
     }
   else
     {
      EchoToGui(StringFormat(
         "ERR %s %s ret=%u %s last=%s",
         action, _Symbol,
         g_trade.ResultRetcode(),
         g_trade.ResultRetcodeDescription(),
         g_trade.ResultComment()));
     }
  }

//+------------------------------------------------------------------+
void DoTradeStub(ENUM_ORDER_TYPE type, const string kind)
  {
   double price = (type == ORDER_TYPE_BUY) ? SymbolInfoDouble(_Symbol, SYMBOL_ASK)
                                            : SymbolInfoDouble(_Symbol, SYMBOL_BID);
   string side = (type == ORDER_TYPE_BUY) ? "BUY" : "SELL";
   string msg = StringFormat(
      "ACK stub %s %s %s lot=%.2f price=%s SL=%s TP=%s fill=%s auto=%s (enable Real trading for OrderSend)",
      kind, side, _Symbol, g_volume,
      DoubleToString(price, (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS)),
      DoubleToString(g_sl, (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS)),
      DoubleToString(g_tp, (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS)),
      g_fill,
      (string)g_autotrade);
   EchoToGui(msg);
  }

//+------------------------------------------------------------------+
void DoPendingStub(const string sym)
  {
   string msg = StringFormat(
      "ACK stub PENDING type=%s %s vol=%.2f price=%s SL=%s TP=%s auto=%s (enable Real trading for OrderSend)",
      g_order_type, sym, g_volume,
      DoubleToString(g_price, (int)SymbolInfoInteger(sym, SYMBOL_DIGITS)),
      DoubleToString(g_sl, (int)SymbolInfoInteger(sym, SYMBOL_DIGITS)),
      DoubleToString(g_tp, (int)SymbolInfoInteger(sym, SYMBOL_DIGITS)),
      (string)g_autotrade);
   EchoToGui(msg);
  }

//+------------------------------------------------------------------+
void DoMarketTrade(ENUM_ORDER_TYPE type)
  {
   if(!g_real_trade)
     {
      DoTradeStub(type, "Market");
      return;
     }

   string reason;
   if(!TradeAllowedNow(reason))
     {
      EchoToGui("ERR Market blocked: " + reason);
      return;
     }

   double vol = NormalizeVolume(g_volume);
   double sl = (g_sl > 0.0) ? NormalizePricePx(g_sl) : 0.0;
   double tp = (g_tp > 0.0) ? NormalizePricePx(g_tp) : 0.0;
   ConfigureTrade();

   string side = (type == ORDER_TYPE_BUY) ? "BUY" : "SELL";
   EchoToGui(StringFormat("SEND Market %s %s vol=%.2f SL=%s TP=%s auto=%s",
      side, _Symbol, vol,
      DoubleToString(sl, (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS)),
      DoubleToString(tp, (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS)),
      (string)g_autotrade));

   string cmt = (g_comment == "") ? (g_autotrade ? "Hermes auto" : "Hermes UI") : g_comment;
   bool ok = false;
   if(type == ORDER_TYPE_BUY)
      ok = g_trade.Buy(vol, _Symbol, 0, sl, tp, cmt);
   else
      ok = g_trade.Sell(vol, _Symbol, 0, sl, tp, cmt);

   EchoTradeResult(ok, "Market " + side);
  }

//+------------------------------------------------------------------+
void RequestPendingTrade()
  {
   string sym = ResolveTradeSymbol();
   if(!g_real_trade)
     {
      DoPendingStub(sym);
      g_order_symbol = "";
      return;
     }

   string reason;
   if(!TradeAllowedForSymbol(sym, reason))
     {
      EchoToGui("ERR Pending blocked: " + reason);
      g_order_symbol = "";
      return;
     }

   double vol = NormalizeVolumeFor(sym, g_volume);
   double price = NormalizePriceFor(sym, g_price);
   double sl = (g_sl > 0.0) ? NormalizePriceFor(sym, g_sl) : 0.0;
   double tp = (g_tp > 0.0) ? NormalizePriceFor(sym, g_tp) : 0.0;
   double stoplimit = (g_stoplimit > 0.0) ? NormalizePriceFor(sym, g_stoplimit) : 0.0;
   ConfigureTrade();

   string cmt = (g_comment == "") ? (g_autotrade ? "Hermes auto" : "Hermes UI") : g_comment;
   bool ok = false;
   string action = g_order_type;

   EchoToGui(StringFormat("SEND PENDING %s %s vol=%.2f price=%s SL=%s TP=%s",
      g_order_type, sym, vol,
      DoubleToString(price, (int)SymbolInfoInteger(sym, SYMBOL_DIGITS)),
      DoubleToString(sl, (int)SymbolInfoInteger(sym, SYMBOL_DIGITS)),
      DoubleToString(tp, (int)SymbolInfoInteger(sym, SYMBOL_DIGITS))));

   if(g_order_type == "Buy Limit")
      ok = g_trade.BuyLimit(vol, price, sym, sl, tp, ORDER_TIME_GTC, 0, cmt);
   else if(g_order_type == "Sell Limit")
      ok = g_trade.SellLimit(vol, price, sym, sl, tp, ORDER_TIME_GTC, 0, cmt);
   else if(g_order_type == "Buy Stop")
      ok = g_trade.BuyStop(vol, price, sym, sl, tp, ORDER_TIME_GTC, 0, cmt);
   else if(g_order_type == "Sell Stop")
      ok = g_trade.SellStop(vol, price, sym, sl, tp, ORDER_TIME_GTC, 0, cmt);
   else if(g_order_type == "Buy Stop Limit")
      ok = g_trade.OrderOpen(sym, ORDER_TYPE_BUY_STOP_LIMIT, vol, stoplimit, price, sl, tp, ORDER_TIME_GTC, 0, cmt);
   else if(g_order_type == "Sell Stop Limit")
      ok = g_trade.OrderOpen(sym, ORDER_TYPE_SELL_STOP_LIMIT, vol, stoplimit, price, sl, tp, ORDER_TIME_GTC, 0, cmt);
   else
     {
      EchoToGui("ERR unknown pending type: " + g_order_type);
      g_order_symbol = "";
      return;
     }

   EchoTradeResult(ok, action);
   g_order_symbol = "";
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

   GuiController::SendEvent(InpWpfUi48, InpWpfWindow, "txtSymbol",
                            (int)GUI_TEXT_CHANGE, 0, 0, _Symbol);
   GuiController::SendEvent(InpWpfUi48, InpWpfWindow, "txtBid",
                            (int)GUI_TEXT_CHANGE, 0, 0, DoubleToString(bid, digits));
   GuiController::SendEvent(InpWpfUi48, InpWpfWindow, "txtAsk",
                            (int)GUI_TEXT_CHANGE, 0, 0, DoubleToString(ask, digits));

   // Mini-panel button captions with live prices
   GuiController::SendEvent(InpWpfUi48, InpWpfWindow, "btnQuickSell",
                            (int)GUI_TEXT_CHANGE, 0, 0, "SELL  " + DoubleToString(bid, digits));
   GuiController::SendEvent(InpWpfUi48, InpWpfWindow, "btnQuickBuy",
                            (int)GUI_TEXT_CHANGE, 0, 0, "BUY  " + DoubleToString(ask, digits));

   GuiController::SendEvent(InpWpfUi48, InpWpfWindow, "txtAccount",
                            (int)GUI_TEXT_CHANGE, 0, 0, BuildAccountLine());
   GuiController::SendEvent(InpWpfUi48, InpWpfWindow, "txtMarketStatus",
                            (int)GUI_TEXT_CHANGE, 0, 0, BuildMarketStatusLine(tickAge, tickTime));
   PushPositions();
  }


//+------------------------------------------------------------------+
string PosSlotName(const string prefix, const int slot)
  {
   return prefix + IntegerToString(slot);
  }

//+------------------------------------------------------------------+
void PushPositions()
  {
   if(!g_window_ready)
      return;

   for(int i = 0; i < POS_SLOTS; i++)
      g_pos_ticket[i] = 0;

   int slot = 0;
   int total = PositionsTotal();
   for(int i = total - 1; i >= 0 && slot < POS_SLOTS; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0)
         continue;
      if(!PositionSelectByTicket(ticket))
         continue;

      string sym = PositionGetString(POSITION_SYMBOL);
      long typ = PositionGetInteger(POSITION_TYPE);
      double vol = PositionGetDouble(POSITION_VOLUME);
      double price = PositionGetDouble(POSITION_PRICE_OPEN);
      double profit = PositionGetDouble(POSITION_PROFIT);
      double swap = PositionGetDouble(POSITION_SWAP);
      int digits = (int)SymbolInfoInteger(sym, SYMBOL_DIGITS);
      string side = (typ == POSITION_TYPE_BUY) ? "BUY" : "SELL";
      string pl = StringFormat("%+.2f", profit + swap);

      g_pos_ticket[slot] = ticket;
      string line = StringFormat("%s %s vol=%.2f @%s  P/L:%s  #%s",
         side, sym, vol,
         DoubleToString(price, digits),
         pl,
         IntegerToString(ticket));

      GuiController::SendEvent(InpWpfUi48, InpWpfWindow, PosSlotName("txtPos", slot),
                               (int)GUI_TEXT_CHANGE, 0, 0, line);
      GuiController::SendEvent(InpWpfUi48, InpWpfWindow, PosSlotName("rowPos", slot),
                               (int)GUI_ELEMENT_HIDE, 0, 0, "");
      slot++;
     }

   g_pos_shown = slot;
   for(int j = slot; j < POS_SLOTS; j++)
     {
      GuiController::SendEvent(InpWpfUi48, InpWpfWindow, PosSlotName("txtPos", j),
                               (int)GUI_TEXT_CHANGE, 0, 0, "");
      GuiController::SendEvent(InpWpfUi48, InpWpfWindow, PosSlotName("rowPos", j),
                               (int)GUI_ELEMENT_HIDE, 1, 0, "");
     }

   GuiController::SendEvent(InpWpfUi48, InpWpfWindow, "txtPositionsHeader",
                            (int)GUI_TEXT_CHANGE, 0, 0,
                            IntegerToString(total) + " open" + (total > POS_SLOTS ? " (showing " + IntegerToString(POS_SLOTS) + ")" : ""));
   GuiController::SendEvent(InpWpfUi48, InpWpfWindow, "txtPositionsEmpty",
                            (int)GUI_ELEMENT_HIDE, (total > 0 ? 1 : 0), 0, "");
  }

//+------------------------------------------------------------------+
void ClosePositionByTicket(const ulong ticket)
  {
   if(ticket == 0)
     {
      EchoToGui("ERR close: empty ticket");
      return;
     }
   if(!g_real_trade)
     {
      EchoToGui("ACK stub CLOSE ticket=" + IntegerToString(ticket) + " (enable Real trading)");
      return;
     }

   string reason;
   if(!TradeAllowedNow(reason))
     {
      EchoToGui("ERR close blocked: " + reason);
      return;
     }

   ConfigureTrade();
   EchoToGui("SEND CLOSE ticket=" + IntegerToString(ticket));
   bool ok = g_trade.PositionClose(ticket);
   EchoTradeResult(ok, "CLOSE #" + IntegerToString(ticket));
   PushPositions();
  }

//+------------------------------------------------------------------+
void ClosePositionSlot(const int slot)
  {
   if(slot < 0 || slot >= POS_SLOTS)
     {
      EchoToGui("ERR close slot out of range: " + IntegerToString(slot));
      return;
     }
   ClosePositionByTicket(g_pos_ticket[slot]);
  }

//+------------------------------------------------------------------+
void CloseAllPositions()
  {
   int total = PositionsTotal();
   if(total <= 0)
     {
      EchoToGui("CLOSE ALL: no open positions");
      return;
     }
   if(!g_real_trade)
     {
      EchoToGui("ACK stub CLOSE ALL count=" + IntegerToString(total) + " (enable Real trading)");
      return;
     }

   string reason;
   if(!TradeAllowedNow(reason))
     {
      EchoToGui("ERR close all blocked: " + reason);
      return;
     }

   ConfigureTrade();
   EchoToGui("SEND CLOSE ALL count=" + IntegerToString(total));
   int closed = 0;
   // close from end to start
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0)
         continue;
      if(g_trade.PositionClose(ticket))
         closed++;
      else
         EchoToGui(StringFormat("ERR CLOSE #%s ret=%u %s",
            IntegerToString(ticket), g_trade.ResultRetcode(), g_trade.ResultRetcodeDescription()));
     }
   EchoToGui("CLOSE ALL done closed=" + IntegerToString(closed));
   PushPositions();
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
      return StringFormat("%dd %02d:%02d", d, h, m);
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
   // Typical FX window: Sun 22:00 UTC -> Fri 22:00 UTC
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
   if(tickAge < 0)
      return "quote: n/a";
   if(tickAge > 86400)
      return "quote: " + TimeToString(tickTime, TIME_DATE|TIME_SECONDS);
   if(tickAge <= 2)
      return "quote: live (" + IntegerToString(tickAge) + "s)";
   if(tickAge < 60)
      return StringFormat("quote: %s (%ds ago)",
                          TimeToString(tickTime, TIME_SECONDS), tickAge);
   return StringFormat("quote: %s (%s ago)",
                       TimeToString(tickTime, TIME_SECONDS), FormatDuration(tickAge));
  }

//+------------------------------------------------------------------+
//| Leading session at open time.
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
         "Market: OPEN | Session: %s | Closes in: %s (at %s) | %s",
         clockSession, FormatDuration(left), FormatPacific(sessTo), tickInfo);
      datetime nextFrom, nextTo;
      if(FindNextQuoteSessionStart(sessTo, nextFrom, nextTo))
        {
         string openSess = OpeningSessionNameUtc(ServerToUtc(nextFrom));
         line += StringFormat(" | Next: %s (from %s)", FormatPacific(nextFrom), openSess);
        }
      return line;
     }

   datetime nextFrom, nextTo;
   if(FindNextQuoteSessionStart(nowServer, nextFrom, nextTo))
     {
      int wait = (int)(nextFrom - nowServer);
      string openSess = OpeningSessionNameUtc(ServerToUtc(nextFrom));
      string why = IsFxWeekendUtc(nowUtc)
                   ? ("weekend (geo clock: " + clockSession + ")")
                   : ("outside quote session (geo clock: " + clockSession + ")");
      return StringFormat(
         "Market: CLOSED | %s | Opens: %s (in %s) | from session: %s | %s",
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
         "Market: OPEN | Session: %s | Closes in: %s (at %s) | %s",
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
      "Market: CLOSED | weekend (geo clock: %s) | Opens: %s (in %s) | from session: Sydney | %s",
      clockSession, FormatPacificUtc(sunOpen), FormatDuration((int)(sunOpen - nowUtc)), tickInfo);
  }
//+------------------------------------------------------------------+
